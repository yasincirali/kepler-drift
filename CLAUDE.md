# Claude Development Guidelines — Kepler Drift

Standalone mobile game (owner, 1 Nov 2026: "let's build a new game, in a new repo").
No code or brand relation to sandık, Harçlıklarım, Rafım/Coverline or Birdie Smash;
only the way of working is shared. Product name **Kepler Drift** (Turkish store
name may stay "Yörünge"). The project is global from day one: **everything in
the repo is English** (identifiers, comments, docs, workflows, commit messages);
player-facing text is English by default with a localization table (TR first).
Chat with the owner stays Turkish.

## Game (single source: `docs/design.md`, `docs/sound-and-feel.md`)
You are a small probe launched from Earth. **Three skills, one finger:** aim
(hold = lock onto the nearest body and orbit, release = tangent launch), fuel
(tap while flying = burn), soft arrival (enter the target below a speed
threshold; extra orbits brake). Counter-force: heat near the sun limits holding.
System = chapter (12 missions + a boundary mission), 3 stars per mission, codex
collection, Deep Space endless after System 1. Realistic space look: one sun,
hard terminator, thin atmosphere rim, mission-control HUD.
**Every event on three channels:** sound + haptic + visual (owner: "sound and
design must be satisfying too"). Single-channel events are not added.
Reference implementation: `prototype/` (HTML canvas, Mission 3 Mars; 50 automated
checks in `prototype/test.js`). Rules move to Unity through `Assets/KeplerDrift/Core`;
the prototype is frozen as the reference.

## Language and engine (decision 1 Nov 2026)
**Unity 6 (6000.0.58f2, URP 2D) + C#.** Why: realistic 2D lighting, bloom and
planet shading are built in (Light2D, post-processing); the ads/IAP/Game Center
plugin ecosystem is the widest; Birdie Smash's Unity TestFlight lane and GameCI
image are reused as-is. Flutter + Flame was rejected: too much hand-rolled work
for bloom and shaders. Version kept equal to Birdie so the CI cache is shared.

C# best practice (rules in this repo):
- `Core/` is pure C#: **no UnityEngine import**, deterministic, frame-based,
  double precision. Compiles with mono without Unity and is measured by
  `tool/unity_test/MissionBot.cs`. New rule = Core change + MissionBot check line.
- `Game/` MonoBehaviours only for view and input; computation lives in Core.
  Input System (no legacy Input), UI Toolkit (no uGUI), no TextMeshPro.
- Comments are decision records ("why", not "what"), with dates for decisions.
  Settings come from code (`Editor/CiBuild.cs`); `ProjectSettings.asset` is not committed.
- No allocations, LINQ or string concatenation in Update; object pools.
- Zero analyzer warnings (`dotnet format` / Roslyn); `[SerializeField] private`.

## Skill and MCP routing
Call the relevant skill before writing code; if none fits, say "no skill" in the summary.

| Task | Skill | MCP |
|---|---|---|
| Unity game / physics / shader / UI / iOS lane (`unity/`) | `kepler-drift-unity` (first), Unity official plugin skills (`unity-cli`, `urp-postprocessing`, `create-2d-physics`, `ui-uitk`, `optimize-audio`, `implement-in-app-purchases`…) | `unity` (official plugin, Unity CLI; open Editor on PC) and/or `MCP for Unity` (CoplayDev, PC) |
| Game feel, animation timing | `animate`, `apple-design`, `emil-design-eng` | — |
| Menu / HUD design | `ui-ux-pro-max` | `ui-ux-pro-mcp` |
| Code discovery (every task) | — | `codebase-memory-mcp` first (`search_graph`, `trace_path`), then grep |
| Review / bugs / decisions | `code-reviewer`, `debugging-wizard`, `the-fool`, `architecture-designer` | — |

No Unity Editor in the cloud: the `unity` MCP and CLI do not connect there; that is
expected. Verification ladder is in the `kepler-drift-unity` skill. Repo skills live in
`.claude/skills/`; Unity's official skills are installed as a plugin (`tool/cloud_setup.sh`).

## Way of working
- Make changes without waiting for approval; summarize in Turkish at the end. The owner reviews in batches.
- **Push hours:** weekdays 09:00–18:30 TR no push / PR / merge. No direct push to
  `main`; merging to `main` means a TestFlight build (`ios-testflight.yml`).
- Finished, tested work merges on green and ships to TestFlight; tester notes in
  `store_listing/whats_new.txt`.
- Every UI change gets a HIG check (touch ≥ 44 pt, text scaling, contrast, meaning
  never by color alone, safe area, reduce motion: shake/zoom off, sound stays) written
  into the PR and the before/after artifact.
- Secrets never in the repo (Unity license, p8/p12, keystore, ASC key). No secrets in
  Actions logs. The repo is public.
- Texture licenses: NASA (no logos), ESA/Hubble and Solar System Scope CC BY (credits
  screen). Planet Pixel Emporium is **not used**. Credits in `docs/visual-research.md`.
- Owner-only steps go to `OWNER_TODO.md`; deferred code decisions to `TECHNICAL_DEBT.md`.
