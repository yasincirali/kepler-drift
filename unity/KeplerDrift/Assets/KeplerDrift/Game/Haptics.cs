// Haptic feedback, using the prototype's buzz() patterns (ms). SKELETON: on iOS, UIImpactFeedbackGenerator / Core Haptics
// needs a native plugin (Plugins/iOS/KeplerDriftHaptics.mm, TODO). No DllImport is written while the plugin is missing: the
// Xcode linker breaks on a missing symbol (Birdie Smash Native.cs pattern, the plugin comes first). On Android
// Handheld.Vibrate is a fixed ~400 ms and very harsh; it is not used for short clicks.
using UnityEngine;
using KeplerDrift.Core;

namespace KeplerDrift.Game
{
    public sealed class Haptics
    {
        public bool Enabled = true;

        public void Play(EventRecord ev)
        {
            if (!Enabled) return;
            switch (ev.Kind)
            {
                case GameEvent.Lock: Vibrate(10); break;          // light click: lock confirmed
                case GameEvent.Perfect: Vibrate(20); break;
                case GameEvent.Burn: Vibrate(15); break;
                case GameEvent.HeatBurn:
                case GameEvent.Hit: Vibrate(40); break;           // shield -1
                case GameEvent.SignalLost: Vibrate(80); break;
                case GameEvent.Dock: Vibrate(30, 60, 30); break;  // double pulse: docking
            }
        }

        // Pattern: vibrate, pause, vibrate … (ms). TODO: on iOS 10-20 ms -> light/medium impact, 40+ -> heavy, pattern -> CHHapticPattern.
        static void Vibrate(params int[] pattern)
        {
#if UNITY_EDITOR
            if (Application.isBatchMode) return;
#endif
        }
    }
}
