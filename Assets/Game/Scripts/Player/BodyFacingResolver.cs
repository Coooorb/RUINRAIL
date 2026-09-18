using UnityEngine;

namespace RuinRail.Gameplay.Player
{
    public static class BodyFacingResolver
    {
        public static BodyFacing8 Resolve(Vector2 direction)
        {
            var angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            if (angle < 0f)
            {
                angle += 360f;
            }

            var sector = Mathf.RoundToInt(angle / 45f) % 8;
            return (BodyFacing8)sector;
        }
    }
}
