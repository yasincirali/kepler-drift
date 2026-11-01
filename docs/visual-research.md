# Kepler Drift — realistic space visual direction (research, 2026-11-01)

## "Real space" cues that read on a phone
- A single sun, a hard day/night line (terminator): the strongest cue for a sphere.
- A thin atmospheric glow on the lit edge: separates the planet from the black sky.
- A sparse, dim star field; a single low-saturation nebula; only one lens flare, from the sun.
- Scale cues: orbit lines, small moons, the camera pulls back at the moment of the fling.
- References: Solar Smash (photo-textured planets, 10M+ downloads), Spaceflight Simulator, Universe Sandbox, Outer Wilds (stylised but consistent lighting).

## Legal texture sources
| Source | Status | Terms |
|---|---|---|
| NASA | free, commercial use included | no logo, do not imply NASA endorsement, third-party images excluded |
| JPL/Caltech | mostly free | check each item individually |
| ESA/Hubble | CC BY 4.0 | visible "ESA/Hubble" credit (credits screen) |
| Solar System Scope textures | CC BY 4.0 | credit + licence link; confirm on the site |
| Planet Pixel Emporium | restrictive licence | do not bundle the textures in the game — DO NOT USE |
| Procedural | no risk | main path; the Godot "Starfield Parallax" shader is non-commercial, do not take it |

## Cheap techniques in a 2D engine
- Sphere normal on a sprite (from UV) + dot(N, sun) + soft terminator; one sprite, one shader.
- Atmosphere rim: (1 − dot(N, view)) × sun term.
- 3–4 parallax star layers; fBm nebula, gradient colouring; camera position as a uniform.
- Glow: additive sprites on the sun, the probe and its tail; real bloom = threshold + two-pass blur (Flame PostProcess / Unity URP 2D Bloom).
- 2–4% film grain, a single flare sprite; respect Reduce Motion.

## HUD / typography
- "Mission control": monospace telemetry (Share Tech Mono / IBM Plex Mono), 1 px lines, corner brackets, semi-transparent panels. Cyan base, amber warning; do not make the HUD monochrome.
- Neutral sans for titles (NASA 1975 standards manual feel). Never NASA logos.

Sources: nasa.gov/nasa-brand-center/images-and-media, jpl.nasa.gov/jpl-image-use-policy, esahubble.org (CC BY), doc.qt.io solar-system-scope attribution, planetpixelemporium.com/planets.html, godotshaders.com planet shader, jotson.itch.io nebula devlog, docs.flame-engine.org post_processing, hudsandguis.com (The Expanse), standardsmanual.com NASA manual.
