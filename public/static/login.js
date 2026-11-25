'use strict';

const $ = (s) => document.querySelector(s);
const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
const initials = (name) => name.split(/\s+/).filter(Boolean).slice(0, 2).map((w) => w[0].toLocaleUpperCase('az')).join('');

let teachers = [];
let chosen = null;

async function post(url, body) {
  const res = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'fetch' },
    body: JSON.stringify(body),
  });
  const data = await res.json().catch(() => ({}));
  if (!res.ok) throw new Error(data.error || 'Xəta baş verdi');
  return data;
}

function show(view) {
  for (const id of ['pick-view', 'pw-view', 'admin-view']) $('#' + id).hidden = id !== view;
}

function renderTeachers() {
  const grid = $('#teacher-grid');
  if (!teachers.length) {
    grid.innerHTML = '<div class="card empty" style="grid-column:1/-1"><p>Hələ müəllim hesabı yoxdur. Administratora müraciət edin.</p></div>';
    return;
  }
  grid.innerHTML = teachers.map((t) => `
    <button class="teacher-card" data-id="${esc(t.id)}">
      <span class="avatar">${esc(initials(t.name))}</span>
      <span><span class="nm">${esc(t.name)}</span><br><span class="rl">Müəllim</span></span>
    </button>`).join('');
}

$('#teacher-grid').addEventListener('click', (e) => {
  const b = e.target.closest('.teacher-card');
  if (!b) return;
  chosen = teachers.find((t) => t.id === b.dataset.id);
  $('#pw-name').textContent = chosen.name;
  $('#pw-avatar').textContent = initials(chosen.name);
  $('#teacher-error').textContent = '';
  $('#teacher-pw').value = '';
  show('pw-view');
  $('#teacher-pw').focus();
});

document.addEventListener('click', (e) => {
  if (e.target.closest('[data-back]')) { history.replaceState(null, '', '/login'); show('pick-view'); }
});

$('#to-admin').addEventListener('click', () => {
  history.replaceState(null, '', '/login#admin');
  $('#admin-error').textContent = '';
  show('admin-view');
  $('#admin-pw').focus();
});

$('#teacher-form').addEventListener('submit', async (e) => {
  e.preventDefault();
  $('#teacher-error').textContent = '';
  try {
    const r = await post('/api/login/teacher', { teacherId: chosen.id, password: $('#teacher-pw').value });
    location.href = r.redirect;
  } catch (err) {
    $('#teacher-error').textContent = err.message;
  }
});

$('#admin-form').addEventListener('submit', async (e) => {
  e.preventDefault();
  $('#admin-error').textContent = '';
  try {
    const r = await post('/api/login/admin', { password: $('#admin-pw').value });
    location.href = r.redirect;
  } catch (err) {
    $('#admin-error').textContent = err.message;
  }
});

(async () => {
  try { teachers = await (await fetch('/api/teachers')).json(); } catch { teachers = []; }
  renderTeachers();
  if (location.hash === '#admin') { show('admin-view'); $('#admin-pw').focus(); }
})();
