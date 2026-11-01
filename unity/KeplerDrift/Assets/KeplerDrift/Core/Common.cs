// Shared Core parts. NO UnityEngine import in this folder (same rule as Birdie Smash): the rules compile with mono without
// Unity and are checked by tool/unity_test/MissionBot.cs; once tied to Unity they could not be tested in the cloud.
// Why double: so it produces the same numbers as the prototype JS (IEEE double); with float, after a few thousand frames at
// 60 Hz the orbit angle and rock positions diverged and the "same touch = same result" promise broke.
using System;

namespace KeplerDrift.Core
{
    public static class Const
    {
        public const double Tau = Math.PI * 2;
        public const double FrameMs = 1000.0 / 60.0;   // fixed step: the engine never sees real time (determinism)
    }

    // mulberry32: same seed as the prototype -> same rock layout. Exactly equivalent to JS `>>>` and Math.imul uint arithmetic.
    public sealed class Rng
    {
        uint s;
        public Rng(uint seed) { s = seed; }
        public double Next()
        {
            unchecked
            {
                s += 0x6D2B79F5u;
                uint t = (s ^ (s >> 15)) * (1u | s);
                t = (t + ((t ^ (t >> 7)) * (61u | t))) ^ t;
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }
    }

    // Events the presentation layer (sound, haptics, text, hit-stop) listens to. The engine knows no sound/vibration; it only
    // says "what happened".
    public enum GameEvent
    {
        Lock,         // clunk: when a tentative lock is confirmed or on a firm lock (an undone lock plays no sound)
        Release,      // whoosh
        Perfect,      // perfect (Value = chain)
        Burn,         // burn
        DryTick,      // dry: tank empty
        HeatTick,     // tick: heat > 50%, speeds up like a geiger counter
        HeatBurn,     // heat: heat 100%, shield -1 (not a collision; no hit-stop)
        Hit,          // hit: hard contact with rock/body/station, shield -1
        TooFast,      // reached the station at >= 2.6 (comes together with Hit; separate for the text)
        Sample,       // sample (Value = samples collected)
        Brake,        // brake: every half second on extra laps
        Dock,         // dock: soft landing
        SignalLost    // lost: shield gone or probe fell out of frame
    }

    public struct EventRecord
    {
        public GameEvent Kind; public int Value;
        public EventRecord(GameEvent kind, int value) { Kind = kind; Value = value; }
        public override string ToString() { return Kind + (Value != 0 ? "(" + Value + ")" : ""); }
    }
}
