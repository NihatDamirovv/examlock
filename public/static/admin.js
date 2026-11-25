'use strict';

const $ = (s) => document.querySelector(s);
const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);

let teachers = [];
let exams = [];
let editing = null;

async function api(method, url, body) {
  const res = await fetch(url, {
    method,
    headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'fetch' },
    body: body ? JSON.stringify(body) : undefined,
  });
  const data = await res.json().catch(() => ({}));
  if (res.status === 401) { location.href = '/login#admin'; throw new Error(data.error || 'Giriş tələb olunur'); }
  if (!res.ok) throw new Error(data.error || 'Xəta baş verdi');
  return data;
}

function toast(text) {
  const t = $('#toast');
  t.textContent = text;
  t.classList.add('show');
  clearTimeout(toast.timer);
  toast.timer = setTimeout(() => t.classList.remove('show'), 2000);
}

$('#logout').addEventListener('click', async () => {
  await api('POST', '/api/logout').catch(() => {});
  location.href = '/login';
});

// ---------------------------------------------------------------- müəllimlər
function statusTag(t) {
  if (!t.hasPassword) return '<span class="tag warn">Şifrə təyin edilməyib</span>';
  return t.active ? '<span class="tag ok">● Aktiv</span>' : '<span class="tag">Bağlıdır</span>';
}

function renderTeachers() {
  const body = $('#teacher-body');
  if (!teachers.length) {
    body.innerHTML = '<tr><td colspan="4" class="hint">Hələ müəllim yoxdur.</td></tr>';
    return;
  }
  body.innerHTML = teachers.map((t) => `
    <tr data-id="${esc(t.id)}">
      <td><b>${esc(t.name)}</b></td>
      <td>${statusTag(t)}</td>
      <td class="num">${t.examCount}</td>
      <td>
        <div class="actions">
          <button class="small ${t.hasPassword ? '' : 'primary'}" data-act="edit">${t.hasPassword ? 'Redaktə / şifrə' : 'Şifrə təyin et'}</button>
          ${t.hasPassword ? `<button class="small" data-act="toggle">${t.active ? 'Bağla' : 'Aç'}</button>` : ''}
          <button class="small danger" data-act="delete">Sil</button>
        </div>
      </td>
    </tr>`).join('');
}

$('#teacher-body').addEventListener('click', async (e) => {
  const btn = e.target.closest('button[data-act]');
  if (!btn) return;
  const t = teachers.find((v) => v.id === btn.closest('[data-id]').dataset.id);
  try {
    switch (btn.dataset.act) {
      case 'edit':
        openEditor(t);
        break;
      case 'toggle':
        await api('PUT', `/admin/api/teachers/${t.id}`, { active: !t.active });
        toast(t.active ? `${t.name} — giriş bağlandı` : `${t.name} — giriş açıldı`);
        await load();
        break;
      case 'delete':
        if (!confirm(`«${t.name}» silinsin?\n\nOnun imtahanları silinməyəcək — «Bütün imtahanlar» siyahısında sahibsiz qalacaq və başqa müəllimə verə bilərsiniz.`)) return;
        await api('DELETE', `/admin/api/teachers/${t.id}`);
        await load();
        break;
    }
  } catch (err) {
    toast(err.message);
  }
});

function openEditor(t) {
  editing = t || null;
  $('#teacher-dialog-title').textContent = t ? t.name : 'Yeni müəllim';
  $('#t-name').value = t?.name || '';
  $('#t-pw').value = '';
  $('#t-pw').required = !t?.hasPassword;
  $('#t-pw').placeholder = t?.hasPassword ? 'Dəyişmək üçün yeni şifrə yazın' : 'Ən azı 6 simvol';
  $('#t-pw-hint').textContent = t?.hasPassword
    ? 'Boş qalsa, şifrə dəyişmir. Şifrə dəyişəndə müəllimin açıq girişləri bağlanır.'
    : 'Şifrəni müəllimə özünüz çatdırın.';
  $('#teacher-error').textContent = '';
  $('#teacher-dialog').showModal();
  (t ? $('#t-pw') : $('#t-name')).focus();
}

$('#new-teacher').addEventListener('click', () => openEditor(null));
$('#teacher-cancel').addEventListener('click', () => $('#teacher-dialog').close());

$('#teacher-form').addEventListener('submit', async (e) => {
  e.preventDefault();
  const body = { name: $('#t-name').value, password: $('#t-pw').value };
  $('#teacher-save').disabled = true;
  try {
    if (editing) await api('PUT', `/admin/api/teachers/${editing.id}`, body);
    else await api('POST', '/admin/api/teachers', body);
    $('#teacher-dialog').close();
    toast('Yadda saxlandı');
    await load();
  } catch (err) {
    $('#teacher-error').textContent = err.message;
  } finally {
    $('#teacher-save').disabled = false;
  }
});

// ---------------------------------------------------------------- imtahanlar (yalnız baxış + sahibi)
function renderExams() {
  const body = $('#exam-body');
  if (!exams.length) {
    body.innerHTML = '<tr><td colspan="4" class="hint">Hələ imtahan yoxdur.</td></tr>';
    return;
  }
  const opts = (sel) => ['<option value="">— Sahibsiz —</option>']
    .concat(teachers.map((t) => `<option value="${esc(t.id)}" ${t.id === sel ? 'selected' : ''}>${esc(t.name)}</option>`))
    .join('');
  body.innerHTML = exams.map((x) => `
    <tr data-id="${esc(x.id)}">
      <td><b>${esc(x.title)}</b><br><span class="hint" style="word-break:break-all">${esc(x.url)}</span></td>
      <td><select data-owner>${opts(x.teacherId)}</select></td>
      <td class="num hide-sm">${x.sessions}</td>
      <td class="hide-sm">${x.active ? '<span class="tag ok">● Aktiv</span>' : '<span class="tag">Bağlıdır</span>'}</td>
    </tr>`).join('');
}

$('#exam-body').addEventListener('change', async (e) => {
  const sel = e.target.closest('select[data-owner]');
  if (!sel) return;
  const x = exams.find((v) => v.id === sel.closest('[data-id]').dataset.id);
  const name = sel.selectedOptions[0].textContent;
  if (!confirm(`«${x.title}» imtahanı «${name}» adına keçirilsin?`)) { sel.value = x.teacherId || ''; return; }
  try {
    await api('PUT', `/admin/api/exams/${x.id}/owner`, { teacherId: sel.value || null });
    toast('İmtahanın sahibi dəyişdi');
    await load();
  } catch (err) {
    toast(err.message);
    sel.value = x.teacherId || '';
  }
});

async function load() {
  [teachers, exams] = await Promise.all([api('GET', '/admin/api/teachers'), api('GET', '/admin/api/exams')]);
  renderTeachers();
  renderExams();
}

load().catch((err) => toast(err.message));
