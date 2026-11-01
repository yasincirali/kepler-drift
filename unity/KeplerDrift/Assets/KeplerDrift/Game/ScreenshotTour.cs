// Screenshot tour (Birdie Smash ScreenshotTour pattern): the cloud has no Unity Editor and no device; the only way to see the
// real Unity render is to run the Linux player under xvfb in CI (.github/workflows/unity-screenshots.yml).
// Only starts with the `-screenshots <folder>` argument; it never runs in a store build (the argument is never passed).
// Moments are set up with the engine's test helpers (TestLock / TestPlaceProbe), then frozen with GameRoot.Paused: software
// rendering is slow, so "catching the right moment" is not left to real time. Bot decisions are the same as in MissionBot.
// SKELETON: frames + report.txt; x2 text and reduced-motion frames are TODO.
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using KeplerDrift.Core;

namespace KeplerDrift.Game
{
    [DefaultExecutionOrder(-100)]
    public sealed class ScreenshotTour : MonoBehaviour
    {
        string dir; GameRoot root; readonly StringBuilder report = new StringBuilder();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var a = System.Environment.GetCommandLineArgs(); string d = null;
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-screenshots") d = a[i + 1];
            if (d == null) return;
            var g = new GameObject("screenshot_tour"); var t = g.AddComponent<ScreenshotTour>(); t.dir = d; DontDestroyOnLoad(g);
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(dir); QualitySettings.vSyncCount = 0;   // the xvfb display is 0 Hz: vsync would stall on the first frame
            Debug.Log("ScreenshotTour started: " + dir);
            yield return null; root = GameRoot.Instance;
            if (root == null) { Debug.LogError("ScreenshotTour: no GameRoot"); Application.Quit(1); yield break; }
            report.AppendLine("screen " + Screen.width + "x" + Screen.height + " · safe area " + Screen.safeArea);

            yield return Capture("00_first_frame");

            // Orbit Earth into the perfect window, release: PERFECT text and white trail.
            var e = root.Engine; root.Paused = true;
            for (int i = 0; i < 400 && e.PerfectAngle() > 3; i++) e.Step();
            e.Hold(); e.Release(); for (int i = 0; i < 4; i++) e.Step();
            Note("perfect", e); yield return Capture("01_perfect");

            root.Restart(); e = root.Engine; root.Paused = true;
            e.TestLock("venus"); e.Hold(); for (int i = 0; i < 80; i++) e.Step();
            Note("venus_heat", e); yield return Capture("02_venus_heat");

            root.Restart(); e = root.Engine; root.Paused = true;
            e.TestLock("mars"); e.Hold(); for (int i = 0; i < 2000 && (e.Probe.Speed >= 2.6 || e.Probe.Total < Const.Tau); i++) e.Step();
            Note("mars_brake", e); yield return Capture("03_mars_brake");
            for (int i = 0; i < 400 && e.PerfectAngle() > 3; i++) e.Step();
            e.Release(); for (int i = 0; i < 600 && e.Mode == Mode.Play; i++) e.Step();
            for (int i = 0; i < 60; i++) e.Step();
            Note("dock", e); yield return Capture("04_mission_complete");

            root.Restart(); e = root.Engine; root.Paused = true;
            e.TestPlaceProbe(400, 2000, 0, 3); for (int i = 0; i < 600 && e.Mode == Mode.Play; i++) e.Step();
            for (int i = 0; i < 60; i++) e.Step();
            Note("signal_lost", e); yield return Capture("05_signal_lost");

            // TODO: x2 text scale, reduced motion, iPad (768x1024) frames; the asteroid belt and sample pickup moment.
            File.WriteAllText(Path.Combine(dir, "report.txt"), report.ToString());
            Debug.Log("ScreenshotTour finished");
            Application.Quit(0);
        }

        void Note(string name, OrbitEngine e)
        {
            report.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0}: mode {1} frame {2} speed {3:0.00} fuel {4} shield {5} heat {6:0} chain {7} samples {8} stars {9}",
                name, e.Mode, e.Frame, e.Probe.Speed, e.Fuel, e.Shield, e.Heat, e.Chain, e.SamplesTaken, string.Join(",", e.Stars())));
            e.TakeEvents();
        }

        // End of frame: UI Toolkit draws into the back buffer, so ScreenCapture catches the HUD too. Two frames are waited so
        // Sync draws the frozen state.
        IEnumerator Capture(string name)
        {
            yield return null; yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG()); Destroy(tex);
            Debug.Log("ScreenshotTour frame: " + name);
        }
    }
}
