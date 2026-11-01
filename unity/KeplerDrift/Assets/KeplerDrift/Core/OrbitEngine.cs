// Orbit rule engine: an exact counterpart of the prototype's (orbit-proto.js) update/stepProbe/collide/release/burn logic.
// Frame based and deterministic: same Mission + same Hold/Release/Burn sequence (on the same frames) = same result. Real
// time (ms) never enters the engine; the 180 ms tap threshold is counted here in frames (11 frames), the ms measurement in
// GameRoot only decides "was this a tap". Presentation (sound, vibration, hit-stop, particles) reads the event queue.
using System;
using System.Collections.Generic;

namespace KeplerDrift.Core
{
    public enum Mode { Play, Lost, Done }

    public sealed class OrbitEngine
    {
        // ---- Tuning (game design spec; same as the prototype) ----
        public const double LockRange = 150;      // you can lock onto a ring from at most this far away (px)
        // Speed = 0.055 x ring radius -> angular speed is constant regardless of radius: one lap takes the same time on every
        // body (~1.9 s), so the player learns the rhythm from timing, not from body size.
        public const double Omega = 0.055;
        public const double PerfectDegrees = 8;
        public const int TapFrames = 11;           // 180 ms @ 60 Hz
        public const double BurnDv = 2.2;
        public const double BrakeRatio = 0.68, BrakeFloor = 0.25;
        public const double HeatRate = 1, HeatCool = 0.6, HeatAfter = 70;
        public const int InvulnFrames = 70;
        public const int RecaptureFrames = 30;

        public readonly Mission Mission;
        public readonly ProbeShip Probe = new ProbeShip();
        public Mode Mode { get; private set; }
        public int Frame { get; private set; }
        public int Fuel, Shield, SamplesTaken, Chain, Invuln;
        public double Heat;         // 0-100 (%)
        public double CameraY;      // top edge of the view (world px)
        public bool Docked { get; private set; }
        public bool ReduceMotion;   // affects presentation only; rule numbers do not change (reduced motion must not be a handicap)

        readonly List<EventRecord> events = new List<EventRecord>();
        bool[] stars = new bool[3];
        bool held, consumed, feltLock;
        int holdFrame;
        Body lastBody; int lastBodyFrame = -9999;
        double heatTickMs;

        public OrbitEngine(Mission mission)
        {
            Mission = mission;
            Mode = Mode.Play; Fuel = Mission.FuelMax; Shield = Mission.ShieldMax;
            CameraY = mission.WorldH - mission.View;
            var start = mission.Find(mission.StartId);
            Probe.X = start.X; Probe.Y = start.Y - start.RingR;
            SetLock(start, false);   // start locked to the first body: the first decision is "when to let go", not "how to hold"
            Probe.Radius = Probe.TargetR; Probe.Dir = 1;
            feltLock = true;   // no clunk on launch (the player has not done anything yet)
        }

        // ---------------- Input ----------------

        // Finger down. In free flight it tries to lock onto the nearest ring right away; while held it retries every frame
        // (entering a ring while the finger is down also counts as a lock).
        public bool Hold()
        {
            if (Mode != Mode.Play) return false;
            held = true; consumed = false; holdFrame = Frame;
            if (!Probe.Locked) TryLock();
            return Probe.Locked;
        }

        // Finger up. tap = a short touch measured by GameRoot (< 180 ms, < 8 px).
        //   locked + tentative + tap -> lock is undone, the touch becomes fuel (so tapping mid-flight does not lock by accident)
        //   locked                   -> tangent release, perfect window is measured
        //   free + tap               -> burn
        public void Release(bool tap = false)
        {
            if (!held && !Probe.Locked && !tap) return;
            held = false;
            if (Mode != Mode.Play) return;
            if (Probe.Locked)
            {
                if (Probe.Tentative && tap) { UndoLock(); Burn(); }
                else Free(true);
            }
            else if (tap) Burn();
        }

        // +2.2 px/frame along the velocity. Why along the velocity (not toward the finger): one finger = one action; direction
        // is chosen by the release angle, a burn is only the "I can't make it" correction. An empty tank gives a dry tick, no thump.
        public void Burn()
        {
            if (Mode != Mode.Play) return;
            if (Fuel <= 0) { Emit(GameEvent.DryTick); return; }
            Fuel--;
            var p = Probe; double l = p.Speed;
            double dx = l > 0.01 ? p.Vx / l : 0, dy = l > 0.01 ? p.Vy / l : -1;
            p.Vx += dx * BurnDv; p.Vy += dy * BurnDv;
            Emit(GameEvent.Burn);
        }

        // ---------------- Frame ----------------

        public void Step()
        {
            Frame++;
            foreach (var b in Mission.Bodies) b.RailStep(1);
            foreach (var r in Mission.Rocks)
            {
                r.X += r.Vx; r.Rot += r.Spin;
                if (r.X > 480) r.X -= 540; else if (r.X < -60) r.X += 540;   // 60 px margin on both sides: rocks don't "spawn", they flow
            }
            if (Mode == Mode.Play) StepProbe();
            if (Invuln > 0) Invuln--;
            StepFeel();
            StepCamera();
        }

        void StepProbe()
        {
            var p = Probe;
            if (p.Locked)
            {
                var b = p.Lock;
                p.LockFrame++;
                p.Radius += (p.TargetR - p.Radius) * 0.15;   // ease into the ring (no instant teleport)
                double bm = BrakeFactor(), w = Omega * bm;
                p.Angle += w * p.Dir; p.Total += w;
                p.X = b.X + Math.Cos(p.Angle) * p.Radius; p.Y = b.Y + Math.Sin(p.Angle) * p.Radius;
                TangentVelocity(out p.Vx, out p.Vy);
                // HEAT: Venus is close to the sun; 1% per frame while locked -> burns out in ~1.7 s. This forbids taking a
                // brake lap at Venus ("pass fast, don't linger"): on burn-out the probe is forcibly released (not perfect),
                // the finger is "consumed" so a player still holding does not re-lock onto Venus on the same frame and burn
                // a second shield; heat drops to 70, not zero: coming straight back burns out quickly again.
                if (b.Hot)
                {
                    Heat = Math.Min(100, Heat + HeatRate);
                    if (Heat >= 100) { Free(false); consumed = true; LoseShield(GameEvent.HeatBurn); Heat = HeatAfter; }
                }
            }
            else
            {
                p.X += p.Vx; p.Y += p.Vy;
                if (held && !consumed) TryLock();
                if (!p.Locked) Collide();
            }
            if (!p.Locked || !p.Lock.Hot) Heat = Math.Max(0, Heat - HeatCool);
            CollectSamples();
        }

        void StepFeel()
        {
            var p = Probe;
            // A tentative lock is confirmed once 180 ms pass: only then the clunk, otherwise every fuel tap would play a fake lock sound.
            if (p.Locked && p.Tentative && !p.Confirmed && held && Frame - holdFrame >= TapFrames) p.Confirmed = true;
            bool felt = Mode == Mode.Play && p.Locked && (!p.Tentative || p.Confirmed);
            if (felt && !feltLock) Emit(GameEvent.Lock);
            feltLock = felt;
            // Heat ticking: 400 ms interval at 50%, 90 ms at 100%.
            if (Heat > 50 && Mode == Mode.Play)
            {
                heatTickMs -= Const.FrameMs;
                if (heatTickMs <= 0) { Emit(GameEvent.HeatTick); heatTickMs = 400 - (Heat - 50) / 50 * 310; }
            }
            else heatTickMs = 0;
            if (p.Locked && Mode == Mode.Play && BrakeFactor() < 0.999 && Frame % 30 == 0) Emit(GameEvent.Brake);
        }

        // The camera follows upward fast and downward slowly (<= 2 px/frame): a probe escaping downward falls out of frame and
        // becomes SIGNAL LOST, so falling back feels like a mistake. It is in the engine because it is a rule (out of frame = lost).
        void StepCamera()
        {
            double target = Clamp(Probe.Y - Mission.View * 0.55, 0, Mission.WorldH - Mission.View);
            double dy = (target - CameraY) * 0.08;
            CameraY += dy > 0 ? Math.Min(dy, 2) : dy;
        }

        // ---------------- Lock ----------------

        Body NearestRing()
        {
            Body best = null; double bestD = LockRange;
            foreach (var b in Mission.Bodies)
            {
                if (b == lastBody && Frame - lastBodyFrame < RecaptureFrames) continue;   // don't immediately recapture the body just released
                double d = Math.Abs(Hyp(Probe.X - b.X, Probe.Y - b.Y) - b.RingR);
                if (d < bestD) { bestD = d; best = b; }
            }
            return best;
        }

        void TryLock()
        {
            var b = NearestRing();
            // Tentative while the hold time has not elapsed: if the finger lifts within 180 ms the lock is undone (Release).
            if (b != null) SetLock(b, Frame - holdFrame < TapFrames);
        }

        void SetLock(Body b, bool tentative)
        {
            var p = Probe;
            double rx = p.X - b.X, ry = p.Y - b.Y, vx = p.Vx - b.Vx, vy = p.Vy - b.Vy;
            // Spin direction from the cross product of the incoming velocity: the probe orbits in whichever direction it entered
            // the ring. With a fixed direction a probe arriving from the other side would swing 180° on lock (a "U-turn" in the prototype).
            double cross = rx * vy - ry * vx;
            p.Lock = b; p.Angle = Math.Atan2(ry, rx); p.Radius = Math.Max(b.R + 6, Hyp(rx, ry)); p.TargetR = b.RingR;
            p.Dir = cross >= 0 ? 1 : -1; p.Total = 0; p.LockFrame = 0; p.Tentative = tentative; p.Confirmed = false;
            p.SavedX = p.X; p.SavedY = p.Y; p.SavedVx = p.Vx; p.SavedVy = p.Vy;
        }

        void UndoLock()
        {
            var p = Probe; int n = p.LockFrame;
            p.X = p.SavedX + p.SavedVx * n; p.Y = p.SavedY + p.SavedVy * n; p.Vx = p.SavedVx; p.Vy = p.SavedVy;
            p.Lock = null;
        }

        // BRAKE: every extra lap after the first full lap cuts speed by 32%. Continuous, not stepped (0.68^extraLaps), so the
        // speed readout does not jump; floor 0.25 (the probe must not "stop" on the ring, releasing must go somewhere). The first
        // lap is free: taking half a lap while passing through is not punished, the brake only hits deliberate laps. At Mars
        // 4.73 -> < 2.6 in about 1.6 extra laps (with 0.85 it took ~4 laps, far too slow).
        public double BrakeFactor()
        {
            if (!Probe.Locked) return 1;
            double extra = Math.Max(0, Probe.Total / Const.Tau - 1);
            return Math.Max(BrakeFloor, Math.Pow(BrakeRatio, extra));
        }

        void TangentVelocity(out double vx, out double vy)
        {
            var p = Probe; var b = p.Lock;
            double sp = Omega * p.TargetR * BrakeFactor();
            vx = -Math.Sin(p.Angle) * p.Dir * sp + b.Vx;
            vy = Math.Cos(p.Angle) * p.Dir * sp + b.Vy;
        }

        void Free(bool checkPerfect)
        {
            var p = Probe; var b = p.Lock;
            TangentVelocity(out p.Vx, out p.Vy);
            p.Lock = null; lastBody = b; lastBodyFrame = Frame;
            Emit(GameEvent.Release);
            if (!checkPerfect) { Chain = 0; return; }
            double angle = TargetAngle(b);
            if (angle <= PerfectDegrees) { Chain++; Emit(GameEvent.Perfect, Chain); }
            else Chain = 0;
        }

        // PERFECT window: the angle between the release direction (the real velocity vector, including the moving body's velocity)
        // and the direction to the centre of the next target is within ±8°. The centre is used, not the ring: the ring would
        // also reward "good enough" releases; perfect must stay rare so the chain is worth something. For the moving Moon the
        // current position is used (no prediction): the player aims where they see it. No target (end of route) -> +inf.
        double TargetAngle(Body source)
        {
            double tx, ty;
            if (source == null || !Mission.NextTarget(source, out tx, out ty)) return double.PositiveInfinity;
            var p = Probe;
            double dx = tx - p.X, dy = ty - p.Y, dl = Hyp(dx, dy), vl = p.Speed;
            if (vl == 0) vl = 1;
            double cos = Clamp((p.Vx * dx + p.Vy * dy) / (vl * dl), -1, 1);
            return Math.Acos(cos) * 180 / Math.PI;
        }

        // How many degrees from the perfect window a release right now would be (+inf when not locked). For the aim indicator and the bot.
        public double PerfectAngle() { return Probe.Locked ? TargetAngle(Probe.Lock) : double.PositiveInfinity; }

        // ---------------- Collision ----------------

        void Collide()
        {
            var p = Probe; double dx, dy, d, nx, ny;
            foreach (var r in Mission.Rocks)
            {
                dx = p.X - r.X; dy = p.Y - r.Y; d = Hyp(dx, dy);
                if (d < r.HitR && Invuln <= 0)
                {
                    Bounce(dx / (d == 0 ? 1 : d), dy / (d == 0 ? 1 : d), 0.6); LoseShield(GameEvent.Hit);
                    if (Mode != Mode.Play) return;
                }
            }
            foreach (var b in Mission.Bodies)
            {
                dx = p.X - b.X; dy = p.Y - b.Y; d = Hyp(dx, dy);
                if (d < b.R + 3)
                {
                    nx = dx / (d == 0 ? 1 : d); ny = dy / (d == 0 ? 1 : d);
                    p.X = b.X + nx * (b.R + 4); p.Y = b.Y + ny * (b.R + 4);
                    Bounce(nx, ny, 0.5); LoseShield(GameEvent.Hit);
                    if (Mode != Mode.Play) return;
                }
            }
            var s = Mission.Station;
            dx = p.X - s.X; dy = p.Y - s.Y; d = Hyp(dx, dy);
            if (d < Station.CaptureR)
            {
                // Soft landing only at speed < 2.6: this is the only reason to teach the brake lap. A fast arrival bounces and
                // loses a shield; the bounce pushes out of the station so there is no second capture/landing on the same frame.
                if (p.Speed < Station.SafeSpeed) { Dock(); return; }
                nx = dx / (d == 0 ? 1 : d); ny = dy / (d == 0 ? 1 : d);
                p.X = s.X + nx * (Station.CaptureR + 1); p.Y = s.Y + ny * (Station.CaptureR + 1);
                Bounce(nx, ny, 0.55);
                if (Invuln <= 0) Emit(GameEvent.TooFast);
                LoseShield(GameEvent.Hit);
                if (Mode != Mode.Play) return;
            }
            // Out of frame = SIGNAL LOST. 20 px margin at the sides, 60 at the top; at the bottom the camera's lower edge (the camera descends slowly).
            if (p.X < -20 || p.X > Mission.Width + 20 || p.Y < -60 || p.Y > CameraY + Mission.View + 24) Lose();
        }

        void Bounce(double nx, double ny, double keep)
        {
            var p = Probe; double dot = p.Vx * nx + p.Vy * ny;
            if (dot < 0) { p.Vx -= 2 * dot * nx; p.Vy -= 2 * dot * ny; }
            p.Vx = p.Vx * keep + nx * 1.1; p.Vy = p.Vy * keep + ny * 1.1;   // +1.1 outward push: so it does not stick to the surface and lose a shield every frame
        }

        // 70 frames of invulnerability after a hit: a single mistake in the rock lane must not chain away all three shields.
        void LoseShield(GameEvent cause)
        {
            if (Invuln > 0 || Mode != Mode.Play) return;
            Shield--; Invuln = InvulnFrames; Chain = 0;
            Emit(cause);
            if (Shield <= 0) Lose();
        }

        void Lose()
        {
            if (Mode != Mode.Play) return;
            Mode = Mode.Lost; Probe.Lock = null;
            Emit(GameEvent.SignalLost);
        }

        void Dock()
        {
            Mode = Mode.Done; Docked = true;
            Probe.Vx = Probe.Vy = 0;
            stars = new[] { true, SamplesTaken >= Mission.SampleCount, Fuel >= 2 };
            Emit(GameEvent.Dock);
        }

        void CollectSamples()
        {
            foreach (var s in Mission.Samples)
            {
                if (s.Taken) continue;
                if (Hyp(Probe.X - s.X, Probe.Y - s.Y) < SamplePod.PickupR) { s.Taken = true; SamplesTaken++; Emit(GameEvent.Sample, SamplesTaken); }
            }
        }

        // Three stars: docked, 3/3 samples, fuel >= 2. The fuel star measures "did you solve it without burning"; tolerance 2
        // because the Moon's motion makes one correction almost mandatory on the first try. All false before docking.
        public bool[] Stars() { return (bool[])stars.Clone(); }

        // ---------------- Presentation helpers ----------------

        public List<EventRecord> TakeEvents() { var l = new List<EventRecord>(events); events.Clear(); return l; }
        void Emit(GameEvent e, int value = 0) { events.Add(new EventRecord(e, value)); }

        // Only for tests and the screenshot tour: place the probe / firmly lock onto a body (prototype debug.setProbe/lock).
        public void TestPlaceProbe(double x, double y, double vx, double vy)
        {
            Probe.Lock = null; Probe.X = x; Probe.Y = y; Probe.Vx = vx; Probe.Vy = vy;
            CameraY = Clamp(y - Mission.View * 0.55, 0, Mission.WorldH - Mission.View);
        }
        public void TestLock(string id)
        {
            var b = Mission.Find(id); if (b == null) return;
            Probe.X = b.X; Probe.Y = b.Y - b.RingR; Probe.Vx = 1; Probe.Vy = 0;
            SetLock(b, false);
            CameraY = Clamp(Probe.Y - Mission.View * 0.55, 0, Mission.WorldH - Mission.View);
        }

        static double Hyp(double x, double y) { return Math.Sqrt(x * x + y * y); }
        static double Clamp(double v, double a, double b) { return v < a ? a : v > b ? b : v; }
    }
}
