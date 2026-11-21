// Clawd 桌宠 · 工具栏弹窗
const DEF = { enabled: true, size: 1, follow: false, chatty: false, health: true, hiddenSites: [], count: 2 };
const $ = id => document.getElementById(id);
let settings = Object.assign({}, DEF), tabId = null, host = null, pomo = null, reminders = [];

const save = () => chrome.storage.local.set({ settings: Object.assign({}, settings) });
const two = n => String(n).padStart(2, '0');
function fmtLeft(ms) { const s = Math.max(0, Math.ceil(ms / 1000)); return two(Math.floor(s / 60)) + ':' + two(s % 60); }
function fmtIn(ms) {
  const m = Math.max(0, Math.round(ms / 60000));
  if (m < 1) return '马上';
  if (m < 60) return m + ' 分钟后';
  return Math.floor(m / 60) + ' 小时 ' + (m % 60) + ' 分后';
}

function renderSettings() {
  $('enabled').checked = settings.enabled;
  $('follow').checked = settings.follow;
  $('chatty').checked = settings.chatty;
  $('count').checked = (settings.count || 1) >= 2;
  $('health').checked = settings.health;
  $('size').value = String(settings.size | 0);
  document.body.classList.toggle('off', !settings.enabled);
  renderSite();
}

function renderSite() {
  const hidden = !!host && (settings.hiddenSites || []).includes(host);
  $('siteLabel').textContent = host ? `在 ${host} 上显示` : '这个页面放不了桌宠';
  $('siteOn').disabled = !host;
  $('siteOn').checked = !!host && !hidden;
  for (const id of ['summon', 'feed', 'ball', 'dance', 'dreamGame']) $(id).disabled = !host || hidden;
}

function renderPomo() {
  $('pomoStatus').textContent = pomo ? (pomo.mode === 2 ? '休息中 ' : '专注中 ') + fmtLeft(pomo.end - Date.now()) : '未开始';
  $('pomoStop').disabled = !pomo;
}

function renderReminders() {
  const ul = $('rlist');
  ul.replaceChildren();
  for (const r of [...reminders].sort((a, b) => a.due - b.due)) {
    const li = document.createElement('li');
    const t = document.createElement('span'); t.className = 'rt'; t.textContent = r.text; t.title = r.text;
    const w = document.createElement('span'); w.className = 'rw'; w.textContent = fmtIn(r.due - Date.now());
    const x = document.createElement('button'); x.textContent = '×'; x.title = '删掉这个提醒';
    x.addEventListener('click', () => chrome.runtime.sendMessage({ cmd: 'delReminder', id: r.id }));
    li.append(t, w, x);
    ul.append(li);
  }
  $('rclear').hidden = reminders.length === 0;
}

async function tellTab(msg) {
  if (tabId == null) return null;
  try { return await chrome.tabs.sendMessage(tabId, msg); } catch (e) { return null; }
}

async function init() {
  const v = await chrome.storage.local.get(['settings', 'pomo', 'reminders']);
  settings = Object.assign({}, DEF, v.settings || {});
  pomo = v.pomo || null;
  reminders = v.reminders || [];
  renderSettings(); renderPomo(); renderReminders();

  try {
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    if (tab) tabId = tab.id;
  } catch (e) { /* 没关系 */ }
  const r = await tellTab({ cmd: 'hello' });
  host = r && r.host ? r.host : null;
  renderSite();
}

$('enabled').addEventListener('change', e => { settings.enabled = e.target.checked; save(); renderSettings(); });
$('siteOn').addEventListener('change', e => {
  if (!host) return;
  const set = new Set(settings.hiddenSites || []);
  if (e.target.checked) set.delete(host); else set.add(host);
  settings.hiddenSites = Array.from(set);
  if (e.target.checked && !settings.enabled) settings.enabled = true;
  save(); renderSettings();
});
for (const k of ['follow', 'chatty', 'health'])
  $(k).addEventListener('change', e => { settings[k] = e.target.checked; save(); });
$('size').addEventListener('change', e => { settings.size = +e.target.value; save(); });
$('count').addEventListener('change', e => { settings.count = e.target.checked ? 2 : 1; save(); });

$('summon').addEventListener('click', async () => { await tellTab({ cmd: 'summon' }); window.close(); });
$('dance').addEventListener('click', async () => { await tellTab({ cmd: 'dance' }); window.close(); });
$('feed').addEventListener('click', async () => { await tellTab({ cmd: 'feed' }); window.close(); });
$('ball').addEventListener('click', async () => { await tellTab({ cmd: 'ball' }); window.close(); });
$('dreamGame').addEventListener('click', async () => { await tellTab({ cmd: 'dreamGame' }); window.close(); });

for (const b of document.querySelectorAll('[data-mode]'))
  b.addEventListener('click', () => chrome.runtime.sendMessage({ cmd: 'pomo', mode: +b.dataset.mode, minutes: +b.dataset.min }));
$('pomoStop').addEventListener('click', () => chrome.runtime.sendMessage({ cmd: 'pomoStop' }));

function addReminder() {
  const minutes = Math.min(1440, Math.max(1, parseInt($('rmin').value, 10) || 10));
  const text = $('rtext').value.trim() || '时间到啦！';
  chrome.runtime.sendMessage({ cmd: 'remind', minutes, text });
  $('rtext').value = '';
}
$('radd').addEventListener('click', addReminder);
$('rtext').addEventListener('keydown', e => { if (e.key === 'Enter') addReminder(); });
$('rclear').addEventListener('click', () => chrome.runtime.sendMessage({ cmd: 'clearReminders' }));

chrome.storage.onChanged.addListener((ch, area) => {
  if (area !== 'local') return;
  if (ch.settings) { settings = Object.assign({}, DEF, ch.settings.newValue || {}); renderSettings(); }
  if (ch.pomo) { pomo = ch.pomo.newValue || null; renderPomo(); }
  if (ch.reminders) { reminders = ch.reminders.newValue || []; renderReminders(); }
});
setInterval(() => { renderPomo(); if (reminders.length) renderReminders(); }, 1000);

init();
