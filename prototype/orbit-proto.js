/* Kepler Drift — playable prototype (MISSION 3 · MARS).
 * Single file, no external dependencies, no image assets: everything is procedural.
 * Usage: <canvas id="game" width="420" height="880"></canvas>  ->  window.orbitMount('game')
 * Units: px and px/frame (fixed 60 Hz step). World 420 x 2600, y grows downward.
 */
(function () {
  'use strict';

  // ---- Tuning (decision record: numbers come from the game design spec) ----
  var WORLD_H = 2600;
  var ORBIT_GAP = 46;        // orbit ring = body radius + 46
  var LOCK_RANGE = 150;      // halkaya en fazla bu kadar uzaktan kilitlenilir
  var OMEGA = 0.055;         // speed = 0.055 x radius  ->  angular speed is constant regardless of radius
  var PERFECT_DEG = 8;       // perfect window ±8°
  var TAP_MS = 180, TAP_PX = 8;
  var BURN_DV = 2.2, FUEL_MAX = 5, SHIELD_MAX = 3;
  var SAFE_SPEED = 2.6, STATION_R = 40;
  var BRAKE_PER_ORBIT = 0.68; // at Mars 4.7 -> <2.6 px/frame in ~2 extra laps (0.85 took ~4 laps, far too slow)
  var HEAT_RATE = 1, HEAT_COOL = 0.6;
  var STEP = 1000 / 60;
  var TAU = Math.PI * 2;
  var SUN_ANG = Math.atan2(-1, -1);           // sun at the top left
  var SUN = { x: Math.cos(SUN_ANG), y: Math.sin(SUN_ANG) };
  var COL = { bg: '#05070d', cyan: '#7fd8ff', amber: '#ffb347', white: '#ffffff' };
  var FONT = '"IBM Plex Mono", monospace';
  // C5 pentatonic ladder (523…1568 Hz): the tone rises as the chain grows.
  var PENTA = [523.25, 587.33, 659.25, 783.99, 880, 1046.5, 1174.66, 1318.51, 1567.98];

  function rng(seed) { // mulberry32: the same star/rock layout on every launch
    return function () {
      seed |= 0; seed = (seed + 0x6D2B79F5) | 0;
      var t = Math.imul(seed ^ (seed >>> 15), 1 | seed);
      t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
      return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
  }
  function clamp(v, a, b) { return v < a ? a : v > b ? b : v; }
  function hyp(x, y) { return Math.sqrt(x * x + y * y); }

  window.orbitMount = function (canvasId) {
    var cv = document.getElementById(canvasId);
    if (!cv || !cv.getContext) { console.warn('orbitMount: no canvas', canvasId); return null; }
    if (cv.__orbit) return cv.__orbit;
    var g = cv.getContext('2d');
    var W = cv.width, H = cv.height;
    cv.style.touchAction = 'none';

    var mq = window.matchMedia ? window.matchMedia('(prefers-reduced-motion: reduce)') : null;
    var reduced = !!(mq && mq.matches);
    if (mq) {
      var onMq = function (e) { reduced = e.matches; };
      if (mq.addEventListener) mq.addEventListener('change', onMq); else if (mq.addListener) mq.addListener(onMq);
    }

    // ---------------- Sound (fully procedural WebAudio) ----------------
    // Decision: no files/network, every sound is oscillator + noise + filter. A single AudioContext is
    // created on the first pointerdown (the autoplay policy requires a gesture), resume() is tried on
    // every gesture. Every call is try/catch: a suspended/unsupported context never crashes the game, it just goes silent.
    var ac = null, master = null, noiseBuf = null, amb = null, shapeCurve = null;
    var gestured = false, humOn = false;
    var SND_KEY = 'orbit_sound', MASTER = 0.6;
    var muted = false;
    try { muted = window.localStorage.getItem(SND_KEY) === '0'; } catch (e) {}
    function ensureAudio() {
      try {
        if (!ac) {
          var AC = window.AudioContext || window.webkitAudioContext;
          if (AC) { ac = new AC(); buildGraph(); }
        }
        if (ac && ac.state === 'suspended' && ac.resume) { var pr = ac.resume(); if (pr && pr.catch) pr.catch(function () {}); }
      } catch (e) { ac = null; master = null; amb = null; }
    }
    function buildGraph() {
      master = ac.createGain(); master.gain.value = muted ? 0 : MASTER; master.connect(ac.destination);
      // 2 s of white noise is generated once; every burst plays from a random offset so
      // back-to-back burns do not sound like copies of each other.
      noiseBuf = ac.createBuffer(1, ac.sampleRate * 2, ac.sampleRate);
      var d = noiseBuf.getChannelData(0), i;
      for (i = 0; i < d.length; i++) d[i] = Math.random() * 2 - 1;
      // Distortion curve (HEAT thump): tanh, k=6 -> adds warm, dirty harmonics to a sine.
      shapeCurve = new Float32Array(1024);
      for (i = 0; i < 1024; i++) { var x = i / 511.5 - 1; shapeCurve[i] = Math.tanh(6 * x) / Math.tanh(6); }
      // Ambience bed: 55 and 55.5 Hz -> a slow 0.5 Hz beat, the "breath" of the void.
      // The 200 Hz lowpass cuts the triangle's harmonics, only the body remains.
      var bedF = ac.createBiquadFilter(); bedF.type = 'lowpass'; bedF.frequency.value = 200;
      var bedG = ac.createGain(); bedG.gain.value = 0.05; bedF.connect(bedG); bedG.connect(master);
      [['sine', 55], ['triangle', 55.5]].forEach(function (o) { var v = ac.createOscillator(); v.type = o[0]; v.frequency.value = o[1]; v.connect(bedF); v.start(); });
      // Radio crackle: looped noise, 3 kHz bandpass, very quiet.
      var hs = ac.createBufferSource(); hs.buffer = noiseBuf; hs.loop = true;
      var hf = ac.createBiquadFilter(); hf.type = 'bandpass'; hf.frequency.value = 3000; hf.Q.value = 0.8;
      var hg = ac.createGain(); hg.gain.value = 0.015; hs.connect(hf); hf.connect(hg); hg.connect(master); hs.start();
      // The orbit hum always runs, waiting at gain 0: starting a new oscillator on every lock
      // would both click and restart the frequency glide from scratch.
      var ho = ac.createOscillator(); ho.type = 'sawtooth'; ho.frequency.value = 110;
      var hlp = ac.createBiquadFilter(); hlp.type = 'lowpass'; hlp.frequency.value = 520; hlp.Q.value = 2;
      var hgn = ac.createGain(); hgn.gain.value = 0; ho.connect(hlp); hlp.connect(hgn); hgn.connect(master); ho.start();
      amb = { hissG: hg, humO: ho, humG: hgn };
      humOn = false;
    }
    function audioOk() { return !!(ac && master && !muted && ac.state !== 'closed'); }
    function setMuted(m) {
      muted = m;
      try { window.localStorage.setItem(SND_KEY, m ? '0' : '1'); } catch (e) {}
      try { if (master) master.gain.setTargetAtTime(m ? 0 : MASTER, ac.currentTime, 0.03); } catch (e) {}
    }
    // Freezes a scheduled value where it is; cancelScheduledValues alone can snap the value
    // back to the last event and click.
    function holdParam(pm, t) { try { if (pm.cancelAndHoldAtTime) pm.cancelAndHoldAtTime(t); else { pm.cancelScheduledValues(t); pm.setValueAtTime(pm.value, t); } } catch (e) {} }
    // Enveloped oscillator: fast attack, exponential release (natural decay). fEnd -> a downward-gliding "thump".
    function osc(type, f, dur, gain, when, o) {
      if (!audioOk()) return;
      try {
        o = o || {};
        var t0 = ac.currentTime + (when || 0), v = ac.createOscillator(), gn = ac.createGain();
        v.type = type; v.frequency.setValueAtTime(f, t0);
        if (o.fEnd) v.frequency.exponentialRampToValueAtTime(o.fEnd, t0 + dur);
        gn.gain.setValueAtTime(0.0001, t0);
        gn.gain.exponentialRampToValueAtTime(gain, t0 + (o.atk || 0.006));
        gn.gain.exponentialRampToValueAtTime(0.0001, t0 + dur);
        if (o.shape) { var ws = ac.createWaveShaper(); ws.curve = shapeCurve; v.connect(ws); ws.connect(gn); } else v.connect(gn);
        gn.connect(master);
        v.start(t0); v.stop(t0 + dur + 0.05);
      } catch (e) {}
    }
    // Filtered noise burst; f0->f1 sweep and peak time are adjustable.
    function noise(dur, ftype, f0, f1, gain, when, o) {
      if (!audioOk()) return;
      try {
        o = o || {};
        var t0 = ac.currentTime + (when || 0), src = ac.createBufferSource(), fl = ac.createBiquadFilter(), gn = ac.createGain();
        src.buffer = noiseBuf;
        fl.type = ftype; fl.frequency.setValueAtTime(f0, t0); if (o.q) fl.Q.value = o.q;
        if (f1 && f1 !== f0) fl.frequency.exponentialRampToValueAtTime(f1, t0 + dur);
        gn.gain.setValueAtTime(0.0001, t0);
        gn.gain.exponentialRampToValueAtTime(gain, t0 + (o.peak || 0.004));
        gn.gain.exponentialRampToValueAtTime(0.0001, t0 + dur);
        src.connect(fl); fl.connect(gn); gn.connect(master);
        src.start(t0, Math.random() * 1.5); src.stop(t0 + dur + 0.05);
      } catch (e) {}
    }
    // The pentatonic ladder extends forever: 5 steps = 1 octave (octaves of PENTA's first 5 notes).
    function pent(i) { return PENTA[i % 5] * Math.pow(2, Math.floor(i / 5)); }
    var sfx = {
      clunk: function () { noise(0.06, 'lowpass', 400, 400, 0.35); osc('sine', 90, 0.15, 0.4, 0, { fEnd: 55 }); },
      // A bandpass sweeps 400->2400 Hz, gain peaks in the middle: a passing doppler.
      whoosh: function () { noise(0.35, 'bandpass', 400, 2400, 0.25, 0, { q: 1.4, peak: 0.14 }); },
      perfect: function (chain) {
        var r = Math.min(chain - 1, 8); // root stays within 523…1568 Hz
        // Root, two steps up (~+4 semitones), three steps up (~+7): because they are picked from
        // the pentatonic scale the chord always sounds "right" whatever the chain.
        [pent(r), pent(r + 2), pent(r + 3)].forEach(function (f, i) {
          osc('triangle', f, 0.32, 0.11, i * 0.04);
          osc('sine', f * 2, 0.22, 0.035, i * 0.04); // sparkle
        });
        noise(0.12, 'highpass', 6000, 6000, 0.06);
        if (chain >= 3) osc('sine', pent(r) / 2, 0.3, 0.12, 0, { atk: 0.01 });
      },
      burn: function () { noise(0.18, 'lowpass', 1200, 300, 0.3); osc('sine', 70, 0.2, 0.35, 0, { fEnd: 45 }); },
      dry: function () { osc('sine', 1000, 0.003, 0.15, 0, { atk: 0.0008 }); },
      tick: function () { osc('sine', 1500, 0.025, 0.05, 0, { atk: 0.002 }); },
      heat: function () { osc('sine', 50, 0.4, 0.5, 0, { shape: true, atk: 0.01 }); noise(0.3, 'highpass', 2500, 2500, 0.12); },
      hit: function () { osc('sine', 60, 0.25, 0.5, 0, { fEnd: 38 }); noise(0.08, 'bandpass', 1800, 600, 0.2, 0, { q: 0.7 }); },
      sample: function () { osc('sine', 880, 0.06, 0.12); osc('sine', 1320, 0.06, 0.12, 0.065); },
      brake: function () { noise(0.09, 'lowpass', 500, 500, 0.16, 0, { peak: 0.02 }); },
      dock: function () {
        osc('sine', 660, 0.4, 0.12); osc('sine', 990, 0.4, 0.08);
        [523.25, 659.25, 783.99].forEach(function (f, i) { osc('triangle', f, 0.5, 0.11, 0.42 + i * 0.12, { atk: 0.01 }); });
      },
      ping: function (when) { osc('sine', 1320, 0.25, 0.09, when || 0); },
      lost: function () {
        if (!ac || !amb) return;
        try { // the hum cuts hard (no decay): a disconnection feel
          var t = ac.currentTime;
          holdParam(amb.humG.gain, t); amb.humG.gain.setValueAtTime(0, t); humOn = false;
          holdParam(amb.hissG.gain, t); amb.hissG.gain.setValueAtTime(0.03, t); amb.hissG.gain.setValueAtTime(0, t + 0.25);
        } catch (e) {}
        if (!audioOk()) return;
        try { // power loss: 80 Hz saw, lowpass closes 2000->100 Hz
          var t0 = ac.currentTime, v = ac.createOscillator(), fl = ac.createBiquadFilter(), gn = ac.createGain();
          v.type = 'sawtooth'; v.frequency.setValueAtTime(80, t0); v.frequency.exponentialRampToValueAtTime(45, t0 + 0.6);
          fl.type = 'lowpass'; fl.frequency.setValueAtTime(2000, t0); fl.frequency.exponentialRampToValueAtTime(100, t0 + 0.6);
          gn.gain.setValueAtTime(0.15, t0); gn.gain.exponentialRampToValueAtTime(0.0001, t0 + 0.65);
          v.connect(fl); fl.connect(gn); gn.connect(master); v.start(t0); v.stop(t0 + 0.7);
        } catch (e) {}
      },
      reset: function () { // on restart the crackle cut at SIGNAL LOST comes back
        if (!ac || !amb) return;
        try { var t = ac.currentTime; holdParam(amb.hissG.gain, t); amb.hissG.gain.setTargetAtTime(0.015, t, 0.1); } catch (e) {}
      }
    };
    // The hum is derived from state (not events): whichever path lock/release/undo/death comes
    // from, it is right in one place. The frequency is targeted every 3 frames so the
    // AudioParam event queue does not fill up every frame.
    function audioFrame() {
      if (!ac || !amb) return;
      var L = S.lock, want = S.mode === 'play' && !!L && (!L.tentative || L.confirmed);
      try {
        var t = ac.currentTime;
        if (want !== humOn) {
          holdParam(amb.humG.gain, t);
          amb.humG.gain.setTargetAtTime(want ? 0.08 : 0, t, want ? 0.04 : 0.066); // ~120 ms in / ~200 ms out
          humOn = want;
        }
        if (want && S.frame % 3 === 0) {
          var sp = hyp(S.p.vx, S.p.vy);
          amb.humO.frequency.setTargetAtTime(110 + (clamp(sp, 1, 6) - 1) / 5 * 150, t, 0.08);
        }
      } catch (e) {}
    }
    // Haptic feedback: Chrome blocks a vibrate call without a gesture and prints a console
    // warning; so it is never called before the first touch.
    function buzz(pat) {
      if (!gestured || !pat) return;
      try { if (navigator.vibrate) navigator.vibrate(pat); } catch (e) {}
    }
    // Hit-stop: game time stops for 70 ms, drawing continues. Gives a hit "weight".
    var freezeUntil = 0, freezeStart = 0, lastFreezeMs = 0;
    function hitStop() {
      if (reduced) return;
      freezeStart = performance.now(); freezeUntil = freezeStart + 70;
    }

    // ---------------- Background (generated once) ----------------
    var R0 = rng(7);
    var TILE = H + 40;
    function makeStars(n, f, smin, smax, amin, amax) {
      var a = [], tints = ['#ffffff', '#cfdcff', '#fff1dc'];
      for (var i = 0; i < n; i++) a.push({ x: R0() * W, y: R0() * TILE, s: smin + R0() * (smax - smin), a: amin + R0() * (amax - amin), c: tints[(R0() * 3) | 0] });
      return { stars: a, f: f };
    }
    var starLayers = [makeStars(240, 0.05, 0.8, 1.1, 0.15, 0.45), makeStars(90, 0.15, 1, 1.5, 0.3, 0.65), makeStars(30, 0.35, 1.4, 2, 0.55, 0.9)];

    var vignette = document.createElement('canvas'); vignette.width = W; vignette.height = H;
    (function () {
      var v = vignette.getContext('2d');
      var gr = v.createRadialGradient(W / 2, H / 2, H * 0.32, W / 2, H / 2, H * 0.72);
      gr.addColorStop(0, 'rgba(0,0,0,0)'); gr.addColorStop(1, 'rgba(0,0,0,0.62)');
      v.fillStyle = gr; v.fillRect(0, 0, W, H);
    })();

    // ---------------- Bodies ----------------
    function makeBodies() {
      return [
        { id: 'earth', name: 'EARTH', x: 200, y: 2380, r: 34, kind: 'earth', base: ['#a9cdf0', '#2d5d92', '#0a1a2e'], atmo: '140,190,255' },
        { id: 'moon', name: 'MOON', x: 0, y: 0, r: 12, kind: 'moon', base: ['#dedbd4', '#8d8a83', '#2a2927'], rail: { cx: 200, cy: 2380, R: 165, w: 0.0045, a: -1.9 } },
        { id: 'venus', name: 'VENUS', x: 110, y: 1760, r: 26, kind: 'venus', hot: true, base: ['#fffbea', '#e9dcae', '#6d603e'], atmo: '255,240,205' },
        { id: 'ceres', name: 'CERES', x: 300, y: 1430, r: 17, kind: 'rock', base: ['#bcb5a9', '#706960', '#22201c'] },
        { id: 'vesta', name: 'VESTA', x: 140, y: 830, r: 15, kind: 'rock', base: ['#cbc2b3', '#7b7267', '#24211d'] },
        { id: 'mars', name: 'MARS', x: 210, y: 330, r: 40, kind: 'mars', base: ['#eda57e', '#b5532f', '#4a1c0f'], atmo: '230,150,110' }
      ];
    }
    var STATION = { id: 'station', name: 'MARS BASE', x: 340, y: 230 };
    var ROUTE = ['earth', 'moon', 'venus', 'ceres', 'vesta', 'mars', 'station'];

    // The planet image is pre-rendered because the light is fixed (6 gradients + clip every frame is expensive).
    function planetSprite(b) {
      var pad = Math.ceil(b.r * 0.2) + 4, S = (b.r + pad) * 2, c = S / 2, r = b.r;
      var pc = document.createElement('canvas'); pc.width = pc.height = S;
      var p = pc.getContext('2d'), rr = rng(b.r * 31 + 5);
      if (b.atmo) { // thin atmosphere halo (1.12 r, alpha .25)
        var ha = p.createRadialGradient(c, c, r * 0.96, c, c, r * 1.12);
        ha.addColorStop(0, 'rgba(' + b.atmo + ',0.25)'); ha.addColorStop(1, 'rgba(' + b.atmo + ',0)');
        p.fillStyle = ha; p.beginPath(); p.arc(c, c, r * 1.12, 0, TAU); p.fill();
      }
      var bg = p.createRadialGradient(c + SUN.x * r * 0.4, c + SUN.y * r * 0.4, r * 0.1, c, c, r * 1.05);
      bg.addColorStop(0, b.base[0]); bg.addColorStop(0.6, b.base[1]); bg.addColorStop(1, b.base[2]);
      p.save(); p.beginPath(); p.arc(c, c, r, 0, TAU); p.clip();
      p.fillStyle = bg; p.fillRect(0, 0, S, S);
      try { p.filter = 'blur(' + Math.max(1, r * 0.05).toFixed(1) + 'px)'; } catch (e) {}
      var i;
      if (b.kind === 'mars') {
        for (i = 0; i < 6; i++) { // dark regions (Syrtis-like), soft ellipses
          p.fillStyle = 'rgba(70,24,12,' + (0.22 + rr() * 0.2).toFixed(2) + ')';
          p.beginPath(); p.ellipse(c + (rr() - 0.5) * r * 1.3, c + (rr() - 0.4) * r * 1.1, r * (0.18 + rr() * 0.3), r * (0.08 + rr() * 0.14), rr() * 3, 0, TAU); p.fill();
        }
        p.fillStyle = 'rgba(246,244,250,0.8)';
        p.beginPath(); p.ellipse(c - r * 0.08, c - r * 0.93, r * 0.36, r * 0.13, -0.15, 0, TAU); p.fill();
        p.fillStyle = 'rgba(240,240,248,0.55)';
        p.beginPath(); p.ellipse(c + r * 0.1, c + r * 0.95, r * 0.24, r * 0.08, 0.2, 0, TAU); p.fill();
      } else if (b.kind === 'earth') {
        for (i = 0; i < 5; i++) {
          p.fillStyle = 'rgba(96,112,64,' + (0.35 + rr() * 0.2).toFixed(2) + ')';
          p.beginPath(); p.ellipse(c + (rr() - 0.5) * r * 1.4, c + (rr() - 0.5) * r * 1.4, r * (0.15 + rr() * 0.3), r * (0.1 + rr() * 0.2), rr() * 3, 0, TAU); p.fill();
        }
        for (i = 0; i < 7; i++) {
          p.fillStyle = 'rgba(255,255,255,' + (0.18 + rr() * 0.22).toFixed(2) + ')';
          p.beginPath(); p.ellipse(c + (rr() - 0.5) * r * 1.6, c + (rr() - 0.5) * r * 1.6, r * (0.25 + rr() * 0.35), r * (0.04 + rr() * 0.06), -0.3 + rr() * 0.6, 0, TAU); p.fill();
        }
      } else if (b.kind === 'venus') {
        for (i = 0; i < 6; i++) { // thick cloud bands: almost featureless, lightly banded
          p.fillStyle = i % 2 ? 'rgba(255,255,255,0.14)' : 'rgba(196,170,110,0.14)';
          p.beginPath(); p.ellipse(c + (rr() - 0.5) * r * 0.6, c + (i - 2.5) * r * 0.32, r * 1.1, r * 0.1, 0.25, 0, TAU); p.fill();
        }
      } else { // moon / small bodies: craters
        var n = b.kind === 'moon' ? 9 : 7;
        for (i = 0; i < n; i++) {
          var cx = c + (rr() - 0.5) * r * 1.6, cy = c + (rr() - 0.5) * r * 1.6, cr = r * (0.07 + rr() * 0.17);
          p.fillStyle = 'rgba(30,28,26,0.35)'; p.beginPath(); p.arc(cx, cy, cr, 0, TAU); p.fill();
          p.fillStyle = 'rgba(255,250,240,0.12)'; p.beginPath(); p.arc(cx - SUN.x * -cr * 0.3, cy - SUN.y * -cr * 0.3, cr * 0.7, 0, TAU); p.fill();
        }
      }
      try { p.filter = 'none'; } catch (e) {}
      // TERMINATOR: the centre of the dark radial gradient is shifted toward the sun; so the
      // ~45% away from the sun (bottom right) stays in shadow and the boundary is soft.
      var ox = c + SUN.x * r * 0.5, oy = c + SUN.y * r * 0.5;
      var tg = p.createRadialGradient(ox, oy, 0, ox, oy, r * 1.6);
      tg.addColorStop(0, 'rgba(0,0,0,0)'); tg.addColorStop(0.52, 'rgba(0,0,0,0)');
      tg.addColorStop(0.72, 'rgba(2,3,6,0.62)'); tg.addColorStop(0.9, 'rgba(2,3,6,0.93)'); tg.addColorStop(1, 'rgba(2,3,6,0.97)');
      p.fillStyle = tg; p.fillRect(0, 0, S, S);
      p.restore();
      // Thin bright line on the lit limb
      p.strokeStyle = b.kind === 'venus' ? 'rgba(255,252,236,0.85)' : 'rgba(255,246,228,0.55)';
      p.lineWidth = 1.5;
      p.beginPath(); p.arc(c, c, r - 0.75, SUN_ANG - 1.15, SUN_ANG + 1.15); p.stroke();
      return pc;
    }

    function makeRocks() {
      var rr = rng(42), out = [];
      var lanes = [{ y: 1090, v: 0.6, n: 7 }, { y: 1140, v: -0.9, n: 8 }, { y: 1192, v: 0.5, n: 7 }];
      lanes.forEach(function (L) {
        for (var i = 0; i < L.n; i++) {
          var s = 7 + rr() * 8, verts = [], k = 7 + ((rr() * 3) | 0);
          for (var j = 0; j < k; j++) verts.push({ a: (j / k) * TAU + (rr() - 0.5) * 0.5, m: 0.7 + rr() * 0.35 });
          out.push({ x: -60 + (i + rr() * 0.4) * (540 / L.n), y: L.y + (rr() - 0.5) * 16, vx: L.v * (0.8 + rr() * 0.4), s: s, verts: verts, rot: rr() * TAU, spin: (rr() - 0.5) * 0.02 });
        }
      });
      return out;
    }
    var dust = (function () { var rr = rng(9), a = []; for (var i = 0; i < 60; i++) a.push({ x: rr() * W, y: 1065 + rr() * 150, a: 0.08 + rr() * 0.15 }); return a; })();

    // ---------------- State ----------------
    var S, bodies, sprites = {};
    var ptr = { down: false, t: 0, x: 0, y: 0, moved: false, consumed: false, ignoreUp: false };

    function restart() {
      bodies = makeBodies();
      bodies.forEach(function (b) { if (!sprites[b.id]) sprites[b.id] = planetSprite(b); });
      S = {
        mode: 'play', frame: 0, runT: 0, overlayT: 0,
        p: { x: 0, y: 0, vx: 0, vy: 0 }, trail: [],
        lock: null, lastBody: null, lastBodyT: 0,
        fuel: FUEL_MAX, shields: SHIELD_MAX, samples: 0, heat: 0, chain: 0,
        invuln: 0, shake: 0, punch: 0, flame: 0, fuelBlink: 0, landed: false, stars: [false, false, false],
        camY: WORLD_H - H,
        rocks: makeRocks(),
        items: [{ x: 352, y: 2050, got: false }, { x: 52, y: 1012, got: false }, { x: 336, y: 628, got: false }],
        parts: [], texts: [],
        // Visual feedback counters (in frames; never set while reduced motion is on)
        pull: 0, lines: null, ring: 0, flash: 0, flare: 0, shieldFlash: 0, starsShown: 0,
        pop: { speed: 0, sample: 0, fuel: 0 }, prev: { speed: null, sample: null, fuel: null },
        feltLock: false, tickMs: 0
      };
      sfx.reset();
      railStep(0);
      var d = bodies[0];
      S.p.x = d.x; S.p.y = d.y - (d.r + ORBIT_GAP);
      lockOn(d, false); // start locked to the first body
      S.lock.radius = S.lock.targetR;
      S.lock.dir = 1;
    }

    function railStep(n) {
      bodies.forEach(function (b) {
        if (!b.rail) return;
        b.rail.a += b.rail.w * n;
        b.x = b.rail.cx + Math.cos(b.rail.a) * b.rail.R; b.y = b.rail.cy + Math.sin(b.rail.a) * b.rail.R;
        b.vx = -Math.sin(b.rail.a) * b.rail.R * b.rail.w; b.vy = Math.cos(b.rail.a) * b.rail.R * b.rail.w;
      });
    }
    function bodyById(id) { if (id === 'station') return STATION; for (var i = 0; i < bodies.length; i++) if (bodies[i].id === id) return bodies[i]; return null; }
    function nextTarget(b) { var i = ROUTE.indexOf(b.id); return i >= 0 && i < ROUTE.length - 1 ? bodyById(ROUTE[i + 1]) : null; }

    function findLockable() {
      var best = null, bestD = LOCK_RANGE;
      for (var i = 0; i < bodies.length; i++) {
        var b = bodies[i];
        if (b === S.lastBody && S.frame - S.lastBodyT < 30) continue; // don't immediately recapture the body just released
        var d = Math.abs(hyp(S.p.x - b.x, S.p.y - b.y) - (b.r + ORBIT_GAP));
        if (d < bestD) { bestD = d; best = b; }
      }
      return best;
    }

    function lockOn(b, tentative) {
      var p = S.p, rx = p.x - b.x, ry = p.y - b.y;
      var vx = p.vx - (b.vx || 0), vy = p.vy - (b.vy || 0);
      var cross = rx * vy - ry * vx; // >0 -> increasing angle (clockwise on screen)
      S.lock = {
        body: b, angle: Math.atan2(ry, rx), radius: Math.max(b.r + 6, hyp(rx, ry)), targetR: b.r + ORBIT_GAP,
        dir: cross >= 0 ? 1 : -1, accum: 0, frames: 0, tentative: tentative,
        saved: { x: p.x, y: p.y, vx: p.vx, vy: p.vy }
      };
    }

    // BRAKE rule: every extra lap after the first full lap cuts speed by 32%. Applied continuously,
    // not stepped (0.68^extraLaps), so the speed readout does not jump; floor 0.25.
    function brakeMul(L) { var extra = Math.max(0, L.accum / TAU - 1); return Math.max(0.25, Math.pow(BRAKE_PER_ORBIT, extra)); }
    function tangent(L) { return { x: -Math.sin(L.angle) * L.dir, y: Math.cos(L.angle) * L.dir }; }

    function release(checkPerfect) {
      var L = S.lock, p = S.p, b = L.body, t = tangent(L), sp = OMEGA * L.targetR * brakeMul(L);
      p.vx = t.x * sp + (b.vx || 0); p.vy = t.y * sp + (b.vy || 0);
      S.lock = null; S.lastBody = b; S.lastBodyT = S.frame;
      sfx.whoosh();
      if (!reduced) {
        S.pull = 14; // 4% pull-back: the feel of speed "opening up"
        var la = [], n = 7, base = Math.random() * TAU;
        for (var i = 0; i < n; i++) la.push(base + i / n * TAU + (Math.random() - 0.5) * 0.4);
        S.lines = { t: 10, x: p.x, y: p.y, a: la };
      }
      if (!checkPerfect) { S.chain = 0; return; }
      // PERFECT window: the angle between the release direction (real velocity vector) and the
      // direction to the next target's centre is within ±8°. For the moving Moon, its current position.
      var tg = nextTarget(b);
      if (!tg) { S.chain = 0; return; }
      var dx = tg.x - p.x, dy = tg.y - p.y, dl = hyp(dx, dy), vl = hyp(p.vx, p.vy) || 1;
      var cos = clamp((p.vx * dx + p.vy * dy) / (vl * dl), -1, 1);
      if (Math.acos(cos) * 180 / Math.PI <= PERFECT_DEG) perfect(); else S.chain = 0;
    }

    function perfect() {
      S.chain++;
      var p = S.p;
      text(S.chain > 1 ? 'PERFECT ×' + S.chain : 'PERFECT', p.x, p.y - 18, COL.cyan, 15, 60);
      sfx.perfect(S.chain); buzz(20); hitStop();
      burst(p.x, p.y, 20, ['#ffffff', COL.cyan, '#bfeaff'], 2.4, 34);
      if (!reduced) S.punch = 7; // 120 ms ≈ 7 frames, 1.04 scale
    }

    function burn() {
      if (S.fuel <= 0) { S.fuelBlink = 70; sfx.dry(); return; } // empty tank: dry tick, no thump
      S.fuel--;
      var p = S.p, l = hyp(p.vx, p.vy), dx = l > 0.01 ? p.vx / l : 0, dy = l > 0.01 ? p.vy / l : -1;
      p.vx += dx * BURN_DV; p.vy += dy * BURN_DV;
      S.flame = 12;
      for (var i = 0; i < 16; i++) {
        var a = Math.atan2(-dy, -dx) + (Math.random() - 0.5) * 0.7, s = 1 + Math.random() * 2.6;
        S.parts.push({ x: p.x, y: p.y, vx: Math.cos(a) * s + p.vx * 0.3, vy: Math.sin(a) * s + p.vy * 0.3, life: 22, max: 22, c: Math.random() < 0.5 ? '#ffd28a' : '#ff8a3d', s: 1.6, glow: true });
      }
      sfx.burn(); buzz(15);
    }

    function loseShield(label) {
      if (S.invuln > 0 || S.mode !== 'play') return;
      S.shields--; S.invuln = 70; S.chain = 0;
      if (!reduced) S.shake = 12;
      text(label, S.p.x, S.p.y - 20, COL.amber, 15, 70);
      S.shieldFlash = 36;
      // HEAT is not a collision: it has its own thump, no hit-stop (the player already lost the lock).
      if (label === 'HEAT') sfx.heat(); else { sfx.hit(); hitStop(); }
      buzz(40);
      burst(S.p.x, S.p.y, 12, [COL.amber, '#8a8680'], 2, 26);
      if (S.shields <= 0) die();
    }
    function die() {
      if (S.mode !== 'play') return;
      S.mode = 'dead'; S.overlayT = 0; S.lock = null;
      sfx.lost(); buzz(80);
    }
    function land() {
      S.mode = 'done'; S.overlayT = 0; S.landed = true;
      S.p.vx = S.p.vy = 0;
      S.stars = [true, S.samples >= 3, S.fuel >= 2];
      text('SOFT LANDING', STATION.x - 70, STATION.y - 54, COL.white, 17, 90);
      sfx.dock(); buzz([30, 60, 30]);
      if (!reduced) { S.ring = 50; S.flash = 18; }
      burst(S.p.x, S.p.y, 24, ['#ffffff', COL.cyan], 1.6, 40);
    }

    function bounce(nx, ny, keep) {
      var p = S.p, dot = p.vx * nx + p.vy * ny;
      if (dot < 0) { p.vx -= 2 * dot * nx; p.vy -= 2 * dot * ny; }
      p.vx = p.vx * keep + nx * 1.1; p.vy = p.vy * keep + ny * 1.1;
    }

    function text(t, x, y, c, size, life) { S.texts.push({ t: t, x: x, y: y, c: c, size: size, life: life, max: life }); }
    function burst(x, y, n, cols, sp, life) {
      for (var i = 0; i < n; i++) {
        var a = Math.random() * TAU, s = sp * (0.4 + Math.random());
        S.parts.push({ x: x, y: y, vx: Math.cos(a) * s, vy: Math.sin(a) * s, life: life, max: life, c: cols[i % cols.length], s: 1.4, glow: false });
      }
    }

    // ---------------- Update (fixed 60 Hz step) ----------------
    function update() {
      S.frame++;
      railStep(1);
      S.rocks.forEach(function (k) { k.x += k.vx; k.rot += k.spin; if (k.x > 480) k.x -= 540; else if (k.x < -60) k.x += 540; });
      if (S.mode === 'play') stepProbe(); else S.overlayT++;

      for (var i = S.parts.length - 1; i >= 0; i--) { var q = S.parts[i]; q.x += q.vx; q.y += q.vy; q.vx *= 0.96; q.vy *= 0.96; if (--q.life <= 0) S.parts.splice(i, 1); }
      for (i = S.texts.length - 1; i >= 0; i--) { var t = S.texts[i]; t.y -= 0.45; if (--t.life <= 0) S.texts.splice(i, 1); }
      if (S.invuln > 0) S.invuln--;
      if (S.shake > 0) S.shake--;
      if (S.punch > 0) S.punch--;
      if (S.flame > 0) S.flame--;
      if (S.fuelBlink > 0) S.fuelBlink--;
      if (S.pull > 0) S.pull--;
      if (S.lines && --S.lines.t <= 0) S.lines = null;
      if (S.ring > 0) S.ring--;
      if (S.flash > 0) S.flash--;
      if (S.shieldFlash > 0) S.shieldFlash--;
      feelFrame();
      audioFrame();

      // The camera follows upward fast, downward slowly (<=2 px/frame): a probe escaping downward
      // falls out of frame and becomes SIGNAL LOST, so falling back feels like a mistake.
      var target = clamp(S.p.y - H * 0.55, 0, WORLD_H - H);
      var dy = (target - S.camY) * 0.08;
      S.camY += dy > 0 ? Math.min(dy, 2) : dy;
    }

    function feelFrame() {
      var L = S.lock;
      // A tentative lock is "confirmed" once 180 ms pass: the click/clunk only then, otherwise
      // every fuel tap (an undone lock) would play a fake lock sound.
      if (L && L.tentative && !L.confirmed && ptr.down && performance.now() - ptr.t >= TAP_MS) L.confirmed = true;
      var felt = S.mode === 'play' && !!L && (!L.tentative || L.confirmed);
      if (felt && !S.feltLock) { sfx.clunk(); buzz(10); }
      S.feltLock = felt;
      // HEAT ticking: 400 ms interval at 50%, 90 ms at 100% — speeds up like a geiger counter.
      if (S.heat > 50 && S.mode === 'play') {
        S.tickMs -= STEP;
        if (S.tickMs <= 0) { sfx.tick(); S.tickMs = 400 - (S.heat - 50) / 50 * 310; }
      } else S.tickMs = 0;
      if (L && S.mode === 'play' && brakeMul(L) < 0.999 && S.frame % 30 === 0) sfx.brake();
      // Short grow when HUD numbers change; while a value changes continuously (brake) the pop is
      // not re-armed before it ends, otherwise the number would stay swollen.
      var cur = { speed: hyp(S.p.vx, S.p.vy).toFixed(1), sample: S.samples, fuel: S.fuel };
      for (var k in cur) {
        if (S.pop[k] > 0) S.pop[k]--;
        if (cur[k] !== S.prev[k]) { if (S.prev[k] !== null && S.pop[k] <= 0 && !reduced) S.pop[k] = 9; S.prev[k] = cur[k]; }
      }
      var ve = bodyById('venus');
      var want = !reduced && S.mode === 'play' && hyp(S.p.x - ve.x, S.p.y - ve.y) < 160 ? 1 : 0;
      S.flare += (want - S.flare) * 0.08;
      if (S.flare < 0.002) S.flare = 0;
      // Stars: once the panel opens (50 + 18 frames) one by one 250 ms apart; every earned star
      // rings at its own moment so sound and picture land on the same beat.
      if (S.mode === 'done') {
        var s0 = reduced ? 50 : 68;
        var n = S.overlayT < s0 ? 0 : reduced ? 3 : Math.min(3, Math.floor((S.overlayT - s0) / 15) + 1), j = 0;
        while (S.starsShown < n) { var i = S.starsShown++; if (S.stars[i]) sfx.ping(reduced ? 0.1 * j++ : 0); }
      }
    }

    function stepProbe() {
      var p = S.p;
      S.runT++;
      if (S.lock) {
        var L = S.lock, b = L.body;
        L.frames++;
        L.radius += (L.targetR - L.radius) * 0.15; // ease into the ring (no instant teleport)
        var bm = brakeMul(L), w = OMEGA * bm;
        L.angle += w * L.dir; L.accum += w;
        p.x = b.x + Math.cos(L.angle) * L.radius; p.y = b.y + Math.sin(L.angle) * L.radius;
        var t = tangent(L);
        p.vx = t.x * OMEGA * L.targetR * bm + (b.vx || 0); p.vy = t.y * OMEGA * L.targetR * bm + (b.vy || 0);
        // HEAT: Venus is close to the sun; while locked it fills 1% per frame -> burn-out after ~1.7 s.
        // This forbids taking a brake lap at Venus: pass fast, don't linger.
        if (b.hot) {
          S.heat = Math.min(100, S.heat + HEAT_RATE);
          if (S.heat >= 100) { release(false); ptr.consumed = true; loseShield('HEAT'); S.heat = 70; }
        }
      } else {
        p.x += p.vx; p.y += p.vy;
        if (ptr.down && !ptr.consumed) {
          var cand = findLockable();
          // Locking while held: if the tap time has not elapsed yet the lock is "tentative"; if the
          // finger lifts within 180 ms the lock is undone and the touch turns into a fuel burn.
          if (cand) lockOn(cand, performance.now() - ptr.t < TAP_MS);
        }
        collide();
      }
      if (!S.lock || !S.lock.body.hot) S.heat = Math.max(0, S.heat - HEAT_COOL);
      collectItems();
      if (S.mode === 'play') { S.trail.push({ x: p.x, y: p.y }); if (S.trail.length > 18) S.trail.shift(); }
    }

    function collide() {
      var p = S.p, i;
      for (i = 0; i < S.rocks.length; i++) {
        var k = S.rocks[i], dx = p.x - k.x, dy = p.y - k.y, d = hyp(dx, dy);
        if (d < k.s * 0.85 + 3 && S.invuln <= 0) { bounce(dx / (d || 1), dy / (d || 1), 0.6); loseShield('IMPACT'); if (S.mode !== 'play') return; }
      }
      for (i = 0; i < bodies.length; i++) {
        var b = bodies[i]; dx = p.x - b.x; dy = p.y - b.y; d = hyp(dx, dy);
        if (d < b.r + 3) { var nx = dx / (d || 1), ny = dy / (d || 1); p.x = b.x + nx * (b.r + 4); p.y = b.y + ny * (b.r + 4); bounce(nx, ny, 0.5); loseShield('IMPACT'); if (S.mode !== 'play') return; }
      }
      dx = p.x - STATION.x; dy = p.y - STATION.y; d = hyp(dx, dy);
      if (d < STATION_R) {
        if (hyp(p.vx, p.vy) < SAFE_SPEED) { land(); return; }
        nx = dx / (d || 1); ny = dy / (d || 1);
        p.x = STATION.x + nx * (STATION_R + 1); p.y = STATION.y + ny * (STATION_R + 1);
        bounce(nx, ny, 0.55);
        if (S.invuln <= 0) text('TOO FAST', STATION.x - 50, STATION.y - 52, COL.amber, 16, 80);
        loseShield('');
        if (S.mode !== 'play') return;
      }
      if (p.x < -20 || p.x > W + 20 || p.y < -60 || p.y > S.camY + H + 24) die();
    }

    function collectItems() {
      for (var i = 0; i < S.items.length; i++) {
        var it = S.items[i];
        if (it.got) continue;
        if (hyp(S.p.x - it.x, S.p.y - it.y) < 16) {
          it.got = true; S.samples++;
          text('SAMPLE ' + S.samples + '/3', it.x - 28, it.y - 14, COL.cyan, 13, 60);
          sfx.sample();
          burst(it.x, it.y, 10, [COL.cyan, '#ffffff'], 1.4, 24);
        }
      }
    }

    // ---------------- Input ----------------
    function toCanvas(e) {
      var r = cv.getBoundingClientRect();
      return { x: (e.clientX - r.left) * (cv.width / (r.width || 1)), y: (e.clientY - r.top) * (cv.height / (r.height || 1)) };
    }
    // Speaker button: right above the HUD on the right, 32x32 touch area.
    var SPK = { x: W - 14 - 32, y: H - 78 - 14 - 10 - 32, w: 32, h: 32 };
    function onDown(e) {
      e.preventDefault();
      ensureAudio(); gestured = true;
      var c = toCanvas(e);
      // Button before game input: ptr.down is not set, this touch counts as neither fuel nor lock.
      if (c.x >= SPK.x && c.x <= SPK.x + SPK.w && c.y >= SPK.y && c.y <= SPK.y + SPK.h) { setMuted(!muted); return; }
      try { cv.setPointerCapture(e.pointerId); } catch (er) {}
      ptr.down = true; ptr.t = performance.now(); ptr.x = c.x; ptr.y = c.y; ptr.moved = false; ptr.consumed = false; ptr.ignoreUp = false;
      if (S.mode !== 'play') {
        if (S.overlayT > 40) { restart(); ptr.consumed = true; ptr.ignoreUp = true; }
        else ptr.ignoreUp = true;
      }
    }
    function onMove(e) {
      if (!ptr.down) return;
      var c = toCanvas(e);
      if (hyp(c.x - ptr.x, c.y - ptr.y) > TAP_PX) ptr.moved = true;
    }
    function onUp(e, cancelled) {
      if (!ptr.down) return;
      ptr.down = false;
      if (ptr.ignoreUp || S.mode !== 'play') return;
      var isTap = !cancelled && !ptr.moved && performance.now() - ptr.t < TAP_MS;
      if (S.lock) {
        if (S.lock.tentative && isTap) {
          // Undo the tentative lock: the probe ends up having continued on a straight line as if never locked.
          var sv = S.lock.saved, n = S.lock.frames;
          S.p.x = sv.x + sv.vx * n; S.p.y = sv.y + sv.vy * n; S.p.vx = sv.vx; S.p.vy = sv.vy;
          S.lock = null;
          burn();
        } else release(true);
      } else if (isTap) burn();
    }
    cv.addEventListener('pointerdown', onDown);
    cv.addEventListener('pointermove', onMove);
    cv.addEventListener('pointerup', function (e) { onUp(e, false); });
    cv.addEventListener('pointercancel', function (e) { onUp(e, true); });
    cv.addEventListener('contextmenu', function (e) { e.preventDefault(); });

    // ---------------- Drawing ----------------
    function txt(s, x, y, o) {
      o = o || {};
      g.font = (o.w || 400) + ' ' + (o.size || 12) + 'px ' + FONT;
      g.textAlign = o.align || 'left'; g.textBaseline = o.base || 'alphabetic';
      g.fillStyle = o.c || COL.cyan; g.globalAlpha = o.a == null ? 1 : o.a;
      try { g.letterSpacing = (o.ls || 0) + 'px'; } catch (e) {}
      g.fillText(s, x, y);
      try { g.letterSpacing = '0px'; } catch (e) {}
      g.globalAlpha = 1;
    }
    function brackets(x, y, w, h, len, c, a) {
      g.strokeStyle = c; g.globalAlpha = a; g.lineWidth = 1; g.beginPath();
      g.moveTo(x, y + len); g.lineTo(x, y); g.lineTo(x + len, y);
      g.moveTo(x + w - len, y); g.lineTo(x + w, y); g.lineTo(x + w, y + len);
      g.moveTo(x + w, y + h - len); g.lineTo(x + w, y + h); g.lineTo(x + w - len, y + h);
      g.moveTo(x + len, y + h); g.lineTo(x, y + h); g.lineTo(x, y + h - len);
      g.stroke(); g.globalAlpha = 1;
    }
    function mod(a, n) { return ((a % n) + n) % n; }

    function drawBackground() {
      g.fillStyle = COL.bg; g.fillRect(0, 0, W, H);
      // Nebula: two wide, low-saturation soft blotches, very slow parallax.
      var sh = (S.camY - 860) * 0.12;
      var n1 = g.createRadialGradient(300, 380 - sh, 0, 300, 380 - sh, 380);
      n1.addColorStop(0, 'rgba(92,96,160,0.16)'); n1.addColorStop(1, 'rgba(92,96,160,0)');
      g.fillStyle = n1; g.fillRect(0, 0, W, H);
      var n2 = g.createRadialGradient(70, 640 - sh, 0, 70, 640 - sh, 320);
      n2.addColorStop(0, 'rgba(140,84,62,0.13)'); n2.addColorStop(1, 'rgba(140,84,62,0)');
      g.fillStyle = n2; g.fillRect(0, 0, W, H);
      for (var l = 0; l < starLayers.length; l++) {
        var L = starLayers[l], drift = l === 2 && !reduced ? S.frame * 0.02 : 0;
        for (var i = 0; i < L.stars.length; i++) {
          var s = L.stars[i];
          g.globalAlpha = s.a; g.fillStyle = s.c;
          g.fillRect(mod(s.x + drift, W), mod(s.y - S.camY * L.f, TILE) - 20, s.s, s.s);
        }
      }
      g.globalAlpha = 1;
    }

    function drawSun() {
      // The sun is off-frame at the top left; its light leaks in from the left edge next to Venus.
      var cx = -150, cy = 1630, R = 540;
      if (cy + R < S.camY || cy - R > S.camY + H) return;
      var gr = g.createRadialGradient(cx, cy, 0, cx, cy, R);
      gr.addColorStop(0, 'rgba(255,240,210,0.95)'); gr.addColorStop(0.2, 'rgba(255,214,160,0.5)');
      gr.addColorStop(0.42, 'rgba(255,166,90,0.16)'); gr.addColorStop(1, 'rgba(255,120,50,0)');
      g.globalCompositeOperation = 'lighter';
      g.fillStyle = gr; g.fillRect(0, cy - R, W, R * 2);
      g.globalCompositeOperation = 'source-over';
    }

    function visible(y, m) { return y > S.camY - m && y < S.camY + H + m; }

    function drawWorld() {
      var i, b;
      drawSun();
      // Moon rail
      var moon = bodyById('moon');
      g.strokeStyle = 'rgba(160,170,190,0.12)'; g.lineWidth = 1; g.setLineDash([1, 5]);
      g.beginPath(); g.arc(moon.rail.cx, moon.rail.cy, moon.rail.R, 0, TAU); g.stroke();
      // Orbit rings
      var cand = S.mode === 'play' && !S.lock ? findLockable() : null;
      g.setLineDash([3, 7]);
      for (i = 0; i < bodies.length; i++) {
        b = bodies[i];
        if (!visible(b.y, 220)) continue;
        var locked = S.lock && S.lock.body === b;
        g.strokeStyle = COL.cyan; g.globalAlpha = locked ? 0.5 : b === cand ? 0.3 : 0.14;
        g.lineDashOffset = locked && !reduced ? -S.frame * 0.3 : 0;
        g.beginPath(); g.arc(b.x, b.y, b.r + ORBIT_GAP, 0, TAU); g.stroke();
      }
      g.globalAlpha = 1; g.lineDashOffset = 0;
      // Station capture circle
      if (visible(STATION.y, 120)) {
        g.strokeStyle = COL.amber; g.globalAlpha = 0.3; g.setLineDash([2, 5]);
        g.beginPath(); g.arc(STATION.x, STATION.y, STATION_R, 0, TAU); g.stroke();
        g.globalAlpha = 1;
      }
      g.setLineDash([]);

      // Asteroid belt
      if (visible(1140, 160)) {
        for (i = 0; i < dust.length; i++) { g.globalAlpha = dust[i].a; g.fillStyle = '#9a958d'; g.fillRect(dust[i].x, dust[i].y, 1, 1); }
        g.globalAlpha = 1;
        for (i = 0; i < S.rocks.length; i++) {
          var k = S.rocks[i];
          if (k.x < -20 || k.x > W + 20) continue;
          g.save(); g.translate(k.x, k.y);
          var rg = g.createLinearGradient(-k.s, -k.s, k.s, k.s); // rotation is applied to the vertices so the light direction stays fixed
          rg.addColorStop(0, '#a29d95'); rg.addColorStop(0.55, '#45423e'); rg.addColorStop(1, '#121110');
          g.fillStyle = rg; g.beginPath();
          for (var j = 0; j < k.verts.length; j++) {
            var v = k.verts[j], a = v.a + k.rot;
            if (j === 0) g.moveTo(Math.cos(a) * k.s * v.m, Math.sin(a) * k.s * v.m); else g.lineTo(Math.cos(a) * k.s * v.m, Math.sin(a) * k.s * v.m);
          }
          g.closePath(); g.fill(); g.restore();
        }
      }

      // Planets + labels
      for (i = 0; i < bodies.length; i++) {
        b = bodies[i];
        if (!visible(b.y, 160)) continue;
        var sp = sprites[b.id];
        g.drawImage(sp, b.x - sp.width / 2, b.y - sp.height / 2);
        var rr = (b.r + ORBIT_GAP) * 0.72;
        txt(b.name, b.x + rr + 4, b.y - rr - 2, { size: 10, a: 0.5, ls: 1.5 });
      }

      drawStation();

      // Sample capsules
      for (i = 0; i < S.items.length; i++) {
        var it = S.items[i];
        if (it.got || !visible(it.y, 40)) continue;
        var bob = reduced ? 0 : Math.sin(S.frame * 0.05 + i) * 2;
        g.save(); g.translate(it.x, it.y + bob); g.rotate(reduced ? 0 : S.frame * 0.02);
        g.beginPath(); g.moveTo(0, -7); g.lineTo(5, 0); g.lineTo(0, 7); g.lineTo(-5, 0); g.closePath();
        g.fillStyle = 'rgba(127,216,255,0.35)'; g.fill();
        g.strokeStyle = COL.cyan; g.lineWidth = 1; g.stroke();
        g.restore();
      }

      // Particles
      for (i = 0; i < S.parts.length; i++) {
        var q = S.parts[i];
        g.globalAlpha = q.life / q.max;
        if (q.glow) g.globalCompositeOperation = 'lighter';
        g.fillStyle = q.c; g.fillRect(q.x - q.s / 2, q.y - q.s / 2, q.s, q.s);
        g.globalCompositeOperation = 'source-over';
      }
      g.globalAlpha = 1;

      if (S.lines) { // release: radial speed lines, fade in 160 ms
        var lk = S.lines.t / 10, r0 = 14 + (1 - lk) * 22;
        g.globalCompositeOperation = 'lighter'; g.strokeStyle = 'rgba(220,240,255,' + (0.6 * lk).toFixed(3) + ')'; g.lineWidth = 1;
        g.beginPath();
        for (i = 0; i < S.lines.a.length; i++) {
          var la = S.lines.a[i], cxl = Math.cos(la), syl = Math.sin(la);
          g.moveTo(S.lines.x + cxl * r0, S.lines.y + syl * r0); g.lineTo(S.lines.x + cxl * (r0 + 14), S.lines.y + syl * (r0 + 14));
        }
        g.stroke(); g.globalCompositeOperation = 'source-over';
      }
      if (S.ring > 0) { // docking: a ring expanding from the station
        var re = 1 - S.ring / 50, ease = 1 - Math.pow(1 - re, 3);
        g.strokeStyle = COL.white; g.globalAlpha = 0.7 * (1 - re); g.lineWidth = 1.5;
        g.beginPath(); g.arc(STATION.x, STATION.y, 8 + ease * 110, 0, TAU); g.stroke(); g.globalAlpha = 1;
      }

      drawProbe();

      for (i = 0; i < S.texts.length; i++) {
        var t = S.texts[i];
        if (!t.t) continue;
        txt(t.t, t.x, t.y, { size: t.size, w: 600, c: t.c, a: Math.min(1, t.life / 20), ls: 2 });
      }
    }

    function drawStation() {
      var x = STATION.x, y = STATION.y;
      if (!visible(y, 80)) return;
      g.save(); g.translate(x, y); g.rotate(-0.35);
      // Solar panels (dark blue, gridded)
      for (var s = -1; s <= 1; s += 2) {
        g.fillStyle = '#1b2b45'; g.fillRect(s > 0 ? 9 : -27, -5, 18, 10);
        g.strokeStyle = 'rgba(160,190,230,0.35)'; g.lineWidth = 0.6;
        g.strokeRect(s > 0 ? 9 : -27, -5, 18, 10);
        g.beginPath(); for (var k = 1; k < 3; k++) { g.moveTo((s > 0 ? 9 : -27) + k * 6, -5); g.lineTo((s > 0 ? 9 : -27) + k * 6, 5); } g.stroke();
      }
      g.strokeStyle = '#8a8f98'; g.lineWidth = 1; g.beginPath(); g.moveTo(-9, 0); g.lineTo(9, 0); g.stroke();
      var mg = g.createLinearGradient(-5, -7, 5, 7);
      mg.addColorStop(0, '#e6e7ea'); mg.addColorStop(0.6, '#7b7f87'); mg.addColorStop(1, '#2c2f35');
      g.fillStyle = mg; g.fillRect(-5, -8, 10, 16);
      g.strokeStyle = 'rgba(220,225,235,0.7)'; g.beginPath(); g.arc(0, -11, 3, 0, TAU); g.stroke();
      if (Math.floor(S.frame / 30) % 2 === 0) { g.fillStyle = COL.amber; g.fillRect(-1, 7, 2, 2); }
      g.restore();
      txt('MARS BASE', x - 30, y + STATION_R + 14, { size: 10, a: 0.6, ls: 1.5, c: COL.amber });
    }

    function drawProbe() {
      var p = S.p;
      if (S.mode === 'dead') return;
      // Trail
      // Chain colour: 0 faint cyan, 1-2 white, >=3 gold. The player reads their rhythm from the trail.
      var tc = S.chain >= 3 ? '255,206,110' : S.chain >= 1 ? '255,255,255' : '170,222,255';
      g.lineWidth = 1.5 + Math.min(S.chain, 4) * 0.35;
      for (var i = 1; i < S.trail.length; i++) {
        var a = S.trail[i - 1], b = S.trail[i];
        g.strokeStyle = 'rgba(' + tc + ',' + (i / S.trail.length * (S.chain ? 0.6 : 0.45)).toFixed(3) + ')';
        g.beginPath(); g.moveTo(a.x, a.y); g.lineTo(b.x, b.y); g.stroke();
      }
      // Tangent guide while locked (short, dim): shows where releasing will go.
      if (S.lock) {
        var t = tangent(S.lock);
        g.strokeStyle = 'rgba(255,255,255,0.22)'; g.setLineDash([2, 4]);
        g.beginPath(); g.moveTo(p.x, p.y); g.lineTo(p.x + t.x * 46, p.y + t.y * 46); g.stroke(); g.setLineDash([]);
        if (brakeMul(S.lock) < 0.999) txt('BRAKE', p.x + 10, p.y + 18, { size: 11, w: 600, c: COL.amber, ls: 2, a: 0.9 });
      }
      if (S.invuln > 0 && Math.floor(S.frame / 4) % 2 === 0) return;
      g.globalCompositeOperation = 'lighter';
      if (S.flame > 0) {
        var l = hyp(p.vx, p.vy) || 1, dx = -p.vx / l, dy = -p.vy / l, len = 8 + S.flame * 1.6;
        var fg = g.createRadialGradient(p.x + dx * len * 0.4, p.y + dy * len * 0.4, 0, p.x + dx * len * 0.4, p.y + dy * len * 0.4, len);
        fg.addColorStop(0, 'rgba(255,220,150,0.9)'); fg.addColorStop(0.4, 'rgba(255,140,60,0.45)'); fg.addColorStop(1, 'rgba(255,90,30,0)');
        g.fillStyle = fg; g.beginPath(); g.arc(p.x + dx * len * 0.4, p.y + dy * len * 0.4, len, 0, TAU); g.fill();
      }
      var pg = g.createRadialGradient(p.x, p.y, 0, p.x, p.y, 16);
      pg.addColorStop(0, 'rgba(255,255,255,0.9)'); pg.addColorStop(0.25, 'rgba(180,225,255,0.3)'); pg.addColorStop(1, 'rgba(127,216,255,0)');
      g.fillStyle = pg; g.beginPath(); g.arc(p.x, p.y, 16, 0, TAU); g.fill();
      g.globalCompositeOperation = 'source-over';
      g.fillStyle = COL.white; g.beginPath(); g.arc(p.x, p.y, 2.6, 0, TAU); g.fill();
    }

    // On value change 1.0->1.15->1.0 (150 ms). The caller closes it with g.restore().
    function popAt(k, cx, cy) {
      g.save();
      var t = S.pop[k];
      if (t > 0 && !reduced) { var s = 1 + 0.15 * Math.sin(Math.PI * (9 - t) / 9); g.translate(cx, cy); g.scale(s, s); g.translate(-cx, -cy); }
    }
    function drawSpeaker() {
      var x = SPK.x + 8, y = SPK.y + 16;
      g.globalAlpha = 0.6; g.fillStyle = COL.cyan; g.strokeStyle = COL.cyan; g.lineWidth = 1.2;
      g.beginPath(); g.moveTo(x, y - 3); g.lineTo(x + 3, y - 3); g.lineTo(x + 7, y - 7); g.lineTo(x + 7, y + 7); g.lineTo(x + 3, y + 3); g.lineTo(x, y + 3); g.closePath(); g.fill();
      g.beginPath();
      if (muted) { g.moveTo(x + 10, y - 4); g.lineTo(x + 17, y + 4); g.moveTo(x + 17, y - 4); g.lineTo(x + 10, y + 4); }
      else { g.arc(x + 8, y, 5, -0.8, 0.8); g.moveTo(x + 8 + Math.cos(-0.8) * 9, y + Math.sin(-0.8) * 9); g.arc(x + 8, y, 9, -0.8, 0.8); }
      g.stroke(); g.globalAlpha = 1;
    }
    // Lens flare: near Venus, a horizontal streak from the left edge where the sun leaks in toward
    // the screen centre + two ghost rings. Like a real lens reflection it passes through the centre.
    function drawFlare() {
      if (S.flare <= 0) return;
      var sx = 0, sy = clamp(1630 - S.camY, 0, H), cx = W / 2, cy = H / 2, f = S.flare;
      g.globalCompositeOperation = 'lighter';
      var st = g.createLinearGradient(sx, 0, sx + 170, 0);
      st.addColorStop(0, 'rgba(255,236,200,' + (0.4 * f).toFixed(3) + ')'); st.addColorStop(1, 'rgba(255,236,200,0)');
      g.fillStyle = st; g.fillRect(sx, sy - 1, 170, 2);
      [[0.62, 16, 0.07], [1.4, 30, 0.045]].forEach(function (c) {
        var x = sx + (cx - sx) * c[0], y = sy + (cy - sy) * c[0];
        g.fillStyle = 'rgba(255,214,170,' + (c[2] * f).toFixed(3) + ')';
        g.beginPath(); g.arc(x, y, c[1], 0, TAU); g.fill();
      });
      g.globalCompositeOperation = 'source-over';
    }

    function drawHud() {
      // Top left
      txt('MISSION 3 · MARS', 18, 32, { size: 12, w: 600, ls: 2.5, a: 0.95 });
      g.fillStyle = COL.cyan; g.globalAlpha = 0.3; g.fillRect(18, 40, 128, 1); g.globalAlpha = 1;
      // Top right: shields [■][■][■]
      for (var i = 0; i < SHIELD_MAX; i++) {
        var x = W - 18 - (SHIELD_MAX - i) * 24, y = 20, on = i < S.shields;
        var fl = S.shieldFlash > 0 && (reduced || Math.floor(S.shieldFlash / 4) % 2 === 0); // steady amber under reduced motion
        var c = fl ? COL.amber : on ? (S.shields === 1 ? COL.amber : COL.cyan) : COL.cyan;
        g.strokeStyle = c; g.globalAlpha = on ? 0.9 : 0.3; g.lineWidth = 1; g.beginPath();
        g.moveTo(x + 4, y); g.lineTo(x, y); g.lineTo(x, y + 14); g.lineTo(x + 4, y + 14);
        g.moveTo(x + 14, y); g.lineTo(x + 18, y); g.lineTo(x + 18, y + 14); g.lineTo(x + 14, y + 14); g.stroke();
        if (on) { g.fillStyle = c; g.fillRect(x + 5, y + 3, 8, 8); }
        g.globalAlpha = 1;
      }

      // Bottom panel
      var px = 14, ph = 78, py = H - ph - 14, pw = W - 28, cw = pw / 4;
      g.fillStyle = 'rgba(6,12,22,0.62)'; g.fillRect(px, py, pw, ph);
      g.strokeStyle = COL.cyan; g.globalAlpha = 0.22; g.lineWidth = 1; g.strokeRect(px + 0.5, py + 0.5, pw - 1, ph - 1);
      for (i = 1; i < 4; i++) { g.globalAlpha = 0.14; g.fillStyle = COL.cyan; g.fillRect(px + cw * i, py + 12, 1, ph - 24); }
      g.globalAlpha = 1;
      brackets(px - 3, py - 3, pw + 6, ph + 6, 9, COL.cyan, 0.85);
      var ly = py + 22, vy = py + 56;

      var sp = hyp(S.p.vx, S.p.vy);
      txt('SPEED', px + 12, ly, { size: 10, a: 0.6, ls: 2 });
      // White once below the docking threshold: the "you can come in now" signal, without extra text.
      popAt('speed', px + 26, vy - 7);
      txt(sp.toFixed(1), px + 12, vy, { size: 20, w: 600, c: sp < SAFE_SPEED ? COL.white : COL.cyan });
      g.restore();

      var fx = px + cw + 12, blink = S.fuel <= 0 && Math.floor(S.frame / (S.fuelBlink > 0 ? 5 : 15)) % 2 === 0;
      var fc = S.fuel <= 0 ? COL.amber : COL.cyan;
      txt('FUEL', fx, ly, { size: 10, a: S.fuel <= 0 ? (blink ? 1 : 0.35) : 0.6, ls: 2, c: fc });
      popAt('fuel', fx + 30, vy - 7);
      for (i = 0; i < FUEL_MAX; i++) {
        g.globalAlpha = i < S.fuel ? 0.9 : (S.fuel <= 0 ? (blink ? 0.7 : 0.25) : 0.22);
        g.fillStyle = i < S.fuel ? COL.cyan : fc;
        if (i < S.fuel) g.fillRect(fx + i * 13, vy - 15, 9, 16); else { g.strokeStyle = fc; g.strokeRect(fx + i * 13 + 0.5, vy - 14.5, 8, 15); }
      }
      g.globalAlpha = 1;
      g.restore();

      if (S.heat > 0) {
        var hx = px + cw * 2 + 12, hot = S.heat > 60;
        txt('HEAT', hx, ly, { size: 10, a: 0.8, ls: 2, c: hot ? COL.amber : COL.cyan });
        g.strokeStyle = hot ? COL.amber : COL.cyan; g.globalAlpha = 0.6; g.strokeRect(hx + 0.5, vy - 9.5, cw - 24, 8);
        g.globalAlpha = 1; g.fillStyle = S.heat > 60 ? COL.amber : COL.cyan;
        g.fillRect(hx + 2, vy - 8, (cw - 27) * S.heat / 100, 5);
      }

      var ox = px + cw * 3 + 12;
      txt('SAMPLE', ox, ly, { size: 10, a: 0.6, ls: 2 });
      popAt('sample', ox + 18, vy - 7);
      txt(S.samples + '/3', ox, vy, { size: 20, w: 600, c: S.samples >= 3 ? COL.white : COL.cyan });
      g.restore();

      // Hint: first 3 seconds
      if (S.runT < 180 && S.mode === 'play') {
        var a = S.runT < 150 ? 0.85 : 0.85 * (180 - S.runT) / 30;
        txt('Hold · release · tap while flying = fuel', W / 2, py - 18, { size: 12, align: 'center', a: a, c: '#d8f1ff' });
      }
    }

    // Edge arrow: shows the direction of the next target when it is off-frame. Drawn where the ray
    // from the probe to the target meets the inner rectangle that keeps clear of the HUD.
    function drawEdgeArrow() {
      if (S.mode !== 'play') return;
      var from = S.lock ? S.lock.body : S.lastBody || bodies[0];
      var tg = nextTarget(from);
      if (!tg) return;
      var tx = tg.x, ty = tg.y - S.camY, m = tg.r || STATION_R;
      if (tx > -m && tx < W + m && ty > -m && ty < H + m) return; // on screen: hide
      var x0 = 18, x1 = W - 18, y0 = 58, y1 = H - 122;
      var px = clamp(S.p.x, x0, x1), py = clamp(S.p.y - S.camY, y0, y1);
      var dx = tx - px, dy = ty - py, t = Infinity;
      if (dx > 0) t = Math.min(t, (x1 - px) / dx); else if (dx < 0) t = Math.min(t, (x0 - px) / dx);
      if (dy > 0) t = Math.min(t, (y1 - py) / dy); else if (dy < 0) t = Math.min(t, (y0 - py) / dy);
      if (!isFinite(t)) return;
      var ax = px + dx * t, ay = py + dy * t, ang = Math.atan2(dy, dx);
      var a = reduced ? 0.85 : 0.6 + 0.3 * Math.sin(S.frame * 0.12);
      g.save(); g.translate(ax, ay); g.rotate(ang);
      g.globalAlpha = a; g.fillStyle = COL.cyan;
      g.beginPath(); g.moveTo(5, 0); g.lineTo(-5, -5); g.lineTo(-5, 5); g.closePath(); g.fill();
      g.restore();
      // The label sits on the inner side of the arrow; its alignment depends on position so it does not overflow the edge.
      var lx = ax - Math.cos(ang) * 14, ly = ay - Math.sin(ang) * 14 + 3;
      var al = ax > W - 80 ? 'right' : ax < 80 ? 'left' : 'center';
      if (Math.abs(Math.sin(ang)) > 0.7) ly = ay - Math.sin(ang) * 16 + 3;
      txt(tg.name, lx, ly, { size: 10, align: al, a: a, ls: 1.5 });
      g.globalAlpha = 1;
    }

    function star(cx, cy, r, filled) {
      g.beginPath();
      for (var i = 0; i < 10; i++) {
        var a = -Math.PI / 2 + i * Math.PI / 5, rr = i % 2 ? r * 0.45 : r;
        if (i === 0) g.moveTo(cx + Math.cos(a) * rr, cy + Math.sin(a) * rr); else g.lineTo(cx + Math.cos(a) * rr, cy + Math.sin(a) * rr);
      }
      g.closePath();
      if (filled) { g.fillStyle = COL.amber; g.fill(); }
      g.strokeStyle = filled ? COL.amber : 'rgba(127,216,255,0.45)'; g.lineWidth = 1.2; g.stroke();
    }

    function drawOverlay() {
      if (S.mode === 'play') return;
      var delay = S.mode === 'done' ? 50 : 20;
      if (S.overlayT < delay) return;
      var k = Math.min(1, (S.overlayT - delay) / (reduced ? 1 : 18));
      g.globalAlpha = 0.72 * k; g.fillStyle = '#03050a'; g.fillRect(0, 0, W, H); g.globalAlpha = 1;
      var pw = 300, ph = S.mode === 'done' ? 270 : 180, px = (W - pw) / 2, py = H * 0.42 - ph / 2;
      g.globalAlpha = k; g.fillStyle = 'rgba(8,14,26,0.9)'; g.fillRect(px, py, pw, ph); g.globalAlpha = 1;
      brackets(px, py, pw, ph, 12, S.mode === 'done' ? COL.cyan : COL.amber, k);
      if (S.mode === 'done') {
        txt('MISSION COMPLETE', W / 2, py + 48, { size: 22, w: 600, align: 'center', ls: 4, a: k });
        var labels = ['DOCK', 'SAMPLE 3/3', 'FUEL ≥ 2'];
        for (var i = 0; i < 3; i++) {
          var cx = W / 2 + (i - 1) * 84;
          if (i < S.starsShown) {
            var age = S.overlayT - (68 + i * 15), ss = 1;
            if (!reduced && age < 7) ss = 0.6 + 0.4 * (age / 7) + 0.25 * Math.sin(Math.PI * age / 7); // small overshoot, then settle
            g.save(); g.translate(cx, py + 108); g.scale(ss, ss);
            g.globalAlpha = k; star(0, 0, 20, S.stars[i]); g.restore(); g.globalAlpha = 1;
          } else { g.globalAlpha = 0.25 * k; star(cx, py + 108, 20, false); g.globalAlpha = 1; } // placeholder
          txt(labels[i], cx, py + 148, { size: 10, align: 'center', a: 0.7 * k, ls: 1 });
        }
      } else {
        txt('SIGNAL LOST', W / 2, py + 58, { size: 22, w: 600, align: 'center', ls: 4, a: k, c: COL.amber });
        if (Math.floor(S.overlayT / 20) % 2 === 0 || reduced) txt('— — —', W / 2, py + 88, { size: 12, align: 'center', a: 0.5 * k, c: COL.amber });
      }
      var bw = 150, bh = 42, bx = (W - bw) / 2, by = py + ph - bh - 22;
      g.strokeStyle = COL.cyan; g.globalAlpha = k * (S.overlayT > 40 ? 0.9 : 0.35); g.lineWidth = 1; g.strokeRect(bx + 0.5, by + 0.5, bw - 1, bh - 1); g.globalAlpha = 1;
      txt('Retry', W / 2, by + 27, { size: 14, w: 600, align: 'center', a: k * (S.overlayT > 40 ? 1 : 0.4), ls: 2 });
    }

    function render() {
      g.setTransform(1, 0, 0, 1, 0, 0);
      g.globalAlpha = 1; g.globalCompositeOperation = 'source-over';
      drawBackground();
      g.save();
      if (S.shake > 0 && !reduced) g.translate((Math.random() - 0.5) * S.shake, (Math.random() - 0.5) * S.shake);
      var s = 1;
      if (!reduced) {
        if (S.punch > 0) s *= 1 + 0.04 * (S.punch / 7); // PERFECT: inward punch
        if (S.pull > 0) { // release: 4% outward over 3 frames, soft return over 11 frames
          var el = 14 - S.pull, env = el < 3 ? (el + 1) / 3 : Math.pow(1 - (el - 3) / 11, 3);
          s *= 1 - 0.04 * env;
        }
      }
      if (s !== 1) { // scale around the probe's screen position: the player's eye stays put
        var sx = S.p.x, sy = S.p.y - S.camY;
        g.translate(sx, sy); g.scale(s, s); g.translate(-sx, -sy);
      }
      g.translate(0, -S.camY);
      drawWorld();
      g.restore();
      if (S.flash > 0) { g.fillStyle = COL.white; g.globalAlpha = 0.12 * S.flash / 18; g.fillRect(0, 0, W, H); g.globalAlpha = 1; }
      g.drawImage(vignette, 0, 0);
      drawFlare();
      drawEdgeArrow();
      drawHud();
      drawOverlay();
      drawSpeaker();
    }

    // ---------------- Loop ----------------
    var last = null, acc = 0, running = true;
    function loop(ts) {
      if (!running) return;
      if (last == null) last = ts;
      acc += Math.min(100, ts - last); last = ts; // no physics burst when returning to the tab
      while (acc >= STEP) {
        if (freezeUntil) { // hit-stop: accumulated time is dropped so there is no "catch-up" jump after the freeze
          var now = performance.now();
          if (now < freezeUntil) { acc = 0; break; }
          lastFreezeMs = now - freezeStart; freezeUntil = 0;
        }
        update(); acc -= STEP;
      }
      render();
      requestAnimationFrame(loop);
    }
    restart();
    render();
    requestAnimationFrame(loop);

    var api = {
      restart: restart,
      stop: function () { running = false; },
      state: function () {
        return {
          mode: S.mode, locked: !!S.lock, lockedId: S.lock ? S.lock.body.id : null, tentative: S.lock ? S.lock.tentative : false,
          fuel: S.fuel, shields: S.shields, samples: S.samples, heat: S.heat, chain: S.chain,
          speed: hyp(S.p.vx, S.p.vy), x: S.p.x, y: S.p.y, camY: S.camY, stars: S.stars.slice(), reduced: reduced,
          brake: S.lock ? brakeMul(S.lock) : 1,
          frame: S.frame, muted: muted, audio: ac ? ac.state : 'none', frozen: !!freezeUntil, lastFreezeMs: lastFreezeMs,
          starsShown: S.starsShown, speaker: { x: SPK.x, y: SPK.y, w: SPK.w, h: SPK.h }, W: W, H: H
        };
      },
      // Only for testing/tuning: place the probe / lock onto a body.
      debug: {
        setProbe: function (x, y, vx, vy) { S.lock = null; S.trail = []; S.p.x = x; S.p.y = y; S.p.vx = vx; S.p.vy = vy; S.camY = clamp(y - H * 0.55, 0, WORLD_H - H); },
        lock: function (id) { var b = bodyById(id); if (!b || b === STATION) return; S.trail = []; S.p.x = b.x; S.p.y = b.y - b.r - ORBIT_GAP; S.p.vx = 1; S.p.vy = 0; lockOn(b, false); S.camY = clamp(S.p.y - H * 0.55, 0, WORLD_H - H); },
        aimPerfect: function () { // while locked, turn the tangent toward the target and release (for the PERFECT test)
          if (!S.lock) return;
          var L = S.lock, tg = nextTarget(L.body); if (!tg) return;
          var best = 0, bestC = -2;
          for (var i = 0; i < 720; i++) {
            var a = i / 720 * TAU, px = L.body.x + Math.cos(a) * L.targetR, py = L.body.y + Math.sin(a) * L.targetR;
            var tx = -Math.sin(a) * L.dir, ty = Math.cos(a) * L.dir, dx = tg.x - px, dy = tg.y - py, c = (tx * dx + ty * dy) / hyp(dx, dy);
            if (c > bestC) { bestC = c; best = a; }
          }
          L.angle = best; L.radius = L.targetR;
          S.p.x = L.body.x + Math.cos(best) * L.targetR; S.p.y = L.body.y + Math.sin(best) * L.targetR;
          release(true);
        }
      }
    };
    cv.__orbit = api;
    return api;
  };
})();
