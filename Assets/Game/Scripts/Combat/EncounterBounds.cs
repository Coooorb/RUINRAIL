using UnityEngine;

namespace RuinRail.Gameplay.Combat
{
    /// <summary>
    /// Authoritative room-bound context of one encounter actor: the world rectangle its collider must stay inside for
    /// as long as it lives (the interior of the room that owns its encounter — floor plus the doorway-free area inside
    /// the wall ring). Every mover consults it before committing a velocity, a dash endpoint or a knockback step, so the
    /// legal edge behaves like a wall the AI cannot path through: an actor pursuing a target that stands beyond the
    /// edge slides along it and holds there, it never crosses a doorway, and no open door (a lock still pending on a
    /// player in the doorway, an event room that never locks) is an exit. The body clamp in <see cref="FixedUpdate"/>
    /// is the last line — a sub-step correction for a physics push (a blocker engaging on an overlap), never the
    /// mechanism that keeps the actor in the room.
    /// </summary>
    public sealed class EncounterBounds : MonoBehaviour
    {
        private Rigidbody2D _body;
        private Rect _interior;
        private float _radius;
        private bool _bound;

        /// <summary>The room interior (world units) the actor's collider must stay inside.</summary>
        public Rect Interior => _interior;
        /// <summary>Where the body's centre may be: the interior inset by the body radius.</summary>
        public Rect Legal => Inset(_interior, _radius);
        public float BodyRadius => _radius;
        public bool IsBound => _bound;
        public string RoomId { get; private set; } = string.Empty;
        public int RoomNodeId { get; private set; } = -1;
        /// <summary>How often the body had to be pulled back (diagnostics; the movers should keep this at 0 in practice).</summary>
        public int Corrections { get; private set; }

        /// <summary>Binds (or rebinds) the actor to a room interior; the body is placed inside if it is not already.</summary>
        public static EncounterBounds Bind(GameObject actor, Rect interiorWorld, string roomId, int roomNodeId)
        {
            if (actor == null) return null;
            var bounds = actor.GetComponent<EncounterBounds>();
            if (bounds == null) bounds = actor.AddComponent<EncounterBounds>();
            bounds.SetInterior(interiorWorld, roomId, roomNodeId);
            return bounds;
        }

        public void SetInterior(Rect interiorWorld, string roomId, int roomNodeId)
        {
            _interior = interiorWorld;
            RoomId = roomId ?? string.Empty;
            RoomNodeId = roomNodeId;
            _radius = ResolveRadius();
            _bound = true;
            _body ??= GetComponent<Rigidbody2D>();
            var position = _body != null ? _body.position : (Vector2)transform.position;
            var clamped = ClampCenter(position);
            if (clamped != position)
            {
                if (_body != null) _body.position = clamped;
                transform.position = new Vector3(clamped.x, clamped.y, transform.position.z);
            }
        }

        /// <summary>The actor's solid circle collider radius, or a small default when it has none.</summary>
        private float ResolveRadius()
        {
            var circles = GetComponents<CircleCollider2D>();
            foreach (var circle in circles) if (circle != null && !circle.isTrigger) return circle.radius;
            return 0.35f;
        }

        public bool ContainsCenter(Vector2 center) => Legal.Contains(center);

        /// <summary>True while the whole collider is inside the interior (the invariant the tests assert).</summary>
        public bool ColliderInside(Vector2 center, float tolerance = 0.001f)
        {
            var legal = Legal;
            return center.x >= legal.xMin - tolerance && center.x <= legal.xMax + tolerance && center.y >= legal.yMin - tolerance && center.y <= legal.yMax + tolerance;
        }

        public Vector2 ClampCenter(Vector2 center)
        {
            var legal = Legal;
            return new Vector2(Mathf.Clamp(center.x, legal.xMin, legal.xMax), Mathf.Clamp(center.y, legal.yMin, legal.yMax));
        }

        /// <summary>
        /// The velocity the body may actually take this step: any component that would carry the centre past the legal
        /// edge is cut to exactly reach the edge (so the actor slides along it), a component pointing back inside is kept.
        /// </summary>
        public Vector2 ConstrainVelocity(Vector2 position, Vector2 velocity, float deltaTime)
        {
            if (!_bound || deltaTime <= 0f) return velocity;
            var legal = Legal;
            var next = position + velocity * deltaTime;
            if (velocity.x > 0f && next.x > legal.xMax) velocity.x = Mathf.Max(0f, legal.xMax - position.x) / deltaTime;
            else if (velocity.x < 0f && next.x < legal.xMin) velocity.x = -Mathf.Max(0f, position.x - legal.xMin) / deltaTime;
            if (velocity.y > 0f && next.y > legal.yMax) velocity.y = Mathf.Max(0f, legal.yMax - position.y) / deltaTime;
            else if (velocity.y < 0f && next.y < legal.yMin) velocity.y = -Mathf.Max(0f, position.y - legal.yMin) / deltaTime;
            return velocity;
        }

        /// <summary>How far the centre may travel from <paramref name="position"/> along <paramref name="direction"/> before the legal edge, capped.</summary>
        public float FreeDistance(Vector2 position, Vector2 direction, float maxDistance)
        {
            if (!_bound) return maxDistance;
            if (direction.sqrMagnitude < 0.0001f) return 0f;
            direction.Normalize();
            var legal = Legal;
            var free = maxDistance;
            if (direction.x > 0.0001f) free = Mathf.Min(free, (legal.xMax - position.x) / direction.x);
            else if (direction.x < -0.0001f) free = Mathf.Min(free, (legal.xMin - position.x) / direction.x);
            if (direction.y > 0.0001f) free = Mathf.Min(free, (legal.yMax - position.y) / direction.y);
            else if (direction.y < -0.0001f) free = Mathf.Min(free, (legal.yMin - position.y) / direction.y);
            return Mathf.Clamp(free, 0f, maxDistance);
        }

        private void Awake()
        {
            _body = GetComponent<Rigidbody2D>();
        }

        private void FixedUpdate()
        {
            if (!_bound || _body == null) return;
            var position = _body.position;
            var clamped = ClampCenter(position);
            if (clamped == position) return;
            Corrections++;
            _body.position = clamped;
            // The push that carried the body out must not keep carrying it.
            var velocity = _body.linearVelocity;
            if (clamped.x != position.x) velocity.x = 0f;
            if (clamped.y != position.y) velocity.y = 0f;
            _body.linearVelocity = velocity;
        }

        private static Rect Inset(Rect rect, float amount)
        {
            var width = Mathf.Max(0f, rect.width - amount * 2f);
            var height = Mathf.Max(0f, rect.height - amount * 2f);
            var x = width > 0f ? rect.xMin + amount : rect.center.x;
            var y = height > 0f ? rect.yMin + amount : rect.center.y;
            return new Rect(x, y, width, height);
        }
    }
}
