# Kepler Drift — sound design and visual polish (the owner 2026-11-01: "sound and design must feel satisfying too")

## Principle
Sound and effects are the game itself; simple rules, dense feedback. Every action has three
channels: **sound + haptics + visuals**, all three on the same frame. No event stays single-channel.

## Sound language
- Palette: space radio + analog synthesis. Warm low frequencies (gravity), glassy highs
  (PERFECT), broken static (damage/SIGNAL LOST). References: Outer Wilds (calm, warm),
  Alto's Odyssey (pentatonic, soft), Kerbal (radio, sense of realism).
- Music: not a loop but **generative ambient**. Pentatonic drone + sparse arpeggio; as the
  chain grows the harmony thickens (a bass an octave below comes in). No loop fatigue.
- Silence as a tool: on SIGNAL LOST everything cuts out at once, only radio hiss remains.

## Event → sound → haptic → visual
| Event | Sound | Haptic | Visual |
|---|---|---|---|
| Lock | magnetic "clank" + 90 Hz thump; orbit hum pitched by speed | light (10 ms) | ring glows, probe slides onto the ring |
| Release | doppler "whoosh" (band-pass 400→2400 Hz) | none | camera pulls back 4%, speed lines |
| PERFECT | 3-note pentatonic arpeggio + sparkle; chain ≥3 adds a bass note | medium (20 ms) | 70 ms hit-stop, gold trail, flash, zoom pulse |
| FUEL | 180 ms noise burst, low-pass 1200→300 Hz + 70 Hz thump | 15 ms | flame, FUEL count pops |
| HEAT | ticking after 0.5, interval 400→90 ms; distorted 50 Hz when burnt | — | HEAT bar amber, lens flare near Venus |
| Collision | 60 Hz thump + crackle | strong (40 ms) | hit-stop, shield blinks amber, knockback |
| SAMPLE | 880→1320 Hz double blip | — | SAMPLE count pops |
| BRAKE | retro-thruster pulse every 30 frames | — | BRAKE label |
| SOFT LANDING | 660+990 Hz chime, C5-E5-G5 fanfare, ping per star | [30,60,30] | expanding ring, 12% white flash, stars pop one by one |
| SIGNAL LOST | everything cuts, hiss opens, 2000→100 Hz power-down | 80 ms | screen goes dark, telemetry freezes |

iOS haptics: UIImpactFeedbackGenerator (light/medium/heavy) + UINotificationFeedbackGenerator
(success); Android: VibrationEffect. With Reduce Motion on: no shake/zoom, sound stays.

## Visual polish list (✓ = implemented in the prototype)
✓ hit-stop (collision, PERFECT) · ✓ trail colour and width by chain · ✓ camera pull-back on
release + speed lines · ✓ lens flare near Venus · ✓ ring + flash + sequential stars on landing
· ✓ HUD number pop on change · ✓ off-screen target arrow.
In production: real textures + terminator shader, bloom, 2–4% film grain, a single flare on sun
passes, nebula colour warming on a PERFECT chain (chain 5+), particles on the probe tail.

## Check: the "satisfaction" test
Five questions in playtesting: does the lock feel tangible, does the release "launch", is a
PERFECT noticeable (by sound with eyes closed), does a collision hurt, does landing bring relief.
If any of the five is "no", that event's three channels are retuned.
