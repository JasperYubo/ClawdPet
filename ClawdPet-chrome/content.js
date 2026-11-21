// Clawd 桌宠 · Chrome 扩展版：住在网页里的小橙块
// 像素小人、果冻弹簧、拖拽甩飞从 Windows 版移植；番茄钟 / 提醒 / 久坐检测在 background.js。
// 1.1：少说话、多动作。平时的反应都是头顶的小表情泡泡，只有提醒、番茄钟这类正事才用文字；
//      拎起来会手脚乱蹬；会坐、趴、打滚、转圈、做操、挠头、东张西望，还会跳到网页的标题 / 图片 / 按钮上；
//      可以喂零食、扔球陪它玩，偶尔有蝴蝶飞来，你打字、看视频、点网页时它也会凑过来看。
(() => {
  'use strict';
  if (window.top !== window || window.__clawdPetLoaded) return;
  if (!(document.documentElement instanceof HTMLElement)) return;   // XML / SVG 文档不放
  window.__clawdPetLoaded = true;

  // ================================================================ 扩展接口
  // 扩展被重新加载后，旧网页里的 chrome.* 会失效，所以全部包一层
  const ext = typeof chrome !== 'undefined' && chrome.runtime && chrome.runtime.id ? chrome : null;
  const alive = () => { try { return !!(ext && ext.runtime && ext.runtime.id); } catch (e) { return false; } };
  const sget = keys => new Promise(res => {
    if (!alive()) return res({});
    try { ext.storage.local.get(keys, v => { void ext.runtime.lastError; res(v || {}); }); } catch (e) { res({}); }
  });
  const sset = obj => { if (alive()) try { ext.storage.local.set(obj); } catch (e) { /* 扩展已失效 */ } };
  const send = msg => { if (alive()) try { ext.runtime.sendMessage(msg, () => void ext.runtime.lastError); } catch (e) { /* 扩展已失效 */ } };

  // ================================================================ 常量 / 像素素材
  const CW = 24, FOOT = 17;                       // 小人画布 24x18 格，脚底线在第 17 行
  const CAN_W = 60, CAN_H = 42, CAN_FOOT = 34;    // 实际 canvas（留出压扁拉长、打滚、表情泡泡的空间）
  const SPR_L = CAN_W / 2 - 12, SPR_T = CAN_FOOT - FOOT;
  const SIZES = [3, 4, 5, 7], SIZE_NAMES = ['小', '中', '大', '超大'];
  const DEF = { enabled: true, size: 1, follow: false, chatty: false, health: true, hiddenSites: [], count: 2 };
  const C = {
    body: '#D77757', eye: '#181614', blush: '#F7AC9C', heart: '#E9546C', z: '#96A6BE', note: '#7C8CFF',
    star: '#F6C448', bang: '#E5484D', drop: '#6CB4EE', puff: '#D6D0C4', crumb: '#B4673F', tomato: '#E2483C',
    leaf: '#5CAA50', fill: '#FAF9F5', border: '#3D3929', text: '#1F1E1D',
  };
  const HEART = ['.XX.XX.', 'XXXXXXX', 'XXXXXXX', '.XXXXX.', '..XXX..', '...X...'];
  const ZED = ['XXXXX', '...X.', '..X..', '.X...', 'XXXXX'];
  const NOTE = ['..XXX', '..X.X', '..X..', 'XXX..', 'XXX..'];
  const STAR = ['..X..', '..X..', 'XXXXX', '..X..', '..X..'];
  const BANG = ['XX', 'XX', 'XX', 'XX', '..', 'XX'];
  const DROP = ['.X.', '.X.', 'XXX', 'XXX', '.X.'];
  const PUFF = ['.XX.', 'XXXX', '.XX.'];
  const CRUMB = ['XX', 'XX'];
  const QMARK = ['.XXX.', 'X...X', '...X.', '..X..', '.....', '..X..'];
  const ANGER = ['XX.XX', 'X...X', '.....', 'X...X', 'XX.XX'];
  const BULB = ['.XXX.', 'XXXXX', 'XXXXX', '.XXX.', '.....', '.XXX.'];
  const TOMATO = ['...G...', '..GGG..', '.RRGRR.', 'RRRRRRR', 'RRRRRRR', 'RRRRRRR', '.RRRRR.'];
  const EMOTES = {
    heart: [HEART, C.heart], note: [NOTE, C.note], q: [QMARK, C.text], bang: [BANG, C.bang], sweat: [DROP, C.drop],
    star: [STAR, C.star], zz: [ZED, C.z], anger: [ANGER, C.bang], idea: [BULB, C.star],
  };

  // 零食 / 球 / 蝴蝶（每个字符 = 1 格，'.' 透明）
  const SNACKS = [
    { rows: ['.CCC.', 'CcCCC', 'CCCcC', 'CcCCC', '.CCC.'], pal: { C: '#D9A05B', c: '#6B3E1F' } },   // 饼干
    { rows: ['..L..', '.RLR.', 'RRRRR', 'RRRRR', '.RRR.'], pal: { R: '#E2483C', L: '#5CAA50' } },   // 苹果
    { rows: ['.PPP.', 'PPPPP', 'PP.PP', 'DDDDD', '.DDD.'], pal: { P: '#F08DB0', D: '#D9A05B' } },   // 甜甜圈
  ];
  const BALL_PAL = { R: '#E9546C', W: '#FAF9F5', B: '#6CB4EE', Y: '#F6C448' };
  const BALL_Q = ['R', 'W', 'B', 'Y'];
  const FLY = [['W...W', 'WWBWW', '.WBW.', 'WW.WW'], ['.W.W.', '.WBW.', '..B..', '.....']];
  const FLY_COLS = [C.note, C.star, C.heart, '#5CC8A8'];

  // 只有点「说句话」或者打开「偶尔自言自语」才会冒出来
  const LINES = ['嗯？', '今天也辛苦啦', '我在这儿陪你~', '眼睛累了就看看远处', '喝口水吧', '要不要起来走走？',
    '嘿嘿', '这个网页挺好看的', "You're absolutely right!", '我是 Clawd~', '有点无聊…陪我玩会儿？', '扔个球给我嘛'];

  const T = () => performance.now() / 1000;
  const rnd = Math.random;
  const pick = a => a[Math.floor(rnd() * a.length)];
  const clamp = (v, a, b) => (v < a ? a : v > b ? b : v);
  const sign = v => (v > 0 ? 1 : v < 0 ? -1 : 0);

  // 站着休息、可以随时被别的事打断的状态
  const REST = new Set(['idle', 'sit', 'lie', 'look', 'ponder', 'watch', 'type', 'ledge']);
  // 不贴着地面 / 网页元素的状态
  const AIR = new Set(['drag', 'fall', 'splat']);

  // ================================================================ 画小人（1 像素 = 1 格）
  const ARMS = {
    normal: [[-3, 5, 3, 2]],
    raised: [[-3, 4, 3, 2]],
    up: [[-3, 0, 2, 4], [-2, 3, 2, 2]],
    down: [[-2, 5, 2, 4]],
    out: [[-4, 4, 4, 2]],
    upOut: [[-4, 1, 2, 3], [-3, 3, 3, 2]],
    downOut: [[-3, 6, 3, 2], [-4, 7, 2, 3]],
  };
  const FLAIL = ['upOut', 'out', 'downOut', 'out'];

  function paintPose(g, p) {
    const bl = 4, bt = 4 + p.bodyDy, legTop = bt + 10;
    const cell = (col, x, y, w, hh) => { if (w > 0 && hh > 0) { g.fillStyle = col; g.fillRect(x, y, w, hh); } };
    const dots = (col, x, y, pts) => { for (let i = 0; i + 1 < pts.length; i += 2) cell(col, x + pts[i], y + pts[i + 1], 1, 1); };
    const LX = [5, 8, 14, 17];
    for (let i = 0; i < 4; i++) {
      const lx = LX[i], odd = (i & 1) === 1;
      let len = FOOT - legTop, dx = 0;
      switch (p.legs) {
        case 'walkA': if (odd) { len -= 1; dx = p.facing; } break;
        case 'walkB': if (!odd) { len -= 1; dx = p.facing; } break;
        case 'dangleA': len = odd ? 3 : 4; break;
        case 'dangleB': len = odd ? 4 : 3; break;
        case 'tucked': len = 0; break;
        case 'kick': if (i === (p.facing > 0 ? 3 : 0)) { len -= 2; dx = p.facing * 2; } break;
        case 'spread': if (i === 0) dx = -2; else if (i === 3) dx = 2; break;
        case 'hang': {
          // 悬空的腿：上段连着身体，下段跟着甩（两组腿各甩各的）
          const ddx = odd ? p.legDx2 : p.legDx, L = ((i + p.legPh) & 1) ? 3 : 4, mid = Math.trunc(ddx / 2);
          cell(C.body, lx, legTop, 2, 1);
          cell(C.body, lx + mid, legTop + 1, 2, 1);
          cell(C.body, lx + ddx, legTop + 2, 2, L - 2);
          continue;
        }
      }
      cell(C.body, lx + dx, legTop, 2, len);
    }
    for (const [right, arm] of [[false, p.armL], [true, p.armR]])
      for (const [x, y, w, hh] of ARMS[arm]) cell(C.body, right ? CW - (bl + x) - w : bl + x, bt + y, w, hh);
    cell(C.body, bl, bt, 16, 10);

    const ey = bt + 2 + p.lookY, el = 7 + p.lookX, er = 15 + p.lookX;
    switch (p.eyes) {
      case 'open': cell(C.eye, el, ey, 2, 3); cell(C.eye, er, ey, 2, 3); break;
      case 'blink': cell(C.eye, el, ey + 2, 2, 1); cell(C.eye, er, ey + 2, 2, 1); break;
      case 'closed': cell(C.eye, 6, bt + 4, 3, 1); cell(C.eye, 15, bt + 4, 3, 1); break;
      case 'happy': { const pts = [1, 0, 0, 1, 2, 1, 0, 2, 2, 2]; dots(C.eye, 6 + p.lookX, ey, pts); dots(C.eye, 15 + p.lookX, ey, pts); break; }
      case 'squint': dots(C.eye, 7, ey, [0, 0, 1, 1, 0, 2]); dots(C.eye, 15, ey, [1, 0, 0, 1, 1, 2]); break;
      case 'wide': cell(C.eye, el, ey - 1, 2, 4); cell(C.eye, er, ey - 1, 2, 4); break;
      case 'dizzy': { const pts = [0, 0, 2, 0, 1, 1, 0, 2, 2, 2]; dots(C.eye, 6, ey, pts); dots(C.eye, 15, ey, pts); break; }
      case 'focus': cell(C.eye, el, ey + 1, 2, 2); cell(C.eye, er, ey + 1, 2, 2); break;
      case 'heart': { const pts = [0, 0, 2, 0, 0, 1, 1, 1, 2, 1, 1, 2]; dots(C.eye, 6 + p.lookX, ey, pts); dots(C.eye, 15 + p.lookX, ey, pts); break; }
      case 'none': break;   // 转身时看到的是后脑勺
    }
    if (p.blush && p.eyes !== 'none') { cell(C.blush, 5, bt + 5, 2, 1); cell(C.blush, 17, bt + 5, 2, 1); }
    if (p.acc) {   // 第二只：头顶一根小芽，好分辨
      cell('#4E9A44', 12, bt - 2, 1, 2);
      cell('#6CC55A', 10, bt - 3, 2, 1); cell('#6CC55A', 13, bt - 4, 2, 1); cell('#6CC55A', 12, bt - 3, 1, 1);
    }
  }

  const spriteCache = new Map();
  const poseKey = p => [p.eyes, p.armL, p.armR, p.legs, p.lookX, p.lookY, p.bodyDy, p.facing, p.blush ? 1 : 0, p.legDx, p.legDx2, p.legPh, p.acc ? 1 : 0].join(',');
  function spriteFor(p, key) {
    let c = spriteCache.get(key);
    if (!c) {
      if (spriteCache.size > 400) spriteCache.clear();
      c = document.createElement('canvas');
      c.width = 24; c.height = 18;
      paintPose(c.getContext('2d'), p);
      spriteCache.set(key, c);
    }
    return c;
  }

  function glyph(g, rows, col, x, y, u) {
    g.fillStyle = col;
    for (let r = 0; r < rows.length; r++)
      for (let c = 0; c < rows[r].length; c++)
        if (rows[r][c] === 'X') g.fillRect(x + c * u, y + r * u, u, u);
  }

  // ================================================================ DOM（全部放进 closed shadow root，网页样式影响不到）
  function h(tag, props, ...kids) {
    const el = document.createElement(tag);
    if (props) for (const k in props) {
      if (k === 'class') el.className = props[k];
      else el.setAttribute(k, props[k]);
    }
    for (const c of kids.flat()) if (c != null && c !== false) el.append(c.nodeType ? c : String(c));
    return el;
  }

  const CSS = `
    .wrap { position: fixed; left: 0; top: 0; width: 0; height: 0;
      font-family: system-ui, -apple-system, "Segoe UI", Roboto, "Noto Sans SC", "Noto Sans CJK SC", "PingFang SC", "Microsoft YaHei UI", sans-serif;
      -webkit-font-smoothing: antialiased; }
    canvas { display: block; image-rendering: pixelated; }
    #pet, .obj { position: fixed; left: 0; top: 0; pointer-events: none; touch-action: none; cursor: grab;
      user-select: none; -webkit-user-select: none; -webkit-tap-highlight-color: transparent; will-change: transform; }
    #pet { z-index: 2; }
    .obj { z-index: 1; }
    .obj.fly { z-index: 3; cursor: default; }
    #pet.grab, .obj.grab { cursor: grabbing; }
    .pill { background: ${C.fill}; color: ${C.text};
      box-shadow: 0 calc(-1 * var(--p)) 0 0 ${C.border}, 0 var(--p) 0 0 ${C.border},
                  calc(-1 * var(--p)) 0 0 0 ${C.border}, var(--p) 0 0 0 ${C.border}; }
    .bubble { position: fixed; left: 0; top: 0; z-index: 4; display: none; pointer-events: none; will-change: transform; }
    .bubble .box { position: relative; box-sizing: border-box; max-width: min(250px, calc(100vw - 32px));
      padding: calc(var(--p) * 1.5) calc(var(--p) * 3); font-size: var(--fs); line-height: 1.4;
      white-space: pre-wrap; overflow-wrap: anywhere; transform-origin: var(--ox, 50%) 100%;
      animation: clawd-pop .22s cubic-bezier(.3, 1.6, .5, 1) both; }
    .bubble.out .box { animation: clawd-out .22s ease-in both; }
    @keyframes clawd-pop { from { transform: scale(.4); opacity: 0; } to { transform: none; opacity: 1; } }
    @keyframes clawd-out { from { opacity: 1; } to { transform: translateY(-4px); opacity: 0; } }
    .bubble .tail { position: absolute; top: 100%; left: 0; }
    .badge { position: fixed; left: 0; top: 0; z-index: 4; display: none; align-items: center; gap: calc(var(--p) * 1.5);
      padding: var(--p) calc(var(--p) * 2.5); font-size: calc(var(--fs) * 0.92); font-weight: 700;
      font-variant-numeric: tabular-nums; white-space: nowrap; pointer-events: none; will-change: transform; }
    .menu { position: fixed; left: 0; top: 0; z-index: 5; display: none; box-sizing: border-box; min-width: 224px; max-width: 300px;
      padding: 6px; background: #fff; color: ${C.text}; border: 2px solid ${C.border}; border-radius: 8px;
      box-shadow: 0 8px 28px rgba(0,0,0,.2); font-size: 13px; line-height: 1.35; pointer-events: auto;
      user-select: none; -webkit-user-select: none; text-align: left; }
    .menu .item { padding: 6px 10px; border-radius: 5px; cursor: pointer; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .menu .item:hover, .menu .chip:hover, .menu .btn:hover { background: #F3F1EA; }
    .menu .check::before { content: ''; display: inline-block; width: 1.3em; color: ${C.body}; font-weight: 700; }
    .menu .check.on::before { content: '✓'; }
    .menu .row { display: flex; align-items: center; flex-wrap: wrap; gap: 4px; padding: 5px 10px; }
    .menu .lab { color: #75736B; margin-right: 2px; }
    .menu .chip { padding: 2px 8px; border: 1px solid #DAD6CB; border-radius: 5px; cursor: pointer; }
    .menu .chip.on, .menu .chip.on:hover, .menu .btn.primary, .menu .btn.primary:hover { background: ${C.body}; border-color: ${C.body}; color: #fff; }
    .menu hr { border: 0; border-top: 1px solid #ECE9E0; margin: 4px; }
    .menu .title { padding: 4px 10px 2px; font-weight: 700; }
    .menu .field { display: block; padding: 5px 10px; color: #4A4843; }
    .menu input { display: block; box-sizing: border-box; width: 100%; margin: 4px 0 0; padding: 5px 8px; font: inherit;
      color: ${C.text}; background: #fff; border: 1px solid #CFCBBF; border-radius: 5px; outline: none;
      user-select: text; -webkit-user-select: text; }
    .menu input:focus { border-color: ${C.body}; }
    .menu .inline { display: flex; align-items: center; gap: 6px; }
    .menu .inline input { width: 90px; }
    .menu .btns { display: flex; justify-content: flex-end; gap: 6px; padding: 8px 10px 4px; }
    .menu .btn { padding: 4px 14px; border: 1px solid #CFCBBF; border-radius: 5px; cursor: pointer; }
    #pet.dreamy { cursor: pointer; }
    .obj.front { z-index: 3; }
    .bar { position: fixed; left: 0; top: 0; z-index: 4; display: none; pointer-events: auto; will-change: transform; touch-action: none;
      user-select: none; -webkit-user-select: none; }
    .bar.on { display: block; }
    .bar .bbox { display: flex; gap: 2px; padding: 2px; transform-origin: 50% 100%; animation: clawd-pop .18s cubic-bezier(.3, 1.6, .5, 1) both; }
    .bb { display: flex; align-items: center; justify-content: center; width: 28px; height: 28px; border-radius: 3px; cursor: pointer; }
    .bb:hover { background: #ECE8DC; }
    .bb:active { background: #E0DACB; }
    .bb canvas { display: block; image-rendering: pixelated; pointer-events: none; }
    .dreamBack { position: fixed; left: 0; top: 0; width: 100vw; height: 100vh; z-index: 6; display: none;
      background: rgba(10, 12, 32, .5); pointer-events: auto; touch-action: none; }
    .dream { position: fixed; left: 50%; top: 50%; z-index: 7; display: none; transform: translate(-50%, -50%);
      box-sizing: border-box; padding: 8px; background: #1B1E3A; color: #ECE9FB; border: 2px solid #0D0F22; border-radius: 10px;
      box-shadow: 0 12px 40px rgba(0,0,0,.45); font-size: 13px; line-height: 1.4; pointer-events: auto;
      user-select: none; -webkit-user-select: none; touch-action: none; }
    .dhead { display: flex; align-items: center; gap: 12px; margin: 0 2px 6px; }
    .dtitle { font-weight: 700; white-space: nowrap; }
    .dhud { flex: 1; text-align: right; white-space: nowrap; font-variant-numeric: tabular-nums; color: #CFCBF2; }
    .dhud b { color: #F0718A; font-weight: 400; letter-spacing: 1px; }
    .dx { cursor: pointer; padding: 0 6px; font-size: 20px; line-height: 1; color: #A9A5D6; }
    .dx:hover { color: #fff; }
    .dstage { position: relative; }
    .dcv { display: block; image-rendering: pixelated; border-radius: 4px; }
    .dmsg { position: absolute; inset: 0; display: none; flex-direction: column; align-items: center; justify-content: center; gap: 12px;
      background: rgba(16, 18, 44, .72); border-radius: 4px; text-align: center; padding: 10px; }
    .dmt { white-space: pre-wrap; font-size: 14px; line-height: 1.6; }
    .dmb { display: flex; gap: 10px; }
    .dpad { display: flex; align-items: center; gap: 8px; margin-top: 8px; }
    .dbtn { display: inline-flex; align-items: center; justify-content: center; min-width: 46px; height: 36px; padding: 0 12px;
      box-sizing: border-box; border-radius: 8px; background: #2D3264; color: #ECE9FB; cursor: pointer; touch-action: none; font-size: 15px; }
    .dbtn:hover { background: #3A4080; }
    .dbtn.on { background: #4A51A0; }
    .dbtn.big { background: ${C.body}; color: #fff; min-width: 76px; }
    .dbtn.big:hover, .dbtn.big.on { background: #E58A69; }
    .dpad .dbtn.big { margin-left: auto; }
    .dtip { color: #8F8BC0; font-size: 12px; }
  `;

  // ---------------------------------------------------------------- 一个网页只要一组 Clawd
  // 不小心把扩展装了两份时，两份的脚本互相看不见对方的变量，但能看见网页上的 <clawd-pet>。
  // 规则：版本新的留下；版本一样就留后来的那组（更新扩展后新放进来的那组）；输掉的那组自己退场。
  // 同一组里的几只（slot 0、slot 1）共用同一个版本号和出生时间，互相不会让位。
  const MY_VER = (() => { try { return (ext && ext.runtime.getManifest && ext.runtime.getManifest().version) || '0'; } catch (e) { return '0'; } })();
  const MY_BORN = Date.now(), MY_ID = ext ? String(ext.runtime.id) : '';
  function verCmp(a, b) {
    const pa = String(a).split('.').map(n => parseInt(n, 10) || 0), pb = String(b).split('.').map(n => parseInt(n, 10) || 0);
    for (let i = 0; i < Math.max(pa.length, pb.length); i++) if ((pa[i] || 0) !== (pb[i] || 0)) return (pa[i] || 0) - (pb[i] || 0);
    return 0;
  }

  // 同一个网页里的几只 Clawd 互相认识：都登记在这里
  const world = { pets: [], dreamOwner: null };

  // ================================================================ 生成一只 Clawd
  // slot 0 是原来那只；slot 1 是第二只（头上长一根小芽），设置里关掉「两只」时它就不出来
  function makeClawd(slot) {
    const host = document.createElement('clawd-pet');
    host.style.cssText = 'all:initial !important;position:fixed !important;left:0 !important;top:0 !important;' +
      'width:0 !important;height:0 !important;z-index:2147483647 !important;pointer-events:none !important;display:block !important;';

    host.setAttribute('data-clawd-v', MY_VER);
    host.setAttribute('data-clawd-t', String(MY_BORN));
    host.setAttribute('data-clawd-id', MY_ID);
    host.setAttribute('data-clawd-slot', String(slot));
    let retired = false;
    function rivalWins() {
      for (const el of document.querySelectorAll('clawd-pet')) {
        if (el === host || el.hasAttribute('data-clawd-retired')) continue;
        const c = verCmp(el.getAttribute('data-clawd-v') || '0', MY_VER);
        if (c > 0) return true;
        if (c < 0) continue;
        const t = +el.getAttribute('data-clawd-t') || 0;
        if (t !== MY_BORN) { if (t > MY_BORN) return true; continue; }
        if ((el.getAttribute('data-clawd-id') || '') < MY_ID) return true;
      }
      return false;
    }
    const root = host.attachShadow({ mode: 'closed' });
    try { const sh = new CSSStyleSheet(); sh.replaceSync(CSS); root.adoptedStyleSheets = [sh]; }
    catch (e) { root.append(h('style', null, CSS)); }

    const cvs = h('canvas', { id: 'pet' });
    const textEl = h('span'), tailEl = h('canvas', { class: 'tail' });
    const boxEl = h('div', { class: 'box pill' }, textEl, tailEl);
    const bubbleEl = h('div', { class: 'bubble' }, boxEl);
    const tomatoEl = h('canvas'), timeEl = h('span');
    const badgeEl = h('div', { class: 'badge pill' }, tomatoEl, timeEl);
    const menuEl = h('div', { class: 'menu' });
    // 鼠标停在它身上时，头顶冒出来的一排快捷按钮
    const BAR_ITEMS = [['feed', '喂一口'], ['ball', '扔个球'], ['sleep', '睡一会儿'], ['dreamGame', '做个梦（小游戏）'], ['more', '更多…']];
    const barBtns = {};
    const barBox = h('div', { class: 'bbox pill' }, BAR_ITEMS.map(([act, title]) => (barBtns[act] = h('span', { class: 'bb', 'data-act': act, title }, h('canvas')))));
    const barEl = h('div', { class: 'bar' }, barBox);
    const wrap = h('div', { class: 'wrap' }, cvs, bubbleEl, badgeEl, barEl, menuEl);
    root.append(wrap);
    const g = cvs.getContext('2d');

    // ================================================================ 状态
    let settings = Object.assign({}, DEF);
    let S = 4, dpr = 1, P = 2, PD = 2;
    let cx = 0, posY = 0, velX = 0, velY = 0, impact = 0;          // cx = 身体中心，posY = 脚底线（视口坐标）
    let sq = 0, sqV = 0, lean = 0, leanV = 0, anchorY = FOOT * 4, hop = 0;
    let dragVX = 0, dragVY = 0, shake = 0, shakeAcc = 0;
    let rot = 0, flip = 1, sitOff = 0, rollX0 = 0, rollDir = 1, curBodyDy = 0;
    let legA = 0, legAV = 0, legB = 0, legBV = 0, legPh = 0, armPh = 0;
    // 被拎着时像钟摆一样绕抓住的点晃（swing），腿一直蹬（kickPh）
    let swing = 0, swingV = 0, pivX = 0, prevDVX = 0, accX = 0, kickPh = 0;
    // 走路：有加速减速的地面速度，按走过的距离换脚
    let gvx = 0, walkPh = 0, lastBodyDy = 0, lastFacing = 1;
    let state = 'fall', stT = 0, stDur = 1e9, mood = 'plain';
    let pendingMood = 'plain', pendingJumpV = 0, pendingJumpVX = 0, pendingIgnore = false;
    let facing = 1, pendingDance = false, pendingDizzy = false, sneezed = false, alertSticky = false;
    // 睡觉：afk = 你离开了（你一回来就醒）；nap = 自己打个盹（睡够了自己醒）
    let sleepMode = 'afk', napDur = 0, nextNap = 0, sleepLedge = false, dozeNods = 0, jerkUntil = 0, twitchUntil = 0;
    let noseT0 = 0, noseLen = 6, noseInfo = null, nextTwitch = 0, nextDream = 0, lastDream = null, wakeLook = false;
    let afterLandSay = null, afterLandWave = false, afterLandThen = null, hopsLeft = 0, fellOff = false, aimJump = null;
    let lookX = 0, lookY = 0, nextBlink = 2, blinkUntil = 0, nextGlance = 0, attn = null, lookSeq = [];
    let lastCursor = { x: -1e4, y: -1e4 }, lastCursorMove = -100, cursorSpeed = 0, lastMoveT = 0, lastStartle = -1e9, lastReach = -100;
    let mouseDown = false, dragging = false, downId = -1, downPt = { x: 0, y: 0 }, downT = 0, grabDX = 0, grabDY = 0, grabbed = null;
    let trail = [], clicks = [], lastClickT = -10, petDist = 0, lastPetMove = 0, lastHover = null, blushUntil = 0, pets = [];
    let bubbleText = null, bubbleUntil = 0, bubbleW = 0, bubbleH = 0, lastBubblePos = '', bubbleHideTimer = 0;
    let emote = null;
    let badgeW = 0, lastBadgeText = '', lastBadgePos = '';
    let nextChatter = 0, nextFx = 0, nextSave = 0, nextFly = 0, nextFoodCheck = 0;
    let energy = 0.6, fedTimes = [];
    let perch = null, plats = [], platsT = -10, ignoreEl = null, pageBg = null;
    let walkTarget = null, walkSpeed = 0, walkThen = null;
    let chaseObj = null, kicksLeft = 0, kicked = false, lastBonk = -10;
    let typeT = -10, typeCount = 0, typeFlip = 0, typeAt = null, watchAt = null, peekSide = 1;
    let lastScrollT = -10, lastClickWalk = -100, lastSelReact = -100, selTimer = 0;
    const parts = [], objs = [];
    let idleState = 'active', pomo = null, reminderCount = 0, lastEventId = null;
    let hotEl = null, menuOpen = false, menuAt = { x: 0, y: 0 }, remindInputs = null;
    let barOn = false, hoverSince = -1, barKeepUntil = 0, barW = 0, barH = 0, barTop = 0, lastBarPos = '', feedQueue = 0;
    let rubDir = 0, rubDirY = 0, rubFlips = [], touchBarAt = 0;
    let dragHover = false, lastDragOverHit = -10;
    let hiddenNow = true, hiddenBySetting = false, started = false;
    let now = T(), lastFrameT = now, raf = 0;
    let lastKey = '', lastLeft = null, lastTop = null, lastParts = 0, lastEmote = false;

    // ---------------------------------------------------------------- 和另一只 Clawd 的关系
    // mate：正在一起做事的那只（贴贴 / 追着玩 / 被追）；别的时候各玩各的，但会互相看、互相反应
    let mate = null, nextSocial = 0, lastBump = -10, lastJealous = -100, tagBack = null;
    const api = {
      slot,
      info: () => ({ cx, posY, state, facing, grounded: grounded(), perchEl: perch ? perch.el : null, rest: REST.has(state),
        sleeping: state === 'sleep' || state === 'doze', hidden: hiddenNow || retired, S, energy, sitOff, velY }),
      react: (kind, from, x) => reactTo(kind, from, x),
      bonk: o => bonk(o),
      removeObj: o => removeObj(o),
      objs: () => objs,
    };
    world.pets[slot] = api;
    // 另一只（看得见的那只）
    function friend() { for (const p of world.pets) if (p && p !== api && !p.info().hidden) return p; return null; }
    // 两只共用的东西（比如球）：不管是谁扔出来的都能找到
    function worldFind(kind) {
      for (const p of world.pets) { if (!p) continue; const o = p.objs().find(q => q.kind === kind && !q.gone); if (o) return o; }
      return null;
    }
    // 两只在同一层：同一块地面或同一个网页元素上，高度也差不多
    function sameLevel(fi) {
      return fi.grounded && grounded() && fi.perchEl === (perch ? perch.el : null) && Math.abs((fi.posY - fi.sitOff) - (posY - sitOff)) < 6 * S;
    }

    const de = document.documentElement;
    const quirks = document.compatMode === 'BackCompat';
    function vw() { const w = de.clientWidth; return !quirks && w > 0 ? Math.min(w, window.innerWidth) : window.innerWidth; }
    function vh() { const hh = de.clientHeight; return !quirks && hh > 0 ? Math.min(hh, window.innerHeight) : window.innerHeight; }

    // ================================================================ 尺寸
    function drawTail() {
      tailEl.width = 4 * PD; tailEl.height = 3 * PD;
      tailEl.style.width = 4 * PD / dpr + 'px'; tailEl.style.height = 3 * PD / dpr + 'px';
      const t = tailEl.getContext('2d');
      t.clearRect(0, 0, tailEl.width, tailEl.height);
      const px = (col, x, y, w) => { t.fillStyle = col; t.fillRect(x * PD, y * PD, w * PD, PD); };
      px(C.fill, 0, 0, 4); px(C.border, 0, 1, 1); px(C.fill, 1, 1, 2); px(C.border, 3, 1, 1); px(C.border, 1, 2, 2);
    }
    function drawTomato() {
      tomatoEl.width = 7 * PD; tomatoEl.height = 7 * PD;
      tomatoEl.style.width = 7 * PD / dpr + 'px'; tomatoEl.style.height = 7 * PD / dpr + 'px';
      const t = tomatoEl.getContext('2d');
      t.clearRect(0, 0, tomatoEl.width, tomatoEl.height);
      for (let r = 0; r < TOMATO.length; r++)
        for (let c = 0; c < TOMATO[r].length; c++) {
          const ch = TOMATO[r][c];
          if (ch === '.') continue;
          t.fillStyle = ch === 'R' ? C.tomato : C.leaf;
          t.fillRect(c * PD, r * PD, PD, PD);
        }
    }

    function applySize() {
      S = SIZES[clamp(settings.size | 0, 0, SIZES.length - 1)];
      dpr = window.devicePixelRatio || 1;
      PD = Math.max(1, Math.round(Math.max(2, Math.round(S / 2)) * dpr));
      P = PD / dpr;
      cvs.width = Math.round(CAN_W * S * dpr); cvs.height = Math.round(CAN_H * S * dpr);
      cvs.style.width = cvs.width / dpr + 'px'; cvs.style.height = cvs.height / dpr + 'px';
      wrap.style.setProperty('--p', P + 'px');
      wrap.style.setProperty('--fs', Math.max(12, Math.round(S * 2.9)) + 'px');
      drawTail(); drawTomato(); drawBarIcons(); lastBarPos = '';
      if (barOn) { barW = barEl.offsetWidth; barH = barEl.offsetHeight; }
      for (const o of objs) sizeObj(o);
      anchorY = FOOT * S; lastKey = ''; lastLeft = lastTop = null;
      refreshBubble(false); refreshBadge(true);
    }

    // ================================================================ 文字气泡（只给正事用）/ 番茄钟牌子 / 头顶表情
    function say(text, secs) {
      bubbleText = text; bubbleUntil = now + secs;
      clearTimeout(bubbleHideTimer);
      bubbleEl.classList.remove('out');
      refreshBubble(true);
    }
    function clearBubble() {
      if (bubbleText == null) return;
      bubbleText = null;
      bubbleEl.classList.add('out');
      clearTimeout(bubbleHideTimer);
      bubbleHideTimer = setTimeout(() => { if (bubbleText == null) { bubbleEl.style.display = 'none'; bubbleEl.classList.remove('out'); } }, 240);
    }
    function refreshBubble(pop) {
      if (bubbleText == null) { if (!bubbleEl.classList.contains('out')) bubbleEl.style.display = 'none'; return; }
      if (hiddenNow) { bubbleEl.style.display = 'none'; return; }
      textEl.textContent = bubbleText;
      bubbleEl.style.display = 'block';
      if (pop) { boxEl.style.animation = 'none'; void boxEl.offsetWidth; boxEl.style.animation = ''; }
      bubbleW = boxEl.offsetWidth; bubbleH = boxEl.offsetHeight;
      lastBubblePos = '';
    }
    function refreshBadge(force) {
      if (!pomo || hiddenNow || slot > 0) {   // 番茄钟牌子只挂在第一只身上
        if (badgeEl.style.display !== 'none') badgeEl.style.display = 'none';
        lastBadgeText = '';
        return;
      }
      const secs = Math.max(0, Math.ceil((pomo.end - Date.now()) / 1000));
      const t = (pomo.mode === 2 ? '休 ' : '') + String(Math.floor(secs / 60)).padStart(2, '0') + ':' + String(secs % 60).padStart(2, '0');
      if (t !== lastBadgeText || force) {
        lastBadgeText = t; timeEl.textContent = t;
        badgeEl.style.display = 'flex';
        badgeW = badgeEl.offsetWidth; lastBadgePos = '';
      }
    }
    function emo(kind, secs) { emote = { kind, born: now, until: now + secs }; }

    // ---------------------------------------------------------------- 快捷按钮条
    const BAR_ICONS = {
      feed: () => [SNACKS[0].rows, SNACKS[0].pal],
      ball: () => [ballRows(0), BALL_PAL],
      sleep: () => [ZED, { X: '#7F8FB0' }],
      dreamGame: () => [['..XXX..', '.XXXXX.', 'XXXXXXX', '.XXXXX.'], { X: '#8F86D0' }],
      more: () => [['X.X.X'], { X: C.text }],
    };
    function drawBarIcons() {
      const u = Math.max(1, Math.round(3 * dpr));
      for (const [act] of BAR_ITEMS) {
        const [rows, pal] = BAR_ICONS[act](), c = barBtns[act].firstChild;
        c.width = rows[0].length * u; c.height = rows.length * u;
        c.style.width = c.width / dpr + 'px'; c.style.height = c.height / dpr + 'px';
        const t = c.getContext('2d');
        for (let r = 0; r < rows.length; r++)
          for (let q = 0; q < rows[r].length; q++) {
            const ch = rows[r][q];
            if (ch === '.' || !pal[ch]) continue;
            t.fillStyle = pal[ch]; t.fillRect(q * u, r * u, u, u);
          }
      }
    }
    function showBar(secs) {
      if (dream || hiddenNow || menuOpen || dragging) return;
      barKeepUntil = Math.max(barKeepUntil, now + (secs || 0.8));
      barBtns.sleep.title = state === 'sleep' || state === 'doze' ? '叫醒它' : '睡一会儿';
      barBtns.ball.title = worldFind('ball') ? '收起球' : '扔个球';
      if (barOn) return;
      barOn = true;
      barEl.classList.add('on');
      barW = barEl.offsetWidth; barH = barEl.offsetHeight; lastBarPos = '';
    }
    function hideBar() { if (!barOn) return; barOn = false; barEl.classList.remove('on'); }
    function inBar(x, y) {
      if (!barOn) return false;
      const r = barEl.getBoundingClientRect();
      return x >= r.left - 4 && x <= r.right + 4 && y >= r.top - 4 && y <= r.bottom + 6;
    }
    function barAction(act) {
      barKeepUntil = now + 1.5;
      switch (act) {
        case 'feed': feedNow(); break;                 // 按钮条留着，可以接着喂
        case 'ball': hideBar(); toggleBall(); break;
        case 'sleep': hideBar(); if (state === 'sleep' || state === 'doze') wake(false); else if (grounded()) startYawn('nap', 90 + rnd() * 60); break;
        case 'dreamGame': hideBar(); openDream(); break;
        case 'more': { const r = barEl.getBoundingClientRect(); hideBar(); openMenu(r.left, r.bottom); break; }
      }
    }
    barEl.addEventListener('pointerup', e => { const b = e.target.closest('[data-act]'); if (b) { now = T(); barAction(b.getAttribute('data-act')); } });
    barEl.addEventListener('pointerdown', e => e.preventDefault());
    for (const t of ['pointerdown', 'pointerup', 'mousedown', 'mouseup', 'click', 'dblclick', 'contextmenu', 'touchstart'])
      barEl.addEventListener(t, e => e.stopPropagation());

    function layoutOverlay() {
      const W = vw();
      if (barOn) {
        const bx = Math.round(clamp(cx - barW / 2, 4, Math.max(4, W - barW - 4)));
        barTop = Math.max(4, Math.round(posY - (14 - curBodyDy) * S - 5 * S - barH));
        const key = bx + ',' + barTop;
        if (key !== lastBarPos) { lastBarPos = key; barEl.style.transform = `translate(${bx}px,${barTop}px)`; }
      }
      if (bubbleText != null) {
        const bx = Math.round(clamp(cx - bubbleW / 2, 4 + P, Math.max(4 + P, W - bubbleW - 4 - P)));
        let by = Math.round((barOn ? barTop : posY - (14 - curBodyDy) * S) - 3 * P - bubbleH);
        if (by < 4 + P) by = Math.round(4 + P);
        const tx = Math.round(clamp(cx - bx - 2 * P, 2 * P, Math.max(2 * P, bubbleW - 6 * P)));
        const key = bx + ',' + by + ',' + tx;
        if (key !== lastBubblePos) {
          lastBubblePos = key;
          bubbleEl.style.transform = `translate(${bx}px,${by}px)`;
          tailEl.style.left = tx + 'px';
          boxEl.style.setProperty('--ox', tx + 2 * P + 'px');
        }
      }
      if (pomo && lastBadgeText) {
        let bx = cx + 11 * S + 2;
        if (bx + badgeW > W - 4 - P) bx = cx - 11 * S - 2 - badgeW;
        const by = posY - 12 * S;
        const key = Math.round(bx) + ',' + Math.round(by);
        if (key !== lastBadgePos) { lastBadgePos = key; badgeEl.style.transform = `translate(${Math.round(bx)}px,${Math.round(by)}px)`; }
      }
    }

    // ================================================================ 粒子
    function spawn(shape, color, x, y, vx, vy, life, grav) {
      if (parts.length < 60) parts.push({ shape, color, x, y, vx, vy, life, grav });
    }
    const spawnHeart = () => spawn(HEART, C.heart, (9 + Math.floor(rnd() * 5)) * S, 1 * S, (rnd() - 0.5) * S * 4, -S * 9, 1.1, 0);
    const spawnZ = () => spawn(ZED, C.z, 18 * S, 5 * S, S * 3, -S * 5, 2.2, 0);
    const spawnNote = () => { const l = rnd() < 0.5; spawn(NOTE, C.note, (l ? 0 : 21) * S, 5 * S, (l ? -1 : 1) * S * 3, -S * 7, 1.3, 0); };
    const spawnBang = () => spawn(BANG, C.bang, 21 * S, 0, 0, -S * 2, 1.0, 0);
    const spawnSweat = () => spawn(DROP, C.drop, 20 * S, 3 * S, S * 3, -S * 2, 0.9, S * 30);
    const spawnSparkle = () => spawn(STAR, C.star, Math.floor(rnd() * 22) * S, (Math.floor(rnd() * 8) - 2) * S, (rnd() - 0.5) * S * 6, -S * 6, 1.0, 0);
    const spawnCrumb = () => spawn(CRUMB, C.crumb, (10 + Math.floor(rnd() * 5)) * S, 11 * S, (rnd() - 0.5) * S * 10, -S * 5, 0.7, S * 60);
    function spawnDust() {
      spawn(PUFF, C.puff, 1 * S, 15 * S, -S * 7, -S * 1.5, 0.45, 0);
      spawn(PUFF, C.puff, 20 * S, 15 * S, S * 7, -S * 1.5, 0.45, 0);
    }
    function spawnOrbitStar() {
      const a = now * 6;
      spawn(STAR, C.star, 11 * S + Math.cos(a) * 9 * S, 1 * S + Math.sin(a) * 1.5 * S, 0, 0, 0.36, 0);
    }
    function updateParticles(dt) {
      for (let i = parts.length - 1; i >= 0; i--) {
        const q = parts[i];
        q.life -= dt; q.vy += q.grav * dt; q.x += q.vx * dt; q.y += q.vy * dt;
        if (q.life <= 0) parts.splice(i, 1);
      }
    }

    // ================================================================ 网页元素当台子：标题、图片、按钮、卡片……
    const PLAT_SEL = 'h1,h2,h3,h4,img,video,picture,svg,canvas,iframe,button,input,textarea,select,pre,blockquote,table,figure,[role="button"],[role="img"]';
    const TEXTY = new Set(['H1', 'H2', 'H3', 'H4']);

    function alphaOf(c) {
      if (!c || c === 'transparent') return 0;
      let m = /rgba?\(([^)]+)\)/.exec(c);
      if (m) { const p = m[1].split(/[\s,/]+/).filter(Boolean); return p.length > 3 ? parseFloat(p[3]) : 1; }
      m = /\/\s*([\d.]+)(%?)\s*\)/.exec(c);
      if (m) return m[2] ? parseFloat(m[1]) / 100 : parseFloat(m[1]);
      return 1;
    }
    function getPageBg() {
      if (pageBg != null) return pageBg;
      for (const el of [document.body, de]) {
        if (!el) continue;
        const bg = getComputedStyle(el).backgroundColor;
        if (alphaOf(bg) > 0.3) return (pageBg = bg);
      }
      return (pageBg = 'rgb(255, 255, 255)');
    }
    // 看起来像一块「板子」：有边框、阴影或者和网页底色不一样的背景
    function boxy(el) {
      const cs = getComputedStyle(el);
      if (cs.visibility === 'hidden' || parseFloat(cs.opacity) < 0.3) return false;
      if (cs.boxShadow && cs.boxShadow !== 'none') return true;
      if (parseFloat(cs.borderTopWidth) >= 1 && cs.borderTopStyle !== 'none' && alphaOf(cs.borderTopColor) > 0.25) return true;
      if (alphaOf(cs.backgroundColor) > 0.3 && cs.backgroundColor !== getPageBg()) return true;
      return !!cs.backgroundImage && cs.backgroundImage !== 'none';
    }

    // 量一下台子的顶边 → { l, r, t }（视口坐标）
    function measure(pp) {
      const el = pp.el;
      if (!el.isConnected) return null;
      if (pp.kind === 'text') {
        // 标题按第一行文字的宽度算，不然会站在空气上
        let rects;
        try { rects = pp.range.getClientRects(); } catch (e) { return null; }
        let first = null, l = 1e9, r = -1e9;
        for (const q of rects) {
          if (q.width < 1 || q.height < 1) continue;
          if (!first) first = q;
          if (Math.abs(q.top - first.top) > 3) continue;
          l = Math.min(l, q.left); r = Math.max(r, q.right);
        }
        if (!first) return null;
        return { l, r, t: first.top + pp.fs * 0.14 };
      }
      const q = el.getBoundingClientRect();
      if (q.width < 1 || q.height < 1) return null;
      return { l: q.left, r: q.right, t: q.top };
    }

    function platFor(el, W, H) {
      if (!el || el === host || el === de || el === document.body) return null;
      const pp = { el, kind: TEXTY.has(el.tagName) ? 'text' : 'box' };
      if (pp.kind === 'text') {
        pp.range = document.createRange(); pp.range.selectNodeContents(el);
        pp.fs = parseFloat(getComputedStyle(el).fontSize) || 16;
      }
      const m = measure(pp);
      if (!m) return null;
      const visL = Math.max(m.l, 0), visR = Math.min(m.r, W);
      if (visR - visL < 22 * S) return null;                     // 太窄站不下
      if (m.t < 22 * S || m.t > H - 18 * S) return null;         // 头顶要有地方；离地面太近也没意思
      // 顶边真的露在外面（没被浮层、吸顶栏盖住）
      const hit = document.elementFromPoint((visL + visR) / 2, m.t + 3);
      if (!hit || (hit !== el && !el.contains(hit))) return null;
      return Object.assign(pp, m);
    }

    // 在视口里撒一圈探针，找能站的元素（只看屏幕上的，不管网页多长都很快）
    function scanPlats() {
      platsT = now;
      const W = vw(), H = vh(), seen = new Set(), walked = new Set(), out = [];
      const add = el => { if (!el || seen.has(el)) return; seen.add(el); const p = platFor(el, W, H); if (p) out.push(p); };
      const cols = 9, rows = 7, span = Math.max(1, H - 40 * S);
      for (let i = 0; i < cols && out.length < 30; i++)
        for (let j = 0; j < rows; j++) {
          const hit = document.elementFromPoint((i + 0.5) / cols * W, 22 * S + (j + 0.5) / rows * span);
          if (!hit || hit === host) continue;
          const m = hit.closest(PLAT_SEL);
          if (m) { add(m); continue; }
          let el = hit;
          for (let d = 0; el && el !== document.body && el !== de && d < 8; d++, el = el.parentElement) {
            if (walked.has(el)) break;
            walked.add(el);
            const q = el.getBoundingClientRect();
            if (q.width < 26 * S || q.width > W * 0.96 || q.height < 10 * S) continue;
            if (boxy(el)) { add(el); break; }
          }
        }
      plats = out;
    }

    function setPerch(pp) {
      if (!pp) { perch = null; return; }
      perch = { el: pp.el, kind: pp.kind, range: pp.range, fs: pp.fs, off: 0, lastT: null, m: null };
      const m = measure(perch);
      if (m) { perch.m = m; perch.off = cx - m.l; perch.lastT = m.t; }
    }
    // 从台子上起跳时，记下它，落下来还能踩回去
    function keepPerchAsPlat() {
      if (!perch) return;
      const m = measure(perch);
      if (!m) return;
      const ex = plats.find(p => p.el === perch.el);
      if (ex) Object.assign(ex, m);
      else plats.push(Object.assign({ el: perch.el, kind: perch.kind, range: perch.range, fs: perch.fs }, m));
    }
    function moveX(d) { cx += d; if (perch) perch.off += d; }
    function surfaceSpan() {   // 当前脚下这块地方，左右能走到哪
      const W = vw();
      let lo = 11 * S, hi = W - 11 * S;
      if (perch && perch.m) { lo = Math.max(lo, perch.m.l + 4 * S); hi = Math.min(hi, perch.m.r - 4 * S); }
      if (hi < lo) { const mid = (lo + hi) / 2; lo = hi = mid; }
      return [lo, hi];
    }

    // ================================================================ 小物件：零食、球、蝴蝶
    function makeObj(kind, x, y, extra) {
      const el = h('canvas', { class: 'obj' + (kind === 'fly' ? ' fly' : '') });
      wrap.insertBefore(el, bubbleEl);
      const o = Object.assign({ kind, x, y, vx: 0, vy: 0, el, w: 5, hgt: 5, scale: 1, key: '', lastPos: '',
        born: now, touched: now, rest: false, held: false, gone: false, owner: api }, extra);
      sizeObj(o);
      objs.push(o);
      return o;
    }
    function sizeObj(o) {
      o.u = Math.max(1, Math.round(S * dpr * o.scale));
      o.el.width = o.w * o.u; o.el.height = o.hgt * o.u;
      o.el.style.width = o.el.width / dpr + 'px'; o.el.style.height = o.el.height / dpr + 'px';
      o.key = ''; o.lastPos = '';
    }
    function removeObj(o) {
      if (o.gone) return;
      o.gone = true; o.held = false;
      o.el.remove();
      const i = objs.indexOf(o);
      if (i >= 0) objs.splice(i, 1);
      if (chaseObj === o) chaseObj = null;
      if (grabbed === o) grabbed = null;
      if (hotEl === o.el) hotEl = null;
    }
    const findObj = kind => objs.find(o => o.kind === kind && !o.gone);
    const objW = o => o.w * o.u / dpr, objH = o => o.hgt * o.u / dpr;

    function ballRows(k) {
      const q = i => BALL_Q[(i + k + 400) & 3];
      const a = q(0), b = q(1), c = q(2), d = q(3);   // 左上 右上 右下 左下
      return ['.' + a + a + b + b + '.', a + a + a + b + b + b, a + a + a + b + b + b,
        d + d + d + c + c + c, d + d + d + c + c + c, '.' + d + d + c + c + '.'];
    }
    function objLook(o) {
      if (o.kind === 'ball') { const k = Math.floor(o.spin) & 3; return { rows: ballRows(k), pal: BALL_PAL, key: 'b' + k }; }
      if (o.kind === 'snack') {
        const b = o.bite | 0, w = o.w;
        const rows = o.snack.rows.map((row, r) => row.split('').map((ch, c) => (b && c >= w - b * 2 + (r & 1)) ? '.' : ch).join(''));
        return { rows, pal: o.snack.pal, key: 's' + b };
      }
      const f = o.onHead ? (Math.floor(now / 0.6) & 1) : (Math.floor(o.flap) & 1);
      return { rows: FLY[f], pal: { W: o.col, B: C.border }, key: 'f' + f };
    }
    function drawObj(o) {
      const lk = objLook(o);
      if (lk.key !== o.key) {
        o.key = lk.key;
        const t = o.el.getContext('2d'), u = o.u;
        t.clearRect(0, 0, o.el.width, o.el.height);
        for (let r = 0; r < lk.rows.length; r++)
          for (let c = 0; c < lk.rows[r].length; c++) {
            const ch = lk.rows[r][c];
            if (ch === '.') continue;
            t.fillStyle = lk.pal[ch];
            t.fillRect(c * u, r * u, u, u);
          }
      }
      const left = Math.round((o.x - objW(o) / 2) * dpr) / dpr, top = Math.round((o.y - objH(o)) * dpr) / dpr;
      const pk = left + ',' + top;
      if (pk !== o.lastPos) { o.lastPos = pk; o.el.style.transform = `translate(${left}px,${top}px)`; }
    }

    function updBall(o, dt) {
      if (o.held) return;
      const W = vw(), H = vh(), r = objW(o) / 2;
      o.vy = Math.min(o.vy + S * 520 * dt, S * 900);
      o.x += o.vx * dt; o.y += o.vy * dt;
      if (o.x - r < 0) { o.x = r; o.vx = Math.abs(o.vx) * 0.7; }
      if (o.x + r > W) { o.x = W - r; o.vx = -Math.abs(o.vx) * 0.7; }
      if (o.y - 2 * r < 0 && o.vy < 0) { o.y = 2 * r; o.vy = -o.vy * 0.5; }
      if (o.y >= H) {
        o.y = H;
        if (o.vy > S * 40) { o.vy = -o.vy * 0.62; o.vx *= 0.92; }
        else { o.vy = 0; o.vx *= Math.max(0, 1 - 3.2 * dt); if (Math.abs(o.vx) < S * 2) o.vx = 0; }
      }
      o.spin -= (o.vx * dt) / (4.7 * S);
      o.rest = o.vx === 0 && o.vy === 0 && o.y >= H;
      for (const p of world.pets) if (p && !p.info().hidden) p.bonk(o);   // 两只都能撞到球
      if (now - o.touched > 600 && o.rest) removeObj(o);    // 十分钟没人玩就收起来
    }

    // 球撞到小人：弹开；小人跳起来时顶到球 = 头球
    function bonk(o) {
      if (hiddenNow || state === 'drag' || now - lastBonk < 0.2) return;
      const pr = 9 * S, py = posY - 8 * S, br = objW(o) / 2, by = o.y - br;
      const dx = o.x - cx, dy = by - py, d = Math.hypot(dx, dy);
      if (d > pr + br || d < 1) return;
      // 正在追这个球：先用身子把球停住，再踢出去（不然球一碰到就被弹走，永远踢不到）
      if (state === 'chase' && chaseObj === o && grounded() && !o.held) {
        o.vx *= 0.2; if (o.vy < 0) o.vy *= 0.3;
        o.x = cx + sign(dx || 1) * (pr + br) * 0.8;
        lastBonk = now; o.touched = now; o.rest = false;
        startKick(o); return;
      }
      const nx = dx / d, ny = dy / d;
      o.x = cx + nx * (pr + br); o.y = py + ny * (pr + br) + br;
      const air = state === 'fall';
      const pvx = air ? velX : 0, pvy = air ? velY : 0;
      const rel = (o.vx - pvx) * nx + (o.vy - pvy) * ny;
      if (rel < 0) { o.vx -= 1.6 * rel * nx; o.vy -= 1.6 * rel * ny; }
      if (air && velY < 0 && ny < -0.3) { o.vy = Math.min(o.vy, velY * 1.1 - S * 80); o.vx += dx * 2.5; spawnSparkle(); }
      lastBonk = now; o.touched = now; o.rest = false;
      if (Math.abs(rel) > S * 120 && grounded()) {
        if (state === 'sleep' || state === 'doze') { wake(true); return; }   // 睡着被球砸醒
        sqV -= 3; leanV -= sign(dx) * 2;
        if (state !== 'chase' && state !== 'kick' && state !== 'sleep' && state !== 'eat') { spawnBang(); startChase(o, 2 + Math.floor(rnd() * 3)); }
      }
    }

    function updSnack(o, dt) {
      if (o.hand) {
        if (state === 'eat' && chaseObj === o) {   // 拿在嘴边，跟着嚼的节奏一抖一抖
          o.x = cx + facing * S; o.y = posY - (3 + curBodyDy) * S + Math.round(Math.sin(now * 22)) * 0.5 * S;
          return;
        }
        o.hand = false; o.el.classList.remove('front');   // 吃到一半被打断：零食掉下去
      }
      if (o.eating && !(state === 'eat' && chaseObj === o)) o.eating = false;
      if (o.held || o.eating) return;
      const W = vw(), H = vh(), hw = objW(o) / 2;
      o.vy = Math.min(o.vy + S * 520 * dt, S * 800);
      o.x += o.vx * dt; o.y += o.vy * dt;
      if (o.x < hw) { o.x = hw; o.vx = Math.abs(o.vx) * 0.4; }
      if (o.x > W - hw) { o.x = W - hw; o.vx = -Math.abs(o.vx) * 0.4; }
      if (o.y >= H) {
        o.y = H;
        if (o.vy > S * 60) { o.vy = -o.vy * 0.25; o.vx *= 0.6; }
        else { o.vy = 0; o.vx *= Math.max(0, 1 - 6 * dt); if (Math.abs(o.vx) < S) o.vx = 0; }
      }
      o.rest = o.vy === 0 && o.vx === 0 && o.y >= H;
    }

    function headTopY() { return posY - (13 - curBodyDy) * S * (1 + sq) - hop; }
    function updFly(o, dt) {
      const W = vw(), H = vh(), age = now - o.born;
      o.flap += dt * 12;
      if (o.onHead) {
        o.x = cx + o.headDx; o.y = headTopY() + 0.5 * S;
        const still = REST.has(state) || state === 'sleep' || state === 'doze' || state === 'happy';
        if (now > o.headUntil || !still || hiddenNow) {
          o.onHead = false; o.vy = -S * 70; o.vx = (rnd() - 0.5) * S * 60; o.nextT = 0; o.noLandUntil = now + 8;
        }
        return;
      }
      const leaving = age > o.life;
      const restful = REST.has(state) || state === 'sleep' || state === 'doze';
      if (o.landing && (!restful || leaving)) { o.landing = false; o.nextT = 0; }
      if (o.landing) { o.tx = cx + o.headDx; o.ty = headTopY() - 1 * S; }   // 慢慢飞向它的头顶
      else if (now > o.nextT) {
        o.nextT = now + 1.2 + rnd() * 2;
        if (leaving) { o.tx = o.x < W / 2 ? -60 : W + 60; o.ty = H * 0.2 * rnd(); }
        else if (restful && age > 5 && now > (o.noLandUntil || 0) && rnd() < 0.4) { o.landing = true; o.headDx = (rnd() - 0.5) * 8 * S; }
        else {
          o.tx = clamp(cx + (rnd() - 0.5) * 70 * S, 20, W - 20);
          o.ty = clamp(posY - (22 + rnd() * 30) * S, 20, H - 20 * S);
        }
      }
      const max = S * 55;
      o.vx += ((o.tx - o.x) * 1.4 - o.vx * 0.9) * dt;
      o.vy += ((o.ty - o.y) * 1.4 - o.vy * 0.9) * dt + Math.sin(now * 9 + o.ph) * S * 90 * dt;
      const sp = Math.hypot(o.vx, o.vy);
      if (sp > max) { o.vx *= max / sp; o.vy *= max / sp; }
      o.x += o.vx * dt; o.y += o.vy * dt;
      if (leaving && (o.x < -40 || o.x > W + 40)) { removeObj(o); return; }
      // 小人安安静静的时候，蝴蝶可能落到它头上
      if (o.landing) {
        if (Math.hypot(o.x - (cx + o.headDx), o.y - headTopY()) < 4 * S) {
          o.landing = false; o.onHead = true; o.headUntil = now + 4 + rnd() * 5; o.vx = o.vy = 0;
          if (state !== 'sleep') { emo('heart', 1.6); blushUntil = now + 3; stDur = Math.max(stDur, stT + (o.headUntil - now)); }
        }
      }
    }
    function flyDodge(o) { o.vy = -S * 90; o.vx = (o.x >= cx ? 1 : -1) * S * 60; o.ty = o.y - 25 * S; o.tx = o.x + o.vx; o.nextT = now + 1.2; o.noLandUntil = now + 6; }

    function updObjs(dt) {
      for (let i = objs.length - 1; i >= 0; i--) {
        const o = objs[i];
        if (o.kind === 'ball') updBall(o, dt);
        else if (o.kind === 'snack') updSnack(o, dt);
        else updFly(o, dt);
        if (!o.gone) drawObj(o);
      }
    }

    function spawnFly() {
      const W = vw(), H = vh(), side = rnd() < 0.5 ? -1 : 1;
      const o = makeObj('fly', side < 0 ? -10 : W + 10, H * (0.25 + rnd() * 0.35),
        { w: 5, hgt: 4, scale: 0.75, col: pick(FLY_COLS), life: 30 + rnd() * 25, flap: 0, ph: rnd() * 6, tx: W / 2, ty: H / 2, nextT: 0, onHead: false });
      o.vx = -side * S * 30;
      return o;
    }
    // 喂一口：零食直接递到它嘴边，马上开吃；吃的时候再点就排队，一口接一口
    function feedNow() {
      if (!grounded()) return false;
      fedTimes = fedTimes.filter(t => now - t < 300);
      if (state === 'eat' && chaseObj && chaseObj.hand) { feedQueue = Math.min(3, feedQueue + 1); return true; }
      if (fedTimes.length >= 6) { resetPlans(); setState('refuse', 0.9); emo('sweat', 1.3); return false; }   // 吃不下了，摇头
      if (state === 'sleep' || state === 'doze') sqV += 6;     // 睡着也闻得到，一下子醒了
      const o = makeObj('snack', cx + facing * S, posY - 3 * S, { w: 5, hgt: 5, snack: pick(SNACKS), bite: 0, hand: true });
      o.el.classList.add('front');
      startEat(o);
      const f = friend(); if (f) f.react('beg', api);   // 另一只跑过来眼巴巴看着
      return true;
    }
    function toggleBall() {
      const old = worldFind('ball');   // 两只共用一个球
      if (old) { old.owner.removeObj(old); return null; }
      const dir = cx > vw() / 2 ? -1 : 1;
      const b = makeObj('ball', cx + dir * 12 * S, Math.max(12 * S, posY - 26 * S), { w: 6, hgt: 6, spin: 0 });
      b.vx = dir * S * (40 + rnd() * 30); b.vy = -S * 100;
      if (grounded() && state !== 'sleep') { emo('bang', 0.9); startChase(b, 3 + Math.floor(rnd() * 4)); }
      return b;
    }

    // ================================================================ 状态机
    const grounded = () => !AIR.has(state) && state !== 'land' && state !== 'crouch';
    function setState(s, dur) {
      if (state === 'sleep' && s !== 'sleep') { popNose(); if (emote && emote.kind === 'dream') emote = null; }
      if (s !== 'spin') flip = 1;
      if (s !== 'roll') rot = 0;
      if (emote && emote.kind === 'dots' && s !== 'ponder') emote = null;
      state = s; stT = 0; stDur = dur;
    }
    function resetPlans() {
      if (chaseObj && chaseObj.eating) chaseObj.eating = false;
      chaseObj = null; walkTarget = null; walkThen = null; walkSpeed = 0; afterLandThen = null; hopsLeft = 0; aimJump = null;
      mate = null; tagBack = null;
    }
    function startIdle() { setState('idle', 1 + rnd() * 2.2); }
    function startWalk(dir, dur, speed) { facing = dir; walkTarget = null; walkThen = null; walkSpeed = speed || S * 9; setState('walk', dur); }
    function walkTo(x, speed, then) {
      const [lo, hi] = surfaceSpan();
      walkTarget = clamp(x, lo, hi); walkSpeed = speed || S * 10; walkThen = then || null;
      facing = walkTarget >= cx ? 1 : -1;
      setState('walk', 30);
    }
    function arrive() {
      const f = walkThen;
      walkTarget = null; walkThen = null; walkSpeed = 0;
      if (f) f(); else startIdle();
    }
    function startSit() { if (perch && rnd() < 0.55) startLedge(); else setState('sit', 4 + rnd() * 7); }
    function startLedge() { setState('ledge', 5 + rnd() * 9); }
    function startLie(d) { setState('lie', d || 6 + rnd() * 8); }
    function startLook() {
      lookSeq = [];
      let t = 0;
      for (let i = 0; i < 3 + Math.floor(rnd() * 3); i++) { t += 0.5 + rnd() * 0.6; lookSeq.push([t, pick([-1, 1, 0]), pick([0, 0, -1, 1])]); }
      setState('look', t + 0.4);
    }
    function startPonder() { setState('ponder', 3 + rnd() * 2.5); emo('dots', stDur); }
    function startHops() { hopsLeft = 1 + Math.floor(rnd() * 3); jump('happy', S * (38 + rnd() * 14), true); }
    function startSpin() { setState('spin', rnd() < 0.5 ? 0.9 : 1.35); }
    function startExercise() { setState('exercise', 2.6 + rnd() * 2); }
    function startStretch() { setState('stretch', 1.1); }
    function startScratch() { setState('scratch', 1.2 + rnd() * 0.6); if (rnd() < 0.4) emo('q', 1.2); }
    function startTeeter() { setState('teeter', 1.4 + rnd() * 0.6); }
    function startPeek(side) { peekSide = side; setState('peek', 3.5 + rnd() * 3); }
    function startRoll() {
      const [lo, hi] = surfaceSpan(), need = 46 * S;
      let dir = facing;
      if (dir > 0 && hi - cx < need) dir = -1;
      else if (dir < 0 && cx - lo < need) dir = 1;
      if (!perch && (dir > 0 ? hi - cx : cx - lo) < need) { startHops(); return; }   // 地方不够，改成蹦两下
      rollDir = dir; facing = dir; rollX0 = cx; setState('roll', 0.95);
    }
    function startYawn(mode, dur) {
      resetPlans();
      sleepMode = mode; napDur = dur || 0;
      sleepLedge = !!perch && rnd() < 0.45;   // 在台子上：坐在边上、腿垂下去睡
      setState('yawn', 1.6); emo('zz', 1.6);
    }
    function startSleep(mode) {
      sleepMode = mode; setState('sleep', 1e9); nextFx = now + 0.8;
      noseT0 = now + 1.5; noseLen = 5 + rnd() * 4; noseInfo = null;
      nextTwitch = now + 8 + rnd() * 8; nextDream = now + 5 + rnd() * 6; lastDream = null;
    }
    // 自己打个盹：一半是坐着打瞌睡、点几下头才睡着，一半是打个哈欠直接躺下
    function startNap(dur) {
      resetPlans();
      nextNap = now + 180 + rnd() * 180;
      const f = friend(); if (f) f.react('sleepy', api);   // 另一只也困的话会挨过来一起睡
      const d = dur || 25 + rnd() * 45;
      if (!perch && rnd() < 0.5) { sleepMode = 'nap'; napDur = d; sleepLedge = false; dozeNods = 2 + Math.floor(rnd() * 2); setState('doze', 2.2 + rnd()); }
      else startYawn('nap', d);
    }
    function wake(startled) {
      if (startled) { spawnBang(); sqV += 5; jump('plain', S * 70, false); }
      else { startStretch(); wakeLook = !!(lastDream && lastDream.food) && rnd() < 0.7; }   // 梦到吃的：醒来先找找
    }
    // 鼻涕泡破掉
    function popNose() {
      if (!noseInfo) return;
      const n = noseInfo; noseInfo = null;
      for (let i = 0; i < 6; i++) {
        const a = i / 6 * Math.PI * 2;
        spawn(CRUMB, '#8FC4EA', (n.x + Math.cos(a) * n.r) * S, (n.y + Math.sin(a) * n.r) * S, Math.cos(a) * S * 12, Math.sin(a) * S * 12 - S * 3, 0.35, S * 40);
      }
    }
    function sleepTwitch() {
      const r = rnd();
      if (r < 0.35) { popNose(); noseT0 = now + 1; facing = -facing; leanV += facing * 3; sqV += 2; }   // 翻个身（鼻涕泡顺手破了）
      else if (r < 0.65) { twitchUntil = now + 0.3; sqV += 3; }                      // 手一抽
      else emo(rnd() < 0.5 ? 'note' : 'heart', 1.4);                                 // 说梦话
    }
    const DREAMS = [
      { name: '饼干', rows: SNACKS[0].rows, pal: SNACKS[0].pal, food: true }, { name: '苹果', rows: SNACKS[1].rows, pal: SNACKS[1].pal, food: true },
      { name: '甜甜圈', rows: SNACKS[2].rows, pal: SNACKS[2].pal, food: true }, { name: '球', rows: ballRows(0), pal: BALL_PAL },
      { name: '爱心', rows: HEART, pal: { X: C.heart } }, { name: '蝴蝶', rows: FLY[0], frames: FLY, pal: { W: C.note, B: C.border } },
      { name: '音符', rows: NOTE, pal: { X: C.note } },
    ];
    // 做梦：头上冒云朵泡泡，点泡泡可以钻进梦里玩
    function startDream() { const d = pick(DREAMS); lastDream = d; emote = { kind: 'dream', icon: d, born: now, until: now + 6 }; }
    function startFall(m) { mood = m; impact = 0; setState('fall', 1e9); }
    // 离开地面：从台子上起跳要先把台子记下来
    function launch(m, vy, vx, ignore) {
      if (perch) { keepPerchAsPlat(); if (ignore) ignoreEl = perch.el; perch = null; }
      sitOff = 0;
      velX = vx || gvx * 0.6; gvx = 0;   // 边走边跳：带着一点往前冲的劲
      velY = -vy; sqV += 6; startFall(m);
    }
    // 起跳：先蹲下蓄力，再弹出去
    function jump(m, v, crouch, vx, ignore) {
      if (crouch && grounded()) {
        pendingJumpV = v; pendingJumpVX = vx || 0; pendingIgnore = !!ignore; pendingMood = m; mood = m;
        setState('crouch', 0.09); return;
      }
      launch(m, v, vx, ignore);
    }
    function startHappy(dur) { setState('happy', dur); nextFx = now; }
    function startDance() { setState('dance', 3.2); nextFx = now; }
    function startWave(dur) { setState('wave', dur); }
    function startAlert(text, dur, sticky) { setState('alert', dur); nextFx = now; alertSticky = !!sticky; say(text, dur); }
    function startDizzy(dur) { setState('dizzy', dur || 2.2); nextFx = now; }
    function startSneeze() { sneezed = false; setState('sneeze', 1.1); }
    function celebrate(big, text) { say(text, 3.5); for (let i = 0; i < 4; i++) spawnSparkle(); jump('celebrate', S * (big ? 115 : 90), true); }
    function startle() { spawnBang(); sqV += 6; jump('plain', S * 55, false); }
    function dropIn(x) {
      const W = vw();
      cx = W > 22 * S ? clamp(x, 11 * S, W - 11 * S) : W / 2;
      posY = 16 * S; velX = velY = 0; mouseDown = dragging = false; perch = null;
      resetPlans();
      startFall('plain'); afterLandWave = true;
    }
    function summon() { dropIn(now - lastCursorMove < 5 ? lastCursor.x : vw() / 2); }

    function checkAfk() { if (idleState !== 'active') { startYawn('afk'); return true; } return false; }

    // 走到台子边上再跳下去
    function hopDown(dir, then) {
      if (!perch || !perch.m) { if (then) then(); else startIdle(); return; }
      const m = perch.m;
      const d = dir || (cx - m.l < m.r - cx ? -1 : 1);
      const go = () => { facing = d; afterLandThen = then || null; jump('happy', S * (40 + rnd() * 20), true, d * S * (90 + rnd() * 60), true); };
      const edge = d < 0 ? m.l + 5 * S : m.r - 5 * S;
      if (Math.abs(edge - cx) < 6 * S) go(); else walkTo(edge, S * 12, go);
    }

    // 找一个够得着的台子跳上去
    function climbTarget() {
      if (now - platsT > 2) scanPlats();
      const here = perch ? perch.el : null, base = posY - sitOff;
      let best = null, bestScore = -1e9;
      for (const p of plats) {
        if (p.el === here || p.r - p.l < 18 * S) continue;
        const up = base - p.t;               // >0：台子比脚高
        if (up > 95 * S || up < -60 * S || Math.abs(up) < 5 * S) continue;
        const tx = clamp(cx, p.l + 8 * S, p.r - 8 * S), dx = tx - cx;
        if (Math.abs(dx) > (perch ? 80 : 400) * S) continue;   // 在地上可以先走过去再跳
        const score = -Math.abs(dx) / (3 * S) - Math.abs(up) / (2 * S) + (up > 0 ? 12 : 0) + rnd() * 30;
        if (score > bestScore) { bestScore = score; best = { p, tx }; }
      }
      return best;
    }
    function climbTo(c) {
      if (!perch && Math.abs(c.tx - cx) > 45 * S) {
        // 离得远：先走到台子底下附近，再重新量一下跳上去
        const dir = sign(c.tx - cx);
        walkTo(c.tx - dir * 28 * S, S * 15, () => {
          const m = measure(c.p);
          if (!m || m.r - m.l < 18 * S) { startIdle(); return; }
          Object.assign(c.p, m);
          const up = posY - sitOff - m.t;
          if (up > 95 * S || up < -60 * S) { emo('q', 1.2); startIdle(); return; }
          climbTo({ p: c.p, tx: clamp(cx, m.l + 8 * S, m.r - 8 * S) });
        });
        return;
      }
      const G = S * 520, up = posY - sitOff - c.p.t;
      const apex = Math.max(up, 0) + Math.min(10 * S, Math.max(2 * S, c.p.t - 16 * S));
      const vy = Math.sqrt(2 * G * apex);
      const tFlight = vy / G + Math.sqrt(2 * Math.max(0, apex - up) / G);
      const vx = clamp((c.tx - cx) / Math.max(0.2, tFlight), -S * 420, S * 420);
      facing = vx >= 0 ? 1 : -1;
      aimJump = { vy, vx, ignore: up < 0 };
      attn = { x: c.tx, y: c.p.t, until: now + 1.2 };
      setState('aim', 0.35 + rnd() * 0.35);
    }

    function playingVideo() {
      const W = vw(), H = vh();
      for (const v of document.querySelectorAll('video')) {
        if (v.paused || v.ended || v.readyState < 2) continue;
        const r = v.getBoundingClientRect();
        if (r.width < 160 || r.height < 90 || r.bottom < 0 || r.top > H || r.right < 0 || r.left > W) continue;
        return v;
      }
      return null;
    }
    function goWatch(v) {
      const r = v.getBoundingClientRect();
      walkTo(r.left + r.width / 2 + (rnd() - 0.5) * r.width * 0.5, S * 16, () => {
        const rr = v.getBoundingClientRect();
        watchAt = { x: rr.left + rr.width / 2, y: rr.top + rr.height / 2 };
        setState('watch', 8 + rnd() * 12);
      });
    }

    function startChase(o, kicks) {
      if (!o || o.gone) return;
      chaseObj = o; if (kicks != null) kicksLeft = kicks;
      walkTarget = null; walkThen = null;
      o.touched = now;
      setState('chase', 25);
    }
    function startEat(o) {
      resetPlans();
      chaseObj = o; o.eating = true; o.vx = o.vy = 0;
      if (!o.hand) facing = o.x >= cx ? 1 : -1;
      setState('eat', o.hand ? 1.3 : 1.5); nextFx = now; sqV -= 3;
    }
    function finishEat() {
      const o = chaseObj && chaseObj.kind === 'snack' ? chaseObj : null;
      chaseObj = null; mood = 'plain';
      if (o) {
        removeObj(o);
        fedTimes = fedTimes.filter(t => now - t < 300); fedTimes.push(now);
        energy = Math.min(1, energy + 0.15);
        spawnHeart();
        if (feedQueue > 0) { feedQueue--; if (feedNow()) return; }               // 还有排队的：接着吃
        if (fedTimes.length >= 4) { blushUntil = now + 4; emo('heart', 2); startLie(4); return; }   // 吃饱了，摊一会儿
        emo('heart', 1.4);
      }
      startHappy(0.9);
    }
    function startKick(o) { chaseObj = o; facing = o.x >= cx ? 1 : -1; kicked = false; setState('kick', 0.32); }

    function startReach() { facing = lastCursor.x >= cx ? 1 : -1; setState('reach', 0.9 + rnd() * 0.6); }
    function reachJump() {
      const h = posY - sitOff - 12 * S - lastCursor.y;
      if (now - lastCursorMove > 6 || h < 0 || rnd() < 0.25) { startIdle(); return; }   // 够不着就算了
      const v = Math.sqrt(2 * S * 520 * clamp(h, 6 * S, 60 * S));
      jump('reach', v, true, clamp((lastCursor.x - cx) * 1.6, -S * 120, S * 120));
    }
    function checkReach() {
      if (!(REST.has(state) || state === 'walk') || state === 'type' || now - lastReach < 15 || hotEl) return;
      const still = now - lastCursorMove;
      if (still < 0.8 || still > 6) return;
      const dx = lastCursor.x - cx, up = posY - sitOff - 16 * S - lastCursor.y;
      if (Math.abs(dx) < 16 * S && up > 3 * S && up < 32 * S) { lastReach = now; walkTarget = null; walkThen = null; startReach(); }
    }

    // 待机时挑下一件事做：精力足就爱蹦跶，没精神就坐着趴着
    function chooseNext() {
      const opts = [], add = (w, f) => { if (w > 0) opts.push([w, f]); };
      const e = energy, W = vw();
      add(24, () => startWalk(rnd() < 0.5 ? -1 : 1, 2 + rnd() * 4, e > 0.7 && rnd() < 0.3 ? S * 15 : S * 9));
      add(6, () => setState('idle', 1.5 + rnd() * 2.5));
      add(8 + 10 * (1 - e), startSit);
      add(6, startLook);
      add(5, startPonder);
      add(2 + 5 * e, startHops);
      add(1 + 3 * e, startSpin);
      add(1 + 3 * e, startRoll);
      add(1 + 3 * e, startExercise);
      add(3, startStretch);
      add(3, startScratch);
      add(2 + 6 * (1 - e), () => startLie());
      add(2 * e, startDance);
      add(1.2, startSneeze);
      if (now - lastCursorMove < 8 && lastCursor.x > 0) {
        add(3, () => { facing = lastCursor.x >= cx ? 1 : -1; startWave(1.4); emo('note', 1.4); });
        add(4, () => walkTo(lastCursor.x + (lastCursor.x > cx ? -14 : 14) * S, S * 14, () => setState('sit', 4 + rnd() * 4)));
      }
      if (now >= nextNap) {   // 没精神、夜里更容易打个盹
        const hr = new Date().getHours();
        add((e < 0.45 ? 6 : 1.5) + (hr >= 22 || hr < 7 ? 4 : 0), () => startNap());
      }
      const cl = climbTarget();
      if (cl) add(10 + 14 * e, () => climbTo(cl));
      if (perch) { add(5, () => hopDown()); add(5, startLedge); }
      else if (cx < 40 * S || cx > W - 40 * S) {
        const side = cx < W / 2 ? -1 : 1;
        add(3, () => walkTo(side < 0 ? 11 * S : W - 11 * S, S * 9, () => startPeek(side)));
      }
      const ball = worldFind('ball');
      if (ball && !ball.held) add(8 + 16 * e, () => startChase(ball, 2 + Math.floor(rnd() * 4)));
      addSocial(add, e);
      const fly = findObj('fly');
      if (fly && !fly.onHead) add(16, () => startChase(fly));
      const vid = playingVideo();
      if (vid) add(18, () => goWatch(vid));
      let total = 0;
      for (const o of opts) total += o[0];
      let r = rnd() * total;
      for (const [w, f] of opts) { r -= w; if (r <= 0) { f(); return; } }
      startIdle();
    }

    // ================================================================ 两只一起玩
    // 待机时可能挑的「和另一只一起做的事」
    function addSocial(add, e) {
      const f = friend();
      if (!f || now < nextSocial) return;
      const fi = f.info();
      if (fi.state === 'drag' || fi.state === 'fall' || fi.state === 'tag' || fi.state === 'flee') return;
      const dx = fi.cx - cx, dist = Math.abs(dx), dir = dx >= 0 ? 1 : -1;
      if (sameLevel(fi)) {
        if (fi.state === 'dance') add(25, () => startDance());                          // 它在跳舞：跟着一起跳
        if (fi.sleeping && (e < 0.6 || rnd() < 0.3)) add(12, () => napBeside(f));      // 它在睡：挨着它一起睡
        if (fi.rest && !fi.sleeping && fi.state !== 'type') {
          add(7, () => goSnuggle(f));                                                     // 贴贴
          if (dist < 60 * S) add(3 + 6 * e, () => startTag(f));                          // 你追我跑（离得不太远才玩）
        }
        if (dist > 34 * S && !fi.sleeping) add(4, () => goGreet(f));                      // 走过去打个招呼
      } else if (!fi.sleeping && fi.grounded) {
        // 不在一层：去找它
        if (fi.perchEl && fi.perchEl !== (perch ? perch.el : null)) {
          if (now - platsT > 2) scanPlats();
          const p = plats.find(q => q.el === fi.perchEl);
          if (p) add(6, () => { nextSocial = now + 6; climbTo({ p, tx: clamp(fi.cx - dir * 19 * S, p.l + 8 * S, p.r - 8 * S) }); });
        } else if (!fi.perchEl && perch) add(5, () => { nextSocial = now + 6; hopDown(dir); });
      }
    }
    function goGreet(f) {
      nextSocial = now + 8;
      const fi = f.info(), dir = fi.cx >= cx ? 1 : -1;
      walkTo(fi.cx - dir * 26 * S, S * 12, () => {
        facing = f.info().cx >= cx ? 1 : -1;
        startWave(1.4); emo('note', 1.4);
        f.react('wave', api);
      });
    }
    function goSnuggle(f) {
      nextSocial = now + 10;
      const fi = f.info(), side = cx <= fi.cx ? -1 : 1;   // 从现在这边靠过去
      walkTo(fi.cx + side * 16.5 * S, S * 11, () => {
        const fj = f.info();
        if (!sameLevel(fj) || !fj.rest || Math.abs(fj.cx - cx) > 22 * S) { startIdle(); return; }
        const dur = 4 + rnd() * 3;
        if (!f.react('snuggle', api, dur)) { startIdle(); return; }
        mate = f; facing = fj.cx >= cx ? 1 : -1;
        setState('snuggle', dur); nextFx = now + 0.4;
      });
    }
    function startTag(f) {
      nextSocial = now + 12;
      if (!f.react('flee', api)) { goGreet(f); return; }
      resetPlans(); mate = f;
      setState('tag', 8 + rnd() * 2); emo('bang', 0.8);
    }
    function napBeside(f) {
      nextSocial = now + 20; nextNap = now + 200;
      const fi = f.info(), side = cx <= fi.cx ? -1 : 1;
      walkTo(fi.cx + side * 17.5 * S, S * 9, () => { facing = f.info().cx >= cx ? 1 : -1; emo('heart', 1.2); startYawn('nap', 30 + rnd() * 40); });
    }

    // 另一只对我做了什么 / 发生了什么：我这边的反应（返回 false = 现在顾不上）
    function reactTo(kind, from, x) {
      if (hiddenNow || retired || dream || !from) return false;
      now = T();
      const fi = from.info(), dir = fi.cx >= cx ? 1 : -1;
      const free = (REST.has(state) && state !== 'type') || state === 'walk' || state === 'happy';
      const goNear = then => walkTo(fi.cx - dir * 19 * S, S * 14, () => { facing = from.info().cx >= cx ? 1 : -1; then(); });
      switch (kind) {
        case 'look': attn = { x: fi.cx, y: fi.posY - 8 * S, until: now + 1.8 }; return true;
        case 'wave':
          if (!free || !grounded()) return false;
          resetPlans(); facing = dir; startWave(1.4); emo('note', 1.4); return true;
        case 'snuggle':
          if (!REST.has(state) || state === 'type' || state === 'sleep' || !grounded()) return false;
          resetPlans(); mate = from; facing = dir; setState('snuggle', x || 5); nextFx = now + 0.8; return true;
        case 'flee':
          if (!free || !grounded() || !sameLevel(fi)) return false;
          resetPlans(); mate = from; facing = -dir; emo('bang', 0.8); setState('flee', 6 + rnd() * 2); return true;
        case 'caught':
          if (state !== 'flee') return false;
          resetPlans(); blushUntil = now + 2; setState('giggle', 1.2); nextFx = now;
          if (rnd() < 0.45) tagBack = from;   // 笑完换它来追
          return true;
        case 'dance':
          if (REST.has(state) && grounded() && rnd() < 0.7) { resetPlans(); startDance(); return true; }
          return false;
        case 'jealous':   // 你摸了另一只：它跑过来在旁边气鼓鼓
          if (!free || !grounded() || now - lastJealous < 25 || rnd() > 0.6) return false;
          lastJealous = now;
          if (!sameLevel(fi)) { attn = { x: fi.cx, y: fi.posY - 8 * S, until: now + 2 }; emo('anger', 1.4); return true; }
          resetPlans(); goNear(() => { emo('anger', 1.6); setState('sit', 2.5 + rnd() * 2); });
          return true;
        case 'beg':       // 另一只在吃东西：跑过来眼巴巴看着
          if (!free || !grounded() || rnd() > 0.85) return false;
          if (!sameLevel(fi)) { attn = { x: fi.cx, y: fi.posY - 8 * S, until: now + 2 }; emo('q', 1.2); return true; }
          resetPlans(); goNear(() => { emo('q', 1.4); setState('sit', 3 + rnd() * 2); });
          return true;
        case 'comfort':   // 另一只被摔晕了：跑过去安慰
          if (!free || !grounded() || rnd() > 0.6) return false;
          if (!sameLevel(fi)) { attn = { x: fi.cx, y: fi.posY - 8 * S, until: now + 2 }; emo('sweat', 1.2); return true; }
          resetPlans(); goNear(() => { emo('heart', 1.6); startHappy(1); });
          return true;
        case 'sleepy':    // 另一只要睡了：自己也有点困的话，挨过去一起睡
          if (!REST.has(state) || !grounded() || energy > 0.65 || rnd() > 0.5 || !sameLevel(fi)) return false;
          napBeside(from); return true;
        case 'receive':   // 另一只把球传过来了：去接
          if (!(free || state === 'chase') || !grounded() || !x || x.gone) return false;
          startChase(x, 1 + Math.floor(rnd() * 3)); emo('bang', 0.6); return true;
        case 'bonked':    // 被另一只从天上砸到
          sqV -= 6; leanV -= dir * 3; spawnBang();
          if (state === 'sleep' || state === 'doze') wake(true);
          else if (grounded() && state !== 'drag') { if ((x || 0) > S * 300) { emo('anger', 1.5); resetPlans(); startDizzy(1.6); } else emo('bang', 1); }
          return true;
        case 'bump':      // 走着走着撞到一起：各自弹开一点
          sqV -= 2.5; leanV -= dir * 2.5; gvx = -dir * S * 8;
          if (REST.has(state) || state === 'walk') emo(rnd() < 0.5 ? 'q' : 'heart', 1);
          return true;
      }
      return false;
    }

    function updTag(dt) {   // 追：跑到它身边就算抓到
      const f = mate, fi = f && f.info();
      if (!fi || fi.hidden || !sameLevel(fi) || stT >= stDur || fi.state !== 'flee') { mate = null; startIdle(); return; }
      const dx = fi.cx - cx;
      facing = dx >= 0 ? 1 : -1;
      groundMove(facing * S * 34, dt, 7);
      const [lo, hi] = surfaceSpan();
      if (cx < lo || cx > hi) { moveX(clamp(cx, lo, hi) - cx); gvx = 0; }
      if (Math.abs(dx) < 20 * S) { f.react('caught', api); mate = null; emo('note', 1.2); startHappy(0.8); }
    }
    function updFlee(dt) {  // 跑：被逼到边上就掉头从它身边擦过去
      const f = mate, fi = f && f.info();
      if (!fi || fi.hidden || stT >= stDur || fi.state !== 'tag') { mate = null; startIdle(); return; }
      let dir = cx >= fi.cx ? 1 : -1;
      const [lo, hi] = surfaceSpan();
      if ((dir > 0 && cx >= hi - 2 * S) || (dir < 0 && cx <= lo + 2 * S)) dir = -dir;
      facing = dir;
      groundMove(dir * S * 20, dt, 6);
      if (cx < lo || cx > hi) { moveX(clamp(cx, lo, hi) - cx); gvx = 0; }
      attn = { x: fi.cx, y: fi.posY - 8 * S, until: now + 0.3 };   // 一边跑一边回头看
    }
    function updSnuggle() {  // 贴贴：靠在一起，冒爱心
      const f = mate, fi = f && f.info();
      if (!fi || fi.hidden || !sameLevel(fi) || Math.abs(fi.cx - cx) > 22 * S || (fi.state !== 'snuggle' && stT > 0.5)) { mate = null; startIdle(); return; }
      facing = fi.cx >= cx ? 1 : -1;
      if (now >= nextFx) { nextFx = now + 1.1 + rnd() * 0.7; spawnHeart(); }
      blushUntil = now + 0.3;
      if (stT >= stDur) { mate = null; startIdle(); }
    }
    // 同一层上的两只别叠在一起；走着撞上了互相弹开
    function keepApart(dt) {
      const f = friend();
      if (!f || !grounded() || state === 'peek' || state === 'roll') return;
      const fi = f.info();
      if (!sameLevel(fi)) return;
      const dx = cx - fi.cx, d = Math.abs(dx);
      const min = state === 'snuggle' || fi.state === 'snuggle' ? 16 * S : 17.5 * S;   // 身子 16 格宽：贴贴时刚好挨着，平时别叠在一起
      if (d >= min) return;
      const dir = d < 0.5 ? (slot === 0 ? -1 : 1) : sign(dx);
      moveX(dir * Math.min((min - d) / 2, S * 30 * dt));
      const [lo, hi] = surfaceSpan();
      if (cx < lo || cx > hi) moveX(clamp(cx, lo, hi) - cx);
      const moving = s => s === 'walk' || s === 'chase';
      if (slot === 0 && d < 16 * S && now - lastBump > 4 && (moving(state) || moving(fi.state)) && state !== 'snuggle') {
        lastBump = now; reactTo('bump', f); f.react('bump', api);
      }
    }

    function updIdle() {
      if (checkAfk()) return;
      if (settings.follow && now - lastCursorMove < 6) {
        const dx = lastCursor.x - cx;
        if (Math.abs(dx) > 20 * S) { startWalk(dx > 0 ? 1 : -1, 10); return; }
      }
      if (stT >= stDur) chooseNext();
    }

    function atEdge(lo, hi) {
      const r = rnd();
      if (perch) {
        if (r < 0.3) { facing = -facing; leanV -= facing * 1.5; }
        else if (r < 0.5) startLedge();
        else if (r < 0.7) startTeeter();
        else if (r < 0.88) hopDown(facing);
        else startLook();
        return;
      }
      facing = cx <= lo ? 1 : -1;   // 地面上：转身接着走
      leanV -= facing * 1.5;
    }

    // 地面上移动：速度慢慢加上去、慢慢减下来，腿按走过的距离迈步（不会脚底打滑）
    function groundMove(target, dt, accel) {
      gvx += (target - gvx) * Math.min(1, dt * (accel || 6));
      if (target === 0 && Math.abs(gvx) < 0.2 * S) gvx = 0;
      moveX(gvx * dt);
      walkPh += Math.abs(gvx) * dt / (S * 1.5);
    }

    function updWalk(dt) {
      if (checkAfk()) return;
      let speed = walkSpeed || S * 9;
      const chasing = settings.follow && now - lastCursorMove < 6 && walkTarget == null;
      if (walkTarget != null) {
        const dx = walkTarget - cx;
        if (Math.abs(dx) < 1.5 * S) { arrive(); return; }
        facing = dx > 0 ? 1 : -1;
        speed = Math.min(speed, Math.abs(dx) * 3 + S * 3);   // 快到了就放慢
      } else if (chasing) {
        const dx = lastCursor.x - cx;
        if (Math.abs(dx) < 6 * S) { startIdle(); return; }
        facing = dx > 0 ? 1 : -1;
        speed = S * 14;
      }
      groundMove(facing * speed, dt, 5);
      const [lo, hi] = surfaceSpan();
      if (cx < lo || cx > hi) {
        moveX((cx < lo ? lo : hi) - cx);
        gvx = 0;
        if (walkTarget != null) { arrive(); return; }
        if (chasing) { startIdle(); return; }
        atEdge(lo, hi);
        return;
      }
      if (stT >= stDur) startIdle();
    }

    function updChase(dt) {
      const o = chaseObj;
      if (!o || o.gone) { chaseObj = null; startIdle(); return; }
      if (stT >= stDur) { chaseObj = null; startSit(); return; }
      attn = { x: o.x, y: o.y - 2 * S, until: now + 0.4 };
      if (o.held) return;                               // 你拿着呢：盯着看
      if (o.kind !== 'fly' && perch) { hopDown(o.x >= cx ? 1 : -1, () => startChase(o)); return; }   // 东西在地上，先跳下去
      const dx = o.x - cx, adx = Math.abs(dx), H = vh();
      if (adx > 2 * S) facing = dx > 0 ? 1 : -1;
      if (o.kind === 'snack') {
        if (adx < 8 * S) { if (o.rest) startEat(o); return; }
      } else if (o.kind === 'ball') {
        const low = o.y > H - 8 * S;
        if (adx < 11 * S && low) { startKick(o); return; }
        if (adx < 14 * S && !low && o.vy > -S * 40 && o.y < posY - 12 * S && o.y > posY - 40 * S) {
          afterLandThen = () => { if (!o.gone && kicksLeft > 0) startChase(o); else startIdle(); };
          jump('happy', S * 100, false); return;        // 头球！
        }
      } else if (adx < 10 * S && o.y > posY - 48 * S) {  // 蝴蝶：扑一下，它会躲
        if (rnd() < 0.7) flyDodge(o);
        chaseObj = null;
        afterLandThen = () => { emo(rnd() < 0.5 ? 'q' : 'note', 1.2); startIdle(); };
        jump('happy', S * (80 + rnd() * 30), true);
        return;
      }
      const speed = o.kind === 'fly' ? S * 14 : S * 42;
      groundMove(adx > 3 * S ? facing * Math.min(speed, adx * 4) : 0, dt, 8);
      const [lo, hi] = surfaceSpan();
      if (cx < lo || cx > hi) { moveX(clamp(cx, lo, hi) - cx); gvx = 0; }
    }

    function updKick() {
      const o = chaseObj;
      if (!kicked && stT >= 0.1) {
        kicked = true;
        if (o && !o.gone && !o.held && Math.abs(o.x - cx) < 14 * S) {
          let dir = o.x >= cx ? 1 : -1;
          // 另一只在同一层：多半把球传给它（按距离算力气，滚到它脚边），它会去接、再传回来
          const f = friend(), fi = f && f.info();
          const gap = fi ? Math.abs(fi.cx - o.x) : 0;
          if (fi && sameLevel(fi) && !fi.sleeping && gap > 16 * S && gap < 180 * S && rnd() < 0.75) {
            dir = fi.cx >= o.x ? 1 : -1;
            o.vx = dir * clamp(gap * 1.9, S * 30, S * 150); o.vy = -S * (25 + rnd() * 30);
            f.react('receive', api, o);
          } else {
            const big = rnd() < 0.25;   // 大多是轻轻带一下，偶尔来一脚大的
            o.vx = dir * S * (big ? 80 + rnd() * 50 : 30 + rnd() * 40); o.vy = -S * (big ? 90 + rnd() * 60 : 30 + rnd() * 50);
          }
          o.touched = now; o.rest = false;
          spawn(PUFF, C.puff, (dir > 0 ? 20 : 2) * S, 15 * S, dir * S * 6, -S * 2, 0.4, 0);
          kicksLeft--; energy = Math.min(1, energy + 0.02);
        }
      }
      if (stT >= stDur) {
        if (o && !o.gone && kicksLeft > 0) startChase(o);
        else { chaseObj = null; if (rnd() < 0.5) { startHappy(0.9); emo('note', 1.2); } else startSit(); }
      }
    }

    function updRoll() {
      const k = Math.min(1, stT / stDur), e = k < 0.5 ? 2 * k * k : 1 - Math.pow(-2 * k + 2, 2) / 2;
      rot = rollDir * e * Math.PI * 2;
      moveX(rollX0 + rollDir * e * 48 * S - cx);
      if (!perch) { const [lo, hi] = surfaceSpan(); if (cx < lo || cx > hi) moveX(clamp(cx, lo, hi) - cx); }
      if (stT >= stDur) { rot = 0; if (rnd() < 0.4) startDizzy(1.0); else { startHappy(0.5); emo('star', 1); } }
    }

    function updTeeter() {
      if (stT < stDur) return;
      if (perch && rnd() < 0.3) {   // 没站稳，掉下去了
        ignoreEl = perch.el; perch = null; fellOff = true;
        velX = facing * S * 40; velY = 0; startFall('plain');
      } else { emo('sweat', 1.3); facing = -facing; startWalk(facing, 1 + rnd() * 1.5); }
    }

    function updPeek() {
      const W = vw(), k = stT < 0.5 ? stT / 0.5 : stT > stDur - 0.5 ? Math.max(0, (stDur - stT) / 0.5) : 1;
      const inner = peekSide > 0 ? W - 11 * S : 11 * S, outer = peekSide > 0 ? W + 3 * S : -3 * S;
      cx = inner + (outer - inner) * k;
      if (stT >= stDur) { cx = inner; if (rnd() < 0.5) emo('note', 1.2); startIdle(); }
    }

    function updFall(dt) {
      const prevY = posY;
      velY = Math.min(velY + S * 520 * dt, S * 700);
      cx += velX * dt; posY += velY * dt;
      const W = vw(), H = vh();
      if (cx - 11 * S < 0 || cx + 11 * S > W) {
        const side = cx - 11 * S < 0 ? -1 : 1;
        cx = side < 0 ? 11 * S : W - 11 * S;
        if (Math.abs(velX) > S * 300 && W > 40 * S) { startSplat(side); return; }   // 扔太狠，糊在墙上
        velX = -side * Math.abs(velX) * 0.5; leanV -= side * 2;
      }
      const ceil = 15 * S;
      if (posY < ceil) { posY = ceil; if (velY < 0) { velY = -velY * 0.3; sqV -= 3; } }
      if (mood === 'reach' && now - lastCursorMove < 3 && hitPet(lastCursor.x, lastCursor.y)) {   // 抓到鼠标了
        mood = 'happy'; spawnHeart(); emo('heart', 1.4);
      }
      if (velY <= 0) return;
      // 掉到另一只头上：在它头上弹一下，它被砸得一缩
      const f = friend(), fi = f && f.info();
      if (fi && fi.grounded && velY > S * 60 && Math.abs(cx - fi.cx) < 11 * S) {
        const top = fi.posY - 13 * S;
        if (prevY <= top + 2 && posY >= top) {
          const v = velY;
          posY = top; velY = -Math.max(S * 110, v * 0.55); velX += (cx >= fi.cx ? 1 : -1) * S * 50; sqV -= 4;
          spawnSparkle(); f.react('bonked', api, v);
          return;
        }
      }
      if (now - platsT > 0.35 && posY < H - 20 * S) scanPlats();
      for (const p of plats) {
        if (p.el === ignoreEl || cx < p.l + 3 * S || cx > p.r - 3 * S) continue;
        if (prevY <= p.t + 2 && posY >= p.t) {
          const m = measure(p);
          if (!m) continue;
          Object.assign(p, m);
          if (prevY <= m.t + 2 && posY >= m.t && cx >= m.l + 2 * S && cx <= m.r - 2 * S) { touchDown(m.t, p); return; }
        }
      }
      if (posY >= H) touchDown(H, null);
    }

    function touchDown(y, plat) {
      posY = y;
      const v = velY;
      if (v > impact) impact = v;
      sqV -= clamp(v / (S * 55), 1, 9);   // 啪叽
      leanV += velX / (S * 300);
      if (v > S * 110) spawnDust();
      if (v > S * 150) { velY = -v * 0.32; velX *= 0.6; return; }   // 弹一下
      velX = 0; velY = 0; ignoreEl = null;
      setPerch(plat);
      setState('land', 0.12);
    }

    let splatSide = 1;
    function startSplat(side) { splatSide = side; velX = velY = 0; sqV += 9; leanV = 0; spawnBang(); resetPlans(); setState('splat', 0.7); }

    function afterLand() {
      const imp = impact; impact = 0;
      if (afterLandThen) { const f = afterLandThen; afterLandThen = null; f(); return; }
      const fr = friend();
      if (pendingDizzy) { pendingDizzy = pendingDance = false; spawnSweat(); emo('anger', 1.5); startDizzy(2.2); if (fr) fr.react('comfort', api); return; }
      if (pendingDance) { pendingDance = false; startDance(); return; }
      if (hopsLeft > 0) { hopsLeft--; jump('happy', S * (38 + rnd() * 14), true); return; }
      if (afterLandSay) { const t = afterLandSay; afterLandSay = null; afterLandWave = false; startWave(2); say(t, 5); return; }
      if (afterLandWave) { afterLandWave = false; startWave(1.6); emo('note', 1.6); return; }
      if (imp > S * 300 && mood === 'plain') { emo('sweat', 1.5); startDizzy(2.2); if (fr) fr.react('comfort', api); return; }   // 被扔 / 摔下来才会晕，自己跳不会
      if (fellOff) { fellOff = false; emo('sweat', 1.5); setState('idle', 1.2); return; }
      if (mood === 'celebrate') { startHappy(1.6); return; }
      startIdle();
    }

    // 每帧把小人贴在脚下的地面 / 网页元素上；元素被滚走了就掉下来
    function checkSupport(dt) {
      const W = vw(), H = vh();
      const want = state === 'ledge' || (state === 'sleep' && sleepLedge) ? 3 * S : 0;
      sitOff += (want - sitOff) * Math.min(1, dt * 10);
      if (Math.abs(sitOff - want) < 0.2) sitOff = want;
      if (perch) {
        const m = measure(perch);
        if (!m || m.t < 14 * S || m.r - m.l < 8 * S) { leavePerch(); return; }
        if (m.t >= H) { perch = null; posY = H + sitOff; return; }   // 被滚到地面以下：就当回到地上
        if (perch.lastT != null) {
          const d = m.t - perch.lastT;
          if (Math.abs(d) > 0.5) { sqV += clamp(d / (S * 6), -2.5, 2.5); lastScrollT = now; }   // 网页滚动时晃一晃
        }
        perch.lastT = m.t; perch.m = m;
        cx = m.l + perch.off;
        if (cx < m.l - 1 * S || cx > m.r + 1 * S || cx < -4 * S || cx > W + 4 * S) { leavePerch(); return; }
        posY = m.t + sitOff;
        return;
      }
      if (state !== 'peek') cx = W > 22 * S ? clamp(cx, 11 * S, W - 11 * S) : W / 2;
      if (posY - sitOff < H - 1) { velX = velY = 0; startFall('plain'); }
      else posY = H + sitOff;
    }
    function leavePerch() {
      if (perch) ignoreEl = perch.el;
      perch = null; sitOff = 0; fellOff = true;
      if (chaseObj && chaseObj.eating) chaseObj.eating = false;
      walkTarget = null; walkThen = null;
      velX = 0; velY = 0; startFall('plain');
    }

    // ================================================================ 果冻弹簧 / 手脚
    function updDragVel(dt) {
      if (state === 'drag' && trail.length >= 2) {
        const a = trail[0], b = trail[trail.length - 1];
        let vx = 0, vy = 0;
        if (T() - b[0] < 0.06 && b[0] - a[0] > 0.005) { vx = (b[1] - a[1]) / (b[0] - a[0]); vy = (b[2] - a[2]) / (b[0] - a[0]); }
        const pv = dragVX;
        dragVX += (vx - dragVX) * Math.min(1, dt * 12);
        dragVY += (vy - dragVY) * Math.min(1, dt * 12);
        if (dt > 0) accX += ((dragVX - pv) / dt - accX) * Math.min(1, dt * 20);
        shake += (Math.hypot(dragVX, dragVY) - shake) * Math.min(1, dt * 6);
        if (shake > S * 120) shakeAcc += dt;
      } else { dragVX = dragVY = 0; accX = 0; shake *= Math.max(0, 1 - dt * 4); }
    }

    // 腿像钟摆：被拎着甩的时候跟着晃，一组跟得快、一组慢半拍；手跟着乱挥
    function updLimbs(dt) {
      let tgt = 0;
      if (state === 'drag') tgt = clamp(-dragVX / (S * 110) - swingV * 0.9, -2.4, 2.4);
      else if (state === 'fall' || state === 'splat') tgt = clamp(-velX / (S * 140), -2, 2);
      legAV += (70 * (tgt - legA) - 4 * legAV) * dt; legA = clamp(legA + legAV * dt, -2.4, 2.4);
      legBV += (40 * (tgt - legB) - 2.6 * legBV) * dt; legB = clamp(legB + legBV * dt, -2.4, 2.4);
      const busy = state === 'drag' ? shake / (S * 40) : 0;
      legPh += dt * (state === 'drag' ? Math.min(10, 2.5 + busy * 1.5) : state === 'fall' ? 6 : 2);
      armPh += dt * (state === 'drag' ? Math.min(12, 4 + busy * 2) : 5);
      // 拎着的时候腿一直在空中蹬（越晃蹬得越快）
      kickPh += dt * (state === 'drag' ? Math.min(11, 4.5 + busy * 1.2) : state === 'fall' ? 5 : 0);
    }

    // 钟摆：抓住的点是支点，手往右一拉身子往左甩，然后来回荡几下
    function updSwing(dt) {
      if (state === 'drag') {
        const arm = -grabDY - 8 * S;                       // 支点到身体重心的距离（抓脚的话重心在上面）
        const Lp = Math.max(5 * S, Math.abs(arm)), gain = arm > 0 ? 1 : 0.35;
        const n = 3, hh = dt / n;
        for (let i = 0; i < n; i++) {
          const a = -(S * 520 / Lp) * Math.sin(swing) - 2.2 * swingV + gain * (accX / Lp) * Math.cos(swing);
          swingV += a * hh; swing += swingV * hh;
        }
        if (swing > 1) { swing = 1; if (swingV > 0) swingV *= -0.3; }
        if (swing < -1) { swing = -1; if (swingV < 0) swingV *= -0.3; }
        pivX += (grabDX - pivX) * Math.min(1, dt * 20);
      } else if (swing || swingV) {
        swingV += (-70 * swing - 10 * swingV) * dt; swing += swingV * dt;   // 松手后很快摆正
        if (Math.abs(swing) < 0.003 && Math.abs(swingV) < 0.02) swing = swingV = 0;
        pivX += (0 - pivX) * Math.min(1, dt * 10);
      }
    }

    function updSpring(dt) {
      let tq = 0, tl = 0, hp = 0;
      switch (state) {
        case 'idle':
          tq = 0.025 * Math.sin(now * 2.4);
          if (now - lastCursorMove < 2) {
            const dx = lastCursor.x - cx, r = 30 * S;
            if (Math.abs(dx) < r) tl = sign(dx) * 0.07 * (1 - Math.abs(dx) / r);   // 好奇地凑过去
          }
          break;
        case 'walk': case 'chase': {
          // 并拢那一帧身子微微抬起、迈步那一帧落下：一颠一颠；往前走时身子往前倾
          const moving = Math.abs(gvx) > 1.5 * S, passing = (Math.floor(walkPh) & 1) === 1;
          tq = moving ? (passing ? 0.04 : -0.03) : 0;
          hp = moving && passing ? 0.4 * S : 0;
          tl = clamp(gvx / (S * 110), -0.16, 0.16);
          break;
        }
        case 'sit': case 'watch': tq = -0.04 + 0.02 * Math.sin(now * 2); tl = lookX * 0.03; break;
        case 'ledge': tq = 0.02 * Math.sin(now * 2.4); break;
        case 'lie': tq = -0.08 + 0.02 * Math.sin(now * 1.5); break;
        case 'look': { const [lx, ly] = curLook(); tl = lx * 0.09; tq = ly < 0 ? 0.06 : 0; break; }
        case 'ponder': tq = 0.02 * Math.sin(now * 2); tl = 0.04; break;
        case 'type': tq = -0.03 + 0.02 * Math.sin(now * 3); tl = lookX * 0.04; break;
        case 'scratch': tq = 0.03; tl = 0.05 * Math.sin(now * 18); break;
        case 'spin': tq = 0.05; break;
        case 'exercise': { const up = (Math.floor(stT / 0.3) & 1) === 0; tq = up ? 0.14 : -0.1; hp = up ? 1.6 * S : 0; break; }
        case 'teeter': tl = facing * (0.3 + 0.12 * Math.sin(now * 13)); tq = -0.04; break;
        case 'peek': tl = -peekSide * 0.1; break;
        case 'kick': tq = stT < 0.1 ? -0.1 : 0.1; tl = -facing * 0.1; break;
        case 'reach': tq = 0.2 + 0.04 * Math.sin(now * 12); hp = Math.abs(Math.sin(now * 8)) * 0.8 * S; tl = sign(lastCursor.x - cx) * 0.05; break;
        case 'aim': tq = -0.15; tl = facing * 0.08; break;
        case 'splat': tq = 0.35; break;
        case 'refuse': tl = 0.14 * Math.sin(now * 26); tq = -0.04; break;
        case 'giggle': tq = 0.04 * Math.sin(now * 20); tl = 0.12 * Math.sin(now * 26); break;
        case 'snuggle': tq = -0.03 + 0.02 * Math.sin(now * 2.2); tl = facing * (0.13 + 0.03 * Math.sin(now * 2.2)); break;   // 往它那边靠
        case 'tag': case 'flee': {
          const moving = Math.abs(gvx) > 1.5 * S, passing = (Math.floor(walkPh) & 1) === 1;
          tq = moving ? (passing ? 0.05 : -0.04) : 0; hp = moving && passing ? 0.6 * S : 0;
          tl = clamp(gvx / (S * 100), -0.2, 0.2);
          break;
        }
        case 'sleep': tq = -0.03 + 0.04 * Math.sin(now * 1.7); if (sleepLedge) tl = facing * 0.06; break;
        case 'doze': { const k = Math.min(1, stT / stDur); tq = -0.04 - 0.12 * k; tl = facing * 0.14 * k * k; break; }
        case 'drag': tq = 0.12 + 0.03 * Math.sin(kickPh * Math.PI * 2); tl = clamp(dragVX / (S * 700), -0.2, 0.2); break;
        case 'fall': tq = clamp(Math.abs(velY) / (S * 1500), 0, 0.22); tl = clamp(velX / (S * 900), -0.3, 0.3); break;
        case 'crouch': tq = -0.32; break;
        case 'happy':
          tq = 0.04 * Math.sin(now * 9);
          if (mood === 'celebrate') hp = Math.abs(Math.sin(now * Math.PI / 0.3)) * 1.2 * S;
          break;
        case 'dance': { const ph = Math.sin(now * Math.PI / 0.44); tq = -0.05 + 0.12 * Math.abs(ph); tl = 0.2 * ph; hp = Math.abs(ph) * 2.2 * S; break; }
        case 'wave': tl = 0.05 * Math.sin(now * Math.PI / 0.22); break;
        case 'alert': { const ph = Math.abs(Math.sin(now * Math.PI / 0.36)); tq = 0.1 * ph; hp = ph * 1.6 * S; break; }
        case 'dizzy': tl = 0.16 * Math.sin(now * 5); tq = -0.05; break;
        case 'yawn': tq = 0.2 * Math.min(1, stT / 0.6); break;
        case 'stretch': tq = stT < 0.6 ? 0.24 : 0; break;
        case 'sneeze': if (stT < 0.5) { tq = -0.12; tl = -facing * 0.1; } break;
        case 'eat': tq = -0.06 + 0.14 * Math.sin(stT * 22); break;
      }
      if (dragHover) { tq = 0.14 + 0.03 * Math.sin(now * 10); tl = 0; }
      hop += (hp - hop) * Math.min(1, dt * 25);

      const K = 330, CD = 11, KL = 190, CL = 9, n = 3, hh = dt / n;
      for (let i = 0; i < n; i++) {
        const a = K * (tq - sq) - CD * sqV; sqV += a * hh; sq += sqV * hh;
        const b = KL * (tl - lean) - CL * leanV; leanV += b * hh; lean += leanV * hh;
      }
      if (sq < -0.5) { sq = -0.5; if (sqV < 0) sqV = 0; }
      if (sq > 0.6) { sq = 0.6; if (sqV > 0) sqV = 0; }
      lean = clamp(lean, -0.7, 0.7);
      const ay = dragging ? FOOT * S + grabDY : FOOT * S;
      anchorY += (ay - anchorY) * Math.min(1, dt * 14);
    }

    // ================================================================ 眼睛 / 姿势
    function updateEyes() {
      if (now >= nextBlink) {
        blinkUntil = now + 0.13;
        nextBlink = rnd() < 0.2 ? now + 0.32 : now + 2.2 + rnd() * 4.5;
      }
      let tx = null, ty = null;
      const fr = friend(), fri = fr && fr.info();
      if (attn && now < attn.until) { tx = attn.x; ty = attn.y; }
      else if (fri && (fri.state === 'drag' || fri.state === 'fall' || fri.state === 'splat')) { tx = fri.cx; ty = fri.posY - 8 * S; }   // 另一只被拎起来 / 扔出去：盯着看
      else if (now - lastCursorMove < 1.5) { tx = lastCursor.x; ty = lastCursor.y; }
      else {
        const fly = findObj('fly');
        if (fly && !fly.onHead && Math.abs(fly.x - cx) < 70 * S) { tx = fly.x; ty = fly.y; }
        else if (now - lastCursorMove < 4) { tx = lastCursor.x; ty = lastCursor.y; }
      }
      if (tx != null) {
        const dx = tx - cx, dy = ty - (posY - 10 * S);
        lookX = dx > 8 * S ? 1 : dx < -8 * S ? -1 : 0;
        lookY = dy < -8 * S ? -1 : dy > 8 * S ? 1 : 0;
      } else if (now >= nextGlance) {
        nextGlance = now + 1.5 + rnd() * 3;
        const r = Math.floor(rnd() * 6);
        lookX = r === 0 ? -1 : r === 1 ? 1 : 0;
        lookY = r === 2 ? -1 : 0;
      }
    }

    // 四帧走路：迈左脚 → 并拢 → 迈右脚 → 并拢；几乎没在动就站着
    function walkLegs() {
      if (Math.abs(gvx) < 1.5 * S) return 'stand';
      return ['walkA', 'stand', 'walkB', 'stand'][Math.floor(walkPh) & 3];
    }

    function curLook() {   // 东张西望：按时间表换方向
      let lx = 0, ly = 0;
      for (const [t, x, y] of lookSeq) { if (stT >= t) { lx = x; ly = y; } }
      return [lx, ly];
    }

    function buildPose() {
      const p = { eyes: now < blinkUntil ? 'blink' : 'open', armL: 'normal', armR: 'normal', legs: 'stand', lookX, lookY,
        bodyDy: 0, facing, blush: false, legDx: 0, legDx2: 0, legPh: 0, acc: slot === 1 };
      const ph = per => Math.floor(now / per) & 1;
      const hang = () => {
        const k = state === 'fall' ? Math.sin(kickPh * Math.PI * 2) : 0;   // 掉下去的时候也在蹬腿
        p.legs = 'hang'; p.legDx = clamp(Math.round(legA + k), -2, 2); p.legDx2 = clamp(Math.round(legB - k), -2, 2); p.legPh = Math.floor(legPh) & 1;
      };
      switch (state) {
        case 'walk': p.legs = walkLegs(); p.lookX = facing; p.lookY = 0; break;
        case 'chase': {
          p.legs = walkLegs();
          const a = (Math.floor(walkPh) & 2) === 0;
          p.armL = a ? 'raised' : 'normal'; p.armR = a ? 'normal' : 'raised';
          if (chaseObj && chaseObj.kind === 'snack') { p.eyes = 'happy'; p.blush = true; }
          break;
        }
        case 'sit': p.bodyDy = 2; p.armL = p.armR = 'down'; break;
        case 'watch': p.bodyDy = 2; p.armL = p.armR = 'down'; if ((Math.floor(now / 4) % 3) === 2) p.eyes = 'happy'; break;
        case 'ledge':
          p.legs = 'hang'; p.armL = p.armR = 'down';
          p.legDx = Math.round(Math.sin(now * 2.6) * 1.3); p.legDx2 = -p.legDx; p.legPh = ph(0.5);
          break;
        case 'lie': p.bodyDy = 3; p.legs = 'tucked'; p.armL = p.armR = 'out'; break;
        case 'look': {
          const [lx, ly] = curLook();
          p.lookX = lx; p.lookY = ly;
          if (ly < 0) p.armR = 'raised';
          break;
        }
        case 'ponder': p.armR = 'raised'; p.lookX = 1; p.lookY = -1; break;
        case 'type':
          p.bodyDy = 2; p.eyes = 'focus';
          p.armL = typeFlip ? 'raised' : 'normal'; p.armR = typeFlip ? 'normal' : 'raised';
          if (typeAt) { p.lookX = typeAt.x > cx + 8 * S ? 1 : typeAt.x < cx - 8 * S ? -1 : 0; p.lookY = typeAt.y < posY - 18 * S ? -1 : 0; }
          break;
        case 'scratch': p.armR = ph(0.09) === 0 ? 'up' : 'upOut'; p.eyes = 'squint'; break;
        case 'spin': p.armL = p.armR = 'out'; p.eyes = flip < 0 ? 'none' : 'happy'; p.lookX = p.lookY = 0; break;
        case 'roll': p.eyes = 'squint'; p.armL = p.armR = 'up'; p.legs = 'tucked'; p.lookX = p.lookY = 0; break;
        case 'exercise': {
          const up = (Math.floor(stT / 0.3) & 1) === 0;
          p.armL = p.armR = up ? 'up' : 'down'; p.legs = up ? 'spread' : 'stand'; p.eyes = up ? 'happy' : 'open'; p.lookX = p.lookY = 0;
          break;
        }
        case 'teeter': {
          const f = Math.floor(now / 0.08) & 3;
          p.armL = FLAIL[f]; p.armR = FLAIL[(f + 2) & 3]; p.eyes = 'wide'; p.lookX = facing; p.lookY = 1;
          break;
        }
        case 'peek': p.lookX = -peekSide; p.lookY = 0; break;
        case 'kick': p.legs = 'kick'; p.armL = p.armR = 'out'; p.eyes = 'happy'; break;
        case 'reach': p.armL = p.armR = 'up'; p.lookY = -1; p.legs = ph(0.12) === 0 ? 'stand' : 'walkA'; break;
        case 'aim': p.armL = p.armR = 'down'; p.eyes = 'focus'; break;
        case 'splat': p.armL = p.armR = 'up'; p.legs = 'spread'; p.eyes = 'squint'; p.lookX = p.lookY = 0; break;
        case 'refuse': p.eyes = 'squint'; p.armL = p.armR = 'down'; p.lookX = p.lookY = 0; break;
        case 'snuggle':   // 贴贴：眯着眼笑，朝它那边的手伸过去
          p.eyes = 'happy'; p.lookX = facing; p.lookY = 0;
          if (facing > 0) p.armR = 'out'; else p.armL = 'out';
          break;
        case 'tag': case 'flee': {
          p.legs = walkLegs();
          const a = (Math.floor(walkPh) & 2) === 0;
          if (state === 'tag') { p.armL = a ? 'raised' : 'normal'; p.armR = a ? 'normal' : 'raised'; p.eyes = 'happy'; }
          else { p.armL = p.armR = a ? 'up' : 'upOut'; p.eyes = (Math.floor(now / 0.4) & 1) ? 'happy' : 'wide'; }
          p.lookY = 0;
          break;
        }
        case 'giggle': {
          const a = ph(0.1) === 0;
          p.armL = a ? 'up' : 'raised'; p.armR = a ? 'raised' : 'up'; p.eyes = 'happy'; p.blush = true; p.lookX = p.lookY = 0;
          break;
        }
        case 'sleep':
          if (sleepLedge) {   // 坐在边上睡，腿慢慢晃
            p.legs = 'hang'; p.legDx = Math.round(Math.sin(now * 1.1) * 0.8); p.legDx2 = -p.legDx; p.armL = p.armR = 'down';
          } else { p.legs = 'tucked'; p.bodyDy = 3; }
          p.eyes = now < blushUntil ? 'happy' : 'closed';   // 睡着被摸：嘴角上扬
          if (now < twitchUntil) p.armL = p.armR = 'out';
          p.lookX = p.lookY = 0;
          break;
        case 'doze': {
          const k = stT / stDur;
          p.bodyDy = 2; p.armL = p.armR = 'down'; p.lookX = p.lookY = 0;
          p.eyes = now < jerkUntil ? 'wide' : now < blushUntil ? 'happy' : k < 0.45 ? 'focus' : 'closed';
          break;
        }
        case 'drag': {
          // 被拎起来：往上提手被甩到下面，往下压手举过头；使劲晃就手脚乱蹬
          const intense = shake > S * 70;
          if (dragVY < -S * 140) p.armL = p.armR = 'downOut';
          else if (dragVY > S * 140) p.armL = p.armR = 'up';
          else if (intense) { const f = Math.floor(armPh) & 3; p.armL = FLAIL[f]; p.armR = FLAIL[(f + 2) & 3]; }
          else { const f = Math.floor(armPh) & 3; p.armL = ['upOut', 'out', 'upOut', 'up'][f]; p.armR = ['out', 'upOut', 'up', 'upOut'][f]; }   // 一直扑腾
          // 两组腿交替往前蹬（剪刀腿），再叠上被甩出去的方向
          const kick = Math.sin(kickPh * Math.PI * 2) * (intense ? 1.6 : 1.2);
          p.legs = 'hang';
          p.legDx = clamp(Math.round(legA * 0.7 + kick), -2, 2);
          p.legDx2 = clamp(Math.round(legB * 0.7 - kick), -2, 2);
          p.legPh = Math.floor(kickPh * 2) & 1;
          p.eyes = intense ? 'squint' : stT > 2.5 ? 'happy' : 'wide';
          if (stT > 2.5 && !intense) p.blush = true;
          p.lookX = p.lookY = 0;
          break;
        }
        case 'fall':
          p.lookX = p.lookY = 0;
          if (mood !== 'plain') { p.eyes = 'happy'; p.blush = true; p.armL = p.armR = 'up'; p.legs = 'dangleA'; }
          else { p.eyes = 'wide'; const f = Math.floor(armPh) & 3; p.armL = FLAIL[f]; p.armR = FLAIL[(f + 2) & 3]; hang(); }
          break;
        case 'crouch': p.eyes = mood !== 'plain' ? 'happy' : 'squint'; p.armL = p.armR = 'down'; p.lookX = p.lookY = 0; break;
        case 'land': if (mood !== 'plain') p.eyes = 'happy'; break;
        case 'happy':
          p.eyes = 'happy'; p.blush = true; p.lookX = p.lookY = 0;
          if (mood === 'celebrate') p.armL = p.armR = ph(0.2) === 0 ? 'up' : 'raised';
          else p.armL = p.armR = 'raised';
          break;
        case 'dance': {
          const odd = ph(0.22) === 1;
          p.armL = odd ? 'up' : 'normal'; p.armR = odd ? 'normal' : 'up';
          p.legs = odd ? 'walkA' : 'walkB'; p.facing = odd ? 1 : -1;
          p.lookX = odd ? 1 : -1; p.lookY = 0; p.eyes = 'happy';
          break;
        }
        case 'wave':
          if (facing < 0) p.armL = ph(0.22) === 0 ? 'up' : 'upOut'; else p.armR = ph(0.22) === 0 ? 'up' : 'upOut';
          p.eyes = 'happy'; p.blush = true;
          break;
        case 'alert': {
          const up = ph(0.18) === 0;
          p.armL = up ? 'up' : 'raised'; p.armR = up ? 'raised' : 'up';
          p.eyes = 'wide'; p.lookX = p.lookY = 0;
          break;
        }
        case 'dizzy': p.eyes = 'dizzy'; p.armL = p.armR = 'down'; p.lookX = p.lookY = 0; break;
        case 'yawn': p.armL = p.armR = 'up'; p.eyes = 'closed'; p.lookX = p.lookY = 0; break;
        case 'stretch': p.armL = p.armR = 'up'; p.eyes = 'happy'; p.lookX = p.lookY = 0; break;
        case 'sneeze':
          p.lookX = p.lookY = 0;
          if (stT < 0.5) p.eyes = 'squint'; else if (stT < 0.75) p.eyes = 'wide';
          break;
        case 'eat': {
          const a = ph(0.12) === 0;
          p.armL = a ? 'raised' : 'normal'; p.armR = a ? 'normal' : 'raised';
          p.eyes = chaseObj ? 'heart' : 'happy'; p.blush = true; p.lookX = p.lookY = 0;
          break;
        }
      }
      // 鼠标停在它身上不动：害羞
      if (hotEl === cvs && !mouseDown && now - lastCursorMove > 1.2 && now - lastCursorMove < 10 && REST.has(state)) { p.blush = true; if (p.eyes === 'open') p.eyes = 'happy'; }
      if (dragHover) { p.eyes = 'wide'; p.armL = p.armR = 'raised'; p.lookX = p.lookY = 0; }
      if (now < blushUntil) p.blush = true;
      // 坐下 / 站起 / 转身时身子软软地弹一下，而不是一下子换姿势
      if (p.bodyDy !== lastBodyDy) { sqV += p.bodyDy > lastBodyDy ? -3.5 : 3.5; lastBodyDy = p.bodyDy; }
      if (p.facing !== lastFacing) { if (state !== 'dance') { sqV -= 1.2; leanV += p.facing * 0.8; } lastFacing = p.facing; }
      curBodyDy = p.bodyDy;
      return p;
    }

    // 头顶的小表情泡泡（画在小人的 canvas 里，跟着一起动）
    function drawEmote(Ax, headY) {
      const age = now - emote.born, left0 = emote.until - now;
      if (left0 < 0.25 && (Math.floor(left0 * 24) & 1) === 0) return;   // 快消失时闪一下
      const u = Math.max(2, Math.round(S * dpr / 2));
      let rows, col, pal = null;
      const thought = emote.kind === 'dream';   // 梦：云朵泡泡，下面是一串小圆点
      if (thought) { rows = emote.icon.rows; pal = emote.icon.pal; }
      else if (emote.kind === 'dots') { rows = [['X....', 'X.X..', 'X.X.X'][Math.floor(age / 0.35) % 3]]; col = C.text; }
      else [rows, col] = EMOTES[emote.kind] || EMOTES.q;
      const gw = 7, gh = 6, bw = gw + 4, bh = gh + 4;
      const side = facing >= 0 ? 1 : -1;
      const rise = Math.round(Math.max(0, 1 - age / 0.15) * 2) * u;
      const left = Math.round((Ax + side * 7 * S) * dpr) - Math.round(bw * u / 2);
      const top = Math.max(0, Math.round((headY - 0.5 * S) * dpr) - (bh + 2) * u + rise);
      const m = Math.floor(bw / 2);
      g.fillStyle = C.fill;
      g.fillRect(left + u, top + u, (bw - 2) * u, (bh - 2) * u);
      if (!thought) { g.fillRect(left + (m - 1) * u, top + (bh - 1) * u, 3 * u, u); g.fillRect(left + m * u, top + bh * u, u, u); }
      g.fillStyle = C.border;
      g.fillRect(left + u, top, (bw - 2) * u, u);
      g.fillRect(left, top + u, u, (bh - 2) * u);
      g.fillRect(left + (bw - 1) * u, top + u, u, (bh - 2) * u);
      if (thought) {
        // 记下梦泡泡在网页上的位置：点它可以钻进梦里
        dreamHit = { l: lastLeft + left / dpr, t: lastTop + top / dpr, r: lastLeft + (left + bw * u) / dpr, b: lastTop + (top + (bh + 5) * u) / dpr };
        g.fillRect(left + u, top + (bh - 1) * u, (bw - 2) * u, u);
        // 两个小圆圈，从头顶飘上去
        const d1 = left + (m - side * 2) * u, d2 = left + (m - side * 4) * u, hu = Math.round(u / 2);
        g.fillRect(d1 - u, top + bh * u + hu, 3 * u, 3 * u);
        g.fillStyle = C.fill; g.fillRect(d1, top + (bh + 1) * u + hu, u, u);
        g.fillStyle = C.border; g.fillRect(d2, top + (bh + 3) * u + hu, 2 * u, 2 * u);
      } else {
        g.fillRect(left + u, top + (bh - 1) * u, (m - 2) * u, u);
        g.fillRect(left + (m + 2) * u, top + (bh - 1) * u, (bw - m - 3) * u, u);
        g.fillRect(left + (m - 1) * u, top + bh * u, u, u);
        g.fillRect(left + (m + 1) * u, top + bh * u, u, u);
        g.fillRect(left + m * u, top + (bh + 1) * u, u, u);
      }
      const rw = rows[0].length, rh = rows.length;
      const gx = left + (2 + Math.floor((gw - rw) / 2)) * u, gy = top + (2 + Math.floor((gh - rh) / 2)) * u;
      if (!pal) { glyph(g, rows, col, gx, gy, u); return; }
      for (let r = 0; r < rh; r++)
        for (let c = 0; c < rows[r].length; c++) {
          const ch = rows[r][c];
          if (ch === '.' || !pal[ch]) continue;
          g.fillStyle = pal[ch]; g.fillRect(gx + c * u, gy + r * u, u, u);
        }
    }

    // 鼻涕泡：像素圆圈，跟着呼吸一鼓一鼓地变大，最后啪地破掉
    function drawNose(map, bodyDy) {
      if (now < noseT0) { noseInfo = null; return; }
      const k = Math.min(1, (now - noseT0) / noseLen);
      const r = (0.7 + 1.9 * Math.min(1, k * 1.1)) * (1 + 0.12 * Math.sin(now * 3.4));
      const side = facing >= 0 ? 1 : -1, bt = 4 + bodyDy;
      const ucx = 12 + side * 0.3, vcy = bt + 5 + r * 0.95;   // 小人画布里的格子坐标：两眼正中间的「鼻子」下面，往下鼓
      noseInfo = { x: ucx, y: vcy, r };
      const [X, Y] = map(ucx, vcy);
      const uu = Math.max(1, Math.round(S * dpr / 2)), R = r * S * dpr / uu, Ri = Math.ceil(R + 1);
      const ox = Math.round(X * dpr), oy = Math.round(Y * dpr);
      for (let j = -Ri; j <= Ri; j++)
        for (let i = -Ri; i <= Ri; i++) {
          const d = Math.hypot(i, j);
          if (d > R + 0.5) continue;
          g.fillStyle = d > R - 0.7 ? '#5FA4D8' : 'rgba(214,238,255,0.88)';
          g.fillRect(ox + i * uu, oy + j * uu, uu, uu);
        }
      if (R >= 2) { g.fillStyle = '#FFFFFF'; g.fillRect(ox + Math.round(-R * 0.45) * uu, oy + Math.round(-R * 0.45) * uu, uu, uu); }
    }

    // ================================================================ 主循环
    function tick(dt) {
      stT += dt;
      if (dragHover && now - lastDragOverHit > 0.25) dragHover = false;
      const hr = new Date().getHours();
      energy += ((hr >= 23 || hr < 6 ? 0.3 : 0.55) - energy) * dt / 240;
      updateEyes();
      updateParticles(dt);
      if (emote && now >= emote.until) emote = null;
      if (bubbleText != null && now >= bubbleUntil) clearBubble();
      if (now >= nextChatter) {
        nextChatter = now + 240 + rnd() * 240;
        if (settings.chatty && slot === 0 && !(pomo && pomo.mode === 1) && bubbleText == null && REST.has(state)) say(randomLine(), 4);
      }
      refreshBadge(false);
      if (mouseDown && !dragging && now - downT > 0.35) beginDrag();
      if (now >= nextFly) {
        nextFly = now + 150 + rnd() * 240;
        if (!findObj('fly') && document.visibilityState === 'visible') spawnFly();
      }
      if (now >= nextFoodCheck) {   // 地上有吃的：放下手头的事跑过去
        nextFoodCheck = now + 0.5;
        const sleepy = (state === 'sleep' || state === 'doze') && stT > 1;   // 睡着了也闻得到吃的
        if ((REST.has(state) && state !== 'type') || (state === 'walk' && walkTarget == null) || sleepy) {
          const f = objs.find(o => o.kind === 'snack' && !o.held && !o.eating);
          if (f) { walkTarget = null; walkThen = null; if (sleepy) sqV += 6; emo('bang', 0.8); startChase(f); }
        }
      }
      // 鼠标在它身上停了一小会儿：头顶冒出快捷按钮；它也乖乖站住等你
      if (!barOn && hoverSince >= 0 && hotEl === cvs && now - hoverSince > 0.45 && !mouseDown && grounded() && !dream && !menuOpen) showBar();
      if (touchBarAt && grounded() && state !== 'crouch') { if (now - touchBarAt < 3) showBar(3.5); touchBarAt = 0; }
      if (barOn) {
        if (hotEl === cvs || inBar(lastCursor.x, lastCursor.y)) barKeepUntil = Math.max(barKeepUntil, now + 0.6);
        if (now > barKeepUntil || dream || menuOpen || mouseDown || !grounded()) hideBar();
        else if (state === 'walk' && walkTarget == null) startIdle();
        else if (state === 'idle') stT = Math.min(stT, stDur - 0.3);
      }
      if (!barOn) checkReach();

      switch (state) {
        case 'idle': updIdle(); break;
        case 'walk': updWalk(dt); break;
        case 'chase': updChase(dt); break;
        case 'sit': case 'lie': case 'look': case 'watch': case 'ledge':
          if (checkAfk()) break;
          if (state === 'watch' && watchAt) attn = { x: watchAt.x, y: watchAt.y, until: now + 0.3 };
          if (stT >= stDur) { if (state === 'lie') startStretch(); else startIdle(); }
          break;
        case 'ponder':
          if (checkAfk()) break;
          if (stT >= stDur) { if (rnd() < 0.4) { emo('idea', 1.2); jump('happy', S * 50, true); } else startIdle(); }
          break;
        case 'type': case 'refuse': if (stT >= stDur) startIdle(); break;
        case 'snuggle': updSnuggle(); break;
        case 'tag': updTag(dt); break;
        case 'flee': updFlee(dt); break;
        case 'sleep':
          if (now >= nextFx) { nextFx = now + 1.5; spawnZ(); }
          if (now >= noseT0 + noseLen) { popNose(); sqV += 1; noseT0 = now + 1.2 + rnd(); noseLen = 5 + rnd() * 4; }
          if (now >= nextTwitch) { nextTwitch = now + 9 + rnd() * 10; sleepTwitch(); }
          if (now >= nextDream && !emote) { nextDream = now + 12 + rnd() * 14; startDream(); }
          if (sleepMode === 'afk' && idleState === 'active') wake(false);
          else if (sleepMode === 'nap' && stT >= napDur) { if (idleState !== 'active') sleepMode = 'afk'; else wake(false); }
          break;
        case 'doze':   // 打瞌睡：头越来越低，猛地一点又醒过来，几次之后睡着
          if (stT >= stDur) {
            if (--dozeNods > 0) { sqV += 5; jerkUntil = now + 0.35; setState('doze', 1.8 + rnd() * 1.4); }
            else startSleep('nap');
          }
          break;
        case 'fall': updFall(dt); break;
        case 'splat':
          if (stT >= stDur) { pendingDizzy = true; velX = -splatSide * S * 25; velY = S * 10; startFall('plain'); }
          break;
        case 'crouch': if (stT >= stDur) launch(pendingMood, pendingJumpV, pendingJumpVX, pendingIgnore); break;
        case 'aim':
          if (stT >= stDur) { const a = aimJump; aimJump = null; if (a) launch('happy', a.vy, a.vx, a.ignore); else startIdle(); }
          break;
        case 'land': if (stT >= stDur) afterLand(); break;
        case 'happy':
          if (mood === 'celebrate' && now >= nextFx) { nextFx = now + 0.3; spawnSparkle(); }
          if (stT >= stDur) startIdle();
          break;
        case 'giggle':
          if (now >= nextFx) { nextFx = now + 0.25; if (rnd() < 0.5) spawnNote(); else spawnHeart(); }
          if (stT >= stDur) {
            const f = tagBack; tagBack = null;   // 被抓到笑完了：有时换自己去追它
            if (f && !f.info().hidden && sameLevel(f.info()) && f.react('flee', api)) { mate = f; setState('tag', 5); emo('bang', 0.8); }
            else startHappy(0.5);
          }
          break;
        case 'dance':
          if (now >= nextFx) { nextFx = now + 0.45; spawnNote(); }
          if (stT >= stDur) startIdle();
          break;
        case 'wave': case 'stretch': case 'scratch': case 'reach':
          if (stT >= stDur) {
            if (state === 'reach') reachJump();
            else if (state === 'stretch' && wakeLook) { wakeLook = false; startLook(); emo('q', 1.4); }
            else startIdle();
          }
          break;
        case 'alert':
          if (now >= nextFx) { nextFx = now + 1.2; spawnBang(); }
          if (stT >= stDur) { alertSticky = false; startIdle(); }
          break;
        case 'dizzy':
          if (now >= nextFx) { nextFx = now + 0.12; spawnOrbitStar(); }
          if (stT >= stDur) startIdle();
          break;
        case 'yawn': if (stT >= stDur) startSleep(sleepMode); break;
        case 'sneeze':
          if (!sneezed && stT >= 0.5) {
            sneezed = true;
            sqV += 8; leanV += facing * 4;
            spawn(PUFF, C.puff, (facing > 0 ? 22 : 0) * S, 9 * S, facing * S * 10, 0, 0.5, 0);
            spawn(PUFF, C.puff, (facing > 0 ? 22 : 0) * S, 11 * S, facing * S * 8, S * 2, 0.5, 0);
          }
          if (stT >= stDur) startIdle();
          break;
        case 'eat':
          if (now >= nextFx) { nextFx = now + 0.14; spawnCrumb(); }
          if (chaseObj && chaseObj.kind === 'snack') chaseObj.bite = Math.min(2, Math.floor(stT / 0.5));
          if (stT >= stDur) finishEat();
          break;
        case 'spin':
          flip = Math.cos(stT / 0.45 * Math.PI * 2);
          if (stT >= stDur) { flip = 1; if (rnd() < 0.4) startDizzy(1.1); else startHappy(0.6); }
          break;
        case 'roll': updRoll(); break;
        case 'exercise':
          if (stT >= stDur) { emo('sweat', 1.4); energy = Math.max(0.1, energy - 0.12); startIdle(); }
          break;
        case 'teeter': updTeeter(); break;
        case 'peek': updPeek(); break;
        case 'kick': updKick(); break;
      }
      if (!AIR.has(state)) { checkSupport(dt); keepApart(dt); }
      // 停下来时不是急刹车，而是滑两步慢慢停住
      if (gvx && state !== 'walk' && state !== 'chase') {
        if (grounded() && state !== 'roll' && state !== 'peek') {
          groundMove(0, dt, 9);
          const [lo, hi] = surfaceSpan();
          if (cx < lo || cx > hi) { moveX(clamp(cx, lo, hi) - cx); gvx = 0; }
        } else gvx = 0;
      }
      updDragVel(dt);
      updLimbs(dt);
      updSwing(dt);
      updSpring(dt);
      updObjs(dt);
      if (now >= nextSave) { nextSave = now + 10; savePos(); }
    }

    function render() {
      // canvas 摆到整数设备像素上，像素画才不会糊
      const left = Math.round((cx - CAN_W / 2 * S) * dpr) / dpr;
      const top = Math.round((posY - CAN_FOOT * S) * dpr) / dpr;
      if (left !== lastLeft || top !== lastTop) {
        lastLeft = left; lastTop = top;
        cvs.style.transform = `translate(${left}px,${top}px)`;
      }
      const pose = buildPose(), pk = poseKey(pose);
      const sx = (1 - sq * 0.6) * flip, sy = 1 + sq, k = -lean, Sd = S * dpr;
      const key = pk + '|' + Math.round(24 * Sd * sx) + '|' + Math.round(18 * Sd * sy) + '|' + Math.round(lean * 14 * Sd) +
        '|' + Math.round(anchorY * dpr) + '|' + Math.round(hop * dpr) + '|' + Math.round(rot * 60) + '|' + Math.round(swing * 120);
      const live = !!emote || state === 'sleep';   // 表情和鼻涕泡每帧都在变
      if (key !== lastKey || parts.length || lastParts || live || lastEmote) {
        lastKey = key; lastParts = parts.length; lastEmote = live;
        g.setTransform(1, 0, 0, 1, 0, 0);
        g.clearRect(0, 0, cvs.width, cvs.height);
        g.imageSmoothingEnabled = false;
        // 果冻变形：以脚底（拖拽时以抓住的点）为锚点缩放 + 斜切；打滚时再绕身体中心转
        const Ax = CAN_W / 2 * S, Ay = CAN_FOOT * S + (anchorY - FOOT * S) - hop, ayc = anchorY / S;
        let a = sx * S, b = 0, c = k * sy * S, d = sy * S, e = Ax - a * 12 - c * ayc, f = Ay - d * ayc;
        // 打滚绕身体中心转；被拎着绕抓住的点（钟摆）转
        const ang = rot || swing;
        if (ang) {
          const co = Math.cos(ang), si = Math.sin(ang);
          const px = rot ? Ax : Ax + pivX, py = rot ? Ay - 8 * S - 4 * S * Math.abs(Math.sin(rot)) : Ay;
          [a, b, c, d, e, f] = [co * a - si * b, si * a + co * b, co * c - si * d, si * c + co * d,
            co * (e - px) - si * (f - py) + px, si * (e - px) + co * (f - py) + py];
        }
        g.setTransform(a * dpr, b * dpr, c * dpr, d * dpr, e * dpr, f * dpr);
        g.drawImage(spriteFor(pose, pk), 0, 0);
        g.setTransform(1, 0, 0, 1, 0, 0);
        if (state === 'sleep' && !rot) drawNose((uc, vc) => [e + a * uc + c * vc, f + b * uc + d * vc], pose.bodyDy);
        else noseInfo = null;
        const u = Math.max(2, Math.round(Sd / 2));
        for (const q of parts) {
          if (q.life < 0.25 && (Math.floor(q.life * 24) & 1) === 0) continue;   // 快消失时闪烁
          glyph(g, q.shape, q.color, Math.round((SPR_L * S + q.x) * dpr), Math.round((SPR_T * S + q.y) * dpr), u);
        }
        dreamHit = null;
        if (emote && bubbleText == null) drawEmote(Ax, rot ? Ay - 20 * S : Ay + sy * S * (4 + pose.bodyDy - ayc));   // 有文字气泡时先不画表情，免得叠在一起
      }
      layoutOverlay();
    }

    function frame(ts) {
      if (retired) { raf = 0; return; }
      raf = requestAnimationFrame(frame);
      const t = ts / 1000;
      // 平时都跑满 60 帧（顺滑）；只有睡着了、四周也没东西在动时降到 30 帧省电
      const quiet = !dream && state === 'sleep' && !mouseDown && !grabbed && !dragHover &&
        Math.abs(sqV) < 0.3 && Math.abs(leanV) < 0.3 && now - lastScrollT > 0.5 && !objs.some(o => !o.rest);
      const minDt = quiet ? 0.033 : 0;
      if (t - lastFrameT < minDt - 0.004) return;
      let dt = t - lastFrameT;
      lastFrameT = t; now = t;
      if (dt > 0.05) dt = 0.05;
      if (dt < 0) dt = 0;
      tick(dt);
      render();
      if (dream) runDream(dt);
    }
    function startLoop() { if (!raf) { lastFrameT = T(); raf = requestAnimationFrame(frame); } }
    function stopLoop() { if (raf) { cancelAnimationFrame(raf); raf = 0; } }

    // ================================================================ 鼠标 / 触摸
    function hitPet(x, y) {
      if (hiddenNow) return false;
      if (rot) return Math.hypot(x - cx, y - (posY - 8 * S)) < 10 * S;
      const sx = Math.max(0.35, Math.abs((1 - sq * 0.6) * flip)), sy = 1 + sq, k = -lean;
      const Ay = posY + (anchorY - FOOT * S) - hop;
      if (swing) {   // 还在晃：把点反着转回去再判断
        const px = cx + pivX, co = Math.cos(-swing), si = Math.sin(-swing), dx = x - px, dy = y - Ay;
        x = px + co * dx - si * dy; y = Ay + si * dx + co * dy;
      }
      const yp = y - Ay;
      const cyc = yp / (sy * S) + anchorY / S;
      const cxc = (x - cx - k * yp) / (sx * S) + 12;
      return cxc >= 1 && cxc <= 23 && cyc >= 3 && cyc <= 17.6;
    }
    function objAt(x, y) {
      if (hiddenNow) return null;
      for (let i = objs.length - 1; i >= 0; i--) {
        const o = objs[i];
        if (o.kind === 'fly' || o.gone) continue;
        const hw = objW(o) / 2 + 3, hh = objH(o);
        if (x >= o.x - hw && x <= o.x + hw && y >= o.y - hh - 3 && y <= o.y + 3) return o;
      }
      return null;
    }
    function setHotEl(el) {
      if (el === hotEl) return;
      if (hotEl) hotEl.style.pointerEvents = 'none';
      hotEl = el;
      if (el) el.style.pointerEvents = 'auto';
    }
    function inMenu(x, y) {
      if (!menuOpen) return false;
      const r = menuEl.getBoundingClientRect();
      return x >= r.left - 2 && x <= r.right + 2 && y >= r.top - 2 && y <= r.bottom + 2;
    }

    function onPointerMove(e) {
      now = T();
      const t = e.timeStamp / 1000, x = e.clientX, y = e.clientY;
      const span = t - lastMoveT, d = Math.hypot(x - lastCursor.x, y - lastCursor.y);
      cursorSpeed = span > 0 && span < 0.2 ? cursorSpeed * 0.6 + (d / span) * 0.4 : 0;
      // 你离开很久后回来：跟你打个招呼
      if (now - lastCursorMove > 90 && REST.has(state) && !hiddenNow && rnd() < 0.5) {
        facing = x >= cx ? 1 : -1; startWave(1.4); emo('note', 1.4);
      }
      lastMoveT = t; lastCursor = { x, y }; lastCursorMove = now;
      if (dream) return;

      if (mouseDown) {
        if (e.pointerId !== downId) return;
        if (!dragging && (Math.abs(x - downPt.x) > 4 || Math.abs(y - downPt.y) > 4)) beginDrag();
        if (dragging) {
          cx = x - grabDX; posY = y - grabDY;
          trail.push([t, x, y]);
          while (trail.length > 2 && t - trail[0][0] > 0.09) trail.shift();
        }
        return;
      }
      if (grabbed) {
        if (e.pointerId !== downId) return;
        grabbed.x = x - grabDX; grabbed.y = y - grabDY;
        trail.push([t, x, y]);
        while (trail.length > 2 && t - trail[0][0] > 0.09) trail.shift();
        if (grabbed.kind === 'snack') attn = { x, y, until: now + 0.5 };
        return;
      }
      if (hiddenNow || e.pointerType === 'touch') return;
      const overD = overDreamBubble(x, y);   // 鼠标放在梦泡泡上：泡泡不消失，鼠标变小手
      if (overD) emote.until = Math.max(emote.until, now + 1.5);
      const over = !overD && hitPet(x, y);
      const ob = over || overD ? null : objAt(x, y);
      setHotEl(over || overD ? cvs : ob ? ob.el : null);
      cvs.classList.toggle('dreamy', overD);
      // 鼠标在它身上（或在按钮条上）：记下来，停够一会儿就冒出按钮条
      if (over || inBar(x, y)) { if (hoverSince < 0) hoverSince = now; barKeepUntil = Math.max(barKeepUntil, now + 0.7); }
      else hoverSince = -1;
      // 鼠标从身边飞快划过：吓一跳
      if (cursorSpeed > S * 500 && (REST.has(state) || state === 'walk') && now - lastStartle > 25) {
        const dx = x - cx, dy = y - (posY - 9 * S);
        if (dx * dx + dy * dy < (12 * S) * (12 * S)) { lastStartle = now; if (rnd() < 0.5) { resetPlans(); startle(); } }
      }
      // 摸摸：要在它身上来回蹭（方向来回换两次以上）才算；只是从身上划过去不算
      if (over && cursorSpeed < S * 450) {
        if (lastHover) {
          const mdx = x - lastHover.x, mdy = y - lastHover.y;
          if (t - lastPetMove > 0.6) { petDist = 0; rubFlips = []; rubDir = rubDirY = 0; }
          petDist += Math.abs(mdx) + Math.abs(mdy);
          lastPetMove = t;
          if (Math.abs(mdx) >= 2) { const dd = sign(mdx); if (rubDir && dd !== rubDir) rubFlips.push(t); rubDir = dd; }
          if (Math.abs(mdy) >= 2) { const dd = sign(mdy); if (rubDirY && dd !== rubDirY) rubFlips.push(t); rubDirY = dd; }
          rubFlips = rubFlips.filter(ft => t - ft < 1.2);
          leanV += mdx / (S * 60);
          if (rubFlips.length >= 2 && petDist > 14 * S) { petDist = 0; rubFlips = []; onPetted(); }
        }
        lastHover = { x, y };
      } else { lastHover = null; petDist = 0; rubFlips = []; rubDir = rubDirY = 0; }
    }

    function trailVel() {
      let vx = 0, vy = 0;
      if (trail.length >= 2) {
        const a = trail[0], b = trail[trail.length - 1], span = b[0] - a[0];
        if (span > 0.01 && T() - b[0] < 0.08) { vx = (b[1] - a[1]) / span; vy = (b[2] - a[2]) / span; }
      }
      return [vx, vy];
    }

    function onPointerDown(e) {
      now = T();
      if (menuOpen) { if (inMenu(e.clientX, e.clientY)) return; closeMenu(); }
      if (e.button !== 0 || dream) return;
      const x = e.clientX, y = e.clientY;
      if (overDreamBubble(x, y)) { e.preventDefault(); e.stopImmediatePropagation(); openDream(); return; }   // 钻进梦里
      if (hitPet(x, y)) {
        e.preventDefault(); e.stopImmediatePropagation();
        setHotEl(cvs);
        try { cvs.setPointerCapture(e.pointerId); } catch (err) { /* 没关系 */ }
        mouseDown = true; dragging = false; downId = e.pointerId;
        downPt = { x, y }; downT = now;
        grabDX = x - cx; grabDY = y - posY;
        trail = [[e.timeStamp / 1000, x, y]];
        sqV -= 2;   // 按下去软软的
        return;
      }
      const o = objAt(x, y);
      if (!o) return;
      e.preventDefault(); e.stopImmediatePropagation();
      setHotEl(o.el);
      try { o.el.setPointerCapture(e.pointerId); } catch (err) { /* 没关系 */ }
      grabbed = o; o.held = true; o.vx = o.vy = 0; o.rest = false; o.touched = now;
      if (o.eating) o.eating = false;
      o.el.classList.add('grab');
      downId = e.pointerId; grabDX = x - o.x; grabDY = y - o.y;
      trail = [[e.timeStamp / 1000, x, y]];
    }

    function releaseObj(e, cancel) {
      const o = grabbed;
      grabbed = null;
      o.held = false; o.touched = now;
      o.el.classList.remove('grab');
      try { o.el.releasePointerCapture(e.pointerId); } catch (err) { /* 没关系 */ }
      const [vx, vy] = cancel ? [0, 0] : trailVel();
      const max = S * 700;
      o.vx = clamp(vx, -max, max); o.vy = clamp(vy, -max, max);
      if (o.kind === 'snack' && !cancel && hitPet(e.clientX, e.clientY) && grounded() && state !== 'sleep') { startEat(o); return; }   // 直接喂到嘴里
      if (o.kind === 'ball' && Math.hypot(o.vx, o.vy) > S * 100 && grounded() && state !== 'sleep' && state !== 'eat') {
        emo('bang', 0.8); startChase(o, 2 + Math.floor(rnd() * 4));
      }
    }

    function onPointerUp(e) {
      if (grabbed && e.pointerId === downId) {
        now = T();
        e.preventDefault(); e.stopImmediatePropagation();
        releaseObj(e, false);
        setHotEl(null);
        return;
      }
      if (!mouseDown || e.pointerId !== downId) return;
      now = T();
      e.preventDefault(); e.stopImmediatePropagation();
      mouseDown = false;
      try { cvs.releasePointerCapture(e.pointerId); } catch (err) { /* 没关系 */ }
      if (dragging) endDrag(); else { onClickPet(); if (e.pointerType === 'touch') touchBarAt = now; }   // 触屏点一下：落地后冒出按钮条
      if (e.pointerType === 'touch' || !hitPet(e.clientX, e.clientY)) setHotEl(null);
    }

    function onPointerCancel(e) {
      if (grabbed && e.pointerId === downId) { now = T(); releaseObj(e, true); setHotEl(null); return; }
      if (!mouseDown || e.pointerId !== downId) return;
      now = T();
      mouseDown = false;
      if (dragging) endDrag();
      setHotEl(null);
    }

    function beginDrag() {
      dragging = true;
      hideBar();
      resetPlans();
      if (perch) { perch = null; }
      sitOff = 0;
      velX = velY = 0;
      sqV += 4; shake = 0; shakeAcc = 0; legAV += 6;
      setState('drag', 1e9);
      cvs.classList.add('grab');
    }

    function endDrag() {
      dragging = false;
      cvs.classList.remove('grab');
      const [vx, vy] = trailVel();
      const max = S * 500;
      velX = clamp(vx, -max, max); velY = clamp(vy, -max, max);
      if (shakeAcc > 1.2) pendingDizzy = true;   // 被晃晕了
      startFall('plain');
    }

    function onClickPet() {
      const t = now;
      clicks.push(t);
      clicks = clicks.filter(x => t - x <= 2.5);
      energy = Math.min(1, energy + 0.05);
      if (state === 'sleep' || state === 'doze') { wake(true); return; }
      if (state === 'alert') { alertSticky = false; say('收到~', 1.2); sqV -= 3; startIdle(); return; }
      const air = !grounded();
      if (clicks.length >= 6) {
        clicks = [];
        if (air) pendingDizzy = true; else { resetPlans(); spawnSweat(); emo('anger', 1.5); startDizzy(2.2); }
        return;
      }
      const dbl = t - lastClickT < 0.4;
      lastClickT = t;
      if (air) { if (dbl) pendingDance = true; return; }
      resetPlans();
      const f = friend();
      if (dbl) { startDance(); if (f) f.react('dance', api); return; }   // 跳舞：另一只多半跟着跳
      spawnHeart();
      jump('happy', S * 95, true);
      if (f) f.react('look', api);
    }

    function onPetted() {
      spawnHeart();
      sqV += 1.5;
      energy = Math.min(1, energy + 0.03);
      if (state === 'sleep' || state === 'doze') { blushUntil = now + 2.5; return; }   // 睡梦里笑一下，接着睡
      pets.push(now);
      pets = pets.filter(t => now - t < 2.5);
      if (pets.length >= 4 && grounded() && state !== 'giggle' && state !== 'eat') {   // 挠痒痒：笑得直晃
        pets = []; resetPlans(); setState('giggle', 1.6); nextFx = now; blushUntil = now + 2.5; return;
      }
      if (REST.has(state) || state === 'walk' || state === 'happy' || state === 'wave') {
        resetPlans(); mood = 'plain'; startHappy(1.4);
      } else blushUntil = now + 1.5;
      const f = friend(); if (f) f.react('jealous', api);   // 另一只看见了：吃醋
    }

    function onClick(e) {
      // 点在桌宠 / 小物件上的 click 不让网页收到（菜单里的点击照常）
      if (e.target === host) { if (!inMenu(e.clientX, e.clientY)) { e.preventDefault(); e.stopImmediatePropagation(); } return; }
      if (e.type !== 'click' || hiddenNow || dream) return;
      // 你在网页上点了哪里，它会看过去，偶尔还跑过去瞧瞧
      now = T();
      attn = { x: e.clientX, y: e.clientY, until: now + 2 };
      if (REST.has(state) && state !== 'type' && now - lastClickWalk > 30 && rnd() < 0.2) {
        lastClickWalk = now;
        const [lo, hi] = surfaceSpan(), tx = clamp(e.clientX, lo, hi);
        if (Math.abs(tx - cx) > 15 * S) walkTo(tx, S * 13, () => { emo('q', 1.2); setState('look', 1.6); lookSeq = [[0, 0, -1]]; });
      }
    }

    function onContextMenu(e) {
      if (dream || inMenu(e.clientX, e.clientY) || !hitPet(e.clientX, e.clientY)) return;
      e.preventDefault(); e.stopImmediatePropagation();
      now = T();
      openMenu(e.clientX, e.clientY);
    }

    function onTouchStart(e) {
      const tt = e.touches[0];
      if (dream) { if (e.target === host) e.preventDefault(); return; }   // 玩梦里的游戏时别滚动网页
      if (tt && overDreamBubble(tt.clientX, tt.clientY)) { e.preventDefault(); return; }
      if (tt && !inMenu(tt.clientX, tt.clientY) && (hitPet(tt.clientX, tt.clientY) || objAt(tt.clientX, tt.clientY))) e.preventDefault();   // 别滚动网页
    }

    // 滚轮 / 触控板滚动时被颠一颠
    let wheelAcc = 0, wheelT = 0, lastWheelHop = -10;
    function onWheel(e) {
      if (hiddenNow || menuOpen || dream) return;
      now = T();
      if (!(REST.has(state) || state === 'walk')) return;
      const dy = clamp(e.deltaMode === 1 ? e.deltaY * 16 : e.deltaY, -400, 400);
      leanV -= clamp(dy / (S * 300), -0.4, 0.4);
      sqV -= Math.min(0.6, Math.abs(dy) / (S * 200));
      if (now - wheelT > 0.15) wheelAcc = 0;
      wheelT = now; wheelAcc += Math.abs(dy);
      if (wheelAcc > 120 * S && now - lastWheelHop > 8 && !perch) {
        lastWheelHop = now; wheelAcc = 0;
        if (rnd() < 0.3) { if (rnd() < 0.5) emo('sweat', 1.2); resetPlans(); jump('plain', S * 45, false); }
      }
    }

    // 你在输入框里打字：它坐下来陪你，手跟着一下一下动
    function onKeyDown(e) {
      if (dream) { now = T(); dreamKey(e, true); return; }
      if (menuOpen && e.key === 'Escape') { closeMenu(); return; }
      if (hiddenNow || e.target === host) return;
      const t = e.target;
      const editable = t && (t.isContentEditable || t.tagName === 'TEXTAREA' ||
        (t.tagName === 'INPUT' && !/^(password|checkbox|radio|button|submit|reset|range|color|file|hidden|image)$/i.test(t.type || '')));
      if (!editable) return;
      now = T();
      if (now - typeT > 2.5) typeCount = 0;
      typeT = now; typeCount++; typeFlip ^= 1;
      try { const r = t.getBoundingClientRect(); typeAt = { x: r.left + Math.min(r.width, 320) / 2, y: r.top + r.height / 2 }; } catch (err) { /* 没关系 */ }
      if (state === 'type') { stDur = Math.max(stDur, stT + 2.5); sqV += 0.4; }
      else if (typeCount >= 5 && REST.has(state)) { setState('type', 3); }
    }

    // 选中一段文字：它会看过去
    function onSelection() {
      if (hiddenNow) return;
      const sel = document.getSelection();
      if (!sel || sel.isCollapsed || !sel.rangeCount) return;
      let r;
      try { r = sel.getRangeAt(0).getBoundingClientRect(); } catch (e) { return; }
      if (!r || r.width < 2) return;
      now = T();
      attn = { x: r.left + r.width / 2, y: r.top + r.height / 2, until: now + 3 };
      if (REST.has(state) && now - lastSelReact > 25 && rnd() < 0.35) { lastSelReact = now; emo(rnd() < 0.5 ? 'q' : 'bang', 1.2); }
    }

    // ================================================================ 拖文字 / 文件给它吃
    function onDragOver(e) {
      if (!hitPet(e.clientX, e.clientY)) return;
      e.preventDefault();
      try { e.dataTransfer.dropEffect = 'copy'; } catch (err) { /* 只读时忽略 */ }
      now = T(); lastDragOverHit = now;
      if (!dragHover) { dragHover = true; sqV += 3; if (grounded()) emo('q', 1.2); }
    }

    function onDrop(e) {
      if (!hitPet(e.clientX, e.clientY)) return;
      e.preventDefault(); e.stopImmediatePropagation();
      now = T(); dragHover = false;
      const dt = e.dataTransfer;
      const files = dt && dt.files ? Array.from(dt.files) : [];
      if (files.length) {
        const total = files.reduce((s, f) => s + f.size, 0);
        eat(files.length === 1 ? `吃掉了 ${files[0].name}（${fmtSize(total)}）` : `一口吃掉 ${files.length} 个文件，一共 ${fmtSize(total)}`);
        return;
      }
      const text = ((dt && (dt.getData('text/plain') || dt.getData('text/uri-list'))) || '').trim();
      if (/^https?:\/\/\S+$/i.test(text)) {
        let site = '';
        try { site = new URL(text).hostname; } catch (err) { /* 不是网址 */ }
        eat(site ? `吃掉一个链接，${site} 味的` : '吃掉一个链接~');
      } else if (text) {
        const chars = text.replace(/\s/g, '').length, lines = text.split('\n').length;
        eat(`嚼嚼… ${chars} 个字、${lines} 行`);
      } else eat('嚼嚼…没尝出味道');
    }

    function eat(text) {
      say(text, 4);
      sqV -= 4; emo('heart', 1.2);
      if (grounded()) { resetPlans(); setState('eat', 1.1); nextFx = now; }
    }

    function fmtSize(b) {
      if (b < 1024) return b + ' B';
      if (b < 1024 * 1024) return (b / 1024).toFixed(1) + ' KB';
      return (b / 1024 / 1024).toFixed(1) + ' MB';
    }

    // ================================================================ 右键菜单
    function item(label, act, check) {
      return h('div', { class: 'item' + (check == null ? '' : ' check' + (check ? ' on' : '')), 'data-act': act }, label);
    }
    function chip(label, act, on) { return h('span', { class: 'chip' + (on ? ' on' : ''), 'data-act': act }, label); }

    function openMenu(x, y) { hideBar(); menuOpen = true; menuAt = { x, y }; renderMenu('main'); }
    function closeMenu() {
      if (!menuOpen) return;
      menuOpen = false; remindInputs = null;
      menuEl.style.display = 'none';
      menuEl.replaceChildren();
    }
    function placeMenu() {
      menuEl.style.display = 'block';
      const r = menuEl.getBoundingClientRect(), W = vw(), H = vh();
      let x = menuAt.x + 2, y = menuAt.y + 2;
      if (x + r.width > W - 6) x = Math.max(6, menuAt.x - r.width - 2);
      if (y + r.height > H - 6) y = Math.max(6, H - r.height - 6);
      menuEl.style.transform = `translate(${Math.round(x)}px,${Math.round(y)}px)`;
    }

    function renderMenu(mode) {
      remindInputs = null;
      if (mode === 'remind') {
        const min = h('input', { type: 'number', min: '1', max: '1440', value: '10' });
        const txt = h('input', { type: 'text', maxlength: '100', placeholder: '时间到啦！' });
        menuEl.replaceChildren(
          h('div', { class: 'title' }, '提醒我'),
          h('label', { class: 'field' }, '几分钟后提醒？', h('span', { class: 'inline' }, min, '分钟')),
          h('label', { class: 'field' }, '提醒什么？', txt),
          h('div', { class: 'btns' }, h('span', { class: 'btn', 'data-act': 'close' }, '取消'), h('span', { class: 'btn primary', 'data-act': 'remindOk' }, '好的')));
        remindInputs = { min, txt };
        placeMenu();
        setTimeout(() => { try { txt.focus(); } catch (e) { /* 没关系 */ } }, 0);
        return;
      }
      const pomoRow = [h('span', { class: 'lab' }, '番茄钟'), chip('专注 25', 'pomo:1:25'), chip('专注 50', 'pomo:1:50'), chip('休息 5', 'pomo:2:5')];
      if (pomo) pomoRow.push(chip('停止', 'pomoStop'));
      const kids = [
        h('div', { class: 'row' }, h('span', { class: 'lab' }, '陪它玩'), chip('喂零食', 'feed'),
          chip(worldFind('ball') ? '收起球' : '扔个球', 'ball'), chip('跳舞', 'dance')),
        item(state === 'sleep' || state === 'doze' ? '叫醒它' : '睡一会儿', 'sleep'),
        item(state === 'sleep' ? '钻进它的梦里玩' : '做个梦（横版小游戏）', 'dreamGame'),
        item('说句话', 'say'),
        h('hr'),
        h('div', { class: 'row' }, pomoRow),
        item(reminderCount ? `提醒我…（已有 ${reminderCount} 个）` : '提醒我…', 'remind'),
      ];
      if (reminderCount) kids.push(item(`清空提醒（${reminderCount} 个）`, 'clearRem'));
      kids.push(
        h('hr'),
        item('跟着鼠标走', 'follow', settings.follow),
        item('两只 Clawd（会一起玩）', 'count', (settings.count || 1) >= 2),
        item('偶尔自言自语', 'chatty', settings.chatty),
        item('久坐提醒', 'health', settings.health),
        h('div', { class: 'row' }, h('span', { class: 'lab' }, '大小'), SIZE_NAMES.map((n, i) => chip(n, 'size:' + i, i === (settings.size | 0)))),
        h('hr'),
        item(`在 ${location.hostname || '这个页面'} 上隐藏`, 'hideSite'),
        item('关掉桌宠（所有网页）', 'off'));
      menuEl.replaceChildren(...kids);
      placeMenu();
    }

    function menuAction(act) {
      const [a, b, c] = act.split(':');
      switch (a) {
        case 'say': say(randomLine(), 4); break;
        case 'feed': feedNow(); break;
        case 'dreamGame': closeMenu(); openDream(); return;
        case 'ball': toggleBall(); break;
        case 'dance': if (grounded()) { resetPlans(); startDance(); } break;
        case 'sleep': if (state === 'sleep' || state === 'doze') wake(false); else if (grounded()) startYawn('nap', 90 + rnd() * 60); break;
        case 'pomo': startPomo(+b, +c); break;
        case 'pomoStop': send({ cmd: 'pomoStop' }); pomo = null; refreshBadge(true); say('好，计时停了', 2); break;
        case 'remind': renderMenu('remind'); return;
        case 'remindOk': {
          if (!remindInputs) break;
          const m = clamp(parseInt(remindInputs.min.value, 10) || 10, 1, 1440);
          const text = remindInputs.txt.value.trim() || '时间到啦！';
          send({ cmd: 'remind', minutes: m, text });
          reminderCount++;
          say(`好，${m} 分钟后提醒你`, 2.5);
          if (grounded()) { resetPlans(); jump('happy', S * 60, true); }
          break;
        }
        case 'clearRem': send({ cmd: 'clearReminders' }); reminderCount = 0; say('提醒都清掉了', 2); break;
        case 'count':
          settings.count = (settings.count || 1) >= 2 ? 1 : 2; saveSettings(); renderMenu('main');
          if (settings.count < 2 && slot > 0) closeMenu();   // 第二只自己被关掉了
          return;
        case 'follow': case 'chatty': case 'health':
          settings[a] = !settings[a]; saveSettings(); renderMenu('main'); return;
        case 'size': settings.size = +b; applySize(); sqV += 5; saveSettings(); renderMenu('main'); return;
        case 'hideSite':
          say('好，这个网站我先不来了~ 想我就点工具栏里的 Clawd', 2.5);
          later(1.8, s => { s.hiddenSites = Array.from(new Set([...(s.hiddenSites || []), location.hostname])); });
          break;
        case 'off':
          say('拜拜~ 点工具栏里的 Clawd 图标可以叫我回来', 2.5);
          later(1.8, s => { s.enabled = false; });
          break;
      }
      closeMenu();
    }

    menuEl.addEventListener('click', e => {
      const el = e.target.closest('[data-act]');
      if (el) { now = T(); menuAction(el.getAttribute('data-act')); }
    });
    menuEl.addEventListener('keydown', e => {
      e.stopPropagation();   // 别让网页把输入当成快捷键
      if (e.key === 'Enter' && remindInputs) { e.preventDefault(); now = T(); menuAction('remindOk'); }
      else if (e.key === 'Escape') closeMenu();
    });
    for (const t of ['keyup', 'keypress', 'input', 'pointerdown', 'mousedown', 'pointerup', 'mouseup', 'click', 'contextmenu'])
      menuEl.addEventListener(t, e => e.stopPropagation());

    function saveSettings() { sset({ settings: Object.assign({}, settings) }); }
    function later(sec, mutate) {
      setTimeout(async () => {
        const v = await sget('settings');
        const s = Object.assign({}, DEF, v.settings || {});
        mutate(s);
        settings = s;
        sset({ settings: s });
        applyVisibility();
      }, sec * 1000);
    }

    function startPomo(mode, minutes) {
      const end = Date.now() + minutes * 60000;
      pomo = { mode, end };
      world.lastPomoEnd = end;   // 是这一只自己开的：另一只就别再报一遍了
      refreshBadge(true);
      send({ cmd: 'pomo', mode, minutes, end });
      announcePomo(mode, minutes, true);
    }
    function announcePomo(mode, minutes, hopToo) {
      if (mode === 1) { say(`专注 ${minutes} 分钟，开始！`, 2.5); if (hopToo && grounded()) { resetPlans(); jump('happy', S * 70, true); } }
      else say(`休息 ${minutes} 分钟，起来走走~`, 3);
    }

    function randomLine() {
      const hr = new Date().getHours();
      if (rnd() < 0.25) {
        if (hr >= 23 || hr < 5) return '很晚啦，早点休息吧';
        if (hr === 11 || hr === 12) return '午饭时间到！';
        if (hr === 17) return '快下班啦~';
      }
      return pick(LINES);
    }

    // ================================================================ 梦境小游戏：在 Clawd 的梦里横版跑跳
    // ← → / A D 走，空格 / ↑ 跳（按久一点跳得高）；收集它梦到的东西、踩扁 bug、跑到枕头那儿就睡个好觉。
    // 画面是 256x128 的像素小画布（1 像素 = Clawd 的 1 格），再整数倍放大。
    const DW = 256, DH = 128;
    const DSKY = ['#141833', '#191D3D', '#1F2248', '#262753', '#2E2C5D', '#373167', '#3F3670', '#473B78'];
    const BUG_F = [['..D.D..', '.GGGGG.', 'GGWGWGG', 'GGGGGGG', '.D...D.'], ['..D.D..', '.GGGGG.', 'GGWGWGG', 'GGGGGGG', 'D..D..D']];
    const BUG_FLAT = ['.GGGGG.', 'GGGGGGG'];
    const BUG_PAL = { G: '#5BAF62', D: '#2C3A2E', W: '#FFFFFF' };
    let dream = null, dreamDom = null, dreamHit = null;

    function seeded(seed) {
      let a = seed >>> 0;
      return () => {
        a = (a + 0x6D2B79F5) >>> 0;
        let t = a;
        t = Math.imul(t ^ (t >>> 15), t | 1);
        t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
        return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
      };
    }

    // 随机生成一关：一段段云朵地面（中间有缺口）、飘着的云、要收集的东西、bug、弹簧球、终点的枕头
    function genLevel(seed) {
      const R = seeded(seed);
      const L = { ground: [], clouds: [], items: [], bugs: [], springs: [], goal: null, len: 0 };
      const item = (x, y) => L.items.push({ x, y, got: false, ph: R() * 6 });
      L.ground.push({ x: -40, w: 190, y: 104 });
      let x = 150, y = 104;
      while (x < 2500) {
        const gap = Math.round(14 + R() * 20);
        const ny = clamp(y + (R() < 0.35 ? -10 : R() < 0.5 ? 10 : 0), 88, 112);
        for (let k = 1; k <= 3; k++) item(x + gap * k / 4, Math.min(y, ny) - 14 - Math.sin(k / 4 * Math.PI) * 10);   // 缺口上方一道弧
        x += gap;
        const w = 56 + Math.floor(R() * 110);
        L.ground.push({ x, w, y: ny });
        if (R() < 0.55) {   // 上面飘一朵云，云上有东西
          const cw = 28 + Math.floor(R() * 26), cy = ny - 25 - Math.floor(R() * 4);
          const cxp = x + Math.floor(R() * Math.max(1, w - cw));
          L.clouds.push({ x: cxp, w: cw, y: cy });
          for (let k = 0; k < Math.floor(cw / 12); k++) item(cxp + 6 + k * 12, cy - 8);
          if (R() < 0.4 && w > 70) {   // 再高一层：得踩弹簧球才上得去
            const c2x = cxp + (R() < 0.5 ? -12 : 12), c2y = cy - 36;
            L.clouds.push({ x: c2x, w: 32, y: c2y });
            for (let k = 0; k < 3; k++) item(c2x + 6 + k * 10, c2y - 8);
            const sx = cxp - x > 30 ? cxp - 14 : cxp + cw + 14;
            if (sx > x + 6 && sx < x + w - 6) L.springs.push({ x: sx, y: ny, sq: 0 });
          }
        } else if (w > 70) for (let k = 0; k < 3; k++) item(x + w / 2 - 10 + k * 10, ny - 12);
        if (w > 80 && R() < 0.65) L.bugs.push({ x: x + w / 2, y: ny, minX: x + 6, maxX: x + w - 6, dir: R() < 0.5 ? -1 : 1, alive: true, flat: 0 });
        x += w; y = ny;
      }
      x += 22;
      L.ground.push({ x, w: 160, y: 104 });
      L.goal = { x: x + 84, y: 104 };
      L.len = x + 160;
      return L;
    }

    function buildDreamDom() {
      const title = h('span', { class: 'dtitle' }), hud = h('span', { class: 'dhud' });
      const xBtn = h('span', { class: 'dx', title: '醒来（Esc）' }, '×');
      const cv = h('canvas', { class: 'dcv' });
      const msgText = h('div', { class: 'dmt' }), msgBtns = h('div', { class: 'dmb' });
      const msg = h('div', { class: 'dmsg' }, msgText, msgBtns);
      const pad = h('div', { class: 'dpad' },
        h('span', { class: 'dbtn', 'data-k': 'L' }, '◀'), h('span', { class: 'dbtn', 'data-k': 'R' }, '▶'),
        h('span', { class: 'dtip' }, '← → 走 · 空格跳（按久跳得高）· Esc 醒来'), h('span', { class: 'dbtn big', 'data-k': 'J' }, '跳'));
      const panel = h('div', { class: 'dream' }, h('div', { class: 'dhead' }, title, hud, xBtn), h('div', { class: 'dstage' }, cv, msg), pad);
      const back = h('div', { class: 'dreamBack' });
      wrap.append(back, panel);
      const off = document.createElement('canvas');
      off.width = DW; off.height = DH;
      // 用 pointerup 而不是 click：网页那一层会把落在 Clawd 身上的 click 都拦下来
      xBtn.addEventListener('pointerup', () => { now = T(); closeDream(dream && dream.over ? dream.over : 'quit'); });
      msgBtns.addEventListener('pointerup', e => { const b = e.target.closest('[data-act]'); if (b) { now = T(); dreamAct(b.getAttribute('data-act')); } });
      for (const b of pad.querySelectorAll('[data-k]')) {
        const k = b.getAttribute('data-k');
        const on = e => { e.preventDefault(); try { b.setPointerCapture(e.pointerId); } catch (err) { /* 没关系 */ } b.classList.add('on'); press(k, true); };
        const offFn = () => { if (!b.classList.contains('on')) return; b.classList.remove('on'); press(k, false); };
        b.addEventListener('pointerdown', on);
        b.addEventListener('pointerup', offFn); b.addEventListener('pointercancel', offFn); b.addEventListener('lostpointercapture', offFn);
      }
      // 梦里的点击、滚轮、按键都别漏给网页
      for (const el of [panel, back])
        for (const t of ['pointerdown', 'pointerup', 'mousedown', 'mouseup', 'click', 'dblclick', 'contextmenu', 'touchstart', 'keydown', 'keyup'])
          el.addEventListener(t, e => e.stopPropagation());
      for (const el of [panel, back]) {
        el.addEventListener('wheel', e => { e.preventDefault(); e.stopPropagation(); }, { passive: false });
        el.addEventListener('contextmenu', e => e.preventDefault());
      }
      return { back, panel, title, hud, cv, msg, msgText, msgBtns, view: cv.getContext('2d'), off, offCtx: off.getContext('2d') };
    }

    function sizeDream() {
      if (!dreamDom) return;
      const k = Math.max(1, Math.floor(Math.min((vw() - 40) / DW, (vh() - 150) / DH) * dpr));
      const cv = dreamDom.cv;
      cv.width = DW * k; cv.height = DH * k;
      cv.style.width = DW * k / dpr + 'px'; cv.style.height = DH * k / dpr + 'px';
      dreamDom.panel.style.width = DW * k / dpr + 20 + 'px';
    }

    function openDream() {
      if (dream || hiddenNow || (world.dreamOwner && world.dreamOwner !== api)) return;   // 同一时间只开一个梦
      world.dreamOwner = api;
      now = T();
      closeMenu(); hideBar(); resetPlans(); setHotEl(null);
      if (grabbed) { grabbed.held = false; grabbed.el.classList.remove('grab'); grabbed = null; }
      if (mouseDown || dragging) { mouseDown = dragging = false; cvs.classList.remove('grab'); }
      if (!dreamDom) dreamDom = buildDreamDom();
      if (state !== 'sleep') { sleepLedge = false; startSleep('nap'); }
      sleepMode = 'nap'; napDur = 1e9;   // 梦没做完不醒
      const theme = lastDream || pick(DREAMS);
      lastDream = theme; emote = null;
      dream = { theme, seed: (rnd() * 1e9) | 0, keys: {}, jumpBuf: 0, msgUp: false, msgAct: null, over: null };
      resetDreamRun();
      sizeDream();
      dreamDom.title.textContent = 'Clawd 的梦 · ' + theme.name;
      dreamDom.back.style.display = 'block'; dreamDom.panel.style.display = 'block';
      showDreamMsg(`Clawd 梦到了${theme.name}……\n帮它把梦里的${theme.name}收起来，踩扁捣乱的 bug，\n跑到最后的枕头那儿，它就能睡个好觉。`,
        [['开始做梦', 'start'], ['让它接着睡', 'quit']]);
    }

    function resetDreamRun() {
      const d = dream;
      d.L = genLevel(d.seed);
      d.p = { x: 30, y: 104, vx: 0, vy: 0, ground: true, facing: 1, coyote: 0, inv: 0, dist: 0, cp: { x: 30, y: 104 } };
      d.cam = 0; d.hearts = 3; d.got = 0; d.total = d.L.items.length; d.stomped = 0; d.t = 0; d.parts = []; d.over = null; d.hudKey = '';
      d.keys = {}; d.jumpBuf = 0; d.log = [];
      d.stars =Array.from({ length: 46 }, () => ({ x: rnd() * DW, y: rnd() * 74, ph: rnd() * 6, b: rnd() < 0.25 }));
      d.far = Array.from({ length: 8 }, (_, i) => ({ x: i * 70 + rnd() * 30, y: 62 + rnd() * 22, w: 36 + rnd() * 30 }));
      d.zs = Array.from({ length: 4 }, () => ({ x: rnd() * DW, y: 30 + rnd() * 60, sp: 3 + rnd() * 4 }));
    }

    function showDreamMsg(text, buttons) {
      const d = dream;
      dreamDom.msgText.textContent = text;
      dreamDom.msgBtns.replaceChildren(...buttons.map(([label, act], i) => h('span', { class: 'dbtn' + (i === 0 ? ' big' : ''), 'data-act': act }, label)));
      dreamDom.msg.style.display = 'flex';
      d.msgUp = true; d.msgAct = buttons[0][1];
    }
    function dreamAct(act) {
      const d = dream;
      if (!d) return;
      if (act === 'start' || act === 'again') {
        if (act === 'again') { d.seed = (rnd() * 1e9) | 0; resetDreamRun(); }
        dreamDom.msg.style.display = 'none'; d.msgUp = false; d.keys = {}; d.jumpBuf = 0;
      } else if (act === 'quit') closeDream('quit');
      else if (act === 'wake') closeDream(d.over || 'quit');
    }
    function closeDream(result) {
      if (!dream) return;
      dream = null;
      if (world.dreamOwner === api) world.dreamOwner = null;
      dreamDom.back.style.display = 'none'; dreamDom.panel.style.display = 'none';
      if (state !== 'sleep') return;
      if (result === 'lose') { wake(true); emo('sweat', 1.6); }            // 被 bug 吓醒
      else if (result === 'win') { startStretch(); wakeLook = false; emo('heart', 2.2); spawnHeart(); energy = Math.min(1, energy + 0.3); }   // 睡饱了
      else if (result === 'quit') { napDur = stT + 15 + rnd() * 20; }      // 不玩了：让它接着睡一会儿
    }
    function press(k, down) {
      const d = dream;
      if (!d) return;
      if (k === 'J') {
        if (down && d.msgUp) { dreamAct(d.msgAct); return; }
        if (down && !d.keys.J) d.jumpBuf = 0.12;
      }
      d.keys[k] = down;
    }
    function dreamKey(e, down) {
      const c = e.code;
      const k = c === 'ArrowLeft' || c === 'KeyA' ? 'L' : c === 'ArrowRight' || c === 'KeyD' ? 'R'
        : c === 'Space' || c === 'ArrowUp' || c === 'KeyW' || c === 'KeyZ' || c === 'KeyK' ? 'J' : null;
      if (e.key === 'Escape') { if (down) closeDream(dream.over || 'quit'); }
      else if (e.key === 'Enter') { if (down && dream.msgUp) dreamAct(dream.msgAct); }
      else if (k) { if (!(down && e.repeat)) press(k, down); }
      else return;                                    // 别的键照常给网页
      e.preventDefault(); e.stopImmediatePropagation();
    }
    const inDream = (x, y) => {
      if (!dream || !dreamDom) return false;
      const r = dreamDom.panel.getBoundingClientRect();
      return x >= r.left && x <= r.right && y >= r.top && y <= r.bottom;
    };
    const overDreamBubble = (x, y) => !!dreamHit && !!emote && emote.kind === 'dream' && state === 'sleep' &&
      x >= dreamHit.l && x <= dreamHit.r && y >= dreamHit.t && y <= dreamHit.b;

    function dreamBurst(x, y, col, n, sp) {
      const d = dream;
      for (let i = 0; i < n; i++) {
        const a = rnd() * Math.PI * 2, v = sp * (0.4 + rnd() * 0.6);
        d.parts.push({ x, y, vx: Math.cos(a) * v, vy: Math.sin(a) * v - sp * 0.3, life: 0.35 + rnd() * 0.3, col });
      }
    }
    function hurtDream(fell, dir) {
      const d = dream, p = d.p;
      d.hearts--;
      if (d.log) d.log.push((fell ? 'fell@' : 'bug@') + Math.round(p.x));
      dreamBurst(p.x, p.y - 8, '#F0718A', 8, 60);
      if (d.hearts <= 0) {
        d.over = 'lose';
        showDreamMsg(`被 bug 吓醒了……\n收集了 ${d.got}/${d.total} 个${d.theme.name}，踩扁了 ${d.stomped} 只 bug。`, [['再睡一次', 'again'], ['醒来', 'wake']]);
        return;
      }
      if (fell) { p.x = p.cp.x; p.y = p.cp.y; p.vx = p.vy = 0; d.cam = clamp(p.x - DW * 0.38, 0, d.L.len - DW); }
      else { p.vx = dir * 90; p.vy = -110; p.ground = false; }
      p.inv = 1.5;
    }

    function stepDream(dt) {
      const d = dream, L = d.L, p = d.p, k = d.keys;
      d.t += dt;
      for (const z of d.zs) { z.y -= z.sp * dt; if (z.y < 8) { z.y = 96; z.x = rnd() * DW; } }
      for (let i = d.parts.length - 1; i >= 0; i--) {
        const q = d.parts[i];
        q.life -= dt; q.vy += 160 * dt; q.x += q.vx * dt; q.y += q.vy * dt;
        if (q.life <= 0) d.parts.splice(i, 1);
      }
      for (const sp of L.springs) sp.sq = Math.max(0, sp.sq - dt);
      if (d.msgUp) return;
      // 左右
      const want = (k.R ? 1 : 0) - (k.L ? 1 : 0);
      if (want) { p.vx += want * (p.ground ? 700 : 450) * dt; p.facing = want; }
      else if (p.ground) { const f = 900 * dt; p.vx = Math.abs(p.vx) <= f ? 0 : p.vx - sign(p.vx) * f; }
      p.vx = clamp(p.vx, -78, 78);
      // 跳：落地前一小会儿按也算，离开边缘一小会儿也还能跳
      p.coyote = p.ground ? 0.09 : Math.max(0, p.coyote - dt);
      d.jumpBuf = Math.max(0, d.jumpBuf - dt);
      if (d.jumpBuf > 0 && p.coyote > 0) { p.vy = -178; p.ground = false; p.coyote = 0; d.jumpBuf = 0; dreamBurst(p.x, p.y, '#E9E5FA', 4, 30); }
      if (!k.J && p.vy < -70) p.vy = -70;
      // 横向移动：地面方块的侧面是实心的
      p.x += p.vx * dt;
      for (const s of L.ground)
        if (p.y > s.y + 0.5 && p.x + 6 > s.x && p.x - 6 < s.x + s.w) { p.x = p.x < s.x + s.w / 2 ? s.x - 6 : s.x + s.w + 6; p.vx = 0; }
      p.x = clamp(p.x, 8, L.len - 8);
      // 纵向：地面和云都是从上面踩得住
      const prevY = p.y, was = p.ground;
      p.vy = Math.min(p.vy + 520 * dt, 420);
      p.y += p.vy * dt;
      p.ground = false;
      if (p.vy >= 0) {
        for (const s of L.ground)
          if (p.x + 6 > s.x && p.x - 6 < s.x + s.w && prevY <= s.y + 0.5 && p.y >= s.y) { p.y = s.y; p.vy = 0; p.ground = true; p.cp = { x: clamp(p.x, s.x + 10, s.x + s.w - 10), y: s.y }; }
        for (const s of L.clouds)
          if (p.x + 6 > s.x && p.x - 6 < s.x + s.w && prevY <= s.y + 0.5 && p.y >= s.y) { p.y = s.y; p.vy = 0; p.ground = true; }
        for (const sp of L.springs)
          if (Math.abs(p.x - sp.x) < 8 && prevY <= sp.y - 5 && p.y >= sp.y - 6) { p.y = sp.y - 6; p.vy = -270; p.ground = false; sp.sq = 0.25; dreamBurst(sp.x, sp.y - 6, '#F6C448', 6, 50); }
      }
      if (p.ground && !was) dreamBurst(p.x, p.y, '#E9E5FA', 3, 20);
      if (p.ground) p.dist += Math.abs(p.vx) * dt;
      p.inv = Math.max(0, p.inv - dt);
      if (p.y > DH + 30) { hurtDream(true, 0); return; }
      // bug：从上面踩扁，侧面碰到会疼
      for (const b of L.bugs) {
        if (b.flat > 0) { b.flat -= dt; continue; }
        if (!b.alive) continue;
        b.x += b.dir * 14 * dt;
        if (b.x < b.minX) { b.x = b.minX; b.dir = 1; }
        if (b.x > b.maxX) { b.x = b.maxX; b.dir = -1; }
        if (Math.abs(p.x - b.x) < 9 && p.y > b.y - 5 && p.y - 12 < b.y) {
          if (p.vy > 20 && prevY <= b.y - 3) { b.alive = false; b.flat = 0.6; p.vy = -150; d.stomped++; dreamBurst(b.x, b.y - 3, '#5BAF62', 8, 50); }
          else if (p.inv <= 0) { hurtDream(false, b.x < p.x ? 1 : -1); if (d.over) return; }
        }
      }
      for (const it of L.items)
        if (!it.got && Math.abs(p.x - it.x) < 8 && p.y > it.y - 3 && p.y - 13 < it.y + 3) { it.got = true; d.got++; dreamBurst(it.x, it.y, '#FFF6C8', 6, 45); }
      if (Math.abs(p.x - L.goal.x) < 12 && p.ground && !d.over) {
        d.over = 'win';
        dreamBurst(p.x, p.y - 10, '#F6C448', 16, 70);
        showDreamMsg(`睡得好香！\n收集了 ${d.got}/${d.total} 个${d.theme.name}，踩扁了 ${d.stomped} 只 bug。`, [['醒来', 'wake'], ['再做一个梦', 'again']]);
      }
      const target = clamp(p.x - DW * 0.38, 0, L.len - DW);
      d.cam += (target - d.cam) * Math.min(1, dt * 8);
    }

    function drawRows(c, rows, pal, x, y) {
      for (let r = 0; r < rows.length; r++)
        for (let q = 0; q < rows[r].length; q++) {
          const ch = rows[r][q];
          if (ch === '.' || !pal[ch]) continue;
          c.fillStyle = pal[ch]; c.fillRect(x + q, y + r, 1, 1);
        }
    }
    function cloudTop(c, x, y, w, col) {
      c.fillStyle = col;
      for (let i = 2; i < w - 5; i += 9) { c.fillRect(x + i, y - 2, 6, 2); c.fillRect(x + i + 1, y - 3, 4, 1); }
    }
    function drawDream() {
      const d = dream, D = dreamDom, c = D.offCtx, L = d.L, p = d.p, cam = Math.round(d.cam), t = d.t;
      for (let i = 0; i < DSKY.length; i++) { c.fillStyle = DSKY[i]; c.fillRect(0, i * 16, DW, 16); }
      for (const s of d.stars) {
        const x = Math.floor(((s.x - cam * 0.06) % DW + DW) % DW), on = Math.sin(t * 2.2 + s.ph) > -0.5;
        c.fillStyle = on ? (s.b ? '#FFF3C4' : '#BDB9EA') : '#4E4A86';
        c.fillRect(x, Math.floor(s.y), 1, 1);
        if (s.b && on) { c.fillRect(x - 1, Math.floor(s.y), 3, 1); c.fillRect(x, Math.floor(s.y) - 1, 1, 3); }
      }
      const mx = Math.round(208 - cam * 0.03);   // 月牙：大圆减去错开的小圆
      c.fillStyle = '#F4E9B8';
      for (let y = -11; y <= 11; y++)
        for (let x = -11; x <= 11; x++)
          if (x * x + y * y <= 121 && (x - 5) * (x - 5) + (y + 3) * (y + 3) > 100) c.fillRect(mx + x, 24 + y, 1, 1);
      for (const f of d.far) {
        const x = Math.round(((f.x - cam * 0.3) % 560 + 560) % 560 - 50), y = Math.round(f.y), w = Math.round(f.w);
        c.fillStyle = '#2A2858'; c.fillRect(x, y, w, 10); c.fillRect(x + 5, y - 3, w - 10, 3); c.fillRect(x + 11, y - 5, w - 22, 2);
      }
      for (const z of d.zs) drawRows(c, ZED, { X: '#4B4786' }, Math.round(z.x), Math.round(z.y));
      for (const s of L.ground) {
        const x = Math.round(s.x - cam);
        if (x > DW || x + s.w < 0) continue;
        c.fillStyle = '#E9E5FA'; c.fillRect(x, s.y, s.w, DH - s.y);
        c.fillStyle = '#D2CBF0'; c.fillRect(x, s.y + 6, s.w, DH - s.y - 6);
        c.fillStyle = '#B7AEE0'; c.fillRect(x, s.y + 15, s.w, DH - s.y - 15);
        c.fillRect(x, s.y, 1, DH - s.y); c.fillRect(x + s.w - 1, s.y, 1, DH - s.y);
        cloudTop(c, x, s.y, s.w, '#E9E5FA');
      }
      for (const s of L.clouds) {
        const x = Math.round(s.x - cam);
        if (x > DW || x + s.w < 0) continue;
        c.fillStyle = '#F2EFFD'; c.fillRect(x, s.y, s.w, 4);
        c.fillStyle = '#C4BBE8'; c.fillRect(x + 1, s.y + 4, s.w - 2, 2); c.fillRect(x + 4, s.y + 6, s.w - 8, 1);
        cloudTop(c, x, s.y, s.w, '#F2EFFD');
      }
      for (const sp of L.springs) {
        const x = Math.round(sp.x - cam);
        if (x < -8 || x > DW + 8) continue;
        drawRows(c, ballRows(Math.floor(t * 3) & 3), BALL_PAL, x - 3, sp.y - 6 + (sp.sq > 0 ? 1 : 0));
      }
      const th = d.theme;
      for (const it of L.items) {
        if (it.got) continue;
        const x = Math.round(it.x - cam);
        if (x < -8 || x > DW + 8) continue;
        const rows = th.frames ? th.frames[Math.floor(t * 6 + it.ph) & 1] : th.rows;
        drawRows(c, rows, th.pal, x - (rows[0].length >> 1), Math.round(it.y - (rows.length >> 1) + Math.sin(t * 3 + it.ph) * 1.2));
      }
      for (const b of L.bugs) {
        const x = Math.round(b.x - cam);
        if (x < -8 || x > DW + 8) continue;
        if (b.alive) drawRows(c, BUG_F[Math.floor(t * 5) & 1], BUG_PAL, x - 3, Math.round(b.y) - 5);
        else if (b.flat > 0) drawRows(c, BUG_FLAT, BUG_PAL, x - 3, Math.round(b.y) - 2);
      }
      // 终点：软乎乎的枕头
      const gx = Math.round(L.goal.x - cam), gy = L.goal.y;
      c.fillStyle = '#FFFFFF'; c.fillRect(gx - 11, gy - 7, 22, 6); c.fillRect(gx - 10, gy - 8, 20, 8);
      c.fillStyle = '#B9C3F5'; c.fillRect(gx - 10, gy - 4, 20, 1); c.fillRect(gx - 9, gy - 1, 18, 1);
      if (Math.sin(t * 3) > 0) drawRows(c, ZED, { X: '#C9C6F0' }, gx + 6, gy - 18);
      // Clawd
      if (!(p.inv > 0 && (Math.floor(t * 12) & 1))) {
        const run = p.ground && Math.abs(p.vx) > 8, step = Math.floor(p.dist / 6) & 1;
        const pose = { acc: slot === 1, eyes: p.inv > 1 ? 'squint' : (t % 3.3) < 0.12 ? 'blink' : 'open', armL: 'normal', armR: 'normal',
          legs: 'stand', lookX: p.facing, lookY: !p.ground && p.vy > 80 ? 1 : 0, bodyDy: 0, facing: p.facing, blush: false, legDx: 0, legDx2: 0, legPh: 0 };
        if (!p.ground) { pose.armL = pose.armR = p.vy < 0 ? 'up' : 'upOut'; pose.legs = p.vy < 0 ? 'dangleA' : 'dangleB'; }
        else if (run) { pose.legs = step ? 'walkA' : 'walkB'; pose.armL = step ? 'raised' : 'normal'; pose.armR = step ? 'normal' : 'raised'; }
        c.drawImage(spriteFor(pose, poseKey(pose)), Math.round(p.x - 12 - cam), Math.round(p.y - 17));
      }
      for (const q of d.parts) { c.fillStyle = q.col; c.fillRect(Math.round(q.x - cam), Math.round(q.y), 1, 1); }
      D.view.imageSmoothingEnabled = false;
      D.view.drawImage(D.off, 0, 0, D.cv.width, D.cv.height);
      const hk = d.hearts + '|' + d.got + '|' + d.stomped;
      if (hk !== d.hudKey) {
        d.hudKey = hk;
        D.hud.replaceChildren(h('b', null, '♥'.repeat(Math.max(0, d.hearts)) + '♡'.repeat(Math.max(0, 3 - d.hearts))),
          `   ${d.theme.name} ${d.got}/${d.total}   bug ×${d.stomped}`);
      }
    }
    function runDream(dt) {
      let left = Math.min(dt, 0.05);
      while (left > 1e-6) { const h1 = Math.min(left, 1 / 120); stepDream(h1); left -= h1; if (!dream) return; }
      drawDream();
    }

    // ================================================================ 显示 / 隐藏
    function shouldHideBySetting() {
      return !settings.enabled || (settings.hiddenSites || []).includes(location.hostname) || slot >= (settings.count || 1);   // 关掉「两只」时第二只不出来
    }
    const POS_KEY = slot === 0 ? 'petPos' : 'petPos' + slot;
    const HOME_X = slot === 0 ? 0.72 : 0.3;   // 第一次出现的位置：两只一左一右
    function retire() {
      if (retired) return;
      savePos();
      closeDream('quit'); hideBar(); closeMenu(); setHotEl(null);
      retired = true; hiddenNow = true; dreamHit = null; mouseDown = dragging = false;
      host.setAttribute('data-clawd-retired', '');
      stopLoop();
      for (const o of objs.slice()) removeObj(o);
      host.remove();
    }

    function applyVisibility() {
      if (retired) return;
      const bySetting = shouldHideBySetting();
      const hide = bySetting || !!document.fullscreenElement;
      const comingBack = started && hiddenBySetting && !bySetting;
      hiddenBySetting = bySetting;
      if (hide === hiddenNow && started) return;
      started = true;
      hiddenNow = hide;
      if (hide) {
        host.style.setProperty('display', 'none', 'important');
        closeDream('quit'); hideBar();
        stopLoop(); closeMenu(); setHotEl(null);
        if (grabbed) { grabbed.held = false; grabbed.el.classList.remove('grab'); grabbed = null; }
        if (mouseDown || dragging) { mouseDown = dragging = false; cvs.classList.remove('grab'); startFall('plain'); }
        return;
      }
      host.style.setProperty('display', 'block', 'important');
      if (comingBack) { now = T(); dropIn(vw() * HOME_X); }
      lastKey = ''; lastLeft = lastTop = null;
      for (const o of objs) { o.key = ''; o.lastPos = ''; }
      refreshBubble(false); refreshBadge(true);
      startLoop();
    }

    function savePos() {
      if (hiddenNow || !started) return;
      sset({ [POS_KEY]: { xf: cx / Math.max(1, vw()), t: Date.now() } });
    }

    function handleEvent(ev) {
      if (!ev || ev.id === lastEventId || hiddenNow || document.visibilityState !== 'visible' || Date.now() - (ev.t || 0) > 8000) return;
      lastEventId = ev.id;
      now = T();
      if (slot > 0) {   // 字只让第一只说，第二只跟着蹦 / 跟着紧张就好
        if (!grounded()) return;
        resetPlans();
        if (ev.type === 'celebrate') { for (let i = 0; i < 3; i++) spawnSparkle(); jump('celebrate', S * 80, true); }
        else if (ev.type === 'alert') { setState('alert', Math.min(ev.dur || 8, 4)); nextFx = now; }
        return;
      }
      if (ev.type === 'celebrate') { if (grounded()) { resetPlans(); celebrate(false, ev.text); } else say(ev.text, 4); }
      else if (ev.type === 'alert') { if (grounded()) { resetPlans(); startAlert(ev.text, ev.dur || 8, ev.sticky); } else say(ev.text, ev.dur || 8); }
    }

    // ================================================================ 启动
    const BEHAVIORS = {
      sit: startSit, ledge: startLedge, lie: () => startLie(), look: startLook, ponder: startPonder, hops: startHops,
      spin: startSpin, roll: startRoll, exercise: startExercise, stretch: startStretch, scratch: startScratch,
      dance: startDance, sneeze: startSneeze, teeter: startTeeter, reach: startReach,
      giggle: () => { setState('giggle', 1.6); nextFx = now; },
      peek: () => startPeek(cx < vw() / 2 ? -1 : 1),
      climb: () => { platsT = -10; const c = climbTarget(); if (c) climbTo(c); return !!c; },
      hopDown: () => hopDown(),
      watch: () => { const v = playingVideo(); if (v) goWatch(v); return !!v; },
      nap: () => startNap(),
      walk: () => startWalk(cx < vw() / 2 ? 1 : -1, 3),
      doze: () => { sleepMode = 'nap'; napDur = 30; sleepLedge = false; dozeNods = 3; setState('doze', 2.2); },
      sleep: () => startYawn('nap', 30),
      ledgeSleep: () => { startYawn('nap', 30); sleepLedge = !!perch; return sleepLedge; },
      shortNap: () => { startSleep('nap'); napDur = 4; lastDream = DREAMS[0]; },
      dream: () => startDream(),
      twitch: () => sleepTwitch(),
    };

    async function init() {
      const v = await sget(['settings', 'pomo', 'reminders', 'idleState', POS_KEY, 'greeted']);
      settings = Object.assign({}, DEF, v.settings || {});
      pomo = v.pomo || null;
      reminderCount = (v.reminders || []).length;
      idleState = v.idleState || 'active';
      now = T();
      nextChatter = now + 240;
      nextFly = now + 60 + rnd() * 180;
      nextNap = now + 60 + rnd() * 120;
      applySize();
      const pp = v[POS_KEY];
      if (pp && Date.now() - pp.t < 20 * 60 * 1000 && pp.xf >= 0 && pp.xf <= 1) {
        // 刚在别的网页见过：直接站在老地方，不再从天上掉下来
        cx = pp.xf * vw(); posY = vh(); setState('idle', 1 + rnd() * 2);
      } else {
        dropIn(vw() * HOME_X);
        if (slot > 0) posY = 4 * S;   // 第二只晚一点点落地
        if (!v.greeted && slot === 0) { afterLandSay = '嗨，我们是 Clawd！右键点我们有菜单，也可以把我们拎起来甩~'; sset({ greeted: true }); }
      }
      if (rivalWins()) { retired = true; host.setAttribute('data-clawd-retired', ''); return; }   // 网页上已经有一只更新的 / 先来的：这只就不出来了
      de.appendChild(host);
      applyVisibility();

      window.addEventListener('pointermove', onPointerMove, { capture: true, passive: true });
      window.addEventListener('pointerdown', onPointerDown, true);
      window.addEventListener('pointerup', onPointerUp, true);
      window.addEventListener('pointercancel', onPointerCancel, true);
      window.addEventListener('click', onClick, true);
      window.addEventListener('dblclick', onClick, true);
      window.addEventListener('contextmenu', onContextMenu, true);
      window.addEventListener('touchstart', onTouchStart, { capture: true, passive: false });
      window.addEventListener('wheel', onWheel, { capture: true, passive: true });
      window.addEventListener('scroll', () => { lastScrollT = T(); platsT = -10; }, { capture: true, passive: true });
      window.addEventListener('dragover', onDragOver, true);
      window.addEventListener('drop', onDrop, true);
      window.addEventListener('keydown', onKeyDown, true);
      window.addEventListener('keyup', e => { if (dream) dreamKey(e, false); }, true);
      window.addEventListener('blur', () => { if (dream) dream.keys = {}; });   // 切走了：别让方向键卡住
      window.addEventListener('resize', () => { platsT = -10; if (Math.abs((window.devicePixelRatio || 1) - dpr) > 0.001) applySize(); if (menuOpen) closeMenu(); if (dream) sizeDream(); });
      window.addEventListener('blur', () => { if (menuOpen && !root.activeElement) closeMenu(); });
      window.addEventListener('pagehide', savePos);
      document.addEventListener('selectionchange', () => { clearTimeout(selTimer); selTimer = setTimeout(onSelection, 350); });
      cvs.addEventListener('lostpointercapture', e => { if (mouseDown && e.pointerId === downId) onPointerCancel(e); });
      document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'hidden') { savePos(); closeMenu(); } });
      document.addEventListener('fullscreenchange', applyVisibility);
      // 有些网页会整个替换 <html> 的内容，隔一会儿检查一下自己还在不在
      const watch = setInterval(() => {
        if (retired) { clearInterval(watch); return; }
        // 扩展被重新加载 / 删掉了：旧网页里这只已经和扩展断了线，自己退场（刷新网页后新的那只会出来）
        // 另一份更新的 Clawd 也在这个网页上：让给它
        if (!alive() || rivalWins()) { retire(); clearInterval(watch); return; }
        // 有些网页会整个替换 <html> 的内容：自己还在不在
        if (!host.isConnected && document.documentElement) document.documentElement.appendChild(host);
      }, 1500);

      if (alive()) {
        ext.storage.onChanged.addListener((ch, area) => {
          if (area !== 'local') return;
          now = T();
          if (ch.settings) {
            const old = settings;
            settings = Object.assign({}, DEF, ch.settings.newValue || {});
            if ((old.size | 0) !== (settings.size | 0)) { applySize(); sqV += 5; }
            applyVisibility();
          }
          if (ch.pomo) {
            const nv = ch.pomo.newValue || null;
            const fresh = nv && (!pomo || pomo.end !== nv.end) && !nv.auto;
            pomo = nv;
            refreshBadge(true);
            // 在工具栏里开的番茄钟：由第一只报一声（某一只自己开的，它已经说过了）
            if (fresh && slot === 0 && nv.end !== world.lastPomoEnd && !hiddenNow && document.visibilityState === 'visible')
              announcePomo(nv.mode, Math.round((nv.end - Date.now()) / 60000), false);
          }
          if (ch.reminders) reminderCount = (ch.reminders.newValue || []).length;
          if (ch.idleState) {
            idleState = ch.idleState.newValue || 'active';
            if (idleState === 'active' && state === 'sleep' && sleepMode === 'afk') wake(false);
          }
          if (ch.clawdEvent) handleEvent(ch.clawdEvent.newValue);
        });
        ext.runtime.onMessage.addListener((msg, sender, reply) => {
          now = T();
          if (!msg) return;
          if (retired) { if (msg.cmd === 'hello') reply({ host: location.hostname, hidden: true, retired: true }); else reply({ ok: false }); return; }
          if (msg.cmd === 'hello') reply({ host: location.hostname, hidden: hiddenNow });
          else if (msg.cmd === 'summon') { if (!hiddenNow) summon(); reply({ ok: true }); }
          else if (msg.cmd === 'dance') { if (!hiddenNow && grounded()) { resetPlans(); startDance(); } reply({ ok: true }); }
          else if (msg.cmd === 'feed') { if (!hiddenNow) feedNow(); reply({ ok: true }); }
          // 球和梦只有一份：只让第一只接这两个命令（第一只被藏起来时才让第二只接）
          else if (msg.cmd === 'dreamGame' || msg.cmd === 'ball') {
            const lead = world.pets.find(p => p && !p.info().hidden);
            if (lead !== api) return;
            if (msg.cmd === 'dreamGame') { openDream(); reply({ ok: true }); }
            else { const b = toggleBall(); reply({ ok: true, ball: !!b }); }
          }
        });
      }

      if (window.__clawdTest) {
        window.__clawdDebugs = window.__clawdDebugs || [];
        const dbg = window.__clawdDebugs[slot] = {
          get: () => ({ cx, posY, state, S, bubbleText, hot: hotEl === cvs, menuOpen, hiddenNow, pomo, sq, lean, mood, reminderCount, idleState,
            emote: emote && emote.kind, energy, legA, legB, armPh, shake, flip, rot, sitOff,
            sleepMode, sleepLedge, napDur, nose: noseInfo ? Math.round(noseInfo.r * 10) / 10 : 0,
            perch: perch ? perch.el.tagName + (perch.el.id ? '#' + perch.el.id : '') : null,
            objs: objs.map(o => ({ kind: o.kind, x: Math.round(o.x), y: Math.round(o.y), rest: o.rest, held: o.held, onHead: !!o.onHead, bite: o.bite | 0 })) }),
          menu: menuEl, root,
          act: a => { now = T(); menuAction(a); },
          do: name => { now = T(); resetPlans(); const f = BEHAVIORS[name]; const r = f ? f() : undefined; return r === undefined ? state : r; },
          scan: () => { now = T(); scanPlats(); return plats.map(p => p.el.tagName + (p.el.id ? '#' + p.el.id : '') + '@' + Math.round(p.t)); },
          fly: () => { now = T(); const o = spawnFly(); o.x = cx + 30 * S; o.y = posY - 40 * S; o.born = now - 10; },
          dream: () => dream && { msgUp: dream.msgUp, over: dream.over, log: dream.log, hearts: dream.hearts, got: dream.got, total: dream.total, stomped: dream.stomped,
            px: Math.round(dream.p.x), py: Math.round(dream.p.y), ground: dream.p.ground, len: dream.L.len, goal: dream.L.goal.x,
            bugs: dream.L.bugs.filter(b => b.alive).map(b => Math.round(b.x)), springs: dream.L.springs.length },
          dreamHit: () => dreamHit,
          bar: () => ({ on: barOn, rect: barOn ? (r => ({ l: r.left, t: r.top, r: r.right, b: r.bottom }))(barEl.getBoundingClientRect()) : null }),
          swing: () => ({ swing, swingV, gvx, walkPh }),
          dreamLevel: () => dream && dream.L,
          dreamTeleport: (x, y) => { if (dream) { dream.p.x = x; dream.p.y = y; dream.p.vy = 0; } },
          pose: () => buildPose(),
          set: o => { now = T(); if ('energy' in o) energy = o.energy; if ('idle' in o) { idleState = o.idle; } },
          social: name => {   // 测试用：直接发起和另一只的互动
            now = T(); const f = friend(); if (!f) return 'no friend';
            if (name === 'snuggle') goSnuggle(f); else if (name === 'tag') startTag(f); else if (name === 'greet') goGreet(f);
            else if (name === 'napBeside') napBeside(f); else if (name === 'feed') feedNow(); else if (name === 'pet') onPetted();
            return state;
          },
          moveTo: (x) => { now = T(); resetPlans(); perch = null; cx = x; posY = vh(); gvx = 0; setState('idle', 3); },
          mate: () => mate ? mate.slot : null,
        };
        if (slot === 0) window.__clawdDebug = dbg;
      }
    }

    init();
  }

  makeClawd(0);
  makeClawd(1);
})();
