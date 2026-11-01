const { chromium } = require('playwright');
const path = require('path');
const dir = __dirname;
const url = 'file://' + path.join(dir, 'index.html');
const sleep = ms => new Promise(r => setTimeout(r, ms));
function assert(c, m) { if (!c) { console.error('FAIL:', m); process.exitCode = 1; } else console.log('ok  ', m); }
(async () => {
  const browser = await chromium.launch();
  for (const rm of ['no-preference', 'reduce']) {
    const ctx = await browser.newContext({ viewport: { width: 390, height: 844 }, reducedMotion: rm });
    const page = await ctx.newPage();
    const errors = [];
    page.on('pageerror', e => errors.push(String(e)));
    page.on('console', m => { if (m.type() === 'error') errors.push('console: ' + m.text()); });
    await page.goto(url);
    await sleep(400);
    const st = () => page.evaluate(() => window.api.state());
    const box = await page.locator('#game').boundingBox();
    const cx = box.x + box.width / 2, cy = box.y + box.height / 2;
    let s = await st();
    assert(s.mode === 'play' && s.locked && s.lockedId === 'earth', `[${rm}] starts locked on Earth`);
    assert(s.reduced === (rm === 'reduce'), `[${rm}] reduced-motion detected = ${s.reduced}`);
    if (rm === 'no-preference') await page.screenshot({ path: path.join(dir, 'shot-start.png') });
    await page.evaluate(() => { api.debug.lock('ceres'); });
    await sleep(200);
    if (rm === 'no-preference') await page.screenshot({ path: path.join(dir, 'shot-arrow.png') });
    // mute toggle: tap the speaker hit area (canvas coords → page coords)
    const toPage = (x, y) => [box.x + x * box.width / 420, box.y + y * box.height / 880];
    const spk = s.speaker;
    const [sx, sy] = toPage(spk.x + spk.w / 2, spk.y + spk.h / 2);
    const m0 = (await st()).muted;
    await page.mouse.click(sx, sy); await sleep(50);
    let m1 = await st();
    const stored = await page.evaluate(() => localStorage.getItem('orbit_sound'));
    assert(m1.muted === !m0 && stored === (m1.muted ? '0' : '1') && m1.locked && m1.fuel === 5, `[${rm}] speaker toggles mute ${m0} -> ${m1.muted}, stored '${stored}', no game input`);
    await page.reload(); await sleep(300);
    m1 = await st();
    assert(m1.muted === !m0, `[${rm}] mute persisted across reload (${m1.muted})`);
    await page.mouse.click(sx, sy); await sleep(50);
    assert((await st()).muted === m0, `[${rm}] speaker toggles back`);
    await page.evaluate(() => api.restart()); await sleep(50);
    // hold+release → flying
    await page.mouse.move(cx, cy); await page.mouse.down(); await sleep(300); await page.mouse.up();
    await sleep(50);
    s = await st();
    assert(s.audio !== 'none', `[${rm}] AudioContext created on first gesture (${s.audio})`);
    assert(!s.locked && s.speed > 3.5, `[${rm}] released, flying at ${s.speed.toFixed(2)}`);
    // tap → burn
    const f0 = s.fuel;
    await page.mouse.down(); await sleep(60); await page.mouse.up(); await sleep(50);
    s = await st();
    assert(s.fuel === f0 - 1 && !s.locked, `[${rm}] tap burn fuel ${f0} -> ${s.fuel}, speed ${s.speed.toFixed(2)}`);
    // tap near a ring (tentative lock reverted + burn)
    await page.evaluate(() => api.debug.setProbe(200, 2380 - 80 - 60, 0, -3));
    await page.mouse.down(); await sleep(70); await page.mouse.up(); await sleep(40);
    s = await st();
    assert(!s.locked && s.fuel === f0 - 2, `[${rm}] tap within lock range reverts lock and burns (fuel ${s.fuel})`);
    console.log('     shields before venus', (await st()).shields);
    // hold near a ring → lock
    await page.evaluate(() => { api.restart(); api.debug.setProbe(110 + 72 + 40, 1760, 0, -3); });
    await page.mouse.down(); await sleep(400);
    s = await st();
    assert(s.locked && s.lockedId === 'venus', `[${rm}] hold locks Venus (locked=${s.lockedId})`);
    // heat burns
    for (let i = 0; i < 40; i++) { s = await st(); if (!s.locked) break; await sleep(100); }
    s = await st();
    assert(s.shields === 2 && !s.locked, `[${rm}] Venus heat burned a shield and auto-released (shields ${s.shields}, heat ${s.heat.toFixed(0)})`);
    await page.mouse.up();
    // perfect release
    await page.evaluate(() => { api.debug.lock('ceres'); api.debug.aimPerfect(); });
    s = await st();
    assert(s.chain === 1, `[${rm}] perfect release chain=${s.chain}`);
    await sleep(60);
    if (rm === 'no-preference') await page.screenshot({ path: path.join(dir, 'shot-perfect.png') });
    // burn with 0 fuel does nothing
    await page.evaluate(() => api.debug.setProbe(210, 1000, 0, -2));
    for (let i = 0; i < 5; i++) { await page.mouse.down(); await sleep(40); await page.mouse.up(); await sleep(40); }
    s = await st();
    assert(s.fuel === 0, `[${rm}] fuel exhausted ${s.fuel}`);
    // belt collision
    await page.evaluate(() => { api.restart(); api.debug.setProbe(210, 1300, 0, -3); });
    await sleep(1500);
    s = await st();
    console.log('     after belt pass at x=210: shields', s.shields, 'mode', s.mode);
    let hit = false;
    for (let x = 40; x <= 380 && !hit; x += 20) {
      await page.evaluate(x => { api.restart(); api.debug.setProbe(x, 1260, 0, -4); }, x);
      await sleep(700);
      s = await st(); if (s.shields < 3) { hit = true; console.log('     belt hit at x=' + x + ', shields ' + s.shields + ', speed after knockback ' + s.speed.toFixed(2)); }
    }
    assert(hit, `[${rm}] asteroid collision costs a shield`);
    // hit-stop: crash into Mars' surface, measure game-time freeze
    const hs = await page.evaluate(() => new Promise(res => {
      api.restart(); api.debug.setProbe(210, 330 + 40 + 9, 0, -3);
      const t0 = performance.now(); let hitAt = null, hitFrame = null, frozenSeen = false;
      (function tick() {
        const s = api.state(), t = performance.now() - t0;
        if (hitAt == null && s.shields < 3) { hitAt = t; hitFrame = s.frame; }
        if (hitAt != null && t - hitAt < 40 && t - hitAt > 15 && s.frame === hitFrame) frozenSeen = true;
        if (t < 500) requestAnimationFrame(tick); else res({ hitAt, frozenSeen, ms: s.lastFreezeMs, shields: s.shields });
      })();
    }));
    if (rm === 'reduce') assert(hs.hitAt != null && hs.ms === 0, `[${rm}] no hit-stop under reduced motion (freeze ${hs.ms})`);
    else assert(hs.hitAt != null && hs.ms >= 60 && hs.frozenSeen, `[${rm}] collision hit-stop froze game time ${hs.ms.toFixed(0)} ms`);
    // fast arrival at station → bounce, shield lost
    await page.evaluate(() => { api.restart(); api.debug.setProbe(340, 330, 0, -4); });
    await sleep(800);
    s = await st();
    assert(s.shields === 2 && s.mode === 'play', `[${rm}] fast arrival bounced (shields ${s.shields})`);
    if (rm === 'no-preference') await page.screenshot({ path: path.join(dir, 'shot-mars.png') });
    // Mars brake: lock, wait, check brake <1
    await page.evaluate(() => api.debug.lock('mars'));
    await sleep(100); let orbitsMs = 100;
    for (; orbitsMs < 15000; orbitsMs += 100) { s = await st(); if (s.speed < 2.6) break; await sleep(100); }
    console.log('     Mars orbit speed < 2.6 after ' + (orbitsMs / 1000).toFixed(1) + ' s');
    assert(orbitsMs < 8000, `[${rm}] brake reaches landing speed quickly`);
    s = await st();
    assert(s.brake < 1, `[${rm}] BRAKE after >1 orbit (brake ${s.brake.toFixed(3)}, speed ${s.speed.toFixed(2)})`);
    if (rm === 'no-preference') await page.screenshot({ path: path.join(dir, 'shot-brake.png') });
    // soft landing
    await page.evaluate(() => api.debug.setProbe(340, 300, 0, -1.5));
    const seq = [];
    for (let i = 0; i < 120; i++) { s = await st(); if (seq[seq.length - 1] !== s.starsShown) seq.push(s.starsShown); if (s.starsShown === 3) break; await sleep(25); }
    if (rm === 'reduce') assert(seq.join() === '0,3', `[${rm}] stars shown at once ${seq.join()}`);
    else assert(seq.join() === '0,1,2,3', `[${rm}] stars pop in sequence ${seq.join()}`);
    s = await st();
    assert(s.mode === 'done', `[${rm}] soft landing -> ${s.mode} stars ${JSON.stringify(s.stars)}`);
    if (rm === 'no-preference') await page.screenshot({ path: path.join(dir, 'shot-done.png') });
    await page.mouse.click(cx, cy); await sleep(100);
    s = await st();
    assert(s.mode === 'play' && s.locked, `[${rm}] Retry restarts`);
    // fly off side → dead
    await page.evaluate(() => api.debug.setProbe(400, 2300, 6, 0));
    await sleep(500);
    s = await st();
    assert(s.mode === 'dead', `[${rm}] off-side -> ${s.mode}`);
    await sleep(900);
    if (rm === 'no-preference') await page.screenshot({ path: path.join(dir, 'shot-dead.png') });
    await page.mouse.click(cx, cy); await sleep(100);
    s = await st();
    assert(s.mode === 'play', `[${rm}] restart after SIGNAL LOST`);
    // natural play: release from start and let it run
    await page.mouse.down(); await sleep(250); await page.mouse.up(); await sleep(2500);
    assert(errors.length === 0, `[${rm}] no page errors ${errors.join(' | ')}`);
    await ctx.close();
  }
  await browser.close();
})();
