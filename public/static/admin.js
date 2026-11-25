'use strict';

const $ = (s) => document.querySelector(s);
const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
const fmtTime = (t) => new Date(t).toLocaleTimeString('az-AZ', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
const fmtDate = (t) => new Date(t).toLocaleString('az-AZ', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' });

const EVENT_LABELS = {
  start: 'İmtahana başladı',
  focus_lost: 'Başqa pəncərəyə keçməyə cəhd',
  app_closed: 'Qadağan proqram açıldı və bağlandı',
  blocked_url: 'İcazəsiz sayta keçid cəhdi',
  quit: 'İmtahandan çıxdı',
  quit_failed: 'Səhv çıxış şifrəsi',
  display: 'Ekran dəyişdi (monitor qoşuldu/ayrıldı)',
  session_switch: 'Windows sessiyası dəyişdi',
};
const VIOLATIONS = new Set(['focus_lost', 'app_closed', 'blocked_url', 'quit_failed', 'session_switch']);
const STATE = { online: 'İmtahandadır', lost: 'Əlaqə kəsilib', finished: 'Bitirdi' };

let exams = [];
let editing = null;
let monitorId = null;
let monitorTimer = null;
const openRows = new Set();

async function api(method, url, body) {
  const res = await fetch(url, {
    method,
    headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'fetch' },
    body: body ? JSON.stringify(body) : undefined,
  });
  const data = await res.json().catch(() => ({}));
  if (res.status === 401 && !url.endsWith('/login')) { showLogin(); throw new Error(data.error || 'Giriş tələb olunur'); }
  if (!res.ok) throw new Error(data.error || 'Xəta baş verdi');
  return data;
}

function toast(text) {
  const t = $('#toast');
  t.textContent = text;
  t.classList.add('show');
  clearTimeout(toast.timer);
  toast.timer = setTimeout(() => t.classList.remove('show'), 1800);
}

// ---------------------------------------------------------------- giriş
function showLogin() {
  $('#login-view').hidden = false;
  $('#app-view').hidden = true;
  $('#logout').hidden = true;
  $('#login-pw').focus();
}

$('#login-form').addEventListener('submit', async (e) => {
  e.preventDefault();
  $('#login-error').textContent = '';
  try {
    await api('POST', '/admin/api/login', { password: $('#login-pw').value });
    $('#login-pw').value = '';
    start();
  } catch (err) {
    $('#login-error').textContent = err.message;
  }
});

$('#logout').addEventListener('click', async () => {
  await api('POST', '/admin/api/logout').catch(() => {});
  showLogin();
});

async function start() {
  let me;
  try { me = await api('GET', '/admin/api/me'); } catch { return; }
  $('#login-view').hidden = true;
  $('#app-view').hidden = false;
  $('#logout').hidden = false;

  const notes = [];
  if (!me.launcherReady) notes.push('Serverdə imtahan proqramı (bin/ExamLock.exe) tapılmadı — fayl yükləmək işləməyəcək.');
  if (/\/\/(localhost|127\.|\[::1\])/.test(me.server))
    notes.push(`Server ünvanı <b>${esc(me.server)}</b> — bu ünvan yalnız bu kompüterdə işləyir. Tələbələr üçün PUBLIC_URL təyin edin.`);
  $('#notices').innerHTML = notes.map((n) => `<div class="notice">${n}</div>`).join('');
  await loadExams();
}

// ---------------------------------------------------------------- imtahan siyahısı
async function loadExams() {
  exams = await api('GET', '/admin/api/exams');
  renderExams();
}

function renderExams() {
  const list = $('#exam-list');
  if (!exams.length) {
    list.innerHTML = `<div class="card empty"><h2>Hələ imtahan yoxdur</h2><p>«+ Yeni imtahan» düyməsini basıb imtahan saytının linkini yazın.</p></div>`;
    return;
  }
  list.innerHTML = exams.map((x) => `
    <article class="card" data-id="${x.id}">
      <div class="exam-head">
        <div style="flex:1;min-width:220px">
          <h2 class="exam-title">${esc(x.title)}</h2>
          <div class="exam-url">${esc(x.url)}</div>
        </div>
        <span class="badge ${x.active ? 'on' : ''}">${x.active ? '● Aktiv' : 'Bağlıdır'}</span>
      </div>
      <div class="stats">
        <span>Tələbə: <b>${x.sessions}</b></span>
        <span>İndi imtahanda: <b>${x.online}</b></span>
        <span class="${x.violations ? 'bad' : ''}">Pozuntu: <b>${x.violations}</b></span>
        <span>Çıxış şifrəsi: <b>${x.hasQuitPassword ? 'var' : 'yoxdur'}</b></span>
      </div>
      <div class="actions">
        <a class="btn primary" href="/admin/download/${x.id}">⬇ Faylı yüklə (.exe)</a>
        <button data-act="monitor">İzləmə</button>
        <button data-act="toggle">${x.active ? 'Bağla' : 'Aktiv et'}</button>
        <button data-act="edit">Redaktə</button>
        <button data-act="delete" class="danger">Sil</button>
      </div>
      <div class="linkbox">
        <span class="hint">Tələbə linki:</span>
        <code>${esc(x.studentLink)}</code>
        <button class="small" data-act="copy">Kopyala</button>
      </div>
    </article>`).join('');
}

$('#exam-list').addEventListener('click', async (e) => {
  const btn = e.target.closest('button[data-act]');
  if (!btn) return;
  const id = btn.closest('[data-id]').dataset.id;
  const x = exams.find((v) => v.id === id);
  try {
    switch (btn.dataset.act) {
      case 'copy':
        await navigator.clipboard.writeText(x.studentLink);
        toast('Link kopyalandı');
        break;
      case 'edit':
        openEditor(x);
        break;
      case 'toggle':
        await api('PUT', `/admin/api/exams/${id}`, { title: x.title, url: x.url, extraDomains: x.extraDomains, active: !x.active });
        toast(x.active ? 'İmtahan bağlandı' : 'İmtahan aktivdir');
        await loadExams();
        break;
      case 'delete':
        if (!confirm(`«${x.title}» silinsin? Bütün tələbə nəticələri və qeydlər də silinəcək.`)) return;
        await api('DELETE', `/admin/api/exams/${id}`);
        await loadExams();
        break;
      case 'monitor':
        openMonitor(id);
        break;
    }
  } catch (err) {
    toast(err.message);
  }
});

// ---------------------------------------------------------------- yarat / redaktə
function openEditor(x) {
  editing = x || null;
  $('#exam-dialog-title').textContent = x ? 'İmtahanı redaktə et' : 'Yeni imtahan';
  $('#f-title').value = x?.title || '';
  $('#f-url').value = x?.url || '';
  $('#f-quit').value = '';
  $('#f-quit').placeholder = x?.hasQuitPassword
    ? 'Şifrə təyin edilib — dəyişmək üçün yenisini yazın'
    : 'Boş qalsa, tələbə «Çıxış» ilə özü çıxa bilər';
  $('#f-clear').checked = false;
  $('#f-clear-wrap').hidden = !x?.hasQuitPassword;
  $('#f-domains').value = x?.extraDomains || '';
  $('#f-active').checked = x ? x.active : true;
  $('#exam-error').textContent = '';
  $('#exam-dialog').showModal();
  $('#f-title').focus();
}

$('#new-exam').addEventListener('click', () => openEditor(null));
$('#exam-cancel').addEventListener('click', () => $('#exam-dialog').close());

$('#exam-form').addEventListener('submit', async (e) => {
  e.preventDefault();
  const body = {
    title: $('#f-title').value,
    url: $('#f-url').value,
    quitPassword: $('#f-quit').value,
    clearQuitPassword: $('#f-clear').checked,
    extraDomains: $('#f-domains').value,
    active: $('#f-active').checked,
  };
  $('#exam-save').disabled = true;
  try {
    if (editing) await api('PUT', `/admin/api/exams/${editing.id}`, body);
    else await api('POST', '/admin/api/exams', body);
    $('#exam-dialog').close();
    toast(editing ? 'Yadda saxlandı' : 'İmtahan yaradıldı — indi faylı yükləyə bilərsiniz');
    await loadExams();
  } catch (err) {
    $('#exam-error').textContent = err.message;
  } finally {
    $('#exam-save').disabled = false;
  }
});

// ---------------------------------------------------------------- canlı izləmə
async function openMonitor(id) {
  monitorId = id;
  openRows.clear();
  $('#monitor-body').innerHTML = '<tr><td colspan="5" class="hint">Yüklənir…</td></tr>';
  $('#monitor-dialog').showModal();
  await refreshMonitor();
  monitorTimer = setInterval(refreshMonitor, 5000);
}

function closeMonitor() {
  clearInterval(monitorTimer);
  monitorId = null;
  $('#monitor-dialog').close();
  loadExams().catch(() => {});
}
$('#monitor-close').addEventListener('click', closeMonitor);
$('#monitor-dialog').addEventListener('cancel', (e) => { e.preventDefault(); closeMonitor(); });

async function refreshMonitor() {
  if (!monitorId) return;
  let data;
  try { data = await api('GET', `/admin/api/exams/${monitorId}/sessions`); } catch (err) { toast(err.message); return; }
  const { exam, sessions } = data;
  $('#monitor-title').textContent = exam.title;
  $('#monitor-updated').textContent = 'Yeniləndi: ' + fmtTime(Date.now());
  const finished = sessions.filter((s) => s.state === 'finished').length;
  const lost = sessions.filter((s) => s.state === 'lost').length;
  $('#monitor-stats').innerHTML = `
    <span>Cəmi: <b>${sessions.length}</b></span>
    <span>İmtahanda: <b>${exam.online}</b></span>
    <span>Bitirdi: <b>${finished}</b></span>
    <span class="${lost ? 'bad' : ''}">Əlaqə kəsilib: <b>${lost}</b></span>
    <span class="${exam.violations ? 'bad' : ''}">Pozuntu: <b>${exam.violations}</b></span>`;

  if (!sessions.length) {
    $('#monitor-body').innerHTML = '<tr><td colspan="5" class="hint">Hələ heç bir tələbə başlamayıb.</td></tr>';
    return;
  }
  $('#monitor-body').innerHTML = sessions.map((s) => `
    <tr class="row" data-sid="${s.id}">
      <td><b>${esc(s.student)}</b></td>
      <td><span class="dot ${s.state}"></span>${STATE[s.state]}</td>
      <td class="num">${fmtDate(s.startedAt)}</td>
      <td class="hide-sm">${esc(s.machine)}<br><span class="hint">${esc(s.ip || '')}</span></td>
      <td class="num" style="${s.violations ? 'color:var(--danger);font-weight:700' : ''}">${s.violations}</td>
    </tr>
    <tr ${openRows.has(s.id) ? '' : 'hidden'} data-detail="${s.id}">
      <td colspan="5">
        ${s.reentryBlocked ? `<div style="display:flex;gap:10px;align-items:center;flex-wrap:wrap;margin:4px 0 8px">
          <span class="hint">Bu kompüterdən təkrar giriş bağlıdır.</span>
          <button class="small" data-reentry="${s.id}">Yenidən girişə icazə ver</button></div>` : ''}
        <ul class="events">${s.events.map((ev) => `
          <li class="${VIOLATIONS.has(ev.type) ? 'v' : ''}">
            <time>${fmtTime(ev.at)}</time>
            <span>${esc(EVENT_LABELS[ev.type] || ev.type)}${ev.detail ? ` — <span class="hint">${esc(ev.detail)}</span>` : ''}</span>
          </li>`).join('')}
        </ul>
      </td>
    </tr>`).join('');
}

$('#monitor-body').addEventListener('click', async (e) => {
  const re = e.target.closest('button[data-reentry]');
  if (re) {
    if (!confirm('Bu tələbə həmin kompüterdən imtahana yenidən girə biləcək. Davam edilsin?')) return;
    try {
      await api('POST', `/admin/api/sessions/${re.dataset.reentry}/reentry`);
      toast('Yenidən girişə icazə verildi');
      await refreshMonitor();
    } catch (err) { toast(err.message); }
    return;
  }
  const row = e.target.closest('tr.row');
  if (!row) return;
  const id = row.dataset.sid;
  const detail = document.querySelector(`[data-detail="${id}"]`);
  detail.hidden = !detail.hidden;
  if (detail.hidden) openRows.delete(id); else openRows.add(id);
});

start();
