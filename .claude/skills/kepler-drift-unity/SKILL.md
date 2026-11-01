---
name: kepler-drift-unity
description: Read first for any work on the Kepler Drift Unity 6 project (unity/KeplerDrift) — game rules (Core), view/input (Game), planet shader, sound-and-feel rule, Unity-free verification ladder, CI and TestFlight lane, Unity MCP/CLI. Not for other repos.
---

# Kepler Drift — Unity working guide

Game lives in `unity/KeplerDrift` (Unity 6000.0.58f2, URP 2D). Source rules in
`docs/design.md`, sound/feel in `docs/sound-and-feel.md`, working reference
`prototype/orbit-proto.js`.

## What goes where

| Folder (`Assets/KeplerDrift/`) | Content | Rule |
|---|---|---|
| `Core/` | Pure C#: `OrbitEngine`, `ProbeShip`, `Body`, `Mission` | **No UnityEngine import.** Deterministic, frame-based. New rule here + a check line in `tool/unity_test/MissionBot.cs`. |
| `Game/` | `GameRoot`, `ProbeView`, `BodyView`, `Hud`, `Sfx`, `Haptics`, `ScreenshotTour` | View, input (Input System), sound. No computation. |
| `Shaders/` | `Planet.shader` (sphere normal from UV, sun direction, terminator, atmosphere rim) | Planet = one sprite + this shader; textures NASA/SSS (CC BY), PPE forbidden. |
| `Editor/` | `CiBuild.cs` (iOS build, PlayerSettings from code) | `ProjectSettings.asset` is not committed. |

## Invariants
- Three skills: hold/release, tap while flying = burn, below-threshold arrival; heat limits holding.
  Numbers live in Core (ring r+46, lock range 150, release 0.055·r, perfect ±8°, fuel 5, burn +2.2,
  brake 0.68/orbit floor 0.25, landing threshold 2.6, shields 3). Change them together with MissionBot.
- **Every event on three channels:** sound (procedural, `Sfx.cs`) + haptic (`Haptics.cs`) + visual.
- Reduce motion: shake, zoom, hit-stop, star pops off; sound stays.
- HUD mono type, cyan base + amber warning, meaning never by color alone.
- Everything in the repo is English; player text through the localization table (EN default, TR).

## Verification ladder (no Unity Editor in the cloud)
1. **Core + mission bot** (every Core change):
   `mcs -out:/tmp/missionbot.exe tool/unity_test/MissionBot.cs unity/KeplerDrift/Assets/KeplerDrift/Core/*.cs && mono /tmp/missionbot.exe`
   (`apt-get install -y mono-mcs mono-runtime` if mono is missing).
2. **Game compile:** with Unity module DLLs via mono if available; otherwise step 3.
3. **Real render:** push to a `screens/<topic>` branch → `unity-screenshots.yml` builds a Linux player,
   runs the `-screenshots` tour, writes PNGs + report to the `screenshot-results` branch. Push hours apply.
4. **TestFlight:** only merges to `main` (Unity secrets required), `store_listing/whats_new.txt`.

## Unity tools (PC)
- Unity official Claude Code plugin: skills (`unity-cli`, `urp-postprocessing`, `create-2d-physics`,
  `ui-uitk`, `optimize-audio`, `implement-in-app-purchases`…) + Unity CLI; drive the open Editor through
  the CLI instead of hand-editing scene/asset files. Say so first if no Editor is reachable.
- MCP for Unity (CoplayDev): scene/hierarchy, console, test runs. Install in `OWNER_TODO.md`.
- Neither connects in the cloud; fall back to the ladder.

## Known traps (inherited from Birdie)
- Do not give a `.uss` the same Resources name as a `.uxml` (style fails to load).
- Compile + grep after every merge: a closing brace can end up inside a comment.
- iOS deployment target 15.0; if the Game Center entitlement is missing from the profile, the Fastfile strips it.
- In the CI screenshot tour return to the main menu at the end of each section, or the next section breaks.
