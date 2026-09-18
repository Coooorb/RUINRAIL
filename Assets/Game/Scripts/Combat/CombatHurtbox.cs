using UnityEngine;

namespace RuinRail.Gameplay.Combat
{
    /// <summary>
    /// The combat silhouette of an actor: a trigger box on a child that projectiles, melee arcs and area effects
    /// resolve to the actor's <see cref="IDamageable"/> (every hit path looks the damageable up through the parent
    /// chain), separate from the small feet-level body collider that handles movement and world collision.
    ///
    /// Why it exists: a character sprite stands ~1.5 tiles tall from its feet, but the body collider is a 0.35–0.8
    /// tile circle at the feet — so a crosshair placed on the visible torso pointed a projectile at empty space above
    /// the only collider. The hurtbox covers the body conservatively (narrower than the sprite, up to the shoulders),
    /// and its centre is the aim point every assisted or direct shot targets (spec: hurtbox centre, never the sprite
    /// corner or the health bar).
    /// </summary>
    public sealed class CombatHurtbox : MonoBehaviour
    {
        public const string ChildName = "Hurtbox";

        // Conservative silhouettes for the three actor classes (art/106 §4: player/normal ~32x48 px, Elite larger, Boss
        // significantly larger). Width stays inside the drawn body; height stops below the top of the head.
        public static readonly Vector2 NormalSize = new(0.7f, 1.2f);
        public static readonly Vector2 NormalOffset = new(0f, 0.6f);
        public static readonly Vector2 EliteSize = new(0.9f, 1.6f);
        public static readonly Vector2 EliteOffset = new(0f, 0.8f);
        public static readonly Vector2 BossSize = new(1.5f, 2.2f);
        public static readonly Vector2 BossOffset = new(0f, 1.1f);

        [SerializeField] private BoxCollider2D _box;

        public BoxCollider2D Box => _box;
        public Vector2 Size => _box != null ? _box.size : Vector2.zero;
        public Vector2 Offset => _box != null ? (Vector2)_box.transform.localPosition + _box.offset : Vector2.zero;

        /// <summary>World-space centre of the hurtbox: the point a shot should be aimed at.</summary>
        public Vector2 AimPoint => _box != null ? (Vector2)_box.bounds.center : (Vector2)transform.position;

        /// <summary>True when a world point lies inside the hurtbox.</summary>
        public bool Contains(Vector2 world) => _box != null && _box.OverlapPoint(world);

        /// <summary>Attaches (or returns) the hurtbox of an actor. Idempotent.</summary>
        public static CombatHurtbox Attach(GameObject actor, Vector2 size, Vector2 offset)
        {
            var existing = actor.GetComponent<CombatHurtbox>();
            if (existing != null && existing._box != null) return existing;
            var hurtbox = existing != null ? existing : actor.AddComponent<CombatHurtbox>();
            var child = actor.transform.Find(ChildName);
            if (child == null)
            {
                child = new GameObject(ChildName).transform;
                child.SetParent(actor.transform, false);
            }

            child.localPosition = new Vector3(offset.x, offset.y, 0f);
            child.localRotation = Quaternion.identity;
            var box = child.GetComponent<BoxCollider2D>();
            if (box == null) box = child.gameObject.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.size = size;
            box.offset = Vector2.zero;
            hurtbox._box = box;
            return hurtbox;
        }

        /// <summary>The hurtbox an arbitrary collider belongs to (its actor's), or null.</summary>
        public static CombatHurtbox Of(Component collider) => collider != null ? collider.GetComponentInParent<CombatHurtbox>() : null;
    }
}
