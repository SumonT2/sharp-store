// Renders the original studio-style demo product photos in src/Shop.Web/wwwroot/img/demo (no third-party images).
// Usage: npm install playwright && npx playwright install chromium && node render.js [output-folder]
// Needs ImageMagick (`convert`) for the WebP conversion.
const { chromium } = require('playwright');
const fs = require('fs');
const { execFileSync } = require('child_process');
const OUT = process.argv[2] || __dirname + '/out';
fs.mkdirSync(OUT, { recursive: true });

// ---------- keyboard layouts (key widths in units; {g} = gap) ----------
const K = (w, l = '', r = 'a') => ({ w, l, r });
const G = (g) => ({ g });
const ones = (s, r = 'a') => s.split(' ').map(l => K(1, l, r));
const R60 = [
  [K(1, 'Esc', 'x'), ...ones('1 2 3 4 5 6 7 8 9 0 - ='), K(2, 'Back', 'm')],
  [K(1.5, 'Tab', 'm'), ...ones('Q W E R T Y U I O P [ ]'), K(1.5, '\\', 'm')],
  [K(1.75, 'Caps', 'm'), ...ones("A S D F G H J K L ; '"), K(2.25, 'Enter', 'x')],
  [K(2.25, 'Shift', 'm'), ...ones('Z X C V B N M , . /'), K(2.75, 'Shift', 'm')],
  [K(1.25, 'Ctrl', 'm'), K(1.25, 'Win', 'm'), K(1.25, 'Alt', 'm'), K(6.25, '', 's'), K(1.25, 'Alt', 'm'), K(1.25, 'Fn', 'm'), K(1.25, 'Menu', 'm'), K(1.25, 'Ctrl', 'm')],
];
const R65 = [
  [K(1, 'Esc', 'x'), ...ones('1 2 3 4 5 6 7 8 9 0 - ='), K(2, 'Back', 'm'), K(1, 'Del', 'm')],
  [K(1.5, 'Tab', 'm'), ...ones('Q W E R T Y U I O P [ ]'), K(1.5, '\\', 'm'), K(1, 'PgUp', 'm')],
  [K(1.75, 'Caps', 'm'), ...ones("A S D F G H J K L ; '"), K(2.25, 'Enter', 'x'), K(1, 'PgDn', 'm')],
  [K(2.25, 'Shift', 'm'), ...ones('Z X C V B N M , . /'), K(1.75, 'Shift', 'm'), K(1, '↑', 'm'), K(1, 'End', 'm')],
  [K(1.25, 'Ctrl', 'm'), K(1.25, 'Win', 'm'), K(1.25, 'Alt', 'm'), K(6.25, '', 's'), K(1, 'Alt', 'm'), K(1, 'Fn', 'm'), K(1, 'Ctrl', 'm'), K(1, '←', 'm'), K(1, '↓', 'm'), K(1, '→', 'm')],
];
const R75 = [
  [K(1, 'Esc', 'x'), ...ones('F1 F2 F3 F4 F5 F6 F7 F8 F9 F10 F11 F12', 'm'), K(1, 'Prt', 'm'), K(1, 'Ins', 'm'), K(1, 'Del', 'm')],
  'gap',
  ...R65.map((r, i) => i === 0 ? [...r.slice(0, 14), K(1, 'Home', 'm')] : r),
];
const nav = [[K(1, 'Ins', 'm'), K(1, 'Hm', 'm'), K(1, 'PgUp', 'm')], [K(1, 'Del', 'm'), K(1, 'End', 'm'), K(1, 'PgDn', 'm')], [], [G(1), K(1, '↑', 'm')], [K(1, '←', 'm'), K(1, '↓', 'm'), K(1, '→', 'm')]];
const frow = [K(1, 'Esc', 'x'), G(1), ...ones('F1 F2 F3 F4', 'm'), G(0.5), ...ones('F5 F6 F7 F8', 'm'), G(0.5), ...ones('F9 F10 F11 F12', 'm'), G(0.25), ...ones('Prt Scr Pse', 'm')];
const RTKL = [frow, 'gap', ...R60.map((r, i) => [...r, G(0.25), ...nav[i]])];
const num = [ones('Num / * -', 'm'), ones('7 8 9 +'), [...ones('4 5 6'), K(1, '+', 'm')], [...ones('1 2 3'), K(1, 'Ent', 'x')], [K(2, '0'), K(1, '.'), K(1, 'Ent', 'x')]];
const RFULL = [frow, 'gap', ...RTKL.slice(2).map((r, i) => {
  const pad = i === 3 ? [G(1), K(1, '↑', 'm'), G(1)] : nav[i].length ? [] : [G(3)];
  return [...r, ...pad, G(0.25), ...num[i]];
})];
const LAYOUTS = { '60': R60, '65': R65, '75': R75, 'tkl': RTKL, 'full': RFULL };

// ---------- page builders ----------
function keyboardHTML(o) {
  const U = o.u || 44, rows = LAYOUTS[o.layout];
  let y = 0, maxX = 0, keys = '';
  const skirt = o.low ? 4 : 8;
  rows.forEach(row => {
    if (row === 'gap') { y += U * 0.3; return; }
    let x = 0;
    row.forEach(k => {
      if (k.g !== undefined) { x += k.g * U; return; }
      const role = k.r === 's' ? (o.spaceAccent ? 'x' : 'a') : k.r;
      const c = o[role] || o.a;
      const glow = o.rgb ? `0 0 16px 2px hsla(${Math.round(x / 3 + y / 2) % 360},95%,58%,.75),` : '';
      const fs = k.l.length > 2 ? U * 0.2 : U * 0.3;
      keys += `<div class="key" style="left:${x + 2}px;top:${y + 2}px;width:${k.w * U - 4}px;height:${U - 4}px;background:${c.side};box-shadow:${glow}0 1px 1px rgba(0,0,0,.25)">
        <div class="cap" style="background:linear-gradient(180deg,${c.hi},${c.top} 60%);bottom:${skirt}px;color:${c.lg};font-size:${fs}px;${o.translucent ? 'opacity:.82;' : ''}">${o.noLegends ? '' : k.l}</div></div>`;
      x += k.w * U;
    });
    maxX = Math.max(maxX, x); y += U;
  });
  const pad = o.caseless ? 0 : 18;
  const cse = o.caseless ? '' : `background:${o.case.bg};box-shadow:0 ${o.low ? 8 : 14}px 0 ${o.case.edge},0 ${o.low ? 8 : 14}px 2px ${o.case.edge},0 46px 44px -14px rgba(0,0,0,.42);`;
  const plate = o.caseless ? (o.tray || 'transparent') : o.case.plate;
  return `<div class="kb" style="padding:${pad}px;border-radius:${o.caseless ? 14 : 20}px;${cse}">
    <div style="position:relative;width:${maxX}px;height:${y}px;background:${plate};border-radius:8px;box-shadow:${o.caseless ? 'none' : 'inset 0 2px 6px rgba(0,0,0,.35)'}">${keys}</div>
    ${o.caseless || o.noBadge ? '' : `<div style="position:absolute;right:${pad + 6}px;top:4px;font:700 9px Inter;letter-spacing:.2em;color:${o.case.badge}">${o.brand}</div>`}</div>`;
}
function mouseSVG(o) {
  const holes = o.honey ? (() => {
    let s = ''; for (let r = 0; r < 9; r++) for (let c = 0; c < 8; c++) {
      const cx = 30 + c * 20 + (r % 2) * 10, cy = 150 + r * 17;
      s += `<circle cx="${cx}" cy="${cy}" r="6.5" fill="${o.hole}"/>`;
    } return `<g clip-path="url(#shape)">${s}</g>`;
  })() : '';
  return `<svg viewBox="0 0 200 330" width="${o.w || 340}" style="transform:rotate(${o.rot ?? -14}deg);filter:drop-shadow(0 26px 22px rgba(0,0,0,.32)) drop-shadow(0 4px 4px rgba(0,0,0,.25))">
    <defs>
      <radialGradient id="body" cx="38%" cy="32%" r="80%"><stop offset="0" stop-color="${o.hi}"/><stop offset=".55" stop-color="${o.base}"/><stop offset="1" stop-color="${o.edge}"/></radialGradient>
      <linearGradient id="spec" x1="0" x2="1"><stop offset="0" stop-color="#fff" stop-opacity="0"/><stop offset=".5" stop-color="#fff" stop-opacity="${o.gloss ?? .35}"/><stop offset="1" stop-color="#fff" stop-opacity="0"/></linearGradient>
      <clipPath id="shape"><path d="M100 8C152 8 180 52 182 124C185 206 168 306 100 318C32 306 15 206 18 124C20 52 48 8 100 8Z"/></clipPath>
    </defs>
    <path d="M100 8C152 8 180 52 182 124C185 206 168 306 100 318C32 306 15 206 18 124C20 52 48 8 100 8Z" fill="url(#body)"/>
    ${holes}
    <path d="M100 10V124M22 126Q100 142 178 126" stroke="${o.line}" stroke-width="2" fill="none" opacity=".8"/>
    <rect x="91" y="40" width="18" height="50" rx="9" fill="${o.wheelBg || '#1a1b1f'}"/>
    <rect x="94" y="44" width="12" height="42" rx="6" fill="${o.wheel}"/>
    ${[0, 1, 2, 3, 4, 5, 6].map(i => `<rect x="94" y="${47 + i * 5.6}" width="12" height="1.6" fill="rgba(0,0,0,.35)"/>`).join('')}
    ${o.side ? `<rect x="14" y="140" width="9" height="34" rx="4" fill="${o.line}" opacity=".9"/><rect x="14" y="180" width="9" height="30" rx="4" fill="${o.line}" opacity=".9"/>` : ''}
    ${o.led ? `<path d="M40 286Q100 312 160 286" stroke="${o.led}" stroke-width="4" fill="none" stroke-linecap="round" style="filter:drop-shadow(0 0 6px ${o.led})"/>` : ''}
    <ellipse cx="72" cy="80" rx="24" ry="58" fill="url(#spec)" transform="rotate(-12 72 80)"/>
    <text x="100" y="${o.honey ? 136 : 250}" text-anchor="middle" font-family="Inter" font-weight="800" font-size="11" letter-spacing="3" fill="${o.logo}">${o.brand}</text>
  </svg>`;
}
function switchSVG(stem) {
  return `<svg viewBox="0 0 200 260" width="380" style="filter:drop-shadow(0 24px 20px rgba(0,0,0,.3))">
    <defs><linearGradient id="top" x1="0" x2="1"><stop offset="0" stop-color="#5b5e68" stop-opacity=".85"/><stop offset=".45" stop-color="#9da1ab" stop-opacity=".7"/><stop offset="1" stop-color="#4a4d56" stop-opacity=".9"/></linearGradient>
    <linearGradient id="bot" x1="0" x2="1"><stop offset="0" stop-color="#26282e"/><stop offset=".5" stop-color="#3b3e46"/><stop offset="1" stop-color="#1d1f24"/></linearGradient>
    <linearGradient id="stem" x1="0" x2="1"><stop offset="0" stop-color="${stem[1]}"/><stop offset=".45" stop-color="${stem[0]}"/><stop offset="1" stop-color="${stem[1]}"/></linearGradient></defs>
    <rect x="96" y="214" width="4" height="30" fill="#c9a24a"/><rect x="128" y="214" width="4" height="22" fill="#c9a24a"/>
    <path d="M44 150H156L160 218H40Z" fill="url(#bot)"/>
    <path d="M78 64H122V118H78Z" fill="url(#stem)"/>
    <path d="M70 64H130V80H70Z" fill="url(#stem)" opacity=".9"/>
    <path d="M38 152H162L150 100H50Z" fill="url(#top)"/>
    <path d="M50 100H150" stroke="#c7cad2" stroke-width="2" opacity=".6"/>
    <rect x="92" y="108" width="16" height="9" rx="2" fill="#e8e9ec" opacity=".55"/>
    <text x="100" y="196" text-anchor="middle" font-family="Inter" font-weight="800" font-size="11" letter-spacing="3" fill="#8b8f99">KEYVO</text></svg>`;
}
const css = `*{box-sizing:border-box}body{margin:0;font-family:Inter,sans-serif}
.stage{position:relative;overflow:hidden;display:flex;align-items:center;justify-content:center}
.key{position:absolute;border-radius:7px}
.cap{position:absolute;left:4px;right:4px;top:2px;border-radius:6px;box-shadow:inset 0 1px 0 rgba(255,255,255,.4),inset 0 -2px 3px rgba(0,0,0,.12);padding:4px 6px;font-weight:600;line-height:1;white-space:nowrap;overflow:hidden}
.kb{position:relative}`;
const studio = (inner, o = {}) => `<style>${css}</style><div class="stage" id="s" style="width:${o.w || 900}px;height:${o.h || 900}px;background:${o.bg || 'radial-gradient(120% 90% at 50% 35%,#FBFAF7 0%,#ECEAE4 70%,#E2DFD7 100%)'}">
  ${o.noFloor ? '' : `<div style="position:absolute;left:12%;right:12%;bottom:${o.floor || 22}%;height:60px;background:radial-gradient(50% 50%,rgba(0,0,0,.16),transparent 70%);filter:blur(8px)"></div>`}${inner}</div>`;
const tilt = (html, t, scale = 1) => `<div style="transform:perspective(1800px) rotateX(${t.rx}deg) rotateZ(${t.rz}deg) scale(${scale});transform-style:preserve-3d">${html}</div>`;

// ---------- colourways ----------
const cap = (top, side, lg, hi) => ({ top, side, lg, hi: hi || top });
const kbs = {
  'kb-k68': { layout: '65', brand: 'KEYVO', a: cap('#F7F6F2', '#D9D6CE', '#3A3B40', '#FFFFFF'), m: cap('#B9BCC4', '#979AA3', '#1F2024', '#C9CBD2'), x: cap('#FF5A1F', '#C9400F', '#FFFFFF', '#FF7A47'), case: { bg: 'linear-gradient(180deg,#FFFFFF,#E9E8E4)', edge: '#C6C3BB', plate: '#D5D3CC', badge: '#A9A6A0' } },
  'kb-k75': { layout: '75', brand: 'KEYVO', a: cap('#EDE4D3', '#CFC3AC', '#4B3F30', '#F6EFE2'), m: cap('#7D8B9C', '#5F6B7A', '#F2EEE6', '#8E9BAB'), x: cap('#C8502E', '#9C3B20', '#FFF3E8', '#D9653F'), spaceAccent: false, case: { bg: 'linear-gradient(180deg,#5A5D66,#3F4249)', edge: '#2B2D32', plate: '#34363C', badge: '#8C9099' } },
  'kb-k100': { layout: 'full', brand: 'KEYVO', u: 34, a: cap('#2C2E34', '#1B1C20', '#E8E8EA', '#3A3D44'), m: cap('#22242A', '#141518', '#BFC1C7', '#30333A'), x: cap('#FF5A1F', '#C9400F', '#121316', '#FF7A47'), case: { bg: 'linear-gradient(180deg,#2A2C31,#16171A)', edge: '#0C0D0F', plate: '#101114', badge: '#5D6068' } },
  'kb-a61': { layout: '60', brand: 'ARKON', a: cap('#FFFFFF', '#D8DAD6', '#2E3A36', '#FFFFFF'), m: cap('#9ED9C5', '#76B8A2', '#1E3D33', '#B5E5D4'), x: cap('#3FB58C', '#2D8C6B', '#FFFFFF', '#5CC9A1'), case: { bg: 'linear-gradient(180deg,#FFFFFF,#ECEDEA)', edge: '#C9CBC6', plate: '#DDE0DB', badge: '#9FA6A2' } },
  'kb-a84': { layout: '75', brand: 'ARKON', a: cap('#ECE7F7', '#C9C1DE', '#40375A', '#F6F3FC'), m: cap('#A79BD1', '#8578B4', '#FFFFFF', '#B8AEDC'), x: cap('#6E5BC4', '#53439E', '#FFFFFF', '#8371D3'), case: { bg: 'linear-gradient(180deg,#E3DDF2,#CBC3E3)', edge: '#A69CC6', plate: '#BDB4D9', badge: '#8D82B4' } },
  'kb-v65': { layout: '65', brand: 'VOLTARO', a: cap('#3A3D45', '#24262B', '#DADCE0', '#4A4E57'), m: cap('#2A2C32', '#18191D', '#AEB2BA', '#363940'), x: cap('#E9B949', '#B88C27', '#2A2110', '#F2CB6A'), case: { bg: 'repeating-linear-gradient(90deg,#C9CCD2 0 2px,#D3D6DB 2px 4px),linear-gradient(180deg,#DADDE2,#B7BBC2)', edge: '#8E929A', plate: '#2B2D33', badge: '#6E727A' } },
  'kb-s66': { layout: '65', brand: 'SONIQUE', a: cap('#FFF1F3', '#E8CDD3', '#7A3E4C', '#FFFFFF'), m: cap('#F5B8C4', '#D9909F', '#5E2533', '#F9CAD3'), x: cap('#E2577A', '#B53B5C', '#FFFFFF', '#EC7393'), case: { bg: 'linear-gradient(180deg,#FFE3E8,#F4C9D2)', edge: '#D39EAA', plate: '#EDBFC9', badge: '#C48593' } },
  'kb-k72': { layout: '75', brand: 'KEYVO', low: true, a: cap('#D9DDE3', '#B7BCC4', '#2A2F38', '#E7EAEE'), m: cap('#5C6E8A', '#465570', '#EEF2F8', '#6E809C'), x: cap('#2F80ED', '#1F62BE', '#FFFFFF', '#4B93F0'), case: { bg: 'linear-gradient(180deg,#C6CBD3,#A8AEB8)', edge: '#868C97', plate: '#9AA0AA', badge: '#6C727C' } },
  'kb-a68': { layout: '65', brand: 'ARKON', a: cap('#1F2125', '#121316', '#F2F2F2', '#2B2D33'), m: cap('#F0EFEB', '#CFCDC6', '#1F2125', '#FFFFFF'), x: cap('#F0EFEB', '#CFCDC6', '#1F2125', '#FFFFFF'), case: { bg: 'linear-gradient(180deg,#26282D,#141518)', edge: '#0B0C0E', plate: '#101114', badge: '#5D6068' } },
  'kb-v75': { layout: '75', brand: 'VOLTARO', a: cap('#203250', '#14223A', '#E6D7A8', '#2A3E61'), m: cap('#C9A85A', '#A18338', '#1B2537', '#D8BA73'), x: cap('#C9A85A', '#A18338', '#1B2537', '#D8BA73'), case: { bg: 'linear-gradient(180deg,#25385A,#162540)', edge: '#0D172A', plate: '#121E33', badge: '#C9A85A' } },
  'kb-k65': { layout: '65', brand: 'KEYVO', rgb: true, a: cap('#1B1C21', '#0F1013', '#F4F4F6', '#262830'), m: cap('#1B1C21', '#0F1013', '#C9CBD1', '#262830'), x: cap('#1B1C21', '#0F1013', '#FF8A5C', '#262830'), case: { bg: 'linear-gradient(180deg,#1F2025,#0F1012)', edge: '#060607', plate: '#0B0B0D', badge: '#4E5159' } },
  'kb-s75': { layout: '75', brand: 'SONIQUE', a: cap('#E7ECE3', '#C6CFC0', '#38452F', '#F2F5EE'), m: cap('#8FA585', '#71876A', '#FFFFFF', '#A1B597'), x: cap('#5F7A55', '#475E3F', '#FFFFFF', '#718C66'), case: { bg: 'linear-gradient(180deg,#DCE3D6,#C2CCBA)', edge: '#9DA894', plate: '#B5C0AC', badge: '#7E8A74' } },
  'kb-a75': { layout: '75', brand: 'ARKON', a: cap('#FAFAF8', '#D8D7D2', '#202124', '#FFFFFF'), m: cap('#2A2C31', '#18191C', '#F2F2F2', '#373A40'), x: cap('#E5484D', '#B8343A', '#FFFFFF', '#EC6569'), case: { bg: 'linear-gradient(180deg,#F5F5F3,#E0DFDB)', edge: '#BAB8B2', plate: '#CFCDC7', badge: '#9C9A94' } },
  'kb-v68': { layout: '65', brand: 'VOLTARO', translucent: true, rgb: true, a: cap('#DDF1FA', '#A9D3E6', '#20506A', '#F2FAFE'), m: cap('#9FD3EA', '#74B4D0', '#163F55', '#B8E0F1'), x: cap('#4CB3E0', '#2E8EB9', '#FFFFFF', '#6CC4EA'), case: { bg: 'linear-gradient(180deg,rgba(214,238,248,.95),rgba(170,212,232,.95))', edge: '#7FB3CB', plate: '#BFE0EE', badge: '#5D97B1' } },
  'kb-k87': { layout: 'tkl', brand: 'KEYVO', u: 38, a: cap('#26282D', '#16171A', '#F2F2F2', '#33363C'), m: cap('#26282D', '#16171A', '#B9BCC2', '#33363C'), x: cap('#E5363C', '#B4242A', '#FFFFFF', '#EE5257'), spaceAccent: true, case: { bg: 'linear-gradient(180deg,#2A2C31,#17181B)', edge: '#0B0C0E', plate: '#101114', badge: '#5D6068' } },
};
const front = { rx: 46, rz: -12 }, angle = { rx: 52, rz: -16 };
const jobs = [];
for (const [name, o] of Object.entries(kbs)) {
  const s = o.layout === 'full' ? 0.86 : o.layout === 'tkl' ? 0.92 : o.layout === '60' ? 1.08 : 1.0;
  jobs.push([name, studio(tilt(keyboardHTML(o), front, s), { noFloor: true })]);
}
const k68 = kbs['kb-k68'];
jobs.push(['kb-k68-angle', studio(tilt(keyboardHTML(k68), { rx: 56, rz: 22 }, 1.0), { noFloor: true })]);
jobs.push(['kb-k68-top', studio(tilt(keyboardHTML(k68), { rx: 0, rz: 0 }, 1.08), { noFloor: true })]);
jobs.push(['kb-k68-side', studio(tilt(keyboardHTML(k68), { rx: 68, rz: 6 }, 0.98), { noFloor: true })]);

const mice = {
  'mouse-g3': { brand: 'CLICKRA', base: '#F3F3F1', hi: '#FFFFFF', edge: '#C9C9C4', line: '#BDBDB7', wheel: '#E8E8E6', logo: '#B0B0AA', side: true },
  'mouse-g3-black': { brand: 'CLICKRA', base: '#2B2D33', hi: '#53575F', edge: '#121316', line: '#0C0D0F', wheel: '#3A3D44', logo: '#6A6E77', side: true, gloss: .25 },
  'mouse-g1': { brand: 'CLICKRA', base: '#25272C', hi: '#4A4D55', edge: '#0E0F11', line: '#0A0A0C', wheel: '#3A3D44', logo: '#5A5D66', side: true, led: '#FF5A1F', gloss: .22 },
  'mouse-m7': { brand: 'ARKON', base: '#5B5F68', hi: '#8A8F99', edge: '#33363C', line: '#2E3036', wheel: '#9A9EA6', logo: '#3B3E45', side: true },
  'mouse-w2': { brand: 'ARKON', base: '#BFD3EA', hi: '#E3EEF9', edge: '#8EA9C8', line: '#8EA6C2', wheel: '#F1F5FA', logo: '#7D97B5', w: 270 },
  'mouse-g5': { brand: 'CLICKRA', base: '#C9343A', hi: '#EE5A5F', edge: '#7E1A1E', line: '#5E1215', wheel: '#2A2C31', logo: '#FFD2D3', honey: true, hole: '#1A1B1F', side: true },
};
for (const [name, o] of Object.entries(mice)) jobs.push([name, studio(mouseSVG(o), { floor: 19 })]);

jobs.push(['keycaps-pbt', studio(tilt(keyboardHTML({ ...kbs['kb-k75'], caseless: true, tray: 'transparent' }), { rx: 48, rz: -14 }, 1.0), { noFloor: true })]);
jobs.push(['pad-xl', studio(tilt(`<div style="position:relative;width:820px;height:380px;border-radius:22px;background:radial-gradient(120% 140% at 20% 10%,#2E3138,#17181C 70%);box-shadow:0 6px 0 #0B0C0E,0 40px 50px -10px rgba(0,0,0,.4)">
  <div style="position:absolute;inset:8px;border-radius:16px;border:2px dashed #3F424A"></div>
  <div style="position:absolute;left:44px;bottom:36px;font:800 22px Inter;letter-spacing:.3em;color:#FF5A1F">CLICKRA</div>
  <div style="position:absolute;right:44px;bottom:40px;font:600 13px Inter;letter-spacing:.2em;color:#5C6069">SPEED · XL</div></div>`, { rx: 50, rz: -14 }, 0.95), { noFloor: true })]);
jobs.push(['wrist-rest', studio(tilt(`<div style="width:760px;height:140px;border-radius:70px 70px 40px 40px;background:repeating-linear-gradient(92deg,rgba(0,0,0,.06) 0 3px,transparent 3px 11px),repeating-linear-gradient(88deg,rgba(255,255,255,.05) 0 2px,transparent 2px 17px),linear-gradient(180deg,#B07A4E,#8A5732);box-shadow:0 16px 0 #5E3A20,0 40px 50px -10px rgba(0,0,0,.4),inset 0 3px 0 rgba(255,255,255,.25)"></div>`, { rx: 52, rz: -14 }, 1.05), { noFloor: true })]);
jobs.push(['cable-coil', studio(`<svg viewBox="0 0 400 300" width="720" style="filter:drop-shadow(0 22px 18px rgba(0,0,0,.3))">
  <path d="M20 210C60 210 70 150 110 150" stroke="#2A2C31" stroke-width="12" fill="none" stroke-linecap="round"/>
  ${Array.from({ length: 14 }, (_, i) => `<ellipse cx="${118 + i * 11}" cy="150" rx="12" ry="34" fill="none" stroke="${i % 2 ? '#FF5A1F' : '#E04A12'}" stroke-width="9"/>`).join('')}
  <path d="M272 150C300 150 310 100 350 100" stroke="#FF5A1F" stroke-width="12" fill="none" stroke-linecap="round"/>
  <rect x="340" y="86" width="44" height="28" rx="8" fill="#B9BCC4"/><rect x="380" y="93" width="14" height="14" rx="3" fill="#6B6F78"/>
  <rect x="4" y="198" width="26" height="24" rx="6" fill="#B9BCC4"/></svg>`, { floor: 26 })]);
jobs.push(['switch-red', studio(switchSVG(['#E5484D', '#A52A2F']), { floor: 20 })]);
jobs.push(['switch-brown', studio(switchSVG(['#9A6845', '#5E3B23']), { floor: 20 })]);
jobs.push(['switch-blue', studio(switchSVG(['#3E7BEA', '#2349A6']), { floor: 20 })]);

// hero: dark desk scene, product on the right so text can sit on the left
const heroKb = { ...kbs['kb-k68'], a: cap('#2C2E34', '#1B1C20', '#E8E8EA', '#3A3D44'), m: cap('#3C3F47', '#26282E', '#C9CBD1', '#4A4D56'), case: { bg: 'linear-gradient(180deg,#2E3036,#1A1B1F)', edge: '#0C0D0F', plate: '#121316', badge: '#5D6068' } };
jobs.push(['hero', `<style>${css}</style><div class="stage" id="s" style="width:1680px;height:840px;background:radial-gradient(60% 80% at 72% 60%,#3A2418 0%,#1A1513 45%,#121316 75%);justify-content:flex-end">
  <div style="position:absolute;right:-120px;top:330px;width:1150px;height:560px;border-radius:40px;background:linear-gradient(180deg,#1E2025,#15161A);transform:perspective(1800px) rotateX(58deg) rotateZ(-10deg);box-shadow:0 0 120px rgba(255,90,31,.18)"></div>
  <div style="position:absolute;right:260px;top:210px">${tilt(keyboardHTML(heroKb), { rx: 50, rz: -12 }, 1.12)}</div>
  <div style="position:absolute;right:70px;top:430px">${mouseSVG({ ...mice['mouse-g1'], w: 210, rot: -18 })}</div>
  <div style="position:absolute;right:420px;top:120px;width:520px;height:220px;background:radial-gradient(50% 50%,rgba(255,90,31,.35),transparent 70%);filter:blur(30px)"></div></div>`]);

// Light hero for the light theme: the same desk scene on warm paper, without the dark backdrop.
jobs.push(['hero-light', `<style>${css}</style><div class="stage" id="s" style="width:1680px;height:840px;background:radial-gradient(70% 90% at 70% 55%,#FFFFFF 0%,#F6F3EE 50%,#EEEAE3 100%);justify-content:flex-end">
  <div style="position:absolute;right:-260px;top:340px;width:1000px;height:540px;border-radius:40px;background:linear-gradient(180deg,#E7E3DB,#DDD8CE);transform:perspective(1800px) rotateX(58deg) rotateZ(-10deg);box-shadow:0 30px 60px rgba(60,50,40,.12)"></div>
  <div style="position:absolute;right:170px;top:230px">${tilt(keyboardHTML(kbs['kb-k68']), { rx: 50, rz: -12 }, 0.98)}</div>
  <div style="position:absolute;right:30px;top:440px">${mouseSVG({ ...mice['mouse-g3'], w: 180, rot: -18 })}</div></div>`]);

(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1700, height: 1000 } });
  for (const [name, html] of jobs) {
    await page.setContent(`<!doctype html><html><body>${html}</body></html>`);
    const png = `${OUT}/${name}.png`;
    await page.locator('#s').screenshot({ path: png });
    const size = name.startsWith('hero') ? '1680x840' : '800x800';
    execFileSync('convert', [png, '-resize', size, '-quality', '80', `${OUT}/${name}.webp`]);
    fs.unlinkSync(png);
  }
  await browser.close();
  console.log(jobs.length, 'images');
})();
