// Validation without Unity: compiles Core with mono and runs scripted missions (Birdie Smash BotMatch.cs pattern).
//   mcs -warnaserror+ -out:/tmp/missionbot.exe tool/unity_test/MissionBot.cs unity/KeplerDrift/Assets/KeplerDrift/Core/*.cs && mono /tmp/missionbot.exe
// Checks: start (locked to Earth, 3 shields, 5 fuel), perfect window (chain grows within ±8°, resets outside; also onto the
// moving Moon), Hold lock on the Moon ring and spin direction, tentative lock undone by a tap and turned into fuel, burn
// (+2.2 along velocity, fuel -1, empty tank dry tick), brake (< 2.6 at Mars before 3 extra laps), heat (burn-out at 100%,
// shield -1, forced release, heat 70, held finger consumed), station (fast = bounce + shield -1, slow = dock), stars,
// out of frame = signal lost, determinism (same scenario twice = same state), no NaN.
using System;
using System.Linq;
using KeplerDrift.Core;

static class MissionBot
{
    static int failed = 0, passed = 0;
    static void Check(bool condition, string name)
    {
        if (condition) { passed++; Console.WriteLine("  ok   " + name); }
        else { failed++; Console.WriteLine("  FAIL " + name); }
    }

    // Orbit until inside the perfect window; wait for the best frame (release once the angle stops shrinking). Frame limit: one lap.
    static double OrbitToWindow(OrbitEngine e, int limit = 400)
    {
        double prev = double.PositiveInfinity;
        for (int i = 0; i < limit && e.Mode == Mode.Play; i++)
        {
            double a = e.PerfectAngle();
            if (a <= OrbitEngine.PerfectDegrees && a > prev) return prev;   // best frame passed: the previous angle was the one to release at
            prev = a; e.Step();
        }
        return prev;
    }

    static string State(OrbitEngine e)
    {
        var p = e.Probe;
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}|{1:R}|{2:R}|{3:R}|{4:R}|{5}|{6}|{7}|{8}|{9:R}",
            e.Mode, p.X, p.Y, p.Vx, p.Vy, e.Fuel, e.Shield, e.Chain, e.Frame, e.Heat);
    }

    // Full scenario: Earth -> perfect release -> lock on Moon -> off-target release from the Moon (chain breaks) -> burn.
    static OrbitEngine FirstLeg(bool report)
    {
        var e = new OrbitEngine(Mission.Mars());
        if (report)
        {
            Check(e.Probe.Locked && e.Probe.Lock.Id == "earth", "locked to Earth at start");
            Check(e.Shield == 3 && e.Fuel == 5 && e.SamplesTaken == 0, "shield 3, fuel 5, samples 0");
        }
        e.Hold();
        OrbitToWindow(e);
        double angle = e.PerfectAngle();
        e.Release();
        var ev = e.TakeEvents();
        if (report)
        {
            Check(angle <= 8 && e.Chain == 1 && ev.Any(o => o.Kind == GameEvent.Perfect && o.Value == 1), "Earth -> Moon perfect release (angle " + angle.ToString("0.00") + "°, chain 1)");
            Check(!e.Probe.Locked && ev.Any(o => o.Kind == GameEvent.Release), "release -> free flight + whoosh");
            Check(Math.Abs(e.Probe.Speed - OrbitEngine.Omega * (34 + 46)) < 0.2, "release speed ≈ 0.055 x ring (" + e.Probe.Speed.ToString("0.00") + ")");
        }
        // Hold when close to the Moon ring.
        var moon = e.Mission.Find("moon"); bool locked = false;
        for (int i = 0; i < 600 && e.Mode == Mode.Play; i++)
        {
            e.Step();
            double d = Math.Abs(Math.Sqrt(Math.Pow(e.Probe.X - moon.X, 2) + Math.Pow(e.Probe.Y - moon.Y, 2)) - moon.RingR);
            if (d < 6) { locked = e.Hold(); break; }
        }
        if (report) Check(locked && e.Probe.Lock == moon && e.Probe.Tentative, "Hold on Moon ring -> tentative lock (moving body)");
        for (int i = 0; i < 15; i++) e.Step();
        ev = e.TakeEvents();
        if (report) Check(e.Probe.Locked && e.Probe.Confirmed && ev.Any(o => o.Kind == GameEvent.Lock), "held 180 ms -> lock confirmed, clunk");
        // Perfect release from the Moon (to Venus), including the moving body's velocity.
        OrbitToWindow(e);
        double angle2 = e.PerfectAngle(); e.Release();
        if (report) Check(angle2 <= 8 && e.Chain == 2, "Moon -> Venus perfect, chain 2 (angle " + angle2.ToString("0.00") + "°)");
        // Burn: +2.2 along velocity, fuel -1.
        e.Step(); e.TakeEvents();
        double h0 = e.Probe.Speed, ux = e.Probe.Vx / h0, uy = e.Probe.Vy / h0;
        e.Hold(); e.Release(true);
        ev = e.TakeEvents();
        if (report)
        {
            Check(Math.Abs(e.Probe.Speed - (h0 + 2.2)) < 1e-9 && e.Fuel == 4 && ev.Any(o => o.Kind == GameEvent.Burn), "tap in flight = burn (+2.2, fuel 4)");
            Check(Math.Abs(e.Probe.Vx / e.Probe.Speed - ux) < 1e-9 && Math.Abs(e.Probe.Vy / e.Probe.Speed - uy) < 1e-9, "burn is along velocity (direction unchanged)");
        }
        return e;
    }

    static void OutsideWindow()
    {
        var e = new OrbitEngine(Mission.Mars());
        // Release >= 30° away from the window: chain zero.
        for (int i = 0; i < 400 && e.PerfectAngle() < 30; i++) e.Step();
        double a = e.PerfectAngle(); e.Hold(); e.Release();
        Check(a > 8 && e.Chain == 0 && !e.TakeEvents().Any(o => o.Kind == GameEvent.Perfect), "release outside window (" + a.ToString("0.0") + "°) is not perfect");
        // Near the edge: 9-14° (step 0.055 rad ≈ 3.15°, so this 5° band always contains a frame) is not perfect.
        var n = new OrbitEngine(Mission.Mars());
        for (int i = 0; i < 400; i++) { double x = n.PerfectAngle(); if (x > 9 && x < 14) break; n.Step(); }
        double b = n.PerfectAngle(); n.Hold(); n.Release();
        Check(b > 9 && b < 14 && n.Chain == 0, "just outside the window edge (" + b.ToString("0.0") + "°) is not perfect");
    }

    static void TentativeUndo()
    {
        var e = new OrbitEngine(Mission.Mars());
        var ceres = e.Mission.Find("ceres");
        // Straight flight tangent to the top of the Ceres ring: at the moment of the touch the lock is tentative.
        e.TestPlaceProbe(ceres.X - 20, ceres.Y - ceres.RingR, 1.5, 0);
        e.Hold();
        Check(e.Probe.Locked && e.Probe.Tentative, "pressing in flight on a ring = tentative lock");
        for (int i = 0; i < 5; i++) e.Step();
        e.Release(true);
        var ev = e.TakeEvents();
        Check(!e.Probe.Locked && e.Fuel == 4 && !ev.Any(o => o.Kind == GameEvent.Lock), "short tap: lock undone, turned into fuel, no clunk");
        Check(Math.Abs(e.Probe.Y - (ceres.Y - ceres.RingR)) < 1e-9, "undone lock: probe on the same straight line");
        // Empty tank.
        e.Fuel = 0; e.Hold(); e.Release(true);
        Check(e.TakeEvents().Any(o => o.Kind == GameEvent.DryTick) && e.Fuel == 0, "empty tank: dry tick, fuel does not go negative");
    }

    static void Heat()
    {
        var e = new OrbitEngine(Mission.Mars());
        e.TestLock("venus"); e.Hold();
        int frames = 0;
        while (e.Probe.Locked && frames < 200) { e.Step(); frames++; }
        var ev = e.TakeEvents();
        Check(frames == 100, "Venus heat 1%/frame: burn-out on frame 100 (" + frames + ")");
        // Heat drops to 70, and cooling is applied on the same frame (now free): 69.4. Same order as the prototype.
        Check(e.Shield == 2 && ev.Any(o => o.Kind == GameEvent.HeatBurn) && Math.Abs(e.Heat - (70 - 0.6)) < 1e-9, "burn-out: shield -1, heat 70 (cooling on same frame: " + e.Heat.ToString("0.0") + ")");
        Check(ev.Any(o => o.Kind == GameEvent.HeatTick), "heat > 50% ticking");
        Check(e.Chain == 0, "forced release is not perfect");
        // Finger still held: consumed, no re-lock on Venus.
        for (int i = 0; i < 40; i++) e.Step();
        Check(!e.Probe.Locked || e.Probe.Lock.Id != "venus", "held finger consumed: no re-lock on Venus");
        Check(e.Heat < 70, "heat cools while free (" + e.Heat.ToString("0.0") + ")");
    }

    static OrbitEngine BrakeAndDock(bool samples, int fuelToSpend, bool report)
    {
        var e = new OrbitEngine(Mission.Mars());
        if (samples) foreach (var s in e.Mission.Samples) { e.TestPlaceProbe(s.X, s.Y, 0, 0); e.Step(); }
        if (report) Check(e.SamplesTaken == (samples ? 3 : 0), "sample pickup (" + e.SamplesTaken + "/3)");
        for (int i = 0; i < fuelToSpend; i++) e.Burn();
        e.TestLock("mars"); e.Hold();
        e.Step(); double speed0 = e.Probe.Speed;   // TestLock's temporary velocity (1) turns into tangent velocity on the first frame
        for (int k = 0; k < 2000 && e.Probe.Speed >= Station.SafeSpeed; k++) e.Step();
        double extraLaps = e.Probe.Total / Const.Tau - 1;
        if (report)
        {
            Check(Math.Abs(speed0 - 0.055 * 86) < 0.01, "Mars ring speed 4.73 (" + speed0.ToString("0.00") + ")");
            Check(e.Probe.Speed < 2.6 && extraLaps <= 3, "brake: < 2.6 within " + extraLaps.ToString("0.00") + " extra laps (<= 3)");
            Check(e.TakeEvents().Any(o => o.Kind == GameEvent.Brake), "brake sound event");
        }
        // With speed < 2.6, wait for the frame facing the station and release.
        OrbitToWindow(e);
        double a = e.PerfectAngle(), releaseSpeed = e.Probe.Speed; e.Release();
        for (int i = 0; i < 600 && e.Mode == Mode.Play; i++) e.Step();
        if (report) Check(e.Mode == Mode.Done && e.Docked && e.TakeEvents().Any(o => o.Kind == GameEvent.Dock), "soft landing at the station (release " + releaseSpeed.ToString("0.00") + ", angle " + a.ToString("0.0") + "°)");
        return e;
    }

    static void FastStation()
    {
        var e = new OrbitEngine(Mission.Mars());
        var s = e.Mission.Station;
        e.TestPlaceProbe(s.X - 60, s.Y, 4, 0);
        for (int i = 0; i < 30 && e.Shield == 3; i++) e.Step();
        var ev = e.TakeEvents();
        Check(e.Mode == Mode.Play && e.Shield == 2 && ev.Any(o => o.Kind == GameEvent.TooFast), "station at 4.0: bounce, shield -1, TOO FAST");
        Check(e.Probe.Vx < 0, "bounce pushes outward");
    }

    static void SignalLost()
    {
        var e = new OrbitEngine(Mission.Mars());
        // Falling probe: the camera descends at most 2 px/frame, the probe falls at 3 -> leaves the frame. x = 400: outside
        // Earth and the Moon's rail (200 ± 165).
        e.TestPlaceProbe(400, 2000, 0, 3);
        for (int i = 0; i < 600 && e.Mode == Mode.Play; i++) e.Step();
        Check(e.Mode == Mode.Lost && e.TakeEvents().Any(o => o.Kind == GameEvent.SignalLost), "below frame = SIGNAL LOST");
        var k = new OrbitEngine(Mission.Mars());
        k.TestPlaceProbe(-40, 1500, 0, 0); k.Step();
        Check(k.Mode == Mode.Lost, "outside side edge = SIGNAL LOST");
        // Three rock hits = lost (invulnerability 70 frames apart).
        var z = new OrbitEngine(Mission.Mars());
        for (int i = 0; i < 3; i++)
        {
            var rock = z.Mission.Rocks.First(q => q.X > 40 && q.X < 380);   // first rock inside the frame (a rock at the edge = signal lost)
            z.TestPlaceProbe(rock.X + rock.Vx, rock.Y, 0, 0); z.Step();
            for (int j = 0; j < 71; j++) { z.TestPlaceProbe(10, 1500, 0, 0); z.Step(); }
        }
        Check(z.Shield == 0 && z.Mode == Mode.Lost, "three hits: shield 0 -> lost (" + z.Shield + ")");
    }

    static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("Kepler Drift mission bot");
        Console.WriteLine("[first leg: Earth -> Moon -> Venus]"); var a = FirstLeg(true);
        Console.WriteLine("[perfect window]"); OutsideWindow();
        Console.WriteLine("[tentative lock, fuel]"); TentativeUndo();
        Console.WriteLine("[heat]"); Heat();
        Console.WriteLine("[brake + dock, 3 stars]");
        var m3 = BrakeAndDock(true, 0, true);
        var s3 = m3.Stars();
        Check(s3.All(x => x), "stars: dock + 3/3 samples + fuel >= 2 -> 3/3");
        var m1 = BrakeAndDock(false, 4, false);
        var s1 = m1.Stars();
        Check(m1.Docked && s1[0] && !s1[1] && !s1[2], "stars: no samples and fuel 1 -> dock only (" + string.Join(",", s1) + ")");
        Check(!new OrbitEngine(Mission.Mars()).Stars().Any(x => x), "no stars before docking");
        Console.WriteLine("[station, lost]"); FastStation(); SignalLost();
        Console.WriteLine("[determinism]");
        var b = FirstLeg(false);
        for (int i = 0; i < 300; i++) { a.Step(); b.Step(); }
        Check(State(a) == State(b), "same scenario twice = same state");
        Check(!double.IsNaN(a.Probe.X + a.Probe.Y + a.Probe.Vx + a.Probe.Vy), "no NaN");
        var r = new Rng(42); double r0 = r.Next();
        Check(Math.Abs(r0 - 0.6011037519201636) < 1e-15, "mulberry32(42) first number matches JS (" + r0.ToString("R") + ")");
        Console.WriteLine(failed == 0 ? "OK: all " + passed + " checks passed" : "BROKEN: " + failed + " failed, " + passed + " passed");
        return failed == 0 ? 0 : 1;
    }
}
