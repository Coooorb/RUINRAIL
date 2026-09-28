# Audio and Music

> **Status:** Approved V1 design specification.
> **Game language:** English.

## Weapon Identity

Each weapon class should be recognizable by sound: compact Pistol, rapid SMG, controlled Assault Rifle, heavier Battle Rifle, powerful Shotgun, sharp Sniper, electronic Blaster, Bow draw/release, Knife slash, Spear thrust, Rocket launch + explosion.

Blaster audio communicates Heat: rising energy character near high Heat, warning before Overheat, and a clear vent/cooling sound.

## Enemy Telegraphs

Dangerous attacks have recognizable audio cues but must remain understandable visually with sound off.

## Final V1 Music Scope

V1 targets **11 full music tracks**:
1. Main Menu.
2. The Shelter.
3. Ruined Metro — Exploration.
4. Ruined Metro — Combat.
5. Ruined Metro — Boss.
6. Rustworks — Exploration.
7. Rustworks — Combat.
8. Rustworks — Boss.
9. Overgrown Labs — Exploration.
10. Overgrown Labs — Combat.
11. Overgrown Labs — Boss.

The two Bosses within one biome share that biome's Boss track for V1.

**Biome themes (2026-09-28).** Each biome's three tracks share one identity, told apart by ear before any melody: mode, tempo, lead timbre and one signature layer, following the biome's ambience character. Ruined Metro: natural minor, 76/112/128 BPM (exploration/combat/boss), electric hum, a square-wave station chime with a tunnel echo, rail-joint clacks, a running train-like bass in combat. Rustworks: phrygian (flat 2nd), 66/100/116 BPM, a heavy detuned drone, anvil strikes, steam swells, a 3+3+2 piston bass in combat. Overgrown Labs: dorian (raised 6th), 84/116/132 BPM, bubbling FM-bell arpeggios, a soft shimmer and instrument clicks, a syncopated soft kick in combat. A biome keeps one root across its three tracks so exploration ↔ combat ↔ boss crossfade in key.

**Main Menu theme (2026-09-28).** The menu no longer shares the Shelter's generic bed: a calm 60 BPM, eight-bar theme of soft sine-led pads that breathe on a slow minor progression, a distant low rail hum, a faint low-passed wind, a sparse slow melody and a muffled station chime every four bars — no percussion, noise transients or bright/buzzy voices, and no louder than the bed it replaced. **The Shelter plays the same theme (2026-09-28):** its slot binds the Main Menu asset itself (no copy), so Main Menu ↔ Shelter keeps the bed playing without a restart or crossfade; the old Shelter bed file is no longer bound.

**One bed at a time.** A state change mid-crossfade hands over from where the tracks actually are: the louder track keeps fading out from its current level (it never jumps back up), the quieter one (at most half level) gives its source to the new track, and returning to the track that is still fading out resumes it rather than starting a second copy.

## Music Stingers

Short stingers are required for:
- Legendary Drop.
- Elite Encounter.
- Boss Defeated.
- Extraction Success.
- Expedition Failed.
- Level Up.
- Room Cleared (a won Combat or Elite room; bosses keep Boss Defeated): short and small, since it plays after every such room.

Stingers are not counted as full music tracks.

## Ambience

- Ruined Metro: electricity, tunnels, distant metal, old transit infrastructure.
- Rustworks: machinery, steam, industrial movement.
- Overgrown Labs: electronics, organic/bio ambience, damaged laboratory equipment.

Keep ambience below combat readability.

**Ambience beds (2026-09-28).** Each biome's loop is dark room tone, never a noise floor: its air is noise through a steep low-pass at a low cutoff, and the biome character comes from tonal layers and sparse soft events (Ruined Metro: tunnel air, electrical hum, a distant metal knock; Rustworks: engine cycle, press, a soft steam swell every six seconds; Overgrown Labs: ventilation, a faint electronic chirp, a wet drip). The first beds were filtered white noise with constant high washes; their energy above 2 kHz sat within 3–7 dB of the music and read as a constant rush. The ambience ceiling is 0.2 of the SFX gain, which keeps the reshaped beds at or below their old overall level and 15 dB or more under the music.

## SFX Coverage Rule

There is intentionally **no fixed required SFX file count**. This is a final production decision, not an unresolved design item. Every gameplay-significant action defined by the GDD must receive appropriate audio feedback; variations may be added during production for repetition control.

Legendary drops receive a distinctive reusable sound. UI sounds are subtle; purchase/confirm/cancel/failed-action feedback should be clear but not noisy.

## Ambience Level (implementation note 2026-09-20)
The AUDIO settings page carries an Ambience slider beside Master, Music and SFX. It scales the ambience bus (master × SFX × ambience), which still sits under the fixed ceiling relative to the SFX bus, so ambience can be turned down or off on its own but never rises above combat readability.
