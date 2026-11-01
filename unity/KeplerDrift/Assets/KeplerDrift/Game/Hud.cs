// HUD: UI Toolkit, a single monospaced font (prototype "IBM Plex Mono"). Why monospaced: the speed changes every frame; with a
// proportional font "4.7" -> "4.1" changed width and the column jittered. Labels match the prototype (SPEED / FUEL / HEAT /
// SAMPLE) and come from the Strings table (English default, Turkish when the device is Turkish).
// Colour never carries meaning alone: when speed drops below the docking threshold it turns white AND a "DOCK" label appears;
// the shield turns amber on its last unit and the count is written too.
using System;
using UnityEngine;
using UnityEngine.UIElements;
using KeplerDrift.Core;

namespace KeplerDrift.Game
{
    public sealed class Hud : MonoBehaviour
    {
        public Font monoFont;   // TODO: IBM Plex Mono (OFL) under Assets/KeplerDrift/Fonts; when empty, Unity's built-in font
        public Action OnRetry; public Action<bool> OnMuteChanged;

        static readonly Color Cyan = new Color(0x7f / 255f, 0xd8 / 255f, 1f), Amber = new Color(1f, 0xb3 / 255f, 0x47 / 255f);

        UIDocument doc; VisualElement root, area, panel, endPanel; bool built;
        Label title, shield, speed, speedNote, fuel, heatLabel, sample, hint, toast, endTitle, endSub, stars;
        VisualElement heatBar, heatFill; Button retryButton, soundButton;
        int toastFrames, hintFrames; bool muted; OrbitEngine e;

        void Awake()
        {
            doc = gameObject.AddComponent<UIDocument>();
            var ps = ScriptableObject.CreateInstance<PanelSettings>();
            // Logical size matches the prototype (420 x 880); on wide screens it scales by height so the bottom panel stays under the finger.
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize; ps.referenceResolution = new Vector2Int(420, 880);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight; ps.match = 0.5f;
            var theme = Resources.Load<ThemeStyleSheet>("ui/theme"); if (theme != null) ps.themeStyleSheet = theme;
            doc.panelSettings = ps;
            if (monoFont == null) monoFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        public void Begin(OrbitEngine engine) { e = engine; hintFrames = 0; toastFrames = 0; if (built) endPanel.style.display = DisplayStyle.None; }

        void Build()
        {
            root = doc.rootVisualElement; if (root == null) return;
            root.Clear(); root.pickingMode = PickingMode.Ignore;
            if (monoFont != null) root.style.unityFontDefinition = FontDefinition.FromFont(monoFont);
            root.style.color = Color.white;
            // Inside the safe area: absolutely positioned elements ignore the parent's padding, hence a separate inner container (SafeArea).
            area = new VisualElement(); area.pickingMode = PickingMode.Ignore; area.style.position = Position.Absolute; root.Add(area);

            title = Text(area, 12, true); title.style.position = Position.Absolute; title.style.left = 18; title.style.top = 22; title.style.letterSpacing = 2.5f;
            shield = Text(area, 12, true); shield.style.position = Position.Absolute; shield.style.right = 18; shield.style.top = 22;

            // Sound button: >= 44 pt touch target (HIG); it was 32 px in the prototype, enlarged.
            soundButton = new Button(() => { muted = !muted; OnMuteChanged?.Invoke(muted); WriteSound(); });
            soundButton.style.position = Position.Absolute; soundButton.style.right = 14; soundButton.style.bottom = 108;
            soundButton.style.width = 44; soundButton.style.height = 44; Border(soundButton, Cyan, 0.4f);
            soundButton.style.backgroundColor = new Color(0.02f, 0.05f, 0.09f, 0.6f); soundButton.style.color = Cyan; soundButton.style.fontSize = 10;
            area.Add(soundButton); WriteSound();

            panel = new VisualElement(); panel.pickingMode = PickingMode.Ignore;
            panel.style.position = Position.Absolute; panel.style.left = 14; panel.style.right = 14; panel.style.bottom = 14; panel.style.height = 78;
            panel.style.flexDirection = FlexDirection.Row; panel.style.backgroundColor = new Color(6 / 255f, 12 / 255f, 22 / 255f, 0.62f);
            Border(panel, Cyan, 0.22f); area.Add(panel);
            var c1 = Column(); Text(c1, 10, false, Strings.Get("speed"), 0.6f); speed = Text(c1, 20, true); speedNote = Text(c1, 9, false, "", 0.8f);
            var c2 = Column(); Text(c2, 10, false, Strings.Get("fuel"), 0.6f); fuel = Text(c2, 20, true);
            var c3 = Column(); heatLabel = Text(c3, 10, false, Strings.Get("heat"), 0.8f);
            heatBar = new VisualElement(); heatBar.style.height = 8; heatBar.style.marginTop = 12; Border(heatBar, Cyan, 0.6f);
            heatFill = new VisualElement(); heatFill.style.height = Length.Percent(100); heatBar.Add(heatFill); c3.Add(heatBar);
            var c4 = Column(); Text(c4, 10, false, Strings.Get("sample"), 0.6f); sample = Text(c4, 20, true);

            hint = Text(area, 12, false, Strings.Get("hint"), 0.85f);
            hint.style.position = Position.Absolute; hint.style.left = 0; hint.style.right = 0; hint.style.bottom = 120;
            hint.style.unityTextAlign = TextAnchor.MiddleCenter; hint.style.color = new Color(0xd8 / 255f, 0xf1 / 255f, 1f);
            toast = Text(area, 15, true); toast.style.position = Position.Absolute; toast.style.left = 0; toast.style.right = 0; toast.style.top = 300;
            toast.style.unityTextAlign = TextAnchor.MiddleCenter;

            endPanel = new VisualElement(); endPanel.style.position = Position.Absolute; endPanel.style.left = 0; endPanel.style.right = 0; endPanel.style.top = 0; endPanel.style.bottom = 0;
            endPanel.style.backgroundColor = new Color(3 / 255f, 5 / 255f, 10 / 255f, 0.72f); endPanel.style.justifyContent = Justify.Center; endPanel.style.alignItems = Align.Center;
            var card = new VisualElement(); card.style.width = 300; card.style.paddingTop = 28; card.style.paddingBottom = 22; card.style.alignItems = Align.Center;
            card.style.backgroundColor = new Color(8 / 255f, 14 / 255f, 26 / 255f, 0.9f); Border(card, Cyan, 0.9f); endPanel.Add(card);
            endTitle = Text(card, 22, true); endTitle.style.letterSpacing = 4;
            stars = Text(card, 28, true); stars.style.marginTop = 16;
            endSub = Text(card, 10, false, "", 0.7f); endSub.style.marginTop = 6; endSub.style.whiteSpace = WhiteSpace.Normal; endSub.style.unityTextAlign = TextAnchor.MiddleCenter;
            retryButton = new Button(() => OnRetry?.Invoke()) { text = Strings.Get("retry") };
            retryButton.style.width = 150; retryButton.style.height = 44; retryButton.style.marginTop = 20; retryButton.style.fontSize = 14;   // 44 pt (HIG)
            retryButton.style.backgroundColor = Color.clear; retryButton.style.color = Color.white; Border(retryButton, Cyan, 0.9f);
            card.Add(retryButton);
            endPanel.style.display = DisplayStyle.None; root.Add(endPanel);
            built = true;
        }

        VisualElement Column()
        {
            var c = new VisualElement(); c.pickingMode = PickingMode.Ignore; c.style.flexGrow = 1; c.style.flexBasis = 0;
            c.style.paddingLeft = 12; c.style.paddingTop = 10; panel.Add(c); return c;
        }

        static Label Text(VisualElement parent, int size, bool bold, string text = "", float alpha = 1)
        {
            var l = new Label(text); l.pickingMode = PickingMode.Ignore; l.style.fontSize = size;
            if (bold) l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.color = new Color(1, 1, 1, alpha); l.style.marginLeft = l.style.marginRight = l.style.paddingLeft = l.style.paddingRight = 0;
            parent.Add(l); return l;
        }

        static void Border(VisualElement el, Color c, float alpha)
        {
            var r = new Color(c.r, c.g, c.b, alpha);
            el.style.borderTopWidth = el.style.borderBottomWidth = el.style.borderLeftWidth = el.style.borderRightWidth = 1;
            el.style.borderTopColor = el.style.borderBottomColor = el.style.borderLeftColor = el.style.borderRightColor = r;
        }

        void WriteSound() { if (soundButton != null) soundButton.text = Strings.Get(muted ? "sound_off" : "sound"); }

        public void OnEvent(EventRecord ev)
        {
            if (!built) return;
            string t = null; Color c = Cyan;
            switch (ev.Kind)
            {
                case GameEvent.Perfect: t = ev.Value > 1 ? Strings.Get("perfect") + " ×" + ev.Value : Strings.Get("perfect"); break;
                case GameEvent.Sample: t = Strings.Get("sample") + " " + ev.Value + "/3"; break;
                case GameEvent.HeatBurn: t = Strings.Get("heat"); c = Amber; break;
                case GameEvent.Hit: t = Strings.Get("hit"); c = Amber; break;
                case GameEvent.TooFast: t = Strings.Get("too_fast"); c = Amber; break;
                case GameEvent.Dock: t = Strings.Get("soft_landing"); c = Color.white; break;
            }
            if (t == null) return;
            toast.text = t; toast.style.color = c; toastFrames = 70;
        }

        public void Sync(OrbitEngine engine, bool reduceMotion)
        {
            e = engine;
            if (!built) { Build(); if (!built) return; }
            SafeArea();
            title.text = Strings.Get("mission." + e.Mission.Id, e.Mission.Name);
            string k = ""; for (int i = 0; i < Mission.ShieldMax; i++) k += i < e.Shield ? "■" : "□";
            shield.text = Strings.Get("shield") + " " + k; shield.style.color = e.Shield == 1 ? Amber : Cyan;
            double sp = e.Probe.Speed; bool safe = sp < Station.SafeSpeed;
            // Below the docking threshold: white + "DOCK" note, the "you can come in now" signal (not colour alone).
            speed.text = sp.ToString("0.0", Strings.Culture); speed.style.color = safe ? Color.white : Cyan; speedNote.text = safe ? Strings.Get("landing") : "";
            string f = ""; for (int i = 0; i < Mission.FuelMax; i++) f += i < e.Fuel ? "▮" : "▯";
            fuel.text = f; fuel.style.color = e.Fuel <= 0 ? Amber : Cyan;
            bool hot = e.Heat > 60; var hc = hot ? Amber : Cyan;
            heatLabel.style.visibility = heatBar.style.visibility = e.Heat > 0 ? Visibility.Visible : Visibility.Hidden;
            heatLabel.text = Strings.Get("heat") + " " + Math.Round(e.Heat) + "%"; heatLabel.style.color = hc;
            heatFill.style.width = Length.Percent((float)e.Heat); heatFill.style.backgroundColor = hc;
            sample.text = e.SamplesTaken + "/3"; sample.style.color = e.SamplesTaken >= 3 ? Color.white : Cyan;
            // Hint for the first 3 seconds (180 frames), fading out over the last half second.
            hintFrames++; hint.style.opacity = e.Mode != Mode.Play ? 0 : hintFrames < 150 ? 0.85f : Mathf.Max(0, 0.85f * (180 - hintFrames) / 30f);
            if (toastFrames > 0) toastFrames--; toast.style.opacity = Mathf.Clamp01(toastFrames / 20f);
            // TODO(pop): 9-frame grow when a HUD number changes (none with reduced motion); TODO(edge arrow): when the target is off-frame.
            if (e.Mode == Mode.Play) { endPanel.style.display = DisplayStyle.None; return; }
            endPanel.style.display = DisplayStyle.Flex;
            if (e.Mode == Mode.Done)
            {
                var st = e.Stars(); string s = "";
                for (int i = 0; i < 3; i++) s += (st[i] ? "★" : "☆") + (i < 2 ? "  " : "");
                endTitle.text = Strings.Get("mission_complete"); endTitle.style.color = Color.white; stars.text = s;
                // What each star was for is written too (not colour/shape alone).
                endSub.text = Strings.Get("star_dock") + " " + (st[0] ? "✓" : "–") + "   " + Strings.Get("star_samples") + " " + (st[1] ? "✓" : "–") + "   " + Strings.Get("star_fuel") + " " + (st[2] ? "✓" : "–");
                // TODO(star order): one by one 250 ms apart, a ping on each earned star (prototype feelFrame).
            }
            else { endTitle.text = Strings.Get("signal_lost"); endTitle.style.color = Amber; stars.text = "— — —"; endSub.text = ""; }
        }

        // Safe area: the notch and home indicator must not cut into the HUD.
        void SafeArea()
        {
            var sa = Screen.safeArea; if (Screen.height <= 0) return;
            float scale = 880f / Screen.height;   // approximate: panel scale; ±10% with MatchWidthOrHeight 0.5
            area.style.top = (Screen.height - sa.yMax) * scale; area.style.bottom = sa.yMin * scale;
            area.style.left = sa.xMin * scale; area.style.right = (Screen.width - sa.xMax) * scale;
        }

        // Screen px (Input System: bottom-left origin) -> panel; UI Toolkit expects a top-left origin.
        public bool OverButton(Vector2 screen)
        {
            if (!built || root.panel == null) return false;
            var p = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(screen.x, Screen.height - screen.y));
            for (var el = root.panel.Pick(p); el != null; el = el.parent) if (el is Button) return true;
            return false;
        }
    }
}
