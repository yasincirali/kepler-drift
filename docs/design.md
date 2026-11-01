# Kepler Drift — game design (2026-11-01, the owner: "levelling + realistic space + one mechanic is not enough")

(Turkish store name may stay Yörünge.)

## Thesis
You are a small probe launched from Earth. Travelling mission to mission through the real
solar system, you learn three separate skills: **aim** (lock on, release on the tangent),
**fuel** (tap in flight for a short burn) and **soft arrival** (reach the target slowly).
The one-touch arcade feel stays, but each mission asks for the three in a different mix.
The theme is real: planets from NASA/ESA textures, a single sun, a hard day-night line,
a mission-control HUD. Each planet unlocks a **codex card** (real facts).

## Three skills (core mechanics)
| Skill | Input | What it teaches | Where |
|---|---|---|---|
| Aim | hold = lock/orbit, release = tangent | timing, PERFECT chain | every mission |
| Fuel | tap in flight = burn (5 units) | resource management, course correction | from System 1 |
| Soft arrival | enter the target below the speed threshold; extra laps = BRAKE | reading speed, patience | from late System 1 |

Counterforce that limits staying locked: **HEAT** near bodies close to the sun (a long lock
burns you). This closes off the "hold forever" strategy.

## Hazards and tools (unlocked per system, Angry Birds Space style: every world adds one new thing)
1. Moving moon (target on rails): lead your release.
2. Heat (Venus/Mercury, near a star): limited lock time.
3. Asteroid belt: find a gap on the tangent; a hit costs the shield.
4. Gas giant + ring: low orbit risky/fast, high orbit safe/slow.
5. Binary star: figure-eight orbit.
6. Black hole (System 4): the horizon kills, a whip-around speeds you up.
7. Wormhole pair: teleports while keeping speed; route puzzle.
8. Solar flare / pulsar: rhythmic death zone; hide in the shadow.
9. Rescue: pick up an astronaut/cargo, mass grows, handling changes.
10. Station docking: mid-mission refuel.
Calm variants: zero-g and nebula drag zones (so the feel changes).

## Level structure
- **System = chapter.** 12 missions + 1 "frontier" mission (combines all of the system's mechanics).
- System 1 Inner Solar System (Moon, Venus, Mars; fuel + heat + arrival) → System 2 Asteroid
  belt + Jovian moons (belt, moving moon, gas giant) → System 3 Saturn/Uranus/Neptune
  (ring, twin moons, docking) → System 4 Interstellar (Voyager: black hole, wormhole)
  → System 5 Alpha Centauri, System 6 TRAPPIST-1 (real exoplanet data).
- **3 stars per mission:** arrival · 3/3 SAMPLE · FUEL ≥ 2 (or a lap limit). The star total
  unlocks the next system (soft threshold: 20 stars from 12 missions).
- **Codex** (collection meta, the most common hybrid-casual meta in the West): each new body
  is a card; 6–8 cards should drop in the first 3–5 sessions.
- **Deep Space:** endless mode, daily seed (everyone gets the same galaxy), unlocked after System 1.
- **Light upgrade tree** (probe): fuel tank, heat shield, scanner (predicted route line),
  magnet (sample pull). Samples/stardust as currency. Archero pattern, kept light.

## Visual direction (summary of visual-research.md)
Photo-based planets (Solar System Scope CC BY / NASA), one hard terminator, thin atmosphere
rim, sparse dim stars, a single nebula, glow only on the sun and the probe.
HUD: IBM Plex Mono telemetry, cyan base + amber warning, 1 px lines and corner brackets;
neutral sans for titles. Never the NASA logo; a credits screen (ESA/Hubble, SSS).

## Monetisation
Free. Rewarded ads: "one more chance" (shield), daily fuel. One-time
"no ads + all systems". Probe skins (Voyager, comet, paper plane).
Space Frontier's ad density is a common complaint; stay below it.

## First release scope
Systems 1 + 2 (26 missions), Deep Space, 12 codex cards, 3 skins, no upgrade tree.
Engine: Unity 2D (URP: Light2D, bloom built in) or Flame (with post-process). The Birdie
experience is in Unity; recommendation: Unity.

## Open decisions (the owner)
1. Real solar system (educational, strong codex) or fictional systems?
   Recommendation: start real, System 5+ fictional/exoplanets.
2. Upgrade tree in v1? Recommendation: no, stars + codex are enough.

## English name (decided 1 Nov 2026): Kepler Drift
Candidates considered: Probe One, Slingshot Probe, Perigee (taken by a browser and a PC game), Tangent. "Orbit" is taken (Highkey Games).
