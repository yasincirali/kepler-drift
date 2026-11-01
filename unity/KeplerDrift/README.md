# Kepler Drift — Unity project

The prototype's rules (`prototype/orbit-proto.js`, MISSION 3 · MARS) are being ported to Unity 6. Unity **6000.0.58f2** (same as
Birdie Smash: the GameCI image is cached), URP 2D renderer, Input System, UI Toolkit.

- `Assets/KeplerDrift/Core/` — pure C# rule engine (`OrbitEngine`, `Mission`, `Body`, `ProbeShip`). **No UnityEngine import**;
  checked without Unity: `mcs -warnaserror+ -out:/tmp/missionbot.exe tool/unity_test/MissionBot.cs unity/KeplerDrift/Assets/KeplerDrift/Core/*.cs && mono /tmp/missionbot.exe`
- `Assets/KeplerDrift/Game/` — MonoBehaviour skeleton: `GameRoot` (fixed 60 Hz step, finger -> Hold/Release/Burn), views,
  `Hud` (UI Toolkit), `Strings` (EN default + TR table for player-facing text), `Sfx` (procedural, OnAudioFilterRead),
  `Haptics`, `ScreenshotTour` (CI screenshots, `-screenshots <folder>`).
- `Assets/KeplerDrift/Shaders/Planet.shader` — URP 2D sprite: sphere normal from UV, wrap diffuse (`_SunDirection`), soft
  terminator, rim glow (`_AtmosphereColor`).
- `Assets/KeplerDrift/Editor/CiBuild.cs` — scene (`Assets/Scenes/Mission.unity`), URP asset and PlayerSettings from code;
  no `ProjectSettings.asset` in the repo. `KeplerDrift.EditorTools.CiBuild.iOS` (TestFlight) and `.Linux` (screenshot tour).
  Product name "Kepler Drift", bundle id `com.keplerdrift.app` (must match `ios/fastlane/Appfile` + `Matchfile` + `Fastfile`).

## Opening
1. Unity Hub › **Add** › `unity/KeplerDrift`. On first open the scene and URP asset are set up; if not, menu **Kepler Drift › Set up scene and settings**.
2. Set the Game window to a portrait phone ratio (e.g. 1179×2556) and press **Play**.

## Gameplay
Hold = lock onto the nearest ring (<= 150 px) · release = tangent launch (±8° toward the next target = PERFECT, chain) ·
short tap while flying = fuel (+2.2). Every extra lap while locked cuts speed by 32% (brake); soft landing at the station
below 2.6. Heat builds while locked at Venus. Stars: dock, 3/3 samples, fuel >= 2.

## Language
Code, comments and logs are English. Player-facing text lives only in `Game/Strings.cs` (English default, Turkish when the
device language is Turkish); add a key to both tables instead of writing a literal in a view.
