import express from 'express';
import { DatabaseSync } from 'node:sqlite';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

// ---------------------------------------------------------------- konfiqurasiya
const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const PORT = Number(process.env.PORT) || 3000;
const ADMIN_PASSWORD = process.env.ADMIN_PASSWORD || '';
const DATA_DIR = path.resolve(process.env.DATA_DIR || path.join(ROOT, 'data'));
const PUBLIC_URL = (process.env.PUBLIC_URL || '').trim().replace(/\/+$/, '');
const LAUNCHER_PATH = path.resolve(process.env.LAUNCHER_PATH || path.join(ROOT, 'bin', 'ExamLock.exe'));
const TRUST_PROXY = process.env.TRUST_PROXY ?? 'loopback, linklocal, uniquelocal';

if (ADMIN_PASSWORD.length < 8) {
  console.error('ADMIN_PASSWORD mühit dəyişəni təyin edilməlidir (ən azı 8 simvol).');
  process.exit(1);
}

fs.mkdirSync(DATA_DIR, { recursive: true });
const secretFile = path.join(DATA_DIR, 'secret.key');
if (!fs.existsSync(secretFile)) fs.writeFileSync(secretFile, crypto.randomBytes(32).toString('hex'), { mode: 0o600 });
const SECRET = fs.readFileSync(secretFile, 'utf8').trim();

// Proqramın içinə yazılan konfiqurasiyanın açarı (launcher/Config.cs ilə eyni olmalıdır)
const OVERLAY_KEY = crypto.createHash('sha256').update('ExamLock/overlay/v1/7f3c9a2e5b814d6a').digest();
const OVERLAY_MAGIC = Buffer.from('EXLKCFG1', 'ascii');

const ONLINE_WINDOW_MS = 50_000;
const GO_TTL_MS = 2 * 60_000;
const ADMIN_TTL_MS = 12 * 3600_000;

// ---------------------------------------------------------------- verilənlər bazası
const db = new DatabaseSync(path.join(DATA_DIR, 'examlock.db'));
db.exec(`
  PRAGMA journal_mode = WAL;
  PRAGMA foreign_keys = ON;
  CREATE TABLE IF NOT EXISTS exams (
    id TEXT PRIMARY KEY,
    code TEXT UNIQUE NOT NULL,
    client_key TEXT NOT NULL,
    title TEXT NOT NULL,
    url TEXT NOT NULL,
    extra_domains TEXT NOT NULL DEFAULT '',
    quit_hash TEXT,
    active INTEGER NOT NULL DEFAULT 1,
    created_at INTEGER NOT NULL
  );
  CREATE TABLE IF NOT EXISTS sessions (
    id TEXT PRIMARY KEY,
    exam_id TEXT NOT NULL REFERENCES exams(id) ON DELETE CASCADE,
    token TEXT UNIQUE NOT NULL,
    student TEXT NOT NULL,
    machine TEXT,
    win_user TEXT,
    ip TEXT,
    started_at INTEGER NOT NULL,
    last_seen INTEGER NOT NULL,
    ended_at INTEGER,
    violations INTEGER NOT NULL DEFAULT 0
  );
  CREATE TABLE IF NOT EXISTS go_tokens (
    token TEXT PRIMARY KEY,
    session_id TEXT NOT NULL REFERENCES sessions(id) ON DELETE CASCADE,
    expires_at INTEGER NOT NULL,
    used INTEGER NOT NULL DEFAULT 0
  );
  CREATE TABLE IF NOT EXISTS events (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    session_id TEXT NOT NULL REFERENCES sessions(id) ON DELETE CASCADE,
    type TEXT NOT NULL,
    detail TEXT,
    at INTEGER NOT NULL
  );
  CREATE INDEX IF NOT EXISTS idx_sessions_exam ON sessions(exam_id);
  CREATE INDEX IF NOT EXISTS idx_events_session ON events(session_id);
`);

// Miqrasiya: kompüter identifikatoru və təkrar girişə icazə
{
  const cols = new Set(db.prepare('PRAGMA table_info(sessions)').all().map((c) => c.name));
  if (!cols.has('machine_id')) db.exec('ALTER TABLE sessions ADD COLUMN machine_id TEXT');
  if (!cols.has('allow_reentry')) db.exec('ALTER TABLE sessions ADD COLUMN allow_reentry INTEGER NOT NULL DEFAULT 0');
  db.exec('CREATE INDEX IF NOT EXISTS idx_sessions_machine ON sessions(exam_id, machine_id)');
}

const q = {
  examById: db.prepare('SELECT * FROM exams WHERE id = ?'),
  examByCode: db.prepare('SELECT * FROM exams WHERE code = ?'),
  examsAll: db.prepare('SELECT * FROM exams ORDER BY created_at DESC'),
  examInsert: db.prepare(`INSERT INTO exams (id, code, client_key, title, url, extra_domains, quit_hash, active, created_at)
                          VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)`),
  examUpdate: db.prepare('UPDATE exams SET title = ?, url = ?, extra_domains = ?, quit_hash = ?, active = ? WHERE id = ?'),
  examDelete: db.prepare('DELETE FROM exams WHERE id = ?'),
  examStats: db.prepare(`SELECT COUNT(*) AS sessions,
                                COALESCE(SUM(CASE WHEN ended_at IS NULL AND last_seen > ? THEN 1 ELSE 0 END), 0) AS online,
                                COALESCE(SUM(violations), 0) AS violations
                         FROM sessions WHERE exam_id = ?`),
  sessionInsert: db.prepare(`INSERT INTO sessions (id, exam_id, token, student, machine, win_user, ip, started_at, last_seen, machine_id)
                             VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`),
  // Bu kompüterdə imtahan artıq «Çıxış» ilə bitirilib və müəllim təkrar girişə icazə verməyib?
  finishedOnMachine: db.prepare(`SELECT student FROM sessions
                                 WHERE exam_id = ? AND machine_id = ? AND ended_at IS NOT NULL AND allow_reentry = 0 LIMIT 1`),
  sessionById: db.prepare('SELECT * FROM sessions WHERE id = ?'),
  allowReentry: db.prepare('UPDATE sessions SET allow_reentry = 1 WHERE exam_id = ? AND machine_id = ? AND ended_at IS NOT NULL'),
  sessionByToken: db.prepare('SELECT * FROM sessions WHERE token = ?'),
  sessionsByExam: db.prepare('SELECT * FROM sessions WHERE exam_id = ? ORDER BY started_at DESC'),
  sessionSeen: db.prepare('UPDATE sessions SET last_seen = ? WHERE id = ?'),
  sessionViolation: db.prepare('UPDATE sessions SET violations = violations + 1, last_seen = ? WHERE id = ?'),
  sessionEnd: db.prepare('UPDATE sessions SET ended_at = ?, last_seen = ? WHERE id = ?'),
  eventInsert: db.prepare('INSERT INTO events (session_id, type, detail, at) VALUES (?, ?, ?, ?)'),
  eventsByExam: db.prepare(`SELECT e.session_id, e.type, e.detail, e.at FROM events e
                            JOIN sessions s ON s.id = e.session_id WHERE s.exam_id = ? ORDER BY e.at ASC`),
  goInsert: db.prepare('INSERT INTO go_tokens (token, session_id, expires_at) VALUES (?, ?, ?)'),
  goTake: db.prepare('UPDATE go_tokens SET used = 1 WHERE token = ? AND used = 0 AND expires_at > ? RETURNING session_id'),
  goCleanup: db.prepare('DELETE FROM go_tokens WHERE expires_at < ?'),
  examBySession: db.prepare('SELECT e.* FROM exams e JOIN sessions s ON s.exam_id = e.id WHERE s.id = ?'),
};

setInterval(() => q.goCleanup.run(Date.now() - 3600_000), 10 * 60_000).unref();

// ---------------------------------------------------------------- köməkçilər
const now = () => Date.now();
const token = (n = 24) => crypto.randomBytes(n).toString('base64url');
const sha256hex = (s) => crypto.createHash('sha256').update(s, 'utf8').digest('hex');
const hmac = (s) => crypto.createHmac('sha256', SECRET).update(s).digest('base64url');
const safeEqual = (a, b) => {
  const x = Buffer.from(String(a));
  const y = Buffer.from(String(b));
  return x.length === y.length && crypto.timingSafeEqual(x, y);
};
const escapeHtml = (s) =>
  String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);

const CODE_ALPHABET = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
function newCode() {
  for (;;) {
    let c = '';
    for (const b of crypto.randomBytes(6)) c += CODE_ALPHABET[b % CODE_ALPHABET.length];
    if (!q.examByCode.get(c)) return c;
  }
}

function baseUrl(req) {
  return PUBLIC_URL || `${req.protocol}://${req.get('host')}`;
}

function slugify(title) {
  const map = { ə: 'e', Ə: 'E', ı: 'i', İ: 'I', ö: 'o', Ö: 'O', ü: 'u', Ü: 'U', ş: 's', Ş: 'S', ç: 'c', Ç: 'C', ğ: 'g', Ğ: 'G' };
  const s = String(title)
    .replace(/[əƏıİöÖüÜşŞçÇğĞ]/g, (c) => map[c])
    .normalize('NFKD')
    .replace(/[^\x20-\x7E]/g, '')
    .replace(/[^A-Za-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 40);
  return s || 'imtahan';
}

function normalizeDomain(raw) {
  let d = String(raw).trim().toLowerCase();
  if (!d) return null;
  d = d.replace(/^[a-z]+:\/\//, '').replace(/^\*\./, '').split(/[/?#]/)[0].split(':')[0].replace(/\.$/, '');
  return /^[a-z0-9-]+(\.[a-z0-9-]+)+$/.test(d) ? d : undefined;
}

// Populyar imtahan platformaları üçün giriş səhifələri avtomatik əlavə olunur.
const PRESETS = [
  {
    match: (h) => h === 'forms.gle' || h === 'docs.google.com',
    hosts: ['docs.google.com', 'forms.gle', 'accounts.google.com', 'accounts.youtube.com'],
  },
  {
    match: (h) => /(^|\.)forms\.(office\.com|cloud\.microsoft)$/.test(h),
    hosts: ['forms.office.com', 'forms.cloud.microsoft', 'login.microsoftonline.com', 'login.live.com'],
  },
];

function allowedHosts(exam) {
  const host = new URL(exam.url).hostname.toLowerCase();
  const set = new Set([host]);
  for (const p of PRESETS) if (p.match(host)) p.hosts.forEach((h) => set.add(h));
  for (const d of exam.extra_domains.split('\n')) if (d.trim()) set.add(d.trim());
  return [...set];
}

function buildOverlay(obj) {
  const iv = crypto.randomBytes(16);
  const cipher = crypto.createCipheriv('aes-256-cbc', OVERLAY_KEY, iv);
  const blob = Buffer.concat([iv, cipher.update(JSON.stringify(obj), 'utf8'), cipher.final()]);
  const len = Buffer.alloc(4);
  len.writeInt32LE(blob.length);
  return Buffer.concat([blob, len, OVERLAY_MAGIC]);
}

let launcherCache = { mtime: 0, data: null };
function launcherTemplate() {
  const st = fs.statSync(LAUNCHER_PATH);
  if (st.mtimeMs !== launcherCache.mtime) launcherCache = { mtime: st.mtimeMs, data: fs.readFileSync(LAUNCHER_PATH) };
  return launcherCache.data;
}

function sendLauncher(req, res, exam) {
  let tpl;
  try {
    tpl = launcherTemplate();
  } catch {
    return res.status(500).type('text').send('Proqram faylı serverdə tapılmadı (bin/ExamLock.exe). Administratora müraciət edin.');
  }
  const overlay = buildOverlay({ s: baseUrl(req), c: exam.code, k: exam.client_key });
  const name = `Imtahan-${slugify(exam.title)}.exe`;
  res.setHeader('Content-Type', 'application/vnd.microsoft.portable-executable');
  res.setHeader('Content-Disposition', `attachment; filename="${name}"`);
  res.setHeader('Content-Length', tpl.length + overlay.length);
  res.setHeader('Cache-Control', 'no-store');
  res.write(tpl);
  res.end(overlay);
}

function validateExamInput(body, existing) {
  const title = String(body.title ?? '').trim();
  if (title.length < 1 || title.length > 120) return { error: 'İmtahanın adını yazın (120 simvola qədər).' };

  let url;
  try {
    url = new URL(String(body.url ?? '').trim());
  } catch {
    return { error: 'İmtahan linki düzgün deyil. Məsələn: https://forms.gle/...' };
  }
  if (!['http:', 'https:'].includes(url.protocol)) return { error: 'Link http:// və ya https:// ilə başlamalıdır.' };

  const domains = [];
  for (const line of String(body.extraDomains ?? '').split(/[\n,\s]+/)) {
    const d = normalizeDomain(line);
    if (d === undefined) return { error: `Əlavə sayt düzgün deyil: ${line}` };
    if (d && !domains.includes(d)) domains.push(d);
  }
  if (domains.length > 30) return { error: 'Ən çox 30 əlavə sayt yazmaq olar.' };

  let quitHash = existing?.quit_hash ?? null;
  if (body.clearQuitPassword) quitHash = null;
  const pw = String(body.quitPassword ?? '');
  if (pw.length > 100) return { error: 'Çıxış şifrəsi çox uzundur.' };
  const code = existing?.code ?? newCode();
  if (pw) quitHash = sha256hex(`${code}:${pw}`);

  return { title, url: url.toString(), extraDomains: domains.join('\n'), quitHash, active: body.active ? 1 : 0, code };
}

function examJson(exam, req) {
  const st = q.examStats.get(now() - ONLINE_WINDOW_MS, exam.id);
  return {
    id: exam.id,
    code: exam.code,
    title: exam.title,
    url: exam.url,
    extraDomains: exam.extra_domains,
    hasQuitPassword: !!exam.quit_hash,
    active: !!exam.active,
    createdAt: exam.created_at,
    sessions: st.sessions,
    online: st.online,
    violations: st.violations,
    studentLink: `${baseUrl(req)}/e/${exam.code}`,
  };
}

// ---------------------------------------------------------------- tətbiq
const app = express();
app.disable('x-powered-by');
app.set('trust proxy',
  TRUST_PROXY === 'false' ? false
  : TRUST_PROXY === 'true' ? true
  : /^\d+$/.test(TRUST_PROXY) ? Number(TRUST_PROXY)
  : TRUST_PROXY);
app.use(express.json({ limit: '32kb' }));
app.use((req, res, next) => {
  res.setHeader('X-Content-Type-Options', 'nosniff');
  res.setHeader('Referrer-Policy', 'no-referrer');
  res.setHeader('X-Frame-Options', 'DENY');
  next();
});
app.use('/static', express.static(path.join(ROOT, 'public', 'static'), { maxAge: '1h' }));

// ---------------------------------------------------------------- admin girişi
const COOKIE = 'el_admin';
const passTag = sha256hex(ADMIN_PASSWORD).slice(0, 16); // şifrə dəyişəndə köhnə girişlər etibarsız olur

function parseCookies(req) {
  const out = {};
  for (const part of (req.headers.cookie || '').split(';')) {
    const i = part.indexOf('=');
    if (i > 0) out[part.slice(0, i).trim()] = decodeURIComponent(part.slice(i + 1).trim());
  }
  return out;
}

function isAdmin(req) {
  const v = parseCookies(req)[COOKIE];
  if (!v) return false;
  const [exp, sig] = v.split('.');
  return Number(exp) > now() && safeEqual(sig, hmac(`admin.${passTag}.${exp}`));
}

function setAdminCookie(req, res) {
  const exp = now() + ADMIN_TTL_MS;
  const secure = req.secure ? '; Secure' : '';
  res.setHeader('Set-Cookie', `${COOKIE}=${exp}.${hmac(`admin.${passTag}.${exp}`)}; Path=/; HttpOnly; SameSite=Strict; Max-Age=${ADMIN_TTL_MS / 1000}${secure}`);
}

function requireAdmin(req, res, next) {
  if (!isAdmin(req)) return res.status(401).json({ error: 'Giriş tələb olunur.' });
  if (req.method !== 'GET' && req.get('x-requested-with') !== 'fetch') return res.status(403).json({ error: 'İcazə yoxdur.' });
  next();
}

const loginAttempts = new Map();
app.post('/admin/api/login', (req, res) => {
  const key = req.ip;
  const a = loginAttempts.get(key) || { n: 0, until: 0 };
  if (a.until > now()) return res.status(429).json({ error: 'Çox cəhd edildi. Bir dəqiqə sonra yenidən yoxlayın.' });
  if (!safeEqual(sha256hex(String(req.body?.password ?? '')), sha256hex(ADMIN_PASSWORD))) {
    a.n += 1;
    if (a.n >= 5) { a.n = 0; a.until = now() + 60_000; }
    loginAttempts.set(key, a);
    return res.status(401).json({ error: 'Şifrə səhvdir.' });
  }
  loginAttempts.delete(key);
  setAdminCookie(req, res);
  res.json({ ok: true });
});

app.post('/admin/api/logout', (req, res) => {
  res.setHeader('Set-Cookie', `${COOKIE}=; Path=/; HttpOnly; SameSite=Strict; Max-Age=0`);
  res.json({ ok: true });
});

app.get('/', (req, res) => res.redirect('/admin'));
app.get('/admin', (req, res) => res.sendFile(path.join(ROOT, 'public', 'admin.html')));

app.get('/admin/api/me', requireAdmin, (req, res) => {
  let launcherReady = true;
  try { fs.accessSync(LAUNCHER_PATH); } catch { launcherReady = false; }
  res.json({ ok: true, server: baseUrl(req), launcherReady });
});

app.get('/admin/api/exams', requireAdmin, (req, res) => {
  res.json(q.examsAll.all().map((e) => examJson(e, req)));
});

app.post('/admin/api/exams', requireAdmin, (req, res) => {
  const v = validateExamInput(req.body || {}, null);
  if (v.error) return res.status(400).json({ error: v.error });
  const id = crypto.randomUUID();
  q.examInsert.run(id, v.code, token(24), v.title, v.url, v.extraDomains, v.quitHash, v.active, now());
  res.json(examJson(q.examById.get(id), req));
});

app.put('/admin/api/exams/:id', requireAdmin, (req, res) => {
  const exam = q.examById.get(req.params.id);
  if (!exam) return res.status(404).json({ error: 'İmtahan tapılmadı.' });
  const v = validateExamInput(req.body || {}, exam);
  if (v.error) return res.status(400).json({ error: v.error });
  q.examUpdate.run(v.title, v.url, v.extraDomains, v.quitHash, v.active, exam.id);
  res.json(examJson(q.examById.get(exam.id), req));
});

app.delete('/admin/api/exams/:id', requireAdmin, (req, res) => {
  q.examDelete.run(req.params.id);
  res.json({ ok: true });
});

app.get('/admin/api/exams/:id/sessions', requireAdmin, (req, res) => {
  const exam = q.examById.get(req.params.id);
  if (!exam) return res.status(404).json({ error: 'İmtahan tapılmadı.' });
  const events = new Map();
  for (const e of q.eventsByExam.all(exam.id)) {
    if (!events.has(e.session_id)) events.set(e.session_id, []);
    events.get(e.session_id).push({ type: e.type, detail: e.detail, at: e.at });
  }
  const cutoff = now() - ONLINE_WINDOW_MS;
  res.json({
    exam: examJson(exam, req),
    sessions: q.sessionsByExam.all(exam.id).map((s) => ({
      id: s.id,
      student: s.student,
      machine: s.machine,
      user: s.win_user,
      ip: s.ip,
      startedAt: s.started_at,
      lastSeen: s.last_seen,
      endedAt: s.ended_at,
      violations: s.violations,
      state: s.ended_at ? 'finished' : s.last_seen > cutoff ? 'online' : 'lost',
      reentryBlocked: !!(s.ended_at && s.machine_id && !s.allow_reentry),
      events: events.get(s.id) || [],
    })),
  });
});

// Müəllim: bu tələbənin kompüterindən imtahana yenidən girməyə icazə ver
app.post('/admin/api/sessions/:id/reentry', requireAdmin, (req, res) => {
  const s = q.sessionById.get(req.params.id);
  if (!s) return res.status(404).json({ error: 'Sessiya tapılmadı.' });
  if (s.machine_id) q.allowReentry.run(s.exam_id, s.machine_id);
  res.json({ ok: true });
});

app.get('/admin/download/:id', (req, res) => {
  if (!isAdmin(req)) return res.redirect('/admin');
  const exam = q.examById.get(req.params.id);
  if (!exam) return res.status(404).type('text').send('İmtahan tapılmadı.');
  sendLauncher(req, res, exam);
});

// ---------------------------------------------------------------- tələbə səhifəsi
const studentPage = fs.readFileSync(path.join(ROOT, 'public', 'student.html'), 'utf8');

app.get('/e/:code', (req, res) => {
  const exam = q.examByCode.get(String(req.params.code).toUpperCase());
  if (!exam) return res.status(404).type('html').send(studentPage
    .replaceAll('{{TITLE}}', 'İmtahan tapılmadı')
    .replaceAll('{{BODY_CLASS}}', 'missing')
    .replaceAll('{{DOWNLOAD}}', '#'));
  res.type('html').send(studentPage
    .replaceAll('{{TITLE}}', escapeHtml(exam.title))
    .replaceAll('{{BODY_CLASS}}', exam.active ? '' : 'inactive')
    .replaceAll('{{DOWNLOAD}}', `/e/${exam.code}/download`));
});

app.get('/e/:code/download', (req, res) => {
  const exam = q.examByCode.get(String(req.params.code).toUpperCase());
  if (!exam) return res.status(404).type('text').send('İmtahan tapılmadı.');
  sendLauncher(req, res, exam);
});

// ---------------------------------------------------------------- imtahan proqramı API
function clientExam(req, res) {
  if (!String(req.get('user-agent') || '').startsWith('ExamLock/')) {
    res.status(403).json({ error: 'Yalnız imtahan proqramı ilə.' });
    return null;
  }
  const exam = q.examByCode.get(String(req.body?.c ?? ''));
  if (!exam || !safeEqual(exam.client_key, String(req.body?.k ?? ''))) {
    res.status(404).json({ error: 'İmtahan tapılmadı. Bəlkə müəllim onu silib.' });
    return null;
  }
  return exam;
}

function newGo(sessionId) {
  const t = token(24);
  q.goInsert.run(t, sessionId, now() + GO_TTL_MS);
  return `/go/${t}`;
}

const machineId = (req) => {
  const m = String(req.body?.m ?? '');
  return /^[a-f0-9]{64}$/.test(m) ? m : null;
};
const FINISHED_MSG = 'Siz bu imtahanı bu kompüterdə artıq bitirmisiniz. Yenidən girmək üçün müəllimə müraciət edin.';

app.post('/api/client/info', (req, res) => {
  const exam = clientExam(req, res);
  if (!exam) return;
  const m = machineId(req);
  const finished = !!(m && q.finishedOnMachine.get(exam.id, m));
  res.json({ title: exam.title, active: !!exam.active, finished, finishedMessage: finished ? FINISHED_MSG : null });
});

app.post('/api/client/launch', (req, res) => {
  const exam = clientExam(req, res);
  if (!exam) return;
  if (!exam.active) return res.status(403).json({ error: 'Bu imtahan hazırda aktiv deyil. Müəllim imtahanı açanda yenidən cəhd edin.' });

  const student = String(req.body.student ?? '').replace(/\s+/g, ' ').trim().slice(0, 100);
  if (student.length < 3) return res.status(400).json({ error: 'Ad və soyadınızı tam yazın.' });
  const machine = String(req.body.machine ?? '').slice(0, 100);
  const user = String(req.body.user ?? '').slice(0, 100);
  const m = machineId(req);
  if (m && q.finishedOnMachine.get(exam.id, m)) return res.status(403).json({ error: FINISHED_MSG });

  const id = crypto.randomUUID();
  const t = token(32);
  const ts = now();
  q.sessionInsert.run(id, exam.id, t, student, machine, user, req.ip, ts, ts, m);
  q.eventInsert.run(id, 'start', `${machine} / ${user}`.slice(0, 300), ts);

  res.json({
    session: t,
    title: exam.title,
    go: newGo(id),
    allowed: allowedHosts(exam),
    quitHash: exam.quit_hash,
  });
});

app.post('/api/client/renew', (req, res) => {
  const s = q.sessionByToken.get(String(req.body?.token ?? ''));
  if (!s || s.ended_at) return res.status(404).json({ error: 'Sessiya bitib. Proqramı yenidən açın.' });
  const exam = q.examById.get(s.exam_id);
  if (!exam?.active) return res.status(403).json({ error: 'İmtahan bağlanıb.' });
  res.json({ go: newGo(s.id) });
});

const EVENT_TYPES = new Set(['focus_lost', 'app_closed', 'blocked_url', 'quit', 'quit_failed', 'heartbeat', 'display', 'session_switch']);
const VIOLATIONS = new Set(['focus_lost', 'app_closed', 'blocked_url', 'quit_failed', 'session_switch']);

app.post('/api/client/event', (req, res) => {
  const s = q.sessionByToken.get(String(req.body?.token ?? ''));
  const type = String(req.body?.type ?? '');
  if (!s || !EVENT_TYPES.has(type)) return res.status(400).json({ error: 'Yanlış sorğu.' });
  if (s.ended_at) return res.json({ ok: true });
  const ts = now();
  if (type === 'heartbeat') {
    q.sessionSeen.run(ts, s.id);
    return res.json({ ok: true });
  }
  q.eventInsert.run(s.id, type, String(req.body.detail ?? '').slice(0, 300), ts);
  if (VIOLATIONS.has(type)) q.sessionViolation.run(ts, s.id);
  else if (type === 'quit') q.sessionEnd.run(ts, ts, s.id);
  else q.sessionSeen.run(ts, s.id);
  res.json({ ok: true });
});

// Bir dəfəlik keçid: imtahan linki heç vaxt tələbəyə göstərilmir.
app.get('/go/:t', (req, res) => {
  const blocked = (msg) => res.status(403).type('html').send(
    `<!doctype html><meta charset="utf-8"><title>İmtahan</title><body style="font-family:Segoe UI,sans-serif;padding:48px;color:#111">
     <h2>${msg}</h2><p>İmtahanı yalnız müəllimin verdiyi proqramla aça bilərsiniz.</p></body>`);
  if (!String(req.get('user-agent') || '').includes('ExamLock/')) return blocked('Bu link yalnız imtahan proqramında açılır.');
  const row = q.goTake.get(String(req.params.t), now());
  if (!row) return blocked('Keçid linkinin vaxtı bitib. Proqramda «Yenilə» düyməsini basın.');
  const exam = q.examBySession.get(row.session_id);
  if (!exam?.active) return blocked('İmtahan bağlanıb.');
  res.setHeader('Cache-Control', 'no-store');
  res.redirect(302, exam.url);
});

app.use((err, req, res, next) => {
  if (err?.type === 'entity.parse.failed') return res.status(400).json({ error: 'Yanlış sorğu.' });
  console.error(err);
  res.status(500).json({ error: 'Server xətası.' });
});

app.listen(PORT, () => {
  console.log(`ExamLock serveri işləyir: http://localhost:${PORT}/admin`);
  if (!PUBLIC_URL) console.log('Diqqət: PUBLIC_URL təyin edilməyib — fayllar sorğunun ünvanına bağlanacaq.');
});
