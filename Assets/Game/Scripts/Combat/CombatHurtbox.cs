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

        // The player: the largest boxes that lie inside the drawn body in every live frame of every facing (measured from the
        // shipped sheet, 32 px per tile, feet pivot): the feet and legs (±4.5 px, 0–11 px above the feet) and the lower torso
        // (±3.5 px, 0–19 px), each half a pixel inside the drawn pixels. Nothing is drawn below the feet, so nothing there
        // can be hit: an enemy attack can only damage a player whose drawn body visibly touches its red area.
        public static readonly Vector2 PlayerFeetSize = new(9f / 32f, 11f / 32f);
        public static readonly Vector2 PlayerFeetOffset = new(0f, 5.5f / 32f);
        public static readonly Vector2 PlayerBodySize = new(7f / 32f, 19f / 32f);
        public static readonly Vector2 PlayerBodyOffset = new(0f, 9.5f / 32f);

        [SerializeField] private BoxCollider2D _box;
        [SerializeField] private BoxCollider2D _second;

        /// <summary>
        /// The actor is hit only through this hurtbox (players): its other colliders — the feet-level movement circle, its
        /// own shots in flight — are never a damage surface. Enemy hurtboxes are not exclusive.
        /// </summary>
        public bool Exclusive { get; private set; }

        /// <summary>The boxes of this hurtbox (one for enemies, two for players).</summary>
        public System.Collections.Generic.IEnumerable<BoxCollider2D> Boxes
        {
            get
            {
                if (_box != null) yield return _box;
                if (_second != null) yield return _second;
            }
        }

        /// <summary>True when the collider is one of this hurtbox's boxes.</summary>
        public bool Owns(Collider2D collider) => collider != null && (collider == _box || collider == _second);

        /// <summary>Attaches (or returns) a player's exclusive two-box hurtbox. Idempotent; every player composition calls it.</summary>
        public static CombatHurtbox AttachPlayer(GameObject player)
        {
            var hurtbox = Attach(player, PlayerFeetSize, PlayerFeetOffset);
            if (hurtbox._second == null)
            {
                var second = hurtbox._box.gameObject.AddComponent<BoxCollider2D>();
                second.isTrigger = true;
                second.size = PlayerBodySize;
                second.offset = PlayerBodyOffset - PlayerFeetOffset;
                hurtbox._second = second;
            }

            hurtbox.Exclusive = true;
            return hurtbox;
        }

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
