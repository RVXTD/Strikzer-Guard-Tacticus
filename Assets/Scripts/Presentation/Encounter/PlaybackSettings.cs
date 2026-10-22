// Written by Claude (Anthropic), October 2026; the aim beat added for step 8b; to be reviewed by Alexander Stangl
// per the course syllabus.

using System;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>How fast <see cref="EventPlayback"/> plays the event log back.</summary>
    [Serializable]
    public struct PlaybackSettings
    {
        /// <summary>Walking speed of a moving unit, in metres per second.</summary>
        [Min(0.1f)] public float MoveSpeed;
        /// <summary>Pause for an attack or ability starting, in seconds.</summary>
        [Min(0f)] public float ShotBeat;
        /// <summary>Extra pause before an attack or ability resolves, in seconds, while the camera frames it.</summary>
        [Min(0f)] public float AimBeat;
        /// <summary>Pause for each round that hits or misses, and for damage, in seconds.</summary>
        [Min(0f)] public float HitBeat;
        /// <summary>Pause when a unit is downed or defeated, in seconds.</summary>
        [Min(0f)] public float DownBeat;

        /// <summary>Starting values.</summary>
        public static PlaybackSettings Default => new PlaybackSettings { MoveSpeed = 6f, ShotBeat = 0.12f, AimBeat = 0.3f, HitBeat = 0.15f, DownBeat = 0.4f };
    }
}
