using UnityEngine;

namespace RuinRail.Gameplay.Combat
{
    /// <summary>
    /// Short post-revive invulnerability (84: ~1.5 s), composed into HealthComponent like the dash iFrames: one more
    /// IInvulnerabilityState on the object, expiring on its own; damage applies normally afterwards.
    /// </summary>
    public sealed class ReviveProtection : MonoBehaviour, IInvulnerabilityState
    {
        public float Remaining { get; private set; }
        public bool IsInvulnerable => Remaining > 0f;
        public int Activations { get; private set; }

        public void Begin(float seconds)
        {
            Remaining = Mathf.Max(Remaining, Mathf.Max(0f, seconds));
            Activations++;
        }

        public void Tick(float deltaTime)
        {
            if (Remaining <= 0f) return;
            Remaining = Mathf.Max(0f, Remaining - Mathf.Max(0f, deltaTime));
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }
    }
}
