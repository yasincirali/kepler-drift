// The scene's single root: drives OrbitEngine at a fixed 60 Hz step, turns the finger into engine input, and dispatches events
// to the presentation (Sfx, Haptics, Hud, hit-stop). NO rules here; rules are in Core (checked by MissionBot without Unity).
// Why a fixed step, not Time.deltaTime: the rules are frame based (0.055 rad/frame, heat 1%/frame); a variable step would
// give a different game on ProMotion (120 Hz) than on a 60 Hz device. Accumulated time is capped at 0.1 s so returning from
// the background causes no physics "catch-up" burst (same as the prototype).
// Input: Input System (Pointer.current: touch and mouse through one path). The project setting is switched to "Both" on the
// first CI build and the old input backend may run until Unity restarts; so without ENABLE_INPUT_SYSTEM it falls back to the old Input.
using System.Collections.Generic;
using UnityEngine;
using KeplerDrift.Core;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace KeplerDrift.Game
{
    [DefaultExecutionOrder(-50)]
    public sealed class GameRoot : MonoBehaviour
    {
        // Tap thresholds match the prototype: 180 ms and 8 px (world px, not screen px, so it feels the same on a tablet).
        public const float TapSeconds = 0.18f, TapPx = 8f;
        const double StepSeconds = 1.0 / 60.0;
        const float HitStopSeconds = 0.07f;   // 70 ms: gives a hit "weight"; drawing continues, game time stops

        public static GameRoot Instance { get; private set; }
        public OrbitEngine Engine { get; private set; }
        public Hud HudLayer { get; private set; }
        public bool Paused;              // for frozen moments in ScreenshotTour
        public bool ReduceMotion;        // TODO: iOS UIAccessibilityIsReduceMotionEnabled (native plugin); from Settings for now

        Camera cam; ProbeView probeView; readonly List<BodyView> bodyViews = new List<BodyView>();
        readonly List<Transform> rockViews = new List<Transform>(), sampleViews = new List<Transform>(); Transform stationView;
        Sfx sfx; Haptics haptics;
        double accumulator; float freezeUntil; int endFrames;
        bool pressed, moved, ignore; float pressTime; Vector2 pressPos;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            // The scene is built from code in CiBuild; if the root is missing (an empty scene opened by hand) it is created so the game still starts.
            if (FindFirstObjectByType<GameRoot>() == null) new GameObject("GameRoot").AddComponent<GameRoot>();
        }

        void Awake()
        {
            Instance = this;
            Application.targetFrameRate = 60;   // the fixed step is 60; a higher frame rate only costs battery
            cam = Camera.main;
            if (cam == null) { var g = new GameObject("Camera"); g.tag = "MainCamera"; cam = g.AddComponent<Camera>(); }
            cam.orthographic = true; cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color32(0x05, 0x07, 0x0d, 0xff);   // prototype COL.bg
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 100f;
            sfx = new GameObject("Sfx").AddComponent<Sfx>(); sfx.transform.SetParent(transform);
            haptics = new Haptics();
            HudLayer = new GameObject("Hud").AddComponent<Hud>(); HudLayer.transform.SetParent(transform);
            HudLayer.OnRetry = Restart;
            HudLayer.OnMuteChanged = m => sfx.Muted = m;
            Restart();
        }

        public void Restart()
        {
            foreach (Transform c in transform) if (c.name.StartsWith("world_")) Destroy(c.gameObject);
            bodyViews.Clear(); rockViews.Clear(); sampleViews.Clear();
            Engine = new OrbitEngine(Mission.Mars()) { ReduceMotion = ReduceMotion };
            var worldRoot = new GameObject("world_" + Time.frameCount).transform; worldRoot.SetParent(transform);
            foreach (var b in Engine.Mission.Bodies)
            {
                var v = new GameObject("body_" + b.Id).AddComponent<BodyView>(); v.transform.SetParent(worldRoot); v.Setup(b); bodyViews.Add(v);
            }
            foreach (var r in Engine.Mission.Rocks) rockViews.Add(Marker(worldRoot, "rock", (float)r.S * 2, new Color(0.54f, 0.52f, 0.5f)));
            foreach (var s in Engine.Mission.Samples) sampleViews.Add(Marker(worldRoot, "sample", 10, new Color(0.5f, 0.85f, 1f)));
            // TODO(station visual): the prototype's ring + two panels drawing; for now a faint circle the size of the capture radius.
            stationView = Marker(worldRoot, "station", (float)Station.CaptureR * 2, new Color(0.5f, 0.85f, 1f, 0.35f));
            probeView = new GameObject("probe").AddComponent<ProbeView>(); probeView.transform.SetParent(worldRoot);
            accumulator = 0; freezeUntil = 0; endFrames = 0; pressed = false; ignore = true;
            sfx.ResetHum();
            HudLayer.Begin(Engine);
            Sync();
        }

        Transform Marker(Transform parent, string name, float diameter, Color color)
        {
            var g = new GameObject(name); g.transform.SetParent(parent);
            var sr = g.AddComponent<SpriteRenderer>(); sr.sprite = BodyView.Circle(); sr.color = color; sr.sortingOrder = 2;
            g.transform.localScale = Vector3.one * diameter;
            return g.transform;
        }

        // World px (y down) -> Unity units (y up). 1 unit = 1 prototype px: numbers read the same as in the spec.
        public static Vector3 World(double x, double y, float z = 0) { return new Vector3((float)x, (float)-y, z); }

        void Update()
        {
            ReadInput();
            if (!Paused)
            {
                accumulator += Mathf.Min(0.1f, Time.unscaledDeltaTime);
                while (accumulator >= StepSeconds)
                {
                    if (freezeUntil > 0)
                    {
                        // Hit-stop: accumulated time is dropped so there is no "catch-up" jump after the freeze.
                        if (Time.unscaledTime < freezeUntil) { accumulator = 0; break; }
                        freezeUntil = 0;
                    }
                    Engine.Step(); accumulator -= StepSeconds;
                    if (Engine.Mode != Mode.Play) endFrames++;
                    DispatchEvents();
                }
            }
            Sync();
        }

        void ReadInput()
        {
            bool down, up; Vector2 pos;
#if ENABLE_INPUT_SYSTEM
            var p = Pointer.current;
            if (p == null) return;
            down = p.press.wasPressedThisFrame; up = p.press.wasReleasedThisFrame; pos = p.position.ReadValue();
#else
            down = UnityEngine.Input.GetMouseButtonDown(0); up = UnityEngine.Input.GetMouseButtonUp(0); pos = UnityEngine.Input.mousePosition;
#endif
            if (down)
            {
                // Hud buttons (sound, Retry) before game input: that touch does not count as fuel/lock.
                if (HudLayer.OverButton(pos)) { ignore = true; return; }
                pressed = true; pressTime = Time.unscaledTime; pressPos = pos; moved = false; ignore = false;
                sfx.Wake();
                if (Engine.Mode != Mode.Play)
                {
                    // On the end panel any touch restarts after 40 frames (~0.7 s); an earlier touch is swallowed so the frantic
                    // tap that lost the last shield does not skip the panel instantly.
                    if (endFrames > 40) Restart();
                    ignore = true; return;
                }
                Engine.Hold();
            }
            if (pressed && !moved && WorldPx(pos - pressPos) > TapPx) moved = true;
            if (up && pressed)
            {
                pressed = false;
                if (ignore || Engine.Mode != Mode.Play) return;
                bool tap = !moved && Time.unscaledTime - pressTime < TapSeconds;
                Engine.Release(tap);
            }
            // TODO(pointercancel): if a system gesture (Notification Centre) cancels the touch it must not count as a tap (prototype onUp(e, true)).
        }

        float WorldPx(Vector2 screen) { return screen.magnitude * (cam.orthographicSize * 2f) / Mathf.Max(1, Screen.height); }

        void DispatchEvents()
        {
            foreach (var ev in Engine.TakeEvents())
            {
                sfx.Play(ev); haptics.Play(ev); HudLayer.OnEvent(ev);
                // Hit-stop on perfect and hit; none on HEAT (the player already lost the lock, a second pause would prolong the penalty).
                if (!ReduceMotion && (ev.Kind == GameEvent.Perfect || ev.Kind == GameEvent.Hit)) freezeUntil = Time.unscaledTime + HitStopSeconds;
            }
        }

        void Sync()
        {
            var m = Engine.Mission;
            // On narrow screens (aspect < 420/880) fit horizontally: the out-of-frame rule assumes 420 px in x, the edge must be visible.
            float h = (float)m.View * 0.5f, w = (float)m.Width * 0.5f / Mathf.Max(0.01f, cam.aspect);
            cam.orthographicSize = Mathf.Max(h, w);
            cam.transform.position = World(m.Width * 0.5, Engine.CameraY + m.View * 0.5, -10);
            foreach (var v in bodyViews) v.Sync(Engine);
            for (int i = 0; i < rockViews.Count; i++) { var r = m.Rocks[i]; rockViews[i].position = World(r.X, r.Y); rockViews[i].rotation = Quaternion.Euler(0, 0, -(float)(r.Rot * Mathf.Rad2Deg)); }
            for (int i = 0; i < sampleViews.Count; i++) { var s = m.Samples[i]; sampleViews[i].position = World(s.X, s.Y); sampleViews[i].gameObject.SetActive(!s.Taken); }
            stationView.position = World(m.Station.X, m.Station.Y);
            probeView.Sync(Engine, ReduceMotion);
            HudLayer.Sync(Engine, ReduceMotion);
            sfx.Sync(Engine);
        }
    }
}
