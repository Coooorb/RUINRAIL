using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RuinRail.EditorTools.ArtGen
{
    /// <summary>
    /// A small synthesis kit for generating original game audio as 16-bit PCM WAV.
    ///
    /// The V2 prompt permits procedural generation where the result is suitable for release rather than a test tone,
    /// so this is built from real synthesis primitives — noise bursts with shaped envelopes for impacts, detuned
    /// oscillator stacks for weapons, filtered noise beds for ambience, and short tuned motifs for music — rather
    /// than sine beeps. Everything is deterministic from a seed so a regenerated build is byte-identical.
    ///
    /// All content is original: no sample library, no third-party recording.
    /// </summary>
    public static class AudioSynth
    {
        public const int SampleRate = 44100;

        // ---------------- buffer ----------------

        public sealed class Clip
        {
            public readonly float[] L;
            public readonly float[] R;
            public int Length => L.Length;

            public Clip(float seconds)
            {
                var n = Mathf.Max(1, Mathf.RoundToInt(seconds * SampleRate));
                L = new float[n];
                R = new float[n];
            }

            public void Add(int i, float l, float r)
            {
                if (i < 0 || i >= L.Length) return;
                L[i] += l;
                R[i] += r;
            }

            /// <summary>Scales so the loudest sample sits at <paramref name="peak"/>. Prevents clipping by construction.</summary>
            public void Normalize(float peak = 0.89f)
            {
                var max = 0f;
                for (var i = 0; i < L.Length; i++)
                {
                    max = Mathf.Max(max, Mathf.Abs(L[i]));
                    max = Mathf.Max(max, Mathf.Abs(R[i]));
                }
                if (max < 1e-6f) return;
                var g = peak / max;
                for (var i = 0; i < L.Length; i++) { L[i] *= g; R[i] *= g; }
            }

            /// <summary>Short fades at both ends so no clip starts or ends on a discontinuity (click).</summary>
            public void DeClick(float seconds = 0.004f)
            {
                var n = Mathf.Min(L.Length / 2, Mathf.RoundToInt(seconds * SampleRate));
                for (var i = 0; i < n; i++)
                {
                    var g = (float)i / n;
                    L[i] *= g; R[i] *= g;
                    L[L.Length - 1 - i] *= g; R[R.Length - 1 - i] *= g;
                }
            }

            /// <summary>Crossfades the tail into the head so the clip loops seamlessly (ambience, music beds).</summary>
            public void MakeSeamless(float seconds = 0.35f)
            {
                var n = Mathf.Min(L.Length / 3, Mathf.RoundToInt(seconds * SampleRate));
                for (var i = 0; i < n; i++)
                {
                    var t = (float)i / n;
                    var tail = L.Length - n + i;
                    L[i] = Mathf.Lerp(L[tail], L[i], t);
                    R[i] = Mathf.Lerp(R[tail], R[i], t);
                }
                Array.Resize(ref _dummy, 0);
            }

            private float[] _dummy = Array.Empty<float>();
        }

        // ---------------- envelopes ----------------

        /// <summary>Percussive envelope: instant attack, exponential decay. The shape behind every impact sound.</summary>
        public static float Punch(float t, float decay) => Mathf.Exp(-t / Mathf.Max(0.0005f, decay));

        /// <summary>Attack-decay-sustain-release, for sustained cues and musical notes.</summary>
        public static float Adsr(float t, float dur, float a, float d, float s, float r)
        {
            if (t < a) return t / Mathf.Max(1e-4f, a);
            if (t < a + d) return Mathf.Lerp(1f, s, (t - a) / Mathf.Max(1e-4f, d));
            if (t < dur - r) return s;
            return Mathf.Lerp(s, 0f, Mathf.Clamp01((t - (dur - r)) / Mathf.Max(1e-4f, r)));
        }

        // ---------------- oscillators ----------------

        public static float Saw(float phase) => 2f * (phase - Mathf.Floor(phase + 0.5f));
        public static float Square(float phase, float duty = 0.5f) => (phase - Mathf.Floor(phase)) < duty ? 1f : -1f;
        public static float Tri(float phase) => 1f - 4f * Mathf.Abs(Mathf.Round(phase - 0.25f) - (phase - 0.25f));
        public static float Sine(float phase) => Mathf.Sin(phase * Mathf.PI * 2f);

        /// <summary>A one-pole low-pass, used to shape noise into a material rather than hiss.</summary>
        public sealed class LowPass
        {
            private float _z;
            public float Process(float x, float cutoffHz)
            {
                var a = Mathf.Clamp01(1f - Mathf.Exp(-2f * Mathf.PI * cutoffHz / SampleRate));
                _z += a * (x - _z);
                return _z;
            }
        }

        public sealed class HighPass
        {
            private float _z;
            public float Process(float x, float cutoffHz)
            {
                var a = Mathf.Clamp01(1f - Mathf.Exp(-2f * Mathf.PI * cutoffHz / SampleRate));
                _z += a * (x - _z);
                return x - _z;
            }
        }

        /// <summary>Resonant band emphasis: gives noise a pitched "body" so metal sounds like metal.</summary>
        public sealed class Resonator
        {
            private float _y1, _y2;
            public float Process(float x, float freq, float q)
            {
                var w = 2f * Mathf.PI * freq / SampleRate;
                var r = Mathf.Clamp(1f - w / Mathf.Max(0.5f, q), 0f, 0.9995f);
                var y = x * (1f - r) + 2f * r * Mathf.Cos(w) * _y1 - r * r * _y2;
                _y2 = _y1; _y1 = y;
                return y;
            }
        }

        // ---------------- WAV ----------------

        public static void WriteWav(Clip clip, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            clip.DeClick();
            clip.Normalize();

            const int channels = 2;
            const int bits = 16;
            var samples = clip.Length;
            var dataBytes = samples * channels * bits / 8;

            using var stream = new FileStream(path, FileMode.Create);
            using var w = new BinaryWriter(stream);

            w.Write(new[] { 'R', 'I', 'F', 'F' });
            w.Write(36 + dataBytes);
            w.Write(new[] { 'W', 'A', 'V', 'E' });
            w.Write(new[] { 'f', 'm', 't', ' ' });
            w.Write(16);
            w.Write((short)1);                                   // PCM
            w.Write((short)channels);
            w.Write(SampleRate);
            w.Write(SampleRate * channels * bits / 8);           // byte rate
            w.Write((short)(channels * bits / 8));               // block align
            w.Write((short)bits);
            w.Write(new[] { 'd', 'a', 't', 'a' });
            w.Write(dataBytes);

            for (var i = 0; i < samples; i++)
            {
                w.Write((short)(Mathf.Clamp(clip.L[i], -1f, 1f) * 32760f));
                w.Write((short)(Mathf.Clamp(clip.R[i], -1f, 1f) * 32760f));
            }
        }

        // ---------------- musical helpers ----------------

        /// <summary>Semitone offset from A4 to frequency. Music and stingers are written in semitones, not hertz.</summary>
        public static float Note(int semitonesFromA4) => 440f * Mathf.Pow(2f, semitonesFromA4 / 12f);

        /// <summary>Natural minor scale degrees, the tonality the whole soundtrack sits in.</summary>
        public static readonly int[] MinorScale = { 0, 2, 3, 5, 7, 8, 10 };

        public static int ScaleNote(int root, int degree)
        {
            var octave = Mathf.FloorToInt(degree / 7f);
            var index = ((degree % 7) + 7) % 7;
            return root + MinorScale[index] + octave * 12;
        }

        /// <summary>Plays one tuned note into the buffer with a chosen timbre.</summary>
        public static void PlayNote(Clip clip, float startSec, float durSec, float freq, float gain,
            Func<float, float> osc, float attack = 0.01f, float decay = 0.1f, float sustain = 0.6f, float release = 0.15f,
            float pan = 0f)
        {
            var start = Mathf.RoundToInt(startSec * SampleRate);
            var n = Mathf.RoundToInt(durSec * SampleRate);
            var phase = 0f;
            var lp = new LowPass();

            for (var i = 0; i < n; i++)
            {
                var t = (float)i / SampleRate;
                phase += freq / SampleRate;
                var env = Adsr(t, durSec, attack, decay, sustain, release);
                // Gentle low-pass tracking the envelope: notes open up as they attack and close as they die.
                var s = lp.Process(osc(phase), 800f + 3200f * env) * env * gain;
                clip.Add(start + i, s * (1f - Mathf.Max(0f, pan)), s * (1f + Mathf.Min(0f, pan)));
            }
        }
    }
}
