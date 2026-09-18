using RuinRail.Core.Rendering;
using UnityEngine;

namespace RuinRail.Presentation.Animation
{
    /// <summary>
    /// The one runtime seam that gives a character body its sprite: a "Body" child with a SpriteRenderer on the
    /// Characters sorting layer (y-sorted at the feet, art/102) and a SpriteAnimator bound to the actor's
    /// CharacterAnimationSet. The animation drivers (PlayerAnimationDriver / EnemyAnimationDriver) only choose clips;
    /// without this composition they had nothing to draw into and the actor was an invisible collider.
    /// </summary>
    public static class CharacterVisual
    {
        public const string BodyName = "Body";
        public const string PlayerActorId = "player";

        /// <summary>Attaches (or returns) the body renderer + animator for an actor; the set may be null while art is missing.</summary>
        public static SpriteAnimator Attach(GameObject actor, CharacterAnimationSet set, SortingRole role = SortingRole.Character)
        {
            if (actor == null) return null;
            var existing = actor.GetComponentInChildren<SpriteAnimator>(true);
            if (existing != null)
            {
                if (set != null && existing.Set == null) existing.Configure(existing.Renderer, set);
                return existing;
            }

            var body = new GameObject(BodyName);
            body.transform.SetParent(actor.transform, false);
            body.transform.localPosition = Vector3.zero;
            var renderer = body.AddComponent<SpriteRenderer>();
            renderer.color = Color.white;
            renderer.enabled = true;
            // The sheets are imported with a feet pivot, so the body's own position is the feet position for sorting.
            SpriteSorting.Attach(renderer, role);
            var animator = body.AddComponent<SpriteAnimator>();
            animator.Configure(renderer, set);
            if (set == null) Debug.LogWarning($"{actor.name}: no CharacterAnimationSet bound; the body renderer has no sprite to show.");
            return animator;
        }

        /// <summary>The body renderer of an actor composed through <see cref="Attach"/>, or null.</summary>
        public static SpriteRenderer RendererOf(GameObject actor)
        {
            var animator = actor != null ? actor.GetComponentInChildren<SpriteAnimator>(true) : null;
            return animator != null ? animator.Renderer : null;
        }
    }
}
