using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Gameplay.Player;
using UnityEngine;

namespace RuinRail.Presentation.Animation
{
    /// <summary>art/103: sprite animation runs around 8–12 fps while the simulation stays smooth; nothing here owns gameplay timing.</summary>
    public static class AnimationRules
    {
        public const int MinFps = 8;
        public const int MaxFps = 12;
        public const int DefaultFps = 10;

        public static int ClampFps(int fps) => Mathf.Clamp(fps, MinFps, MaxFps);

        /// <summary>Clip keys the player body needs (art/103 baseline).</summary>
        public static readonly string[] PlayerClipKeys = { "Idle", "Walk", "Dash", "Downed", "GetUp", "Death" };

        /// <summary>Clip keys every enemy, Elite and Boss needs: telegraph is the one that matters most (art/103).</summary>
        public static readonly string[] EnemyClipKeys = { "Idle", "Move", "Telegraph", "Attack", "Recover", "Death" };

        public static readonly BodyFacing8[] AllFacings = (BodyFacing8[])Enum.GetValues(typeof(BodyFacing8));

        public static string ClipId(string key, BodyFacing8 facing) => key + "/" + facing;
    }

    /// <summary>One clip: frames for one state and one of the 8 body facings.</summary>
    [Serializable]
    public sealed class SpriteAnimationClip
    {
        public string Key = "Idle";
        public BodyFacing8 Facing = BodyFacing8.S;
        public Sprite[] Frames = Array.Empty<Sprite>();
        [Range(AnimationRules.MinFps, AnimationRules.MaxFps)] public int FramesPerSecond = AnimationRules.DefaultFps;
        public bool Loop = true;

        public bool HasFrames => Frames != null && Frames.Length > 0 && Frames.Any(f => f != null);
        public string Id => AnimationRules.ClipId(Key, Facing);
    }

    /// <summary>
    /// Frame stepper over a SpriteRenderer. Plays what the set provides at the clip's 8–12 fps; when a clip is missing
    /// it keeps the last frame (or shows nothing) and records the request — the explicit placeholder path, never an
    /// exception. Time advances from the caller (Update or tests); gameplay never waits for it.
    /// </summary>
    public sealed class SpriteAnimator : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer _renderer;
        [SerializeField] private CharacterAnimationSet _set;

        private SpriteAnimationClip _clip;
        private float _time;
        private readonly HashSet<string> _missing = new();

        public SpriteRenderer Renderer => _renderer;
        public CharacterAnimationSet Set => _set;
        public string CurrentKey { get; private set; } = string.Empty;
        public BodyFacing8 CurrentFacing { get; private set; } = BodyFacing8.S;
        public int CurrentFrame { get; private set; }
        public bool IsPlaceholder => _clip == null;
        public bool IsFinished => _clip != null && !_clip.Loop && CurrentFrame >= _clip.Frames.Length - 1;
        public int EffectiveFps => _clip == null ? 0 : AnimationRules.ClampFps(_clip.FramesPerSecond);
        public IReadOnlyCollection<string> MissingClips => _missing;
        public int Plays { get; private set; }
        /// <summary>False when a driver advances the animator itself (avoids a double tick per frame).</summary>
        public bool SelfTicking { get; set; } = true;

        public void Configure(SpriteRenderer renderer, CharacterAnimationSet set)
        {
            _renderer = renderer;
            _set = set;
        }

        /// <summary>Switches clip when the key/facing changed (a restart only on a key change, so facing turns keep the frame phase).</summary>
        public void Play(string key, BodyFacing8 facing)
        {
            if (key == CurrentKey && facing == CurrentFacing && (_clip != null || _missing.Contains(AnimationRules.ClipId(key, facing)))) return;
            var keyChanged = key != CurrentKey;
            CurrentKey = key;
            CurrentFacing = facing;
            if (_set != null && _set.TryGet(key, facing, out var clip))
            {
                _clip = clip;
                if (keyChanged) { _time = 0f; CurrentFrame = 0; }
                else CurrentFrame = Mathf.Min(CurrentFrame, clip.Frames.Length - 1);
                Apply();
            }
            else
            {
                _clip = null;
                _missing.Add(AnimationRules.ClipId(key, facing));
            }

            Plays++;
        }

        public void Tick(float deltaTime)
        {
            if (_clip == null || _clip.Frames.Length == 0) return;
            _time += Mathf.Max(0f, deltaTime);
            var frameDuration = 1f / EffectiveFps;
            var advanced = (int)(_time / frameDuration);
            if (advanced <= 0) return;
            _time -= advanced * frameDuration;
            var next = CurrentFrame + advanced;
            CurrentFrame = _clip.Loop ? next % _clip.Frames.Length : Mathf.Min(next, _clip.Frames.Length - 1);
            Apply();
        }

        private void Apply()
        {
            if (_renderer != null && _clip != null && CurrentFrame < _clip.Frames.Length) _renderer.sprite = _clip.Frames[CurrentFrame];
        }

        private void Update()
        {
            if (SelfTicking) Tick(Time.deltaTime);
        }
    }
}
