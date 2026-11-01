# Kepler Drift

One-finger space arcade game: hold to lock onto a planet, release to fly off on the
tangent, tap while flying to burn fuel, arrive slowly. Real solar system,
mission-control HUD, every event with sound + haptic + visual.

- Design: `docs/design.md`; sound and feel: `docs/sound-and-feel.md`; research: `docs/*-research.md`
- Playable prototype (browser): `prototype/index.html`
- Unity 6 project: `unity/KeplerDrift` (rules in `Assets/KeplerDrift/Core`, Unity-free test in `tool/unity_test`)
- Working rules: `CLAUDE.md`; owner steps: `OWNER_TODO.md`

Verification without Unity:
```
mcs -out:/tmp/missionbot.exe tool/unity_test/MissionBot.cs unity/KeplerDrift/Assets/KeplerDrift/Core/*.cs && mono /tmp/missionbot.exe
```
