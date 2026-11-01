# Technical debt — Kepler Drift

| Item | Why deferred | When |
|---|---|---|
| `Game/Haptics.cs` is empty (no iOS plugin) | First commit was prepared without Unity | At the first Editor open: UIImpactFeedbackGenerator bridge |
| `Game/Sfx.cs` filters are approximations (prototype band-pass/low-pass TODO) | A 1:1 port via OnAudioFilterRead takes time | Once the mix is heard on a phone |
| URP 17.0.4 ↔ 6000.0.58f2 match not verified | No Unity in the cloud | First CI build |
