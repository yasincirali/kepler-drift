// Player-facing text: a tiny two-language table (English default + Turkish). Why a table in code and not Unity Localization:
// one mission and ~25 strings; the package adds an addressables build step and a settings asset the code-built project
// (CiBuild) does not carry. When the string count grows this moves to a data file with the same keys.
// Language comes from the device (Application.systemLanguage); anything other than Turkish falls back to English, and a key
// missing from a language falls back to English too, so a half-translated table never shows an empty label.
using System.Collections.Generic;
using UnityEngine;

namespace KeplerDrift.Game
{
    public static class Strings
    {
        public enum Lang { En, Tr }

        public static Lang Current = Application.systemLanguage == SystemLanguage.Turkish ? Lang.Tr : Lang.En;

        static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            { "mission.mars", "MISSION 3 · MARS" },
            { "shield", "SHIELD" },
            { "speed", "SPEED" },
            { "fuel", "FUEL" },
            { "heat", "HEAT" },
            { "sample", "SAMPLE" },
            { "landing", "DOCK" },
            { "hint", "Hold · release · tap while flying = fuel" },
            { "perfect", "PERFECT" },
            { "hit", "IMPACT" },
            { "too_fast", "TOO FAST" },
            { "soft_landing", "SOFT LANDING" },
            { "mission_complete", "MISSION COMPLETE" },
            { "signal_lost", "SIGNAL LOST" },
            { "retry", "Retry" },
            { "sound", "SOUND" },
            { "sound_off", "SOUND\nOFF" },
            { "star_dock", "DOCK" },
            { "star_samples", "SAMPLE 3/3" },
            { "star_fuel", "FUEL ≥ 2" },
        };

        static readonly Dictionary<string, string> Tr = new Dictionary<string, string>
        {
            { "mission.mars", "GÖREV 3 · MARS" },
            { "shield", "KALKAN" },
            { "speed", "HIZ" },
            { "fuel", "YAKIT" },
            { "heat", "ISI" },
            { "sample", "ÖRNEK" },
            { "landing", "İNİŞ" },
            { "hint", "Basılı tut · bırak · uçarken dokun = yakıt" },
            { "perfect", "KUSURSUZ" },
            { "hit", "ÇARPIŞMA" },
            { "too_fast", "ÇOK HIZLI" },
            { "soft_landing", "YUMUŞAK İNİŞ" },
            { "mission_complete", "GÖREV TAMAM" },
            { "signal_lost", "SİNYAL KAYBI" },
            { "retry", "Yeniden" },
            { "sound", "SES" },
            { "sound_off", "SES\nKAPALI" },
            { "star_dock", "İNİŞ" },
            { "star_samples", "ÖRNEK 3/3" },
            { "star_fuel", "YAKIT ≥ 2" },
        };

        // fallback: used when the key is in neither table (e.g. a mission name not yet added); defaults to the key itself.
        public static string Get(string key, string fallback = null)
        {
            string s;
            if (Current == Lang.Tr && Tr.TryGetValue(key, out s)) return s;
            if (En.TryGetValue(key, out s)) return s;
            return fallback ?? key;
        }

        // Decimal separator follows the language (4.7 / 4,7): the readout is part of the localized UI.
        public static System.Globalization.CultureInfo Culture
        {
            get { return Current == Lang.Tr ? System.Globalization.CultureInfo.GetCultureInfo("tr-TR") : System.Globalization.CultureInfo.InvariantCulture; }
        }
    }
}
