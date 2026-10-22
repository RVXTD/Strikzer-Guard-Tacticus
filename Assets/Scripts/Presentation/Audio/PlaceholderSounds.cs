// Written by Claude (Anthropic), October 2026; to be reviewed by Alexander Stangl per the course syllabus.

using System;
using System.Collections.Generic;
using Game.Rules;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Short placeholder sounds synthesised in code (noise bursts, decaying tones, sweeps and chimes), one per event kind
    /// that makes a sound, so the encounter is audible before real clips exist. Each is built once from a fixed seed and
    /// cached; <see cref="SoundDictionary"/> entries with clips of their own never use them.
    /// </summary>
    public static class PlaceholderSounds
    {
        private const int sampleRate = 22050;
        private static readonly Dictionary<EventKind, AudioClip> cache = new Dictionary<EventKind, AudioClip>();

        /// <summary>The placeholder for an event kind, or null if the kind has none.</summary>
        public static AudioClip For(EventKind kind)
        {
            if (cache.TryGetValue(kind, out AudioClip cached) && cached != null) return cached;
            float[] samples = synthesise(kind);
            if (samples == null) return null;
            AudioClip clip = AudioClip.Create($"Placeholder {kind}", samples.Length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            clip.hideFlags = HideFlags.DontSave;
            cache[kind] = clip;
            return clip;
        }

        private static float[] synthesise(EventKind kind)
        {
            var noise = new System.Random(1000 + (int)kind);
            switch (kind)
            {
                case EventKind.RoundHit: // a crack, then a thud
                    return mix(0.25f, b => { burst(b, noise, 0f, 0.06f, 0.6f, 60f); tone(b, 0.03f, 0.15f, 140f, 80f, 0.8f, 25f); });
                case EventKind.RoundMissed: // a crack, then a whiz
                    return mix(0.25f, b => { burst(b, noise, 0f, 0.06f, 0.6f, 60f); tone(b, 0.04f, 0.14f, 1800f, 700f, 0.15f, 12f); });
                case EventKind.UnitDamaged: // a blast
                    return mix(0.5f, b => { burst(b, noise, 0f, 0.45f, 0.8f, 8f); tone(b, 0f, 0.4f, 70f, 40f, 0.7f, 6f); });
                case EventKind.UnitDowned:
                    return mix(0.45f, b => tone(b, 0f, 0.42f, 440f, 110f, 0.5f, 4f));
                case EventKind.UnitDefeated:
                    return mix(0.5f, b => { burst(b, noise, 0f, 0.3f, 0.5f, 10f); tone(b, 0f, 0.45f, 300f, 60f, 0.5f, 5f); });
                case EventKind.UnitRevived:
                    return mix(0.4f, b => { tone(b, 0f, 0.15f, 330f, 330f, 0.4f, 8f); tone(b, 0.15f, 0.2f, 660f, 660f, 0.4f, 8f); });
                case EventKind.AbilityUsed:
                    return mix(0.3f, b => tone(b, 0f, 0.25f, 200f, 800f, 0.35f, 6f));
                case EventKind.ZonePlaced:
                    return mix(0.3f, b => { tone(b, 0f, 0.1f, 880f, 880f, 0.3f, 10f); tone(b, 0.12f, 0.12f, 660f, 660f, 0.3f, 10f); });
                case EventKind.MoveInterrupted:
                    return mix(0.3f, b => { tone(b, 0f, 0.08f, 1000f, 1000f, 0.35f, 6f); tone(b, 0.12f, 0.08f, 1000f, 1000f, 0.35f, 6f); });
                case EventKind.StatusApplied:
                    return mix(0.3f, b => tone(b, 0f, 0.25f, 110f, 100f, 0.35f, 5f, square: true));
                case EventKind.AmmoPickedUp:
                    return mix(0.25f, b => { tone(b, 0f, 0.1f, 990f, 990f, 0.3f, 12f); tone(b, 0.07f, 0.15f, 1320f, 1320f, 0.3f, 12f); });
                case EventKind.AmmoTraded:
                    return mix(0.25f, b => { tone(b, 0f, 0.1f, 660f, 660f, 0.3f, 12f); tone(b, 0.07f, 0.15f, 990f, 990f, 0.3f, 12f); });
                case EventKind.AmmoScattered:
                    return mix(0.05f, b => tone(b, 0f, 0.04f, 2400f, 2000f, 0.25f, 60f));
                case EventKind.TurnStarted:
                    return mix(0.12f, b => tone(b, 0f, 0.1f, 520f, 560f, 0.3f, 18f));
                default:
                    return null;
            }
        }

        private static float[] mix(float seconds, Action<float[]> write)
        {
            var buffer = new float[Mathf.CeilToInt(seconds * sampleRate)];
            write(buffer);
            for (int i = 0; i < buffer.Length; i++) buffer[i] = Mathf.Clamp(buffer[i], -1f, 1f);
            return buffer;
        }

        // A sine (or square) sweeping from f0 to f1 Hz, starting at `start` seconds, fading as exp(-decay * t).
        private static void tone(float[] buffer, float start, float duration, float f0, float f1, float amplitude, float decay, bool square = false)
        {
            int first = (int)(start * sampleRate), count = Mathf.Min(buffer.Length - first, (int)(duration * sampleRate));
            double phase = 0;
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / sampleRate, progress = (float)i / Mathf.Max(1, count);
                phase += 2.0 * Math.PI * Mathf.Lerp(f0, f1, progress) / sampleRate;
                float wave = (float)Math.Sin(phase);
                if (square) wave = Mathf.Sign(wave) * 0.6f;
                float attack = Mathf.Clamp01(t / 0.004f); // avoid clicks
                buffer[first + i] += wave * amplitude * attack * Mathf.Exp(-decay * t);
            }
        }

        // White noise starting at `start` seconds, fading as exp(-decay * t).
        private static void burst(float[] buffer, System.Random noise, float start, float duration, float amplitude, float decay)
        {
            int first = (int)(start * sampleRate), count = Mathf.Min(buffer.Length - first, (int)(duration * sampleRate));
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / sampleRate;
                buffer[first + i] += (float)(noise.NextDouble() * 2.0 - 1.0) * amplitude * Mathf.Exp(-decay * t);
            }
        }
    }
}
