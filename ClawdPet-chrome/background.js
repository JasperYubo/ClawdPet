// Clawd 桌宠 · 后台：番茄钟、定时提醒、久坐检测
// 这些要在网页关掉、切走之后也能准时响，所以放在 service worker 里，用 chrome.alarms 计时。
// 到点时：弹系统通知 + 往 storage 写一个 clawdEvent，正在看的网页里的 Clawd 会跳出来。

const DEF = { enabled: true, size: 1, follow: false, chatty: false, health: true, hiddenSites: [], count: 2 };
const IDLE_SECS = 180;            // 3 分钟没动鼠标键盘：Clawd 打哈欠睡觉
const SIT_LIMIT = 50 * 60000;     // 连续用 50 分钟提醒起来活动
const BREAK_RESET = 5 * 60000;    // 离开 5 分钟以上，久坐计时重新开始
const HEALTH_LINES = ['已经连续用电脑 50 分钟啦，起来活动一下吧！', '眼睛累了吧？看看远处~', '喝口水，伸个懒腰！'];

const get = keys => chrome.storage.local.get(keys);
const set = obj => chrome.storage.local.set(obj);
const newId = () => Date.now().toString(36) + Math.random().toString(36).slice(2, 7);
const clampMin = m => Math.min(1440, Math.max(1, Math.round(+m || 10)));

try { chrome.idle.setDetectionInterval(IDLE_SECS); } catch (e) { /* 旧版本 */ }

async function setup() {
  try { chrome.idle.setDetectionInterval(IDLE_SECS); } catch (e) { /* 旧版本 */ }
  if (!(await chrome.alarms.get('health'))) chrome.alarms.create('health', { periodInMinutes: 1 });
  try { await set({ idleState: await chrome.idle.queryState(IDLE_SECS) }); } catch (e) { /* 没关系 */ }
  // 浏览器重启后把丢掉的闹钟补回来
  const { pomo, reminders = [] } = await get(['pomo', 'reminders']);
  const soon = Date.now() + 1000;
  if (pomo && !(await chrome.alarms.get('pomo'))) chrome.alarms.create('pomo', { when: Math.max(pomo.end, soon) });
  for (const r of reminders)
    if (!(await chrome.alarms.get('rem:' + r.id))) chrome.alarms.create('rem:' + r.id, { when: Math.max(r.due, soon) });
}
chrome.runtime.onInstalled.addListener(details => {
  setup();
  // 刚装好 / 刚更新：把 Clawd 直接放进已经打开的网页，不用一个个刷新；网页里的旧 Clawd 会自己退场
  if (details && (details.reason === 'install' || details.reason === 'update')) injectOpenTabs();
});
chrome.runtime.onStartup.addListener(setup);

async function injectOpenTabs() {
  if (!chrome.scripting || !chrome.tabs) return;
  let tabs = [];
  try { tabs = await chrome.tabs.query({ url: ['http://*/*', 'https://*/*', 'file:///*'] }); } catch (e) { return; }
  for (const t of tabs) {
    if (t.id == null || t.discarded) continue;
    chrome.scripting.executeScript({ target: { tabId: t.id }, files: ['content.js'] }).catch(() => { /* 有些网页不让放（比如应用商店），跳过 */ });
  }
}

// ---------------------------------------------------------------- 网页 / 弹窗发来的命令
chrome.runtime.onMessage.addListener((msg, sender, reply) => {
  handle(msg || {}).then(r => reply(r || { ok: true }), e => reply({ ok: false, error: String(e) }));
  return true;
});

async function handle(msg) {
  switch (msg.cmd) {
    case 'pomo': return startPomo(msg.mode === 2 ? 2 : 1, clampMin(msg.minutes), msg.end, false);
    case 'pomoStop': await chrome.alarms.clear('pomo'); await set({ pomo: null }); return;
    case 'remind': return addReminder(clampMin(msg.minutes), String(msg.text || '时间到啦！').slice(0, 100));
    case 'delReminder': return delReminder(String(msg.id));
    case 'clearReminders': {
      const { reminders = [] } = await get('reminders');
      for (const r of reminders) await chrome.alarms.clear('rem:' + r.id);
      await set({ reminders: [] });
      return;
    }
  }
}

async function startPomo(mode, minutes, end, auto) {
  const t = Date.now();
  if (!(typeof end === 'number' && end > t && end < t + 1441 * 60000)) end = t + minutes * 60000;
  await set({ pomo: { mode, end, auto: !!auto } });
  chrome.alarms.create('pomo', { when: end });
}

async function addReminder(minutes, text) {
  const id = newId(), due = Date.now() + minutes * 60000;
  const { reminders = [] } = await get('reminders');
  reminders.push({ id, due, text });
  await set({ reminders });
  chrome.alarms.create('rem:' + id, { when: due });
  return { ok: true, id };
}

async function delReminder(id) {
  const { reminders = [] } = await get('reminders');
  await chrome.alarms.clear('rem:' + id);
  await set({ reminders: reminders.filter(r => r.id !== id) });
}

// ---------------------------------------------------------------- 到点
chrome.alarms.onAlarm.addListener(async a => {
  if (a.name === 'pomo') await pomoDone();
  else if (a.name.startsWith('rem:')) await fireReminder(a.name.slice(4));
  else if (a.name === 'health') await checkHealth();
});

async function pomoDone() {
  const { pomo } = await get('pomo');
  if (!pomo) return;
  if (Date.now() < pomo.end - 2000) { chrome.alarms.create('pomo', { when: pomo.end }); return; }
  if (pomo.mode === 1) {
    await startPomo(2, 5, 0, true);   // 专注结束自动进入 5 分钟休息
    notify('专注完成！', '休息 5 分钟吧，起来走走~');
    await event('celebrate', '专注完成！休息 5 分钟~');
  } else {
    await set({ pomo: null });
    notify('休息结束', '继续加油！');
    await event('alert', '休息结束，继续加油！', 6);
  }
}

async function fireReminder(id) {
  const { reminders = [] } = await get('reminders');
  const r = reminders.find(x => x.id === id);
  if (!r) return;
  await set({ reminders: reminders.filter(x => x.id !== id) });
  notify('Clawd 提醒你', r.text, true);
  await event('alert', r.text, 30, true);
}

// ---------------------------------------------------------------- 久坐 / 离开
chrome.idle.onStateChanged.addListener(async st => {
  const t = Date.now();
  const { activity = {} } = await get('activity');
  if (st === 'active') {
    if (activity.idleStart && t - activity.idleStart >= BREAK_RESET) activity.activeSince = t;
    activity.idleStart = 0;
  } else if (!activity.idleStart) {
    activity.idleStart = t - (st === 'idle' ? IDLE_SECS * 1000 : 0);
  }
  if (!activity.activeSince) activity.activeSince = t;
  await set({ idleState: st, activity });
});

async function checkHealth() {
  const t = Date.now();
  const v = await get(['settings', 'activity', 'pomo', 'idleState']);
  const s = Object.assign({}, DEF, v.settings || {});
  const act = v.activity || {};
  // 合盖 / 浏览器关掉过一阵（闹钟停了），重新计时
  if (!act.activeSince || (act.lastTick && t - act.lastTick > BREAK_RESET)) act.activeSince = t;
  act.lastTick = t;
  const focusing = v.pomo && v.pomo.mode === 1;
  if (s.enabled && s.health && !focusing && (v.idleState || 'active') === 'active' &&
      t - act.activeSince > SIT_LIMIT && t - (act.lastNag || 0) > SIT_LIMIT) {
    act.lastNag = t;
    const line = HEALTH_LINES[Math.floor(Math.random() * HEALTH_LINES.length)];
    notify('Clawd：休息一下吧', line);
    await event('alert', line, 8);
  }
  await set({ activity: act });
}

// ---------------------------------------------------------------- 工具
function notify(title, message, sticky) {
  try {
    chrome.notifications.create({ type: 'basic', iconUrl: 'icons/icon128.png', title, message, priority: 2, requireInteraction: !!sticky });
  } catch (e) { /* 通知被关掉了 */ }
}
chrome.notifications.onClicked.addListener(id => chrome.notifications.clear(id));

function event(type, text, dur, sticky) {
  return set({ clawdEvent: { id: newId(), type, text, dur: dur || 0, sticky: !!sticky, t: Date.now() } });
}
