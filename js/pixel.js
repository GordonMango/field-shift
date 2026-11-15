/* ============================================================
   Pixel sprites. Every icon in the UI is drawn from a tiny
   character map and rendered to a canvas, nearest-neighbour.
   ============================================================ */
const Pixel = (() => {
  const PAL = {
    g: '#3fa34d', G: '#2a7a37', l: '#8bd64a', y: '#f2c94c', Y: '#d49a1a', b: '#4fa3ff', B: '#1f5fbf', w: '#ffffff', k: '#1a1a1a',
    r: '#e04b3a', R: '#9c2f22', o: '#ff9f2e', O: '#d9731a', t: '#8a5a2b', T: '#5c3a1a', s: '#e3cf92', S: '#b29a60', c: '#cfd8e3', C: '#8d99a8',
    p: '#f3b7a0', n: '#2b3a4f', i: '#dff6ff', I: '#9fd6f5', m: '#5a4030', d: '#3b2a1a', e: '#1b2e1a', x: '#3a3f4a',
  };
  const MAPS = {
    corn: [
      '....l.......', '...lgl......', '..lgGgl.....', '...lgl..l...', '....g..lgl..', '...yg.lgGgl.',
      '..yYg..lgl..', '..yYg...g...', '...yg..yg...', '....g.yYg...', '....g..g....', '...TGG.G....'],
    wheat: [
      '....y..y....', '...yYy.yYy..', '...yYy.yYy..', '....y...y...', '..y.y.y.y...', '.yYyYyYyYy..',
      '.yYyyYyyYy..', '..y.y.y.y...', '....Y..Y....', '....Y..Y....', '....Y..Y....', '...TTTTTT...'],
    soy: [
      '............', '....gg......', '...gGGg.gg..', '..gGllGgGGg.', '..gGGGGGGGg.', '...gGlGGlg..',
      '..gGGGGGGGg.', '...gGGGGGg..', '....gGGg....', '.....GG.....', '.....GG.....', '...TTTTTT...'],
    tomato: [
      '............', '.....gg.....', '....gGGg....', '...gGGGGg...', '..gGrrGGGg..', '..gGrRrGGg..',
      '..gGGrGGGg..', '...gGGGGrg..', '....gGrRr...', '.....Grr....', '.....GG.....', '...TTTTTT...'],
    drip: [
      '............', '............', '....b..b....', '...bib.bib..', '....b..b....', '............',
      'BBBBBBBBBBBB', 'bbbbbbbbbbbb', 'BBBBBBBBBBBB', '............', '............', '............'],
    mulch: [
      '............', '............', '............', '..sS..sSs...', '.sSsSsSsSsS.', 'sSsSsSsSsSsS',
      'SsSsSsSsSsSs', 'sSsSsSsSsSsS', '.SsSsSsSsSs.', '............', '............', '............'],
    cover: [
      '............', '............', '............', '..l..l..l...', '.l.l.ll.l.l.', '..l.l..l.l..',
      '.l..ll.l..l.', '..l.l.l.l.l.', '.lgllglgllg.', 'gGgGgGgGgGgG', 'TTTTTTTTTTTT', '............'],
    windbreak: [
      '.....G......', '....GGG.....', '...GGGGG..G.', '..GGlGGG.GGG', '....GGG.GlGG', '...GGGGGGGGG',
      '..GGGGGG.GGG', '.GGGGlGGGGGG', '....TT...GG.', '....TT...TT.', '....TT...TT.', '...TTTT.TTTT'],
    drain: [
      '............', '............', '............', '............', 'nnnnnnnnnnnn', 'nCnCnCnCnCnC',
      'nnnnnnnnnnnn', 'nCnCnCnCnCnC', 'nnnnnnnnnnnn', '....b..b....', '...b....b...', '............'],
    rowcover: [
      '............', '....wwww....', '..wwccccww..', '.wccccccccw.', '.wccccccccw.', 'wccccccccccw',
      'wccccccccccw', 'Cc........cC', 'C..........C', 'C..........C', 'C..........C', '............'],
    marigold: [
      '............', '.oo......oo.', 'oYYo....oYYo', 'oYyYo..oYyYo', '.oYo....oYo.', '..g......g..',
      '..g.oo...g..', '..g.oYYo.g..', '..gloYyYo...', '...gl.oog...', '....g..g....', '...TTTTTT...'],
    shovel: [
      '..........kk', '.........kk.', '........kk..', '.......kk...', '......kk....', '.....kk.....',
      '...CCkk.....', '..CcccC.....', '..Cccc......', '..CccC......', '...CC.......', '............'],
    drought: [
      '.....oo.....', '.o...oo...o.', '..o.oyyo.o..', '...oyyyyo...', '..oyyyyyyo..', 'ooyyyyyyyyoo',
      'ooyyyyyyyyoo', '..oyyyyyyo..', '...oyyyyo...', '..o.oyyo.o..', '.o...oo...o.', '.....oo.....'],
    heat: [
      '.....ww.....', '....wrrw....', '....wrrw....', '....wrrw....', '....wrrw....', '....wrrw....',
      '...wrrrrw...', '..wrrRRrrw..', '..wrRRRRrw..', '..wrrRRrrw..', '...wrrrrw...', '....wwww....'],
    wind: [
      '.CCCCCCCCCC.', 'CcccccccccCC', '.CCCCCCCCCC.', '...CcccCC...', '..CCccccCC..', '...CCccCC...',
      '....CccC....', '....CcC.....', '.....CC.....', '.....C......', '....CC......', '....C.......'],
    flood: [
      '............', '............', '....ii......', '..iibbi.....', '.ibbbbbi..i.', 'ibBBBbbbiibi',
      'bBBBBBBbbbBb', 'BBBBBBBBBBBB', 'BbBBBbBBBbBB', 'BBBBBBBBBBBB', 'bBBbBBBbBBbB', 'BBBBBBBBBBBB'],
    pest: [
      '............', '..k......k..', '...k....k...', '...mmmmmm...', '..mdmmmmdm..', '.kmmmmmmmmk.',
      '..mdmmddmm..', '.kmmmmmmmmk.', '..mmddmmdm..', '...mmmmmm...', '..k.k..k.k..', '............'],
    frost: [
      '.....ii.....', '..i..ii..i..', '...i.ii.i...', '....iiii....', '..iiiIIiii..', 'iiiiIIIIiiii',
      'iiiiIIIIiiii', '..iiiIIiii..', '....iiii....', '...i.ii.i...', '..i..ii..i..', '.....ii.....'],
    satellite: [
      'bb........bb', 'bBb......bBb', 'bbb..cc..bbb', 'bBb.cccc.bBb', 'bbbCCccCCbbb', 'bBb.cccc.bBb',
      'bbb..cc..bbb', 'bBb......bBb', 'bb...kk...bb', '.....kk.....', '....ckkc....', '....cccc....'],
    heart: [
      '............', '..rr...rr...', '.rrrr.rrrr..', 'rrrrrrrrrrr.', 'rRrrrrrrrrr.', 'rrrrrrrrrrr.',
      '.rrrrrrrrr..', '..rrrrrrr...', '...rrrrr....', '....rrr.....', '.....r......', '............'],
    calendar: [
      '............', '..k......k..', 'cccccccccccc', 'crrrrrrrrrrc', 'cccccccccccc', 'cwkwkwkwkwkc',
      'cccccccccccc', 'cwkwkwkwkwkc', 'cccccccccccc', 'cwkwkwkwkwkc', 'cccccccccccc', '............'],
    coin: [
      '............', '....yyyy....', '..yyYYYYyy..', '.yYYyyyyYYy.', '.yYyyYYyyYy.', 'yYyyYyyYyyYy',
      'yYyyYYYYyyYy', '.yYyyYYyyYy.', '.yYYyyyyYYy.', '..yyYYYYyy..', '....yyyy....', '............'],
    water: [
      '.....b......', '....bib.....', '....bib.....', '...biib.....', '...biiib....', '..biiiiib...',
      '..biIiiib...', '.biIiiiiib..', '.biIIiiiib..', '.bbiiiiibb..', '..bbbbbbb...', '............'],
    sprout: [
      '............', '.....ll.....', '...llgl.ll..', '..lgGgllgl..', '...lggGgGl..', '....lgGgl...',
      '......g.....', '......g.....', '.....Gg.....', '..TTTTTTTT..', '.TtTtTTtTtT.', '............'],
    soil: [
      '............', '............', '............', '..g..l...g..', '.lGl.gl.lGl.', 'TTTTTTTTTTTT',
      'tTtTtTtTtTtT', 'TtTmTtTTtTmT', 'tTtTtmTtTtTt', 'TmTtTtTtmTtT', 'tTtTtTtTtTtT', '............'],
    farmer: [
      '......ssss......', '.....ssssss.....', '....sSSSSSSs....', '..ssssssssssss..', '..SSSSSSSSSSSS..', '....pppppppp....',
      '....pkppppkp....', '....pppppppp....', '....ppprrppp....', '.....pppppp.....', '......rrrr......', '....rrBBBBrr....',
      '...rrBbBBbBrr...', '...prBBBBBBrp...', '...prBBBBBBrp...', '....BBBBBBBB....', '....BBBBBBBB....', '....BBB..BBB....',
      '....BBB..BBB....', '....BBB..BBB....', '....BBB..BBB....', '....TTT..TTT....', '...TTTT..TTTT...', '................'],
  };
  const SWAPS = { heart_off: ['heart', { r: 'x', R: 'x' }] };
  const cache = {};

  function render(name) {
    if (cache[name]) return cache[name];
    let map = MAPS[name], swap = null;
    if (!map && SWAPS[name]) { map = MAPS[SWAPS[name][0]]; swap = SWAPS[name][1]; }
    if (!map) return '';
    const h = map.length, w = map[0].length, sc = 4;
    const cv = document.createElement('canvas'); cv.width = w * sc; cv.height = h * sc;
    const ctx = cv.getContext('2d');
    for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
      let ch = map[y][x]; if (swap && swap[ch]) ch = swap[ch];
      if (ch === '.') continue;
      ctx.fillStyle = PAL[ch] || '#f0f'; ctx.fillRect(x * sc, y * sc, sc, sc);
    }
    return (cache[name] = cv.toDataURL());
  }
  function img(name, cls = '') { const src = render(name); return src ? `<img class="px ${cls}" src="${src}" alt="${name}" draggable="false">` : ''; }

  // swap the emoji icons in CONTENT for sprites
  function install() {
    Object.values(CONTENT.ITEMS).forEach(it => { it.icon = img(it.id); });
    Object.values(CONTENT.HAZARDS).forEach(t => { t.icon = img(t.id); });
  }
  return { render, img, install, MAPS };
})();
Pixel.install();
