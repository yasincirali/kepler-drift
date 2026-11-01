// Procedural sound (the Unity counterpart of the prototype's WebAudio graph): no files, every sound is oscillator + noise + envelope.
// Why not files: no licensing question, small package, the chain tone (pentatonic) is computed on every chain.
// Why OnAudioFilterRead: creating a clip per event with AudioClip.Create caused GC and latency on the main thread
// (Birdie Smash Sfx.Swoosh pattern); here there is one source and sounds are mixed on the audio thread.
// This file is a SKELETON: event -> sound mapping and basic synthesis are in; the prototype's filters (bandpass/lowpass/
// highpass biquad), waveshaper and full envelopes are TODO. The prototype's 11 event sounds: clunk, whoosh, perfect, burn,
// dry, tick, heat, hit, sample, brake, dock; plus the state sounds ping (star), lost (signal lost) and reset (hum returns).
using System;
using System.Collections.Generic;
using UnityEngine;
using KeplerDrift.Core;

namespace KeplerDrift.Game
{
    public sealed class Sfx : MonoBehaviour
    {
        // C5 pentatonic ladder (523…1568 Hz): the tone rises as the chain grows; because it is pentatonic the chord always
        // sounds "right" whatever the chain.
        static readonly double[] PentaScale = { 523.25, 587.33, 659.25, 783.99, 880 };
        static double Penta(int i) { return PentaScale[i % 5] * Math.Pow(2, i / 5); }

        const float Master = 0.6f;   // prototype MASTER
        public bool Muted;

        enum Wave { Sine, Triangle, Noise, Saw }
        struct Voice { public Wave w; public double f0, f1, dur, gain, attack, t, phase; public float lp; }

        int sr; readonly List<Voice> pending = new List<Voice>(), playing = new List<Voice>(); readonly object gate = new object();
        readonly System.Random rng = new System.Random(5);
        volatile float humTarget, humFTarget = 110; float hum, humF = 110; double humPhase; volatile float crackleTarget = 0.015f; float crackle;
        AudioSource source;

        void Awake()
        {
            sr = AudioSettings.outputSampleRate;
            source = gameObject.AddComponent<AudioSource>(); source.playOnAwake = false; source.loop = true;
            source.clip = AudioClip.Create("silence", sr, 1, sr, false); source.Play();
        }

        // Called on the first touch. On iOS the audio session belongs to Unity (Ambient: the player's music keeps playing,
        // CiBuild); here we only make sure the source is playing.
        public void Wake() { if (source != null && !source.isPlaying) source.Play(); }

        // reset: the crackle cut on SIGNAL LOST comes back on restart. Not named Reset: Unity calls MonoBehaviour.Reset in the Editor.
        public void ResetHum() { crackleTarget = 0.015f; humTarget = 0; }

        void Add(Wave w, double f, double dur, double gain, double delay = 0, double fEnd = 0, double attack = 0.005)
        {
            lock (gate) pending.Add(new Voice { w = w, f0 = f, f1 = fEnd > 0 ? fEnd : f, dur = dur, gain = gain, attack = attack, t = -delay });
        }

        public void Play(EventRecord ev)
        {
            if (Muted) return;
            switch (ev.Kind)
            {
                case GameEvent.Lock:      // clunk: short low noise + 90->55 Hz sine
                    Add(Wave.Noise, 400, 0.06, 0.35); Add(Wave.Sine, 90, 0.15, 0.4, 0, 55); break;   // TODO lowpass 400
                case GameEvent.Release:   // whoosh: bandpass 400->2400 Hz sweep, gain peaks in the middle (a passing doppler)
                    Add(Wave.Noise, 400, 0.35, 0.14, 0, 2400, 0.17); break;   // TODO bandpass q 1.4
                case GameEvent.Perfect:
                {
                    int r = Math.Min(ev.Value - 1, 8);   // root stays within 523…1568 Hz
                    int[] steps = { r, r + 2, r + 3 };   // root, ~+4 and ~+7 semitones
                    for (int i = 0; i < 3; i++) { Add(Wave.Triangle, Penta(steps[i]), 0.32, 0.11, i * 0.04); Add(Wave.Sine, Penta(steps[i]) * 2, 0.22, 0.035, i * 0.04); }
                    Add(Wave.Noise, 6000, 0.12, 0.06);   // TODO highpass 6000 (sparkle)
                    if (ev.Value >= 3) Add(Wave.Sine, Penta(r) / 2, 0.3, 0.12, 0, 0, 0.01);
                    break;
                }
                case GameEvent.Burn:      // burn
                    Add(Wave.Noise, 1200, 0.18, 0.3, 0, 300); Add(Wave.Sine, 70, 0.2, 0.35, 0, 45); break;   // TODO lowpass 1200->300
                case GameEvent.DryTick:   // dry: empty tank, no thump
                    Add(Wave.Sine, 1000, 0.003, 0.15, 0, 0, 0.0008); break;
                case GameEvent.HeatTick:  // tick: geiger
                    Add(Wave.Sine, 1500, 0.025, 0.05, 0, 0, 0.002); break;
                case GameEvent.HeatBurn:  // heat: 50 Hz shaped thump + hiss
                    Add(Wave.Sine, 50, 0.4, 0.5, 0, 0, 0.01); Add(Wave.Noise, 2500, 0.3, 0.12); break;   // TODO waveshaper, highpass 2500
                case GameEvent.Hit:       // hit
                    Add(Wave.Sine, 60, 0.25, 0.5, 0, 38); Add(Wave.Noise, 1800, 0.08, 0.2, 0, 600); break;   // TODO bandpass q .7
                case GameEvent.Sample:    // sample: two notes
                    Add(Wave.Sine, 880, 0.06, 0.12); Add(Wave.Sine, 1320, 0.06, 0.12, 0.065); break;
                case GameEvent.Brake:     // brake: light friction
                    Add(Wave.Noise, 500, 0.09, 0.16, 0, 0, 0.02); break;   // TODO lowpass 500
                case GameEvent.Dock:      // dock: two sines + major arpeggio
                    Add(Wave.Sine, 660, 0.4, 0.12); Add(Wave.Sine, 990, 0.4, 0.08);
                    for (int i = 0; i < 3; i++) Add(Wave.Triangle, new[] { 523.25, 659.25, 783.99 }[i], 0.5, 0.11, 0.42 + i * 0.12, 0, 0.01);
                    // TODO ping: 1320 Hz on each earned star when the star panel opens (on the same beat as the Hud star order)
                    break;
                case GameEvent.SignalLost:   // lost: hum cuts hard (a disconnection feel), 80->45 Hz saw "power loss"
                    humTarget = 0; hum = 0; crackleTarget = 0;
                    Add(Wave.Saw, 80, 0.65, 0.15, 0, 45); break;   // TODO lowpass 2000->100
            }
        }

        // The hum is derived from state (not events): whichever path lock/release/undo/death comes from, it is right in one place.
        public void Sync(OrbitEngine e)
        {
            var p = e.Probe; bool on = !Muted && e.Mode == Mode.Play && p.Locked && (!p.Tentative || p.Confirmed);
            humTarget = on ? 0.08f : 0;
            if (on) humFTarget = 110 + (Mathf.Clamp((float)p.Speed, 1, 6) - 1) / 5f * 150;
        }

        void OnAudioFilterRead(float[] data, int ch)
        {
            lock (gate) { playing.AddRange(pending); pending.Clear(); }
            double dt = 1.0 / sr;
            float kh = 1 - Mathf.Exp(-1f / (sr * (humTarget > hum ? 0.04f : 0.066f))), kf = 1 - Mathf.Exp(-1f / (sr * 0.08f)), kc = 1 - Mathf.Exp(-1f / (sr * 0.1f));
            for (int i = 0; i < data.Length; i += ch)
            {
                hum += (humTarget - hum) * kh; humF += (humFTarget - humF) * kf; crackle += (crackleTarget - crackle) * kc;
                humPhase += humF * dt; double x = Math.Sin(humPhase * 2 * Math.PI) * hum + (rng.NextDouble() * 2 - 1) * crackle * 0.3;
                for (int v = playing.Count - 1; v >= 0; v--)
                {
                    var s = playing[v]; s.t += dt;
                    if (s.t >= s.dur) { playing.RemoveAt(v); continue; }
                    if (s.t >= 0)
                    {
                        double u = s.t / s.dur, f = s.f0 * Math.Pow(s.f1 / s.f0, u);   // exponential glide (exponentialRamp)
                        double env = s.t < s.attack ? s.t / s.attack : Math.Exp(-5 * (s.t - s.attack) / s.dur);
                        s.phase += f * dt; double ph = s.phase - Math.Floor(s.phase), y;
                        switch (s.w)
                        {
                            case Wave.Sine: y = Math.Sin(ph * 2 * Math.PI); break;
                            case Wave.Triangle: y = 1 - 4 * Math.Abs(ph - 0.5); break;
                            case Wave.Saw: y = 2 * ph - 1; break;
                            default:
                                // One-pole lowpass: cutoff ≈ f (TODO: a real biquad, the prototype's filter types)
                                float a = (float)Math.Min(1, 2 * Math.PI * f * dt); s.lp += ((float)(rng.NextDouble() * 2 - 1) - s.lp) * a; y = s.lp; break;
                        }
                        x += y * env * s.gain;
                    }
                    playing[v] = s;
                }
                float o = Muted ? 0 : (float)Math.Max(-1, Math.Min(1, x * Master));
                for (int c = 0; c < ch; c++) data[i + c] = o;
            }
        }
    }
}
