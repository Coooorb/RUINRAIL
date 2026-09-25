using System;
using System.Collections.Generic;
using UnityEngine;
using static RuinRail.EditorTools.ArtGen.AudioSynth;

namespace RuinRail.EditorTools.ArtGen
{
    /// <summary>
    /// Synthesises the 53 SFX, 11 music tracks, 6 stingers and 3 ambience loops the manifest requires.
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
            var rng = new System.Random(StableSeed("amb." + biome));
            var c = new Clip(12f);
            var lp = new LowPass();
            var lp2 = new LowPass();
            var hp = new HighPass();

            for (var i = 0; i < c.Length; i++)
            {
                var t = (float)i / SampleRate;
                var noise = (float)(rng.NextDouble() * 2 - 1);
                float s;

                switch (biome)
                {
                    case "RuinedMetro":
                        // Hollow tunnel air, distant electrical hum, occasional far-off metal.
                        s = lp.Process(noise, 320f) * 0.5f
                            + Sine(50f * t) * 0.08f
                            + Sine(100.5f * t) * 0.04f
                            + (Mathf.Repeat(t, 3.7f) < 0.02f ? hp.Process(noise, 1800f) * 0.4f : 0f);
                        break;

                    case "Rustworks":
                        // Machinery and heat: a low engine cycle, steam, and a rhythmic press.
                        s = lp.Process(noise, 500f) * 0.42f
                            + Saw(38f * t) * 0.09f
                            + lp2.Process(noise, 2600f) * (0.12f + 0.08f * Mathf.Sin(t * 0.7f))
                            + (Mathf.Repeat(t, 2.1f) < 0.05f ? Sine(70f * t) * 0.35f : 0f);
                        break;

                    default:
                        // Labs: ventilation, electronics, and a wet organic drip.
                        s = lp.Process(noise, 900f) * 0.30f
                            + Sine(120f * t) * 0.05f
                            + Sine(2400f * t) * 0.012f * (Mathf.Sin(t * 0.4f) * 0.5f + 0.5f)
                            + (Mathf.Repeat(t, 1.9f) < 0.012f ? hp.Process(noise, 3000f) * 0.3f : 0f);
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
