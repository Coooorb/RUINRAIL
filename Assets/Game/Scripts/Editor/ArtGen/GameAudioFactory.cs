using System;
using System.Collections.Generic;
using UnityEngine;
using static RuinRail.EditorTools.ArtGen.AudioSynth;

namespace RuinRail.EditorTools.ArtGen
{
    /// <summary>
    /// Synthesises the 53 SFX, 11 music tracks, 7 stingers and 3 ambience loops the manifest requires.
    ///
    /// The mix targets FINAL_AUTONOMOUS_COMPLETION_PROMPT_V2 section F: combat-critical cues sit loudest, UI is
    /// subordinate, ambience quieter still, and boss cues are voiced differently from normal enemies so they cannot
    /// be confused. Peak levels are set per family here and every clip is normalised on write, so nothing clips.
    /// </summary>
    public static class GameAudioFactory
    {
        /// <summary>Per-family peak targets. Combat readability first, UI and ambience well below it (spec F).</summary>
        public static float PeakFor(string bus) => bus switch
        {
            "Weapons" => 0.88f,
            "Enemies" => 0.84f,
            "Player" => 0.82f,
            "Loot" => 0.68f,
            "World" => 0.66f,
            "Ui" => 0.48f,
            "Music" => 0.62f,
            "Ambience" => 0.34f,
            _ => 0.7f
        };

        // ---------------- SFX ----------------

        public static Clip BuildSfx(string eventId)
        {
            var rng = new System.Random(StableSeed(eventId));
            return eventId switch
            {
                "weapon.fire.pistol" => Gunshot(rng, 0.20f, 190f, 0.9f, 2600f),
                "weapon.fire.smg" => Gunshot(rng, 0.14f, 230f, 0.7f, 3200f),
                "weapon.fire.assault_rifle" => Gunshot(rng, 0.20f, 165f, 1.0f, 2900f),
                "weapon.fire.battle_rifle" => Gunshot(rng, 0.26f, 130f, 1.2f, 2400f),
                "weapon.fire.shotgun" => Gunshot(rng, 0.38f, 85f, 1.7f, 1700f),
                "weapon.fire.sniper" => Gunshot(rng, 0.46f, 105f, 1.5f, 2100f, tail: 0.30f),
                "weapon.fire.blaster" => Blaster(rng, 0.26f),
                "weapon.bow.draw" => BowDraw(rng),
                "weapon.bow.release" => BowRelease(rng),
                "weapon.melee.knife_slash" => Whoosh(rng, 0.17f, 2400f),
                "weapon.melee.spear_thrust" => Whoosh(rng, 0.22f, 1500f),
                "weapon.fire.rocket_launch" => RocketLaunch(rng),
                "weapon.rocket.explosion" => Explosion(rng, 1.15f),
                "weapon.reload" => Reload(rng),
                "weapon.dry_fire" => Click(rng, 0.09f, 1500f),
                "weapon.blaster.heat_rising" => HeatLoop(rng, 1.6f, false),
                "weapon.blaster.overheat_warning" => WarningBeep(rng, 2, 880f),
                "weapon.blaster.overheat" => Overheat(rng),
                "weapon.blaster.vent" => Vent(rng),
                "weapon.legendary_special" => LegendarySpecial(rng),

                "enemy.telegraph" => Telegraph(rng, 300f, 0.34f),
                "enemy.telegraph.elite" => Telegraph(rng, 220f, 0.44f),
                "enemy.telegraph.boss" => BossTelegraph(rng),
                "enemy.hit" => Impact(rng, 0.17f, 420f, 0.6f),
                "enemy.stagger" => Impact(rng, 0.26f, 250f, 0.9f),
                "enemy.death" => EnemyDeath(rng),
                "enemy.elite.spawn" => EliteSpawn(rng),
                "enemy.boss.phase" => BossPhase(rng),

                "player.hit" => PlayerHit(rng),
                "player.dash" => Whoosh(rng, 0.20f, 1100f),
                "player.downed" => Downed(rng),
                "player.revive.start" => ReviveStart(rng),
                "player.revive.complete" => ReviveComplete(rng),
                "player.death" => PlayerDeath(rng),
                "player.consumable.use" => ConsumableUse(rng),
                "player.heal" => Heal(rng),
                "player.hazard" => Hazard(rng),

                "loot.pickup.item" => Pickup(rng, 0),
                "loot.pickup.coins" => CoinPickup(rng),
                "loot.drop.common" => LootDrop(rng, 0),
                "loot.drop.uncommon" => LootDrop(rng, 1),
                "loot.drop.rare" => LootDrop(rng, 2),
                "loot.drop.epic" => LootDrop(rng, 3),
                "loot.drop.legendary" => LootDrop(rng, 4),
                "loot.chest.open" => ChestOpen(rng),

                "world.door.open" => DoorOpen(rng),
                "world.transit.depart" => TransitDepart(rng),
                "world.merchant.open" => MerchantOpen(rng),

                "ui.navigate" => UiBlip(rng, 520f, 0.05f, 0.30f),
                "ui.confirm" => UiConfirm(rng),
                "ui.cancel" => UiBlip(rng, 300f, 0.08f, 0.32f),
                "ui.failure" => UiFailure(rng),
                "ui.purchase" => UiPurchase(rng),
                _ => Click(rng, 0.08f, 1000f)
            };
        }

        // ---- weapon voices ----

        /// <summary>
        /// A gunshot as three layers: a transient crack, a pitched body that drops fast, and a filtered noise tail.
        /// Class character comes from body frequency, decay and filter cutoff rather than from different algorithms,
        /// so the whole arsenal sounds like one family of weapons (spec 27).
        /// </summary>
        private static Clip Gunshot(System.Random rng, float dur, float bodyHz, float weight, float cutoff, float tail = 0.12f)
        {
            var c = new Clip(dur + tail);
            var lp = new LowPass();
            var hp = new HighPass();
            var res = new Resonator();
            var n = c.Length;

            for (var i = 0; i < n; i++)
            {
                var t = (float)i / SampleRate;
                var noise = (float)(rng.NextDouble() * 2.0 - 1.0);

                // Transient crack.
                var crack = hp.Process(noise, 1800f) * Punch(t, 0.012f) * 0.9f;
                // Pitched body with a fast downward sweep — the "thump".
                var sweep = bodyHz * Mathf.Exp(-t * 26f) + bodyHz * 0.35f;
                var body = Sine(sweep * t) * Punch(t, 0.055f * weight) * weight;
                // Filtered noise tail, the air/room of the shot.
                var air = lp.Process(noise, cutoff) * Punch(t, 0.09f + tail) * 0.35f;
                // A little metallic ring from the action.
                var ring = res.Process(noise * 0.25f, 1400f + bodyHz, 14f) * Punch(t, 0.05f) * 0.25f;

                var s = crack + body + air + ring;
                c.Add(i, s, s * 0.97f);
            }
            return c;
        }

        private static Clip Blaster(System.Random rng, float dur)
        {
            var c = new Clip(dur + 0.2f);
            var lp = new LowPass();
            for (var i = 0; i < c.Length; i++)
            {
                var t = (float)i / SampleRate;
                // Downward-swept detuned saws: energy discharge, industrial rather than space-opera.
                var f = 900f * Mathf.Exp(-t * 9f) + 120f;
                var core = Saw(f * t) * 0.5f + Saw(f * 1.005f * t) * 0.5f;
                var zap = lp.Process(core, 2600f - 1500f * Mathf.Clamp01(t * 4f));
                var env = Punch(t, 0.075f);
                var sizzle = (float)(rng.NextDouble() * 2 - 1) * Punch(t, 0.04f) * 0.3f;
                var s = (zap * env + sizzle) * 0.9f;
                c.Add(i, s, s * 0.95f);
            }
            return c;
        }

        private static Clip BowDraw(System.Random rng)
        {
            var c = new Clip(0.55f);
            var lp = new LowPass();
            for (var i = 0; i < c.Length; i++)
            {
                var t = (float)i / SampleRate;
                var creak = lp.Process((float)(rng.NextDouble() * 2 - 1), 420f + 500f * t);
                // Slow rising tension with a periodic fibre creak.
                var env = Mathf.Clamp01(t / 0.5f) * 0.55f;
                var grain = Mathf.Sin(t * 42f) > 0.6f ? 1.6f : 1f;
                var s = creak * env * grain;
                c.Add(i, s, s * 0.9f);
            }
            return c;
        }

        private static Clip BowRelease(System.Random rng)
        {
            var c = new Clip(0.38f);
            var hp = new HighPass();
            var res = new Resonator();
            for (var i = 0; i < c.Length; i++)
            {
                var t = (float)i / SampleRate;
                var noise = (float)(rng.NextDouble() * 2 - 1);
                var thwack = hp.Process(noise, 900f) * Punch(t, 0.02f);
                var stringRing = res.Process(noise * 0.3f, 320f, 26f) * Punch(t, 0.13f);
                var s = thwack * 0.8f + stringRing * 0.7f;
                c.Add(i, s, s * 0.96f);
            }
            return c;
        }

        private static Clip Whoosh(System.Random rng, float dur, float cutoff)
        {
            var c = new Clip(dur);
            var lp = new LowPass();
            var hp = new HighPass();
            for (var i = 0; i < c.Length; i++)
            {
                var t = (float)i / SampleRate;
                var p = t / dur;
                var noise = (float)(rng.NextDouble() * 2 - 1);
                // Band sweeps up then down: the doppler of something passing the ear.
                var band = hp.Process(lp.Process(noise, cutoff * (0.4f + p)), 300f + 900f * p);
                var env = Mathf.Sin(p * Mathf.PI);
                var s = band * env * 0.8f;
                c.Add(i, s * (1f - p * 0.4f), s * (0.6f + p * 0.4f));   // sweeps across the stereo field
            }
            return c;
        }

        private static Clip RocketLaunch(System.Random rng)
        {
            var c = new Clip(0.85f);
            var lp = new LowPass();
            for (var i = 0; i < c.Length; i++)
            {
                var t = (float)i / SampleRate;
                var noise = (float)(rng.NextDouble() * 2 - 1);
                var ignition = noise * Punch(t, 0.03f) * 0.9f;
                var roar = lp.Process(noise, 900f + 400f * Mathf.Sin(t * 30f)) * Mathf.Exp(-t * 2.4f) * 0.75f;
                var thrust = Sine(70f * t) * Mathf.Exp(-t * 3f) * 0.45f;
                var s = ignition + roar + thrust;
                c.Add(i, s, s * 0.94f);
            }
            return c;
        }

        private static Clip Explosion(System.Random rng, float dur)
        {
            var c = new Clip(dur);
            var lp = new LowPass();
            var lp2 = new LowPass();
            for (var i = 0; i < c.Length; i++)
            {
                var t = (float)i / SampleRate;
                var noise = (float)(rng.NextDouble() * 2 - 1);
                var crack = noise * Punch(t, 0.02f);
                var boom = Sine((55f * Mathf.Exp(-t * 6f) + 32f) * t) * Punch(t, 0.28f) * 1.2f;
                var rumble = lp.Process(noise, 220f) * Punch(t, 0.55f) * 0.8f;
                var debris = lp2.Process(noise, 3000f) * Punch(t, 0.38f) * 0.25f;
                var s = crack + boom + rumble + debris;
                c.Add(i, s, s * 0.92f);
            }
            return c;
        }

        private static Clip Reload(System.Random rng)
        {
            var c = new Clip(0.72f);
            // Three mechanical events: magazine out, magazine in, bolt. Distinct positions so it reads as a sequence.
            foreach (var (at, freq, decay, gain) in new[] { (0.02f, 900f, 0.05f, 0.7f), (0.30f, 620f, 0.07f, 0.9f), (0.55f, 1400f, 0.04f, 0.8f) })
                MechanicalClick(c, rng, at, freq, decay, gain);
            return c;
        }

        private static void MechanicalClick(Clip c, System.Random rng, float at, float freq, float decay, float gain)
        {
            var start = Mathf.RoundToInt(at * SampleRate);
            var res = new Resonator();
            var hp = new HighPass();
            var n = Mathf.RoundToInt(0.2f * SampleRate);
            for (var i = 0; i < n; i++)
            {
                var t = (float)i / SampleRate;
                var noise = (float)(rng.NextDouble() * 2 - 1);
                var body = res.Process(noise, freq, 18f) * Punch(t, decay);
                var tick = hp.Process(noise, 3000f) * Punch(t, 0.006f) * 0.6f;
                var s = (body + tick) * gain;
                c.Add(start + i, s, s * 0.95f);
            }
        }

        private static Clip Click(System.Random rng, float dur, float freq)
        {
            var c = new Clip(dur);
            MechanicalClick(c, rng, 0f, freq, 0.02f, 0.8f);
            return c;
        }

        private static Clip HeatLoop(System.Random rng, float dur, bool critical)
        {
            var c = new Clip(dur);
            var lp = new LowPass();
            for (var i = 0; i < c.Length; i++)
            {
                var t = (float)i / SampleRate;
                var noise = (float)(rng.NextDouble() * 2 - 1);
                // Rising energy bed: a pitched hum plus filtered steam.
                var hum = Saw(180f * t) * 0.3f + Saw(181.3f * t) * 0.3f;
                var steam = lp.Process(noise, critical ? 3000f : 1600f) * 0.4f;
                var s = (hum * 0.5f + steam) * 0.55f;
                c.Add(i, s, s * 0.9f);
            }
            c.MakeSeamless(0.25f);
            return c;
        }

        private static Clip WarningBeep(System.Random rng, int beeps, float freq)
        {
            var c = new Clip(beeps * 0.22f + 0.1f);
            for (var b = 0; b < beeps; b++)
            {
                var start = Mathf.RoundToInt(b * 0.22f * SampleRate);
                var n = Mathf.RoundToInt(0.13f * SampleRate);
                for (var i = 0; i < n; i++)
                {
                    var t = (float)i / SampleRate;
                    var env = Adsr(t, 0.13f, 0.004f, 0.02f, 0.75f, 0.05f);
                    // Square gives it an instrument-panel urgency a sine would not have.
                    var s = Square(freq * t, 0.45f) * env * 0.55f;
                    c.Add(start + i, s, s);
                }
            }
            return c;
        }

        private static Clip Overheat(System.Random rng)
        {
            var c = new Clip(0.9f);
            var lp = new LowPass();
            for (var i = 0; i < c.Length; i++)
            {
                var t = (float)i / SampleRate;
                var noise = (float)(rng.NextDouble() * 2 - 1);
                var alarm = Square(660f * t + Mathf.Sin(t * 12f) * 0.3f, 0.5f) * Punch(t, 0.35f) * 0.5f;
                var hiss = lp.Process(noise, 5200f) * Punch(t, 0.45f) * 0.55f;
                var drop = Sine((240f * Mathf.Exp(-t * 4f) + 60f) * t) * Punch(t, 0.3f) * 0.6f;
                var s = alarm + hiss + drop;
                c.Add(i, s, s * 0.93f);
            }
            return c;
        }

        private static Clip Vent(System.Random rng)
        {
            var c = new Clip(0.75f);
            var lp = new LowPass();
            var hp = new HighPass();
            for (var i = 0; i < c.Length; i++)
            {
                var t = (float)i / SampleRate;
                var noise = (float)(rng.NextDouble() * 2 - 1);
                // Pressure release: bright hiss decaying to a low breath.
                var jet = hp.Process(lp.Process(noise, 7000f - 5000f * Mathf.Clamp01(t * 1.6f)), 400f);
                var env = Mathf.Min(1f, t * 40f) * Mathf.Exp(-t * 3.2f);
                var s = jet * env * 0.85f;
                c.Add(i, s, s * 0.88f);
            }
            return c;
        }

        private static Clip LegendarySpecial(System.Random rng)
        {
            var c = new Clip(1.1f);
            var lp = new LowPass();
            for (var i = 0; i < c.Length; i++)
            {
                var t = (float)i / SampleRate;
                var noise = (float)(rng.NextDouble() * 2 - 1);
                // Charge up, then release: rising fifth into a bright discharge.
                var charge = Saw(Note(-12) * Mathf.Pow(2f, t * 1.2f) * t) * Mathf.Clamp01(t / 0.4f) * Mathf.Exp(-Mathf.Max(0f, t - 0.42f) * 9f);
                var release = t > 0.4f ? lp.Process(noise, 6000f) * Punch(t - 0.4f, 0.14f) * 0.7f : 0f;
                var boom = t > 0.4f ? Sine(90f * (t - 0.4f)) * Punch(t - 0.4f, 0.2f) * 0.8f : 0f;
                var s = charge * 0.45f + release + boom;
                c.Add(i, s, s * 0.95f);
            }
            return c;
        }

        // ---- enemy voices ----

        private static Clip Telegraph(System.Random rng, float freq, float dur)
        {
            var c = new Clip(dur);
            for (var i = 0; i < c.Length; i++)
            {
                var t = (float)i / SampleRate;
                // Rising pitch: the sound of something winding up. Unmistakably a warning.
                var f = freq * (1f + t / dur * 0.7f);
                var env = Adsr(t, dur, 0.02f, 0.05f, 0.8f, 0.09f);
                var s = (Tri(f * t) * 0.6f + Square(f * 0.5f * t, 0.3f) * 0.25f) * env * 0.7f;
                c.Add(i, s, s * 0.95f);
            }
            return c;
        }

        private static Clip BossTelegraph(System.Random rng)
        {
            var c = new Clip(0.72f);
            var lp = new LowPass();
            for (var i = 0; i < c.Length; i++)
            {
                var t = (float)i / SampleRate;
                var noise = (float)(rng.NextDouble() * 2 - 1);
                // Boss cue is voiced an octave down with a sub layer, so it can never be mistaken for a normal enemy.
                var f = 110f * (1f + t * 0.9f);
                var horn = Saw(f * t) * 0.4f + Saw(f * 1.01f * t) * 0.4f;
                var sub = Sine(f * 0.5f * t) * 0.5f;
                var grit = lp.Process(noise, 700f) * 0.3f;
                var env = Adsr(t, 0.72f, 0.05f, 0.1f, 0.85f, 0.2f);
                var s = (horn * 0.5f + sub + grit) * env * 0.8f;
                c.Add(i, s, s * 0.97f);
            }
            return c;
        }

        private static Clip Impact(System.Random rng, float dur, float freq, float weight)
        {
            var c = new Clip(dur);
            var lp = new LowPass();
            var res = new Resonator();
            for (var i = 0; i < c.Length; i++)
            {
                var t = (float)i / SampleRate;
                var noise = (float)(rng.NextDouble() * 2 - 1);
                var thud = Sine((freq * Mathf.Exp(-t * 20f) + freq * 0.4f) * t) * Punch(t, 0.05f * weight) * weight;
                var flesh = lp.Process(noise, 1200f) * Punch(t, 0.045f) * 0.6f;
                var ring = res.Process(noise * 0.2f, freq * 3f, 10f) * Punch(t, 0.06f) * 0.3f;
                var s = thud + flesh + ring;
                c.Add(i, s, s * 0.95f);
            }
            return c;
        }

        private static Clip EnemyDeath(System.Random rng)
        {
            var c = new Clip(0.62f);
            var lp = new LowPass();
            for (var i = 0; i < c.Length; i++)
            {
                var t = (float)i / SampleRate;
                var noise = (float)(rng.NextDouble() * 2 - 1);
                // Falling pitch plus a collapse: something stops working and hits the floor.
                var fall = Saw((260f * Mathf.Exp(-t * 3.2f) + 40f) * t) * Mathf.Exp(-t * 2.6f) * 0.5f;
                var collapse = t > 0.22f ? lp.Process(noise, 900f) * Punch(t - 0.22f, 0.1f) * 0.7f : 0f;
                var s = fall + collapse;
                c.Add(i, s, s * 0.93f);
            }
            return c;
        }

        private static Clip EliteSpawn(System.Random rng)
        {
            var c = new Clip(1.0f);
            var lp = new LowPass();
            for (var i = 0; i < c.Length; i++)
            {
                var t = (float)i / SampleRate;
                var noise = (float)(rng.NextDouble() * 2 - 1);
                var swell = lp.Process(noise, 300f + 2200f * Mathf.Clamp01(t)) * Mathf.Clamp01(t / 0.6f) * 0.5f;
                var horn = Saw(Note(-17) * t) * 0.3f + Saw(Note(-10) * t) * 0.25f;
                var env = Adsr(t, 1.0f, 0.3f, 0.2f, 0.6f, 0.3f);
                var s = (swell + horn * 0.6f) * env * 0.8f;
                c.Add(i, s, s * 0.96f);
            }
            return c;
        }

        private static Clip BossPhase(System.Random rng)
        {
            var c = new Clip(1.5f);
            var lp = new LowPass();
            for (var i = 0; i < c.Length; i++)
            {
                var t = (float)i / SampleRate;
                var noise = (float)(rng.NextDouble() * 2 - 1);
                var sub = Sine(45f * t) * Mathf.Clamp01(t / 0.3f) * Mathf.Exp(-Mathf.Max(0f, t - 0.6f) * 2.2f);
                var stab = t < 0.5f ? (Saw(Note(-24) * t) + Saw(Note(-19) * t)) * 0.3f * Punch(t, 0.3f) : 0f;
                var rise = lp.Process(noise, 400f + 3000f * Mathf.Clamp01(t / 0.9f)) * Mathf.Clamp01(t / 0.9f) * 0.35f;
                var s = sub * 0.9f + stab + rise;
                c.Add(i, s, s * 0.97f);
            }
            return c;
        }

        // ---- player voices ----

        private static Clip PlayerHit(System.Random rng)
        {
            var c = new Clip(0.34f);
            var lp = new LowPass();
            for (var i = 0; i < c.Length; i++)
            {
                var t = (float)i / SampleRate;
                var noise = (float)(rng.NextDouble() * 2 - 1);
                // Dull body impact plus a brief ear-ring, so being hit is felt rather than just heard.
                var thud = Sine((140f * Mathf.Exp(-t * 14f) + 55f) * t) * Punch(t, 0.09f) * 1.1f;
                var muffled = lp.Process(noise, 700f) * Punch(t, 0.06f) * 0.6f;
                var ring = Sine(2400f * t) * Punch(t, 0.22f) * 0.12f;
                var s = thud + muffled + ring;
                c.Add(i, s, s * 0.98f);
            }
            return c;
        }

        private static Clip Downed(System.Random rng)
        {
            var c = new Clip(1.2f);
            var lp = new LowPass();
            for (var i = 0; i < c.Length; i++)
            {
                var t = (float)i / SampleRate;
                var noise = (float)(rng.NextDouble() * 2 - 1);
                var drop = Sine((180f * Mathf.Exp(-t * 2.4f) + 38f) * t) * Mathf.Exp(-t * 1.6f) * 0.9f;
                var breath = lp.Process(noise, 500f) * Mathf.Abs(Mathf.Sin(t * 3.2f)) * 0.3f;
                var s = drop + breath;
                c.Add(i, s, s * 0.95f);
            }
            return c;
        }

        private static Clip ReviveStart(System.Random rng)
        {
            var c = new Clip(0.9f);
            for (var i = 0; i < c.Length; i++)
            {
                var t = (float)i / SampleRate;
                // Steady pulsing tone: something is being worked on, and it is still in progress.
                var pulse = Mathf.Sin(t * Mathf.PI * 4f) * 0.5f + 0.5f;
                var s = Tri(Note(-5) * t) * pulse * 0.45f;
                c.Add(i, s, s * 0.94f);
            }
            c.MakeSeamless(0.2f);
            return c;
        }

        private static Clip ReviveComplete(System.Random rng)
        {
            var c = new Clip(0.85f);
            // Rising major third: unambiguously a good outcome.
            PlayNote(c, 0.00f, 0.30f, Note(-5), 0.45f, Tri);
            PlayNote(c, 0.16f, 0.34f, Note(-1), 0.42f, Tri);
            PlayNote(c, 0.32f, 0.48f, Note(2), 0.48f, Tri);
            return c;
        }

        private static Clip PlayerDeath(System.Random rng)
        {
            var c = new Clip(1.9f);
            var lp = new LowPass();
            for (var i = 0; i < c.Length; i++)
            {
                var t = (float)i / SampleRate;
                var noise = (float)(rng.NextDouble() * 2 - 1);
                var fall = Saw((200f * Mathf.Exp(-t * 1.5f) + 28f) * t) * Mathf.Exp(-t * 1.0f) * 0.55f;
                var wash = lp.Process(noise, 1800f * Mathf.Exp(-t * 1.2f) + 150f) * Mathf.Exp(-t * 0.9f) * 0.4f;
                var toll = t > 0.25f ? Sine(Note(-29) * (t - 0.25f)) * Punch(t - 0.25f, 0.8f) * 0.6f : 0f;
                var s = fall + wash + toll;
                c.Add(i, s, s * 0.96f);
            }
            return c;
        }

        private static Clip ConsumableUse(System.Random rng)
        {
            var c = new Clip(0.5f);
            var hp = new HighPass();
            for (var i = 0; i < c.Length; i++)
            {
                var t = (float)i / SampleRate;
                var noise = (float)(rng.NextDouble() * 2 - 1);
                // Snap-open then a short pressurised release.
                var snap = hp.Process(noise, 2600f) * Punch(t, 0.012f) * 0.8f;
                var hiss = t > 0.05f ? hp.Process(noise, 1500f) * Punch(t - 0.05f, 0.12f) * 0.5f : 0f;
                var s = snap + hiss;
                c.Add(i, s, s * 0.95f);
            }
            return c;
        }

        private static Clip Heal(System.Random rng)
        {
            var c = new Clip(0.8f);
            // Warm ascending pair, quieter than combat cues so it never masks them.
            PlayNote(c, 0.00f, 0.42f, Note(-8), 0.32f, Sine, 0.04f, 0.1f, 0.7f, 0.25f);
            PlayNote(c, 0.14f, 0.48f, Note(-1), 0.30f, Sine, 0.05f, 0.1f, 0.7f, 0.3f);
            PlayNote(c, 0.26f, 0.5f, Note(4), 0.24f, Tri, 0.06f, 0.1f, 0.6f, 0.3f);
            return c;
        }

        private static Clip Hazard(System.Random rng)
        {
            var c = new Clip(0.45f);
            var hp = new HighPass();
            for (var i = 0; i < c.Length; i++)
            {
                var t = (float)i / SampleRate;
                var noise = (float)(rng.NextDouble() * 2 - 1);
                var sizzle = hp.Process(noise, 2200f) * Punch(t, 0.16f) * 0.7f;
                var buzz = Square(140f * t, 0.3f) * Punch(t, 0.1f) * 0.35f;
                var s = sizzle + buzz;
                c.Add(i, s, s * 0.93f);
            }
            return c;
        }

        // ---- loot and world ----

        private static Clip Pickup(System.Random rng, int tier)
        {
            var c = new Clip(0.34f);
            PlayNote(c, 0f, 0.14f, Note(4 + tier * 2), 0.34f, Tri, 0.003f, 0.05f, 0.5f, 0.07f);
            PlayNote(c, 0.07f, 0.18f, Note(11 + tier * 2), 0.28f, Tri, 0.003f, 0.05f, 0.5f, 0.09f);
            return c;
        }

        private static Clip CoinPickup(System.Random rng)
        {
            var c = new Clip(0.4f);
            var res = new Resonator();
            // Struck metal token: two close resonances, not a fantasy chime.
            for (var i = 0; i < c.Length; i++)
            {
                var t = (float)i / SampleRate;
                var noise = (float)(rng.NextDouble() * 2 - 1);
                var strike = res.Process(noise * 0.4f, 2100f, 40f) * Punch(t, 0.13f);
                var second = Sine(3150f * t) * Punch(t, 0.09f) * 0.4f;
                var s = (strike + second) * 0.6f;
                c.Add(i, s, s * 0.9f);
            }
            return c;
        }

        /// <summary>Loot drops climb in pitch and length with rarity, so value is audible before the item is read.</summary>
        private static Clip LootDrop(System.Random rng, int rarity)
        {
            var dur = 0.3f + rarity * 0.16f;
            var c = new Clip(dur + 0.25f);
            var root = -3 + rarity * 2;
            PlayNote(c, 0.00f, dur * 0.6f, Note(root), 0.32f + rarity * 0.03f, Tri, 0.005f, 0.06f, 0.6f, 0.12f);
            PlayNote(c, 0.06f, dur * 0.7f, Note(root + 7), 0.28f + rarity * 0.03f, Tri, 0.006f, 0.06f, 0.6f, 0.14f);
            if (rarity >= 3) PlayNote(c, 0.14f, dur * 0.8f, Note(root + 12), 0.26f, Sine, 0.01f, 0.08f, 0.6f, 0.2f);
            if (rarity >= 4)
            {
                // Legendary gets a shimmering upper octave, the loudest of the drop family (spec 27).
                PlayNote(c, 0.22f, dur * 0.9f, Note(root + 19), 0.24f, Sine, 0.02f, 0.1f, 0.5f, 0.3f);
                PlayNote(c, 0.30f, dur * 0.8f, Note(root + 24), 0.18f, Sine, 0.03f, 0.1f, 0.4f, 0.3f);
            }
            return c;
        }

        private static Clip ChestOpen(System.Random rng)
        {
            var c = new Clip(0.95f);
            MechanicalClick(c, rng, 0.0f, 700f, 0.05f, 0.8f);     // latch
            var lp = new LowPass();
            for (var i = 0; i < c.Length; i++)
            {
                var t = (float)i / SampleRate;
                if (t < 0.12f) continue;
                var noise = (float)(rng.NextDouble() * 2 - 1);
                var creak = lp.Process(noise, 600f + 900f * (t - 0.12f)) * Mathf.Exp(-(t - 0.12f) * 3f) * 0.45f;
                var s = creak;
                c.Add(i, s, s * 0.92f);
            }
            MechanicalClick(c, rng, 0.62f, 380f, 0.09f, 0.7f);    // lid settles
            return c;
        }

        private static Clip DoorOpen(System.Random rng)
        {
            var c = new Clip(1.1f);
            var lp = new LowPass();
            MechanicalClick(c, rng, 0f, 520f, 0.04f, 0.7f);
            for (var i = 0; i < c.Length; i++)
            {
                var t = (float)i / SampleRate;
                if (t < 0.1f) continue;
                var noise = (float)(rng.NextDouble() * 2 - 1);
                // Servo slide: filtered noise with a pitched motor hum that stops at the end of travel.
                var travel = Mathf.Clamp01((t - 0.1f) / 0.7f);
                var motor = Saw(90f * t) * (travel < 1f ? 0.25f : 0f);
                var slide = lp.Process(noise, 1200f) * (travel < 1f ? 0.4f : 0f);
                var s = motor + slide;
                c.Add(i, s, s * 0.9f);
            }
            MechanicalClick(c, rng, 0.85f, 300f, 0.07f, 0.8f);
            return c;
        }

        private static Clip TransitDepart(System.Random rng)
        {
            var c = new Clip(2.4f);
            var lp = new LowPass();
            for (var i = 0; i < c.Length; i++)
            {
                var t = (float)i / SampleRate;
                var noise = (float)(rng.NextDouble() * 2 - 1);
                // Heavy machine building speed, with a rail clatter that accelerates.
                var ramp = Mathf.Clamp01(t / 1.8f);
                var motor = Saw((40f + 55f * ramp) * t) * 0.35f + Saw((40.6f + 55f * ramp) * t) * 0.3f;
                var rumble = lp.Process(noise, 260f) * (0.3f + 0.4f * ramp);
                var clatterRate = 3.5f + 7f * ramp;
                var clatter = Mathf.Repeat(t * clatterRate, 1f) < 0.06f ? noise * 0.5f : 0f;
                var s = (motor * 0.5f + rumble + clatter * 0.6f) * Mathf.Min(1f, t * 3f);
                c.Add(i, s, s * 0.95f);
            }
            return c;
        }

        private static Clip MerchantOpen(System.Random rng)
        {
            var c = new Clip(0.75f);
            MechanicalClick(c, rng, 0f, 880f, 0.03f, 0.6f);
            // Terminal powering up: a short rising arpeggio on a synthetic timbre.
            PlayNote(c, 0.08f, 0.22f, Note(-8), 0.3f, p => Square(p), 0.004f, 0.04f, 0.5f, 0.08f);
            PlayNote(c, 0.20f, 0.24f, Note(-1), 0.28f, p => Square(p), 0.004f, 0.04f, 0.5f, 0.1f);
            PlayNote(c, 0.32f, 0.34f, Note(4), 0.26f, Tri, 0.006f, 0.06f, 0.55f, 0.16f);
            return c;
        }

        // ---- UI (deliberately the quietest family) ----

        private static Clip UiBlip(System.Random rng, float freq, float dur, float gain)
        {
            var c = new Clip(dur + 0.05f);
            PlayNote(c, 0f, dur, freq, gain, p => Square(p), 0.002f, 0.02f, 0.4f, 0.03f);
            return c;
        }

        private static Clip UiConfirm(System.Random rng)
        {
            var c = new Clip(0.28f);
            PlayNote(c, 0f, 0.09f, Note(0), 0.30f, p => Square(p), 0.002f, 0.02f, 0.45f, 0.03f);
            PlayNote(c, 0.07f, 0.14f, Note(7), 0.28f, p => Square(p), 0.002f, 0.02f, 0.45f, 0.05f);
            return c;
        }

        private static Clip UiFailure(System.Random rng)
        {
            var c = new Clip(0.34f);
            // Descending minor second: reads as refusal without being harsh.
            PlayNote(c, 0f, 0.13f, Note(-4), 0.30f, p => Square(p), 0.002f, 0.03f, 0.4f, 0.05f);
            PlayNote(c, 0.10f, 0.18f, Note(-6), 0.30f, p => Square(p), 0.002f, 0.03f, 0.4f, 0.07f);
            return c;
        }

        private static Clip UiPurchase(System.Random rng)
        {
            var c = new Clip(0.45f);
            var res = new Resonator();
            for (var i = 0; i < Mathf.RoundToInt(0.25f * SampleRate); i++)
            {
                var t = (float)i / SampleRate;
                var noise = (float)(rng.NextDouble() * 2 - 1);
                var s = res.Process(noise * 0.3f, 1800f, 35f) * Punch(t, 0.1f) * 0.45f;
                c.Add(i, s, s * 0.9f);
            }
            PlayNote(c, 0.12f, 0.26f, Note(4), 0.26f, Tri, 0.004f, 0.05f, 0.5f, 0.12f);
            return c;
        }

        // ---------------- ambience ----------------

        public static Clip BuildAmbience(string biome)
        {
            // The bed under a whole run, so it must never become a noise floor: the air is noise through a steep
            // (three-pole) low-pass at a low cutoff — distant room tone, not hiss — and each biome's character comes from
            // tonal layers and sparse, softened events instead of broadband noise. (The first version ran white noise
            // through one 6 dB/octave pole, plus a constant 2.6 kHz steam wash in the Rustworks and a 2.4 kHz whine in
            // the Labs: its energy above 2 kHz sat within 3-7 dB of the music's and read as a constant rush.)
            var rng = new System.Random(StableSeed("amb." + biome));
            var c = new Clip(12f);
            var air1 = new LowPass();
            var air2 = new LowPass();
            var air3 = new LowPass();
            var ev1 = new LowPass();
            var ev2 = new LowPass();

            float Air(float noise, float cutoff) => air3.Process(air2.Process(air1.Process(noise, cutoff), cutoff), cutoff);
            float Soft(float noise, float cutoff) => ev2.Process(ev1.Process(noise, cutoff), cutoff);

            for (var i = 0; i < c.Length; i++)
            {
                var t = (float)i / SampleRate;
                var noise = (float)(rng.NextDouble() * 2 - 1);
                float s;

                switch (biome)
                {
                    case "RuinedMetro":
                        // Hollow tunnel air, the old transit's electrical hum, a far-off metal knock now and then.
                        s = Air(noise, 180f) * 1.6f
                            + Sine(50f * t) * 0.10f
                            + Sine(100.5f * t) * 0.045f
                            + (Mathf.Repeat(t, 3.7f) < 0.25f ? Sine(170f * t) * Punch(Mathf.Repeat(t, 3.7f), 0.08f) * 0.12f : 0f);
                        break;

                    case "Rustworks":
                        // Machinery and heat: a low engine cycle, a rhythmic press, and a soft steam swell every six seconds.
                        var steamT = Mathf.Repeat(t, 6f);
                        var steam = steamT < 1.4f ? Mathf.Sin(Mathf.PI * steamT / 1.4f) : 0f;
                        s = Air(noise, 240f) * 1.4f
                            + Saw(38f * t) * 0.07f * (0.6f + 0.4f * Mathf.Sin(t * 1.3f))
                            + Soft(noise, 900f) * steam * steam * 0.18f
                            + (Mathf.Repeat(t, 2.1f) < 0.3f ? Sine(70f * t) * Punch(Mathf.Repeat(t, 2.1f), 0.09f) * 0.30f : 0f);
                        break;

                    case "CryoVaults":
                    {
                        // Cold storage: low ventilation, the refrigeration plant's hum cycling on and off, a pressure
                        // release every seven seconds (a soft low-passed breath, never a hiss), a slow condensation drip,
                        // and a distant metal/ice creak now and then.
                        var cycle = 0.55f + 0.45f * Mathf.Sin(2f * Mathf.PI * t / 12f);
                        var ventT = Mathf.Repeat(t, 7f);
                        var vent = ventT < 1.1f ? Mathf.Sin(Mathf.PI * ventT / 1.1f) : 0f;
                        var dripT2 = Mathf.Repeat(t + 0.8f, 2.9f);
                        var creakT = Mathf.Repeat(t + 2.3f, 5.3f);
                        s = Air(noise, 200f) * 1.5f
                            + (Sine(60f * t) * 0.06f + Sine(120f * t) * 0.025f) * cycle
                            + Soft(noise, 520f) * vent * vent * 0.16f
                            + (dripT2 < 0.12f ? Sine((1100f - 1400f * dripT2) * t) * Punch(dripT2, 0.02f) * 0.04f : 0f)
                            + (creakT < 0.4f ? Sine((210f + 40f * Mathf.Sin(t * 30f)) * t) * Punch(creakT, 0.12f) * 0.05f : 0f);
                        break;
                    }

                    default:
                        // Labs: ventilation, a faint electronic chirp that comes and goes, a soft wet drip.
                        var dripT = Mathf.Repeat(t, 1.9f);
                        s = Air(noise, 320f) * 1.3f
                            + Sine(120f * t) * 0.06f
                            + Sine(880f * t) * 0.006f * Mathf.Max(0f, Mathf.Sin(t * 0.4f))
                            + (dripT < 0.15f ? Sine((620f - 900f * dripT) * t) * Punch(dripT, 0.03f) * 0.05f : 0f);
                        break;
                }

                // Slow stereo drift so the bed never sits dead centre.
                var pan = Mathf.Sin(t * 0.13f) * 0.2f;
                c.Add(i, s * (1f - pan), s * (1f + pan));
            }

            c.MakeSeamless(1.2f);
            return c;
        }

        // ---------------- music ----------------

        /// <summary>
        /// A music role, written as a short loopable bed.
        ///
        /// Each biome has a root and a timbre; intensity chooses the arrangement. Exploration is sparse pads,
        /// Combat adds a driving pulse and percussion, Boss adds a low brass-like motif. The same root across a
        /// biome's three tracks is what lets MusicDirector crossfade between them without the key jumping.
        /// </summary>
        public static Clip BuildMusic(string role)
        {
            // Each biome has its own identity (scale, tempo, timbre, motif and a signature layer); the Main Menu and the
            // Shelter keep the shared bed below exactly as before.
            if (role.StartsWith("RuinedMetro", StringComparison.Ordinal)) return BuildRuinedMetroMusic(role);
            if (role.StartsWith("Rustworks", StringComparison.Ordinal)) return BuildRustworksMusic(role);
            if (role.StartsWith("OvergrownLabs", StringComparison.Ordinal)) return BuildOvergrownLabsMusic(role);
            if (role.StartsWith("CryoVaults", StringComparison.Ordinal)) return BuildCryoVaultsMusic(role);
            if (role == "MainMenu") return BuildMainMenuMusic();

            var rng = new System.Random(StableSeed("mus." + role));
            var (root, intensity, bars) = MusicPlan(role);
            var bpm = intensity switch { 0 => 72f, 1 => 108f, _ => 126f };
            var beat = 60f / bpm;
            var barLen = beat * 4f;
            var dur = barLen * bars;

            var c = new Clip(dur + 1.2f);

            // Pad: sustained root and fifth through the whole loop.
            for (var b = 0; b < bars; b++)
            {
                var at = b * barLen;
                var degree = b % 2 == 0 ? 0 : 5;
                PlayNote(c, at, barLen * 1.05f, Note(ScaleNote(root, degree) - 12), 0.20f, Saw, 0.35f, 0.4f, 0.55f, 0.5f, -0.25f);
                PlayNote(c, at, barLen * 1.05f, Note(ScaleNote(root, degree + 2) - 12), 0.15f, Saw, 0.4f, 0.4f, 0.5f, 0.5f, 0.25f);
            }

            // Melody: a sparse motif, more active as intensity rises.
            var steps = intensity == 0 ? 4 : intensity == 1 ? 8 : 12;
            for (var s = 0; s < steps; s++)
            {
                var at = s * (dur / steps);
                var degree = (intensity == 0 ? new[] { 0, 4, 2, 5 } : new[] { 0, 2, 4, 3, 5, 4, 2, 1 })[s % (intensity == 0 ? 4 : 8)];
                PlayNote(c, at, dur / steps * 0.9f, Note(ScaleNote(root, degree)), 0.17f, Tri, 0.02f, 0.12f, 0.45f, 0.2f,
                    s % 2 == 0 ? -0.15f : 0.15f);
            }

            if (intensity >= 1)
            {
                // Pulse: an eighth-note drive that gives combat its forward motion.
                for (var e = 0; e < bars * 8; e++)
                {
                    var at = e * beat * 0.5f;
                    PlayNote(c, at, beat * 0.4f, Note(ScaleNote(root, 0) - 24), 0.22f, p => Square(p), 0.004f, 0.06f, 0.3f, 0.06f);
                }

                // Percussion: kick on the beat, noise hat off it.
                for (var k = 0; k < bars * 4; k++)
                {
                    var at = k * beat;
                    Kick(c, at, 0.30f);
                    if (k % 2 == 1) Hat(c, rng, at + beat * 0.5f, 0.12f);
                }
            }

            if (intensity >= 2)
            {
                // Boss: a low motif answering the melody, plus a heavier backbeat.
                for (var b = 0; b < bars; b++)
                {
                    var at = b * barLen + barLen * 0.5f;
                    PlayNote(c, at, barLen * 0.45f, Note(ScaleNote(root, b % 2 == 0 ? 0 : 6) - 24), 0.26f, Saw, 0.03f, 0.15f, 0.6f, 0.2f);
                }
                for (var k = 0; k < bars * 4; k++)
                    if (k % 4 == 2) Snare(c, rng, k * beat, 0.26f);
            }

            // The buffer is one release tail longer than the 8-bar grid so the last notes can ring; wrapping that tail
            // onto the head lands the ring-out over the next repetition's downbeat and leaves the file exactly `dur`
            // long, so a looping track stays on the beat instead of slipping by 1.2 s and dipping every repetition.
            var looped = c.WrapTailToLoop(dur);
            looped.MakeSeamless(0.25f);
            return looped;
        }

        // ---------------- biome themes ----------------
        //
        // Three distinct identities that follow each biome's art/105 character, told apart by ear before any note of
        // melody: the mode, the tempo, the lead timbre and one signature layer. Every biome keeps one root across its
        // three tracks (Exploration → Combat → Boss add layers on the same key), so MusicDirector's crossfades never jump.
        //
        //  Ruined Metro   natural minor, 76/112/128 BPM — electric hum, a square-wave station chime with a tunnel echo,
        //                 "da-dum" rail-joint clacks; combat adds a running eighth-note bass like a train.
        //  Rustworks      phrygian (flat 2nd), 66/100/116 BPM, heavy — a detuned saw drone, anvil hammer strikes, steam
        //                 hiss swells; combat adds a 3+3+2 piston bass.
        //  Overgrown Labs dorian (raised 6th), 84/116/132 BPM, glassy — FM-bell bubbling arpeggios over a soft shimmer
        //                 and Geiger-like clicks; combat adds a syncopated soft kick and shaker.
        //  Cryo Vaults    sparse cold minor (no 4th/7th), 70/104/120 BPM, restrained — a cycling refrigeration drone,
        //                 open-fifth glass pads, a relay tick on the beat and a bell motif with a long far echo; combat
        //                 adds a muted eighth-note bass and a soft kick, the boss a low octave stab and a snare.

        private static readonly int[] PhrygianScale = { 0, 1, 3, 5, 7, 8, 10 };
        private static readonly int[] DorianScale = { 0, 2, 3, 5, 7, 9, 10 };
        private static readonly int[] ColdScale = { 0, 2, 3, 7, 8, 12, 14 };

        private static int Degree(int[] scale, int root, int degree)
        {
            var octave = Mathf.FloorToInt(degree / 7f);
            var index = ((degree % 7) + 7) % 7;
            return root + scale[index] + octave * 12;
        }

        private static Clip Finish(Clip c, float dur)
        {
            var looped = c.WrapTailToLoop(dur);
            looped.MakeSeamless(0.25f);
            return looped;
        }

        private static float PulseQuarter(float p) => Square(p, 0.25f);
        private static float Bell(float p) => Sine(p + 0.22f * Sine(p * 3.5f));

        private static Clip BuildRuinedMetroMusic(string role)
        {
            var rng = new System.Random(StableSeed("mus." + role));
            var (root, intensity, bars) = MusicPlan(role);
            var bpm = intensity switch { 0 => 76f, 1 => 112f, _ => 128f };
            var beat = 60f / bpm;
            var barLen = beat * 4f;
            var dur = barLen * bars;
            var c = new Clip(dur + 1.2f);
            var chords = new[] { 0, 5, 3, 4 }; // i – VI – iv – v, two bars each

            // Electric hum: two slightly detuned low sines that beat against each other the whole loop.
            PlayNote(c, 0f, dur, Note(root - 24), 0.13f, Sine, 0.5f, 0.2f, 0.9f, 0.5f, -0.1f);
            PlayNote(c, 0f, dur, Note(root - 24) * 1.006f, 0.11f, Sine, 0.5f, 0.2f, 0.9f, 0.5f, 0.1f);

            // Tunnel pad: soft saw chords, slow attack.
            for (var b = 0; b < bars; b += 2)
            {
                var chord = chords[(b / 2) % chords.Length];
                PlayNote(c, b * barLen, barLen * 2.05f, Note(ScaleNote(root, chord) - 12), 0.12f, Saw, 0.6f, 0.4f, 0.6f, 0.6f, -0.3f);
                PlayNote(c, b * barLen, barLen * 2.05f, Note(ScaleNote(root, chord + 2) - 12), 0.10f, Saw, 0.7f, 0.4f, 0.6f, 0.6f, 0.3f);
            }

            // Station chime: a descending three-note pulse-wave call, answered by its tunnel echo.
            var chimeBars = intensity == 0 ? new[] { 0, 4 } : intensity == 1 ? new[] { 0, 2, 4, 6 } : new[] { 0, 1, 2, 3, 4, 5, 6, 7 };
            foreach (var b in chimeBars)
            {
                var motif = b % 4 == 0 ? new[] { 4, 2, 0 } : new[] { 4, 3, 1 };
                for (var n = 0; n < motif.Length; n++)
                {
                    var at = b * barLen + n * beat;
                    var pitch = Note(ScaleNote(root, motif[n]) + 12);
                    PlayNote(c, at, beat * 0.9f, pitch, 0.15f, PulseQuarter, 0.005f, 0.12f, 0.35f, 0.25f, -0.2f);
                    PlayNote(c, at + beat * 0.75f, beat * 0.9f, pitch, 0.06f, PulseQuarter, 0.005f, 0.12f, 0.35f, 0.25f, 0.35f);
                }
            }

            // Rail joints: "da-dum" metallic clacks on beat 3 of every bar (every half bar once the fight starts).
            for (var b = 0; b < bars; b++)
            {
                RailClack(c, rng, b * barLen + beat * 2f, intensity == 0 ? 0.10f : 0.13f);
                RailClack(c, rng, b * barLen + beat * 2.25f, intensity == 0 ? 0.08f : 0.11f);
                if (intensity >= 1)
                {
                    RailClack(c, rng, b * barLen, 0.11f);
                    RailClack(c, rng, b * barLen + beat * 0.25f, 0.09f);
                }
            }

            if (intensity >= 1)
            {
                // The train: a running eighth-note bass, the octave kicking up on every fourth eighth.
                for (var e = 0; e < bars * 8; e++)
                {
                    var chord = chords[(e / 16) % chords.Length];
                    var up = e % 4 == 3 ? 12 : 0;
                    PlayNote(c, e * beat * 0.5f, beat * 0.42f, Note(ScaleNote(root, chord) - 24 + up), 0.20f, p => Square(p), 0.004f, 0.06f, 0.35f, 0.05f);
                }

                for (var k = 0; k < bars * 4; k++)
                {
                    if (k % 2 == 0) Kick(c, k * beat, 0.30f);
                    Hat(c, rng, k * beat + beat * 0.5f, 0.10f);
                }
            }

            if (intensity >= 2)
            {
                for (var b = 0; b < bars; b++)
                    PlayNote(c, b * barLen + barLen * 0.5f, barLen * 0.45f, Note(ScaleNote(root, b % 2 == 0 ? 0 : 6) - 24), 0.24f, Saw, 0.03f, 0.15f, 0.6f, 0.2f);
                for (var k = 0; k < bars * 4; k++)
                    if (k % 2 == 1) Snare(c, rng, k * beat, 0.24f);
            }

            return Finish(c, dur);
        }

        private static Clip BuildRustworksMusic(string role)
        {
            var rng = new System.Random(StableSeed("mus." + role));
            var (root, intensity, bars) = MusicPlan(role);
            var bpm = intensity switch { 0 => 66f, 1 => 100f, _ => 116f };
            var beat = 60f / bpm;
            var barLen = beat * 4f;
            var dur = barLen * bars;
            var c = new Clip(dur + 1.2f);

            // Furnace drone: root and fifth on detuned saws, very low.
            PlayNote(c, 0f, dur, Note(root - 24), 0.18f, Saw, 0.8f, 0.3f, 0.85f, 0.6f, -0.2f);
            PlayNote(c, 0f, dur, Note(root - 24) * 1.008f, 0.14f, Saw, 0.8f, 0.3f, 0.85f, 0.6f, 0.2f);
            PlayNote(c, 0f, dur, Note(root - 17), 0.09f, Saw, 1.2f, 0.3f, 0.8f, 0.6f, 0f);

            // Hammer and anvil: a heavy strike on the downbeat, a lighter answer on the "and" of 3.
            for (var b = 0; b < bars; b++)
            {
                Anvil(c, rng, b * barLen, 520f, intensity == 0 ? 0.22f : 0.26f);
                Anvil(c, rng, b * barLen + beat * 2.5f, 780f, intensity == 0 ? 0.11f : 0.15f);
            }

            // Steam: a hiss that swells and vents at the end of every second bar.
            for (var b = 1; b < bars; b += 2)
                Steam(c, rng, b * barLen + beat * 2.2f, beat * 1.7f, intensity == 0 ? 0.10f : 0.08f);

            // The flat second: a slow, brassy phrygian call in the low register.
            var call = intensity == 0 ? new[] { 0, 1, 0, -2 } : new[] { 0, 1, 3, 1, 0, -2, -1, 0 };
            var callLen = dur / call.Length;
            for (var n = 0; n < call.Length; n++)
                PlayNote(c, n * callLen, callLen * 0.85f, Note(Degree(PhrygianScale, root, call[n]) - 12), 0.17f, Saw, 0.08f, 0.2f, 0.55f, 0.3f, n % 2 == 0 ? -0.1f : 0.1f);

            if (intensity >= 1)
            {
                // Pistons: a 3+3+2 eighth-note bass figure.
                var pattern = new[] { 0, 3, 6 };
                for (var b = 0; b < bars; b++)
                    foreach (var eighth in pattern)
                        PlayNote(c, b * barLen + eighth * beat * 0.5f, beat * 0.7f, Note(Degree(PhrygianScale, root, eighth == 6 ? 1 : 0) - 24), 0.24f, p => Square(p), 0.004f, 0.08f, 0.4f, 0.06f);

                for (var b = 0; b < bars; b++)
                {
                    Kick(c, b * barLen, 0.32f);
                    Kick(c, b * barLen + beat * 1.5f, 0.26f);
                    Kick(c, b * barLen + beat * 3f, 0.26f);
                }
            }

            if (intensity >= 2)
            {
                for (var b = 0; b < bars; b++)
                    PlayNote(c, b * barLen, barLen * 0.5f, Note(Degree(PhrygianScale, root, b % 2 == 0 ? 0 : 1)), 0.13f, Saw, 0.02f, 0.2f, 0.6f, 0.2f);
                for (var k = 0; k < bars * 4; k++)
                    if (k % 4 == 2) Snare(c, rng, k * beat, 0.28f);
            }

            return Finish(c, dur);
        }

        private static Clip BuildOvergrownLabsMusic(string role)
        {
            var rng = new System.Random(StableSeed("mus." + role));
            var (root, intensity, bars) = MusicPlan(role);
            var bpm = intensity switch { 0 => 84f, 1 => 116f, _ => 132f };
            var beat = 60f / bpm;
            var barLen = beat * 4f;
            var dur = barLen * bars;
            var c = new Clip(dur + 1.2f);
            var chords = new[] { 0, 3, 5, 3 }; // i – IV (the dorian major fourth) – vi° colour – IV

            // Growth pad: soft triangle chords and a high shimmer.
            for (var b = 0; b < bars; b += 2)
            {
                var chord = chords[(b / 2) % chords.Length];
                PlayNote(c, b * barLen, barLen * 2.05f, Note(Degree(DorianScale, root, chord) - 12), 0.13f, Tri, 0.8f, 0.4f, 0.7f, 0.7f, -0.25f);
                PlayNote(c, b * barLen, barLen * 2.05f, Note(Degree(DorianScale, root, chord + 2) - 12), 0.11f, Tri, 0.9f, 0.4f, 0.7f, 0.7f, 0.25f);
                PlayNote(c, b * barLen, barLen * 2.05f, Note(Degree(DorianScale, root, chord + 4) + 12), 0.04f, Sine, 1.2f, 0.4f, 0.8f, 0.8f, 0f);
            }

            // Bubbling cultures: sixteenth-note FM-bell arpeggios over the chord, seeded, sparse while exploring.
            var density = intensity == 0 ? 0.35 : intensity == 1 ? 0.7 : 0.85;
            for (var s = 0; s < bars * 16; s++)
            {
                if (rng.NextDouble() > density) continue;
                var chord = chords[(s / 32) % chords.Length];
                var tone = new[] { 0, 2, 4, 7 }[rng.Next(4)];
                var octave = rng.NextDouble() < 0.3 ? 24 : 12;
                PlayNote(c, s * beat * 0.25f, beat * 0.22f, Note(Degree(DorianScale, root, chord + tone) + octave - 12), 0.12f, Bell, 0.003f, 0.07f, 0.2f, 0.1f,
                    (float)(rng.NextDouble() * 0.8 - 0.4));
            }

            // Instruments ticking: sparse Geiger-like clicks.
            var clicks = intensity == 0 ? bars * 3 : bars * 6;
            for (var i = 0; i < clicks; i++) LabClick(c, rng, (float)(rng.NextDouble() * dur), 0.07f);

            // The raised sixth: a gentle bell melody that names the mode.
            var melody = intensity == 0 ? new[] { 0, 2, 5, 4 } : new[] { 0, 2, 5, 4, 5, 7, 5, 2 };
            var step = dur / melody.Length;
            for (var n = 0; n < melody.Length; n++)
                PlayNote(c, n * step, step * 0.8f, Note(Degree(DorianScale, root, melody[n]) + 12), 0.13f, Bell, 0.01f, 0.3f, 0.4f, 0.3f, n % 2 == 0 ? 0.15f : -0.15f);

            if (intensity >= 1)
            {
                // A syncopated soft kick (1 and the "and" of 2) and a quiet sixteenth shaker.
                for (var b = 0; b < bars; b++)
                {
                    Kick(c, b * barLen, 0.24f);
                    Kick(c, b * barLen + beat * 1.5f, 0.20f);
                    if (intensity >= 2) Kick(c, b * barLen + beat * 3f, 0.22f);
                }

                for (var s = 0; s < bars * 16; s++)
                    if (s % 4 != 0) Hat(c, rng, s * beat * 0.25f, s % 2 == 1 ? 0.05f : 0.08f);
            }

            if (intensity >= 2)
            {
                for (var b = 0; b < bars; b++)
                    PlayNote(c, b * barLen + barLen * 0.5f, barLen * 0.45f, Note(Degree(DorianScale, root, b % 2 == 0 ? 0 : 5) - 24), 0.22f, Saw, 0.03f, 0.15f, 0.6f, 0.2f);
                for (var k = 0; k < bars * 4; k++)
                    if (k % 4 == 2) Snare(c, rng, k * beat, 0.20f);
            }

            return Finish(c, dur);
        }

        private static Clip BuildCryoVaultsMusic(string role)
        {
            var rng = new System.Random(StableSeed("mus." + role));
            var (root, intensity, bars) = MusicPlan(role);
            var bpm = intensity switch { 0 => 70f, 1 => 104f, _ => 120f };
            var beat = 60f / bpm;
            var barLen = beat * 4f;
            var dur = barLen * bars;
            var c = new Clip(dur + 1.6f);
            var chords = new[] { 0, 4, 3, 1 }; // i – VI – v(no third) – ii: open, suspended, never resolving warmly

            // Refrigeration drone: root and fifth far below, swelling with a slow compressor cycle.
            PlayNote(c, 0f, dur, Note(root - 24), 0.12f, Sine, 1.0f, 0.3f, 0.9f, 1.0f, -0.15f);
            PlayNote(c, 0f, dur, Note(root - 17) * 1.002f, 0.07f, Sine, 1.2f, 0.3f, 0.9f, 1.0f, 0.15f);

            // Glass pads: open fifths (no third) on soft sine/triangle, slow attack, wide, with a faint high shimmer.
            for (var b = 0; b < bars; b += 2)
            {
                var chord = chords[(b / 2) % chords.Length];
                var baseNote = Degree(ColdScale, root, chord) - 12;
                PlayNote(c, b * barLen, barLen * 2.1f, Note(baseNote), 0.10f, Tri, 1.1f, 0.5f, 0.7f, 0.9f, -0.35f);
                PlayNote(c, b * barLen, barLen * 2.1f, Note(baseNote + 7), 0.09f, Sine, 1.3f, 0.5f, 0.7f, 0.9f, 0.35f);
                PlayNote(c, b * barLen + beat, barLen * 1.9f, Note(baseNote + 26), 0.025f, Sine, 1.6f, 0.5f, 0.8f, 0.9f, 0f);
            }

            // Relay tick: a small metallic click on every beat (every other while exploring) — the facility still runs.
            for (var k = 0; k < bars * 4; k++)
            {
                if (intensity == 0 && k % 2 == 1) continue;
                MechanicalClick(c, rng, k * beat, k % 4 == 0 ? 2400f : 3100f, 0.018f, intensity == 0 ? 0.035f : 0.05f);
            }

            // A sparse bell motif, each note answered by a quieter echo from the far side of the hall.
            var melody = intensity == 0 ? new[] { 4, -1, 3, -1, 2, -1, 1, -1 } : new[] { 4, 3, -1, 2, 4, 5, -1, 3 };
            var step = dur / melody.Length;
            var echo = beat * 1.5f;
            for (var n = 0; n < melody.Length; n++)
            {
                if (melody[n] < 0) continue;
                var pitch = Note(Degree(ColdScale, root, melody[n]) + 12);
                var pan = n % 2 == 0 ? -0.3f : 0.3f;
                PlayNote(c, n * step, step * 0.7f, pitch, 0.12f, Bell, 0.01f, 0.4f, 0.3f, 0.6f, pan);
                PlayNote(c, n * step + echo, step * 0.7f, pitch, 0.045f, Bell, 0.02f, 0.4f, 0.3f, 0.6f, -pan);
            }

            if (intensity >= 1)
            {
                // Muted eighth-note bass on the chord root (a machine cycling) and a soft kick on 1 and 3.
                for (var b = 0; b < bars; b++)
                {
                    var chord = chords[(b / 2) % chords.Length];
                    for (var e = 0; e < 8; e++)
                        PlayNote(c, b * barLen + e * beat * 0.5f, beat * 0.4f, Note(Degree(ColdScale, root, chord) - 24), e % 2 == 0 ? 0.16f : 0.10f, Tri, 0.005f, 0.08f, 0.4f, 0.08f);
                    Kick(c, b * barLen, 0.22f);
                    Kick(c, b * barLen + beat * 2f, 0.18f);
                }

                for (var s = 0; s < bars * 8; s++)
                    if (s % 2 == 1) Hat(c, rng, s * beat * 0.5f, 0.05f);
            }

            if (intensity >= 2)
            {
                // Boss: a low octave stab each half bar and a snare on 3 — pressure without raising the brightness.
                for (var h = 0; h < bars * 2; h++)
                {
                    var chord = chords[(h / 4) % chords.Length];
                    PlayNote(c, h * barLen * 0.5f, beat * 0.9f, Note(Degree(ColdScale, root, chord) - 24), 0.20f, Saw, 0.01f, 0.2f, 0.4f, 0.2f, -0.1f);
                    PlayNote(c, h * barLen * 0.5f, beat * 0.9f, Note(Degree(ColdScale, root, chord) - 12), 0.10f, Saw, 0.01f, 0.2f, 0.4f, 0.2f, 0.1f);
                }

                for (var k = 0; k < bars * 4; k++)
                    if (k % 4 == 2) Snare(c, rng, k * beat, 0.22f);
            }

            return Finish(c, dur);
        }

        /// <summary>
        /// The Main Menu theme: a calm, unhurried RUINRAIL bed for the screen a player sits on longest, written to be
        /// listened to for minutes without fatigue. It had shared the generic bed (sustained buzzy saw pads under a
        /// triangle motif at 72 BPM) with the Shelter; it now has its own material. Soft sine-led pads that breathe in
        /// and out on a slow minor progression, a distant low rail hum, a faint low-passed wind, a sparse slow melody
        /// with long attacks, and a muffled station chime every four bars. No percussion, no noise transients, no saw or
        /// square buzz, nothing bright: every voice sits low and rounded, so it stays atmospheric and never sounds like
        /// a combat track. 60 BPM, eight bars, on its bar grid like every other bed.
        /// </summary>
        private static Clip BuildMainMenuMusic()
        {
            var rng = new System.Random(StableSeed("mus.MainMenu"));
            var (root, _, bars) = MusicPlan("MainMenu");
            const float bpm = 60f;
            var beat = 60f / bpm;
            var barLen = beat * 4f;
            var dur = barLen * bars;
            var c = new Clip(dur + 2.5f);
            var chords = new[] { 0, 5, 3, 4 }; // i – VI – iv – v, two bars each: minor, but open and unhurried

            float Soft(float p) => 0.82f * Sine(p) + 0.14f * Sine(p * 2f) + 0.04f * Tri(p);

            // Distant rail hum: two low sines beating slowly against each other.
            PlayNote(c, 0f, dur, Note(root - 24), 0.07f, Sine, 2.0f, 0.5f, 1f, 2.0f, -0.1f);
            PlayNote(c, 0f, dur, Note(root - 24) * 1.003f, 0.055f, Sine, 2.0f, 0.5f, 1f, 2.0f, 0.1f);

            // Breathing pads: slow swell in and out over each two-bar chord, overlapping so the bed never gaps.
            for (var b = 0; b < bars; b += 2)
            {
                var chord = chords[(b / 2) % chords.Length];
                var at = b * barLen;
                var len = barLen * 2.4f;
                PlayNote(c, at, len, Note(ScaleNote(root, chord) - 12), 0.105f, Soft, 1.6f, 1.4f, 0.6f, 1.8f, -0.25f);
                PlayNote(c, at, len, Note(ScaleNote(root, chord + 2) - 12), 0.08f, Soft, 1.9f, 1.4f, 0.6f, 1.8f, 0.25f);
                PlayNote(c, at, len, Note(ScaleNote(root, chord + 4) - 12), 0.05f, Sine, 2.2f, 1.4f, 0.6f, 1.8f, 0f);
            }

            // A sparse, slow melody: one note every two beats at most, long attacks, soft sine.
            var melody = new[] { 4, -1, 2, -1, 3, 2, -1, -1, 4, -1, 5, 4, 2, -1, 0, -1 };
            var step = dur / melody.Length;
            for (var n = 0; n < melody.Length; n++)
            {
                if (melody[n] < 0) continue;
                PlayNote(c, n * step, step * 1.6f, Note(ScaleNote(root, melody[n])), 0.10f, Soft, 0.35f, 0.6f, 0.55f, 1.2f, n % 2 == 0 ? -0.2f : 0.2f);
            }

            // A muffled station chime every four bars, low and far away (sine partials only, slow decay).
            for (var b = 0; b < bars; b += 4)
            {
                var at = b * barLen + beat * 0.5f;
                PlayNote(c, at, 3.2f, Note(ScaleNote(root, 4) - 12), 0.07f, Sine, 0.04f, 1.2f, 0.25f, 1.6f, -0.3f);
                PlayNote(c, at + 0.6f, 3.2f, Note(ScaleNote(root, 2) - 12), 0.06f, Sine, 0.04f, 1.2f, 0.25f, 1.6f, 0.3f);
            }

            // Wind through the tunnels: noise low-passed hard, swelling slowly, well under everything else.
            var lp = new LowPass();
            var lp2 = new LowPass();
            var n0 = Mathf.RoundToInt(dur * SampleRate);
            for (var i = 0; i < n0; i++)
            {
                var t = (float)i / SampleRate;
                var swell = 0.55f + 0.45f * Mathf.Sin(2f * Mathf.PI * t / (barLen * 4f));
                var s = lp2.Process(lp.Process((float)(rng.NextDouble() * 2 - 1), 380f), 380f) * swell * 0.05f;
                c.Add(i, s * 0.9f, s);
            }

            var looped = c.WrapTailToLoop(dur);
            looped.MakeSeamless(0.8f);
            return looped;
        }

        /// <summary>A wheel over a rail joint: a bright, very short metallic tick.</summary>
        private static void RailClack(Clip c, System.Random rng, float at, float gain)
        {
            var start = Mathf.RoundToInt(at * SampleRate);
            var n = Mathf.RoundToInt(0.09f * SampleRate);
            var hp = new HighPass();
            for (var i = 0; i < n; i++)
            {
                var t = (float)i / SampleRate;
                var ring = (Sine(1850f * t) + 0.6f * Sine(2930f * t)) * Punch(t, 0.02f);
                var noise = hp.Process((float)(rng.NextDouble() * 2 - 1), 3000f) * Punch(t, 0.006f);
                var s = (ring * 0.6f + noise) * gain;
                c.Add(start + i, s * 0.9f, s);
            }
        }

        /// <summary>A hammer on an anvil: an inharmonic ringing strike with a noisy impact.</summary>
        private static void Anvil(Clip c, System.Random rng, float at, float freq, float gain)
        {
            var start = Mathf.RoundToInt(at * SampleRate);
            var n = Mathf.RoundToInt(0.9f * SampleRate);
            var lp = new LowPass();
            for (var i = 0; i < n; i++)
            {
                var t = (float)i / SampleRate;
                var ring = (Sine(freq * t) + 0.55f * Sine(freq * 2.76f * t) + 0.3f * Sine(freq * 5.4f * t)) * Punch(t, 0.22f);
                var hit = lp.Process((float)(rng.NextDouble() * 2 - 1), 2500f) * Punch(t, 0.012f) * 2f;
                var s = (ring * 0.55f + hit) * gain;
                c.Add(start + i, s, s * 0.9f);
            }
        }

        /// <summary>Venting steam: high-passed noise that swells in and falls away.</summary>
        private static void Steam(Clip c, System.Random rng, float at, float dur, float gain)
        {
            var start = Mathf.RoundToInt(at * SampleRate);
            var n = Mathf.RoundToInt(dur * SampleRate);
            var hp = new HighPass();
            for (var i = 0; i < n; i++)
            {
                var t = (float)i / SampleRate;
                var env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / dur));
                var s = hp.Process((float)(rng.NextDouble() * 2 - 1), 2600f) * env * env * gain;
                c.Add(start + i, s * 0.8f, s);
            }
        }

        /// <summary>A lab instrument's click: a tiny high tick.</summary>
        private static void LabClick(Clip c, System.Random rng, float at, float gain)
        {
            var start = Mathf.RoundToInt(at * SampleRate);
            var n = Mathf.RoundToInt(0.012f * SampleRate);
            var hp = new HighPass();
            var pan = (float)(rng.NextDouble() * 1.2 - 0.6);
            for (var i = 0; i < n; i++)
            {
                var t = (float)i / SampleRate;
                var s = hp.Process((float)(rng.NextDouble() * 2 - 1), 5000f) * Punch(t, 0.002f) * gain;
                c.Add(start + i, s * (1f - Mathf.Max(0f, pan)), s * (1f + Mathf.Min(0f, pan)));
            }
        }

        private static void Kick(Clip c, float at, float gain)
        {
            var start = Mathf.RoundToInt(at * SampleRate);
            var n = Mathf.RoundToInt(0.22f * SampleRate);
            for (var i = 0; i < n; i++)
            {
                var t = (float)i / SampleRate;
                var f = 120f * Mathf.Exp(-t * 32f) + 42f;
                var s = Sine(f * t) * Punch(t, 0.075f) * gain;
                c.Add(start + i, s, s);
            }
        }

        private static void Hat(Clip c, System.Random rng, float at, float gain)
        {
            var start = Mathf.RoundToInt(at * SampleRate);
            var n = Mathf.RoundToInt(0.07f * SampleRate);
            var hp = new HighPass();
            for (var i = 0; i < n; i++)
            {
                var t = (float)i / SampleRate;
                var s = hp.Process((float)(rng.NextDouble() * 2 - 1), 7000f) * Punch(t, 0.017f) * gain;
                c.Add(start + i, s * 0.85f, s);
            }
        }

        private static void Snare(Clip c, System.Random rng, float at, float gain)
        {
            var start = Mathf.RoundToInt(at * SampleRate);
            var n = Mathf.RoundToInt(0.2f * SampleRate);
            var hp = new HighPass();
            for (var i = 0; i < n; i++)
            {
                var t = (float)i / SampleRate;
                var body = Sine(190f * t) * Punch(t, 0.045f) * 0.5f;
                var snap = hp.Process((float)(rng.NextDouble() * 2 - 1), 1800f) * Punch(t, 0.075f);
                var s = (body + snap) * gain;
                c.Add(start + i, s, s * 0.95f);
            }
        }

        /// <summary>Root semitone, intensity (0 explore, 1 combat, 2 boss) and loop length per music role.</summary>
        private static (int root, int intensity, int bars) MusicPlan(string role) => role switch
        {
            "MainMenu" => (-9, 0, 8),
            "Shelter" => (-7, 0, 8),
            "RuinedMetroExploration" => (-9, 0, 8),
            "RuinedMetroCombat" => (-9, 1, 8),
            "RuinedMetroBoss" => (-9, 2, 8),
            "RustworksExploration" => (-4, 0, 8),
            "RustworksCombat" => (-4, 1, 8),
            "RustworksBoss" => (-4, 2, 8),
            "OvergrownLabsExploration" => (-2, 0, 8),
            "OvergrownLabsCombat" => (-2, 1, 8),
            "OvergrownLabsBoss" => (-2, 2, 8),
            "CryoVaultsExploration" => (-6, 0, 8),
            "CryoVaultsCombat" => (-6, 1, 8),
            "CryoVaultsBoss" => (-6, 2, 8),
            _ => (-9, 0, 8)
        };

        // ---------------- stingers ----------------

        public static Clip BuildStinger(string role)
        {
            var rng = new System.Random(StableSeed("stg." + role));
            var c = new Clip(2.0f);

            switch (role)
            {
                case "LegendaryDrop":
                    // The strongest positive cue in the game; pairs with the Legendary glow (spec 27).
                    PlayNote(c, 0.00f, 0.5f, Note(-5), 0.34f, Tri, 0.01f, 0.1f, 0.6f, 0.25f);
                    PlayNote(c, 0.12f, 0.5f, Note(2), 0.32f, Tri, 0.01f, 0.1f, 0.6f, 0.25f);
                    PlayNote(c, 0.24f, 0.7f, Note(7), 0.34f, Tri, 0.01f, 0.1f, 0.6f, 0.35f);
                    PlayNote(c, 0.40f, 0.9f, Note(14), 0.28f, Sine, 0.03f, 0.15f, 0.55f, 0.45f);
                    break;

                case "EliteEncounter":
                    PlayNote(c, 0.00f, 0.55f, Note(-17), 0.34f, Saw, 0.02f, 0.12f, 0.6f, 0.25f);
                    PlayNote(c, 0.00f, 0.55f, Note(-10), 0.26f, Saw, 0.03f, 0.12f, 0.55f, 0.25f);
                    Snare(c, rng, 0.42f, 0.3f);
                    break;

                case "BossDefeated":
                    // Minor resolving up to major: hard-won, not triumphant fanfare.
                    PlayNote(c, 0.00f, 0.6f, Note(-12), 0.32f, Saw, 0.02f, 0.15f, 0.55f, 0.3f);
                    PlayNote(c, 0.30f, 0.7f, Note(-5), 0.30f, Tri, 0.02f, 0.15f, 0.55f, 0.35f);
                    PlayNote(c, 0.62f, 1.0f, Note(0), 0.34f, Tri, 0.03f, 0.2f, 0.6f, 0.5f);
                    PlayNote(c, 0.62f, 1.0f, Note(4), 0.24f, Sine, 0.05f, 0.2f, 0.55f, 0.5f);
                    break;

                case "ExtractionSuccess":
                    PlayNote(c, 0.00f, 0.35f, Note(-5), 0.32f, Tri, 0.01f, 0.08f, 0.55f, 0.18f);
                    PlayNote(c, 0.18f, 0.40f, Note(0), 0.30f, Tri, 0.01f, 0.08f, 0.55f, 0.2f);
                    PlayNote(c, 0.36f, 0.75f, Note(7), 0.32f, Tri, 0.02f, 0.12f, 0.6f, 0.4f);
                    break;

                case "ExpeditionFailed":
                    // Falling minor third into a low toll.
                    PlayNote(c, 0.00f, 0.6f, Note(-5), 0.30f, Saw, 0.02f, 0.2f, 0.5f, 0.3f);
                    PlayNote(c, 0.35f, 0.8f, Note(-8), 0.30f, Saw, 0.03f, 0.2f, 0.5f, 0.4f);
                    PlayNote(c, 0.80f, 1.1f, Note(-20), 0.34f, Sine, 0.04f, 0.3f, 0.45f, 0.6f);
                    break;

                case "RoomCleared":
                    // A room won: a soft mechanical release (the doors), then a quick bright rising fourth that settles.
                    // Short and small on purpose — it plays after every Combat room; the arpeggios (Level Up,
                    // Extraction, Legendary) and the Boss Defeated resolution stay the bigger moments.
                    Snare(c, rng, 0.00f, 0.16f);
                    PlayNote(c, 0.00f, 0.18f, Note(-19), 0.30f, Sine, 0.004f, 0.06f, 0.4f, 0.08f);
                    PlayNote(c, 0.06f, 0.14f, Note(5), 0.26f, Tri, 0.004f, 0.04f, 0.5f, 0.06f);
                    PlayNote(c, 0.16f, 0.40f, Note(10), 0.28f, Tri, 0.006f, 0.08f, 0.5f, 0.22f);
                    PlayNote(c, 0.16f, 0.40f, Note(17), 0.14f, Sine, 0.01f, 0.08f, 0.45f, 0.22f);
                    break;

                default: // LevelUp
                    PlayNote(c, 0.00f, 0.22f, Note(0), 0.30f, p => Square(p), 0.004f, 0.05f, 0.5f, 0.08f);
                    PlayNote(c, 0.12f, 0.22f, Note(4), 0.30f, p => Square(p), 0.004f, 0.05f, 0.5f, 0.08f);
                    PlayNote(c, 0.24f, 0.22f, Note(7), 0.30f, p => Square(p), 0.004f, 0.05f, 0.5f, 0.08f);
                    PlayNote(c, 0.36f, 0.6f, Note(12), 0.32f, Tri, 0.01f, 0.1f, 0.55f, 0.3f);
                    break;
            }

            return c;
        }

        public static int StableSeed(string s)
        {
            unchecked
            {
                var h = 23;
                foreach (var ch in s ?? string.Empty) h = h * 37 + ch;
                return h & 0x7fffffff;
            }
        }
    }
}
