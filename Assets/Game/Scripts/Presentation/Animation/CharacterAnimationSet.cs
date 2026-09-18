using System.Collections.Generic;
using System.Linq;
using RuinRail.Gameplay.Player;
using UnityEngine;

namespace RuinRail.Presentation.Animation
{
    /// <summary>The authored clips of one character/actor. Missing entries are reported, never thrown (art assets are external).</summary>
    [CreateAssetMenu(menuName = "RuinRail/Presentation/Character Animation Set", fileName = "Anim_Character")]
    public sealed class CharacterAnimationSet : ScriptableObject
    {
        [SerializeField] private string _actorId = "";
        [SerializeField] private List<SpriteAnimationClip> _clips = new();

        public string ActorId => _actorId;
        public IReadOnlyList<SpriteAnimationClip> Clips => _clips;

        public void Configure(string actorId, IEnumerable<SpriteAnimationClip> clips)
        {
            _actorId = actorId;
            _clips = clips?.ToList() ?? new List<SpriteAnimationClip>();
        }

        public bool TryGet(string key, BodyFacing8 facing, out SpriteAnimationClip clip)
        {
            clip = _clips.FirstOrDefault(c => c != null && c.Key == key && c.Facing == facing && c.HasFrames);
            return clip != null;
        }

        /// <summary>Clip ids required by <paramref name="keys"/> for all 8 facings that this set does not provide.</summary>
        public IReadOnlyList<string> Missing(IEnumerable<string> keys)
        {
            var missing = new List<string>();
            foreach (var key in keys)
            {
                foreach (var facing in AnimationRules.AllFacings)
                {
                    if (!TryGet(key, facing, out _)) missing.Add(AnimationRules.ClipId(key, facing));
                }
            }

            return missing;
        }
    }
}
