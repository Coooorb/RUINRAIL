using RuinRail.Gameplay.Enemies.Bosses;
using UnityEngine;

namespace RuinRail.Gameplay.Loot
{
    /// <summary>
    /// Boss Cache rule (58/46): the cache is locked until the bound boss encounter reports the boss defeated; it then
    /// opens exactly once like every other source. Binding is explicit so the room runtime wires it, not a scene search.
    /// </summary>
    [RequireComponent(typeof(SupplyChest))]
    public sealed class BossCacheGate : MonoBehaviour
    {
        private SupplyChest _chest;
        private BossEncounter _encounter;

        public SupplyChest Chest => _chest != null ? _chest : _chest = GetComponent<SupplyChest>();
        public bool IsUnlocked => !Chest.IsLocked;

        private void Awake()
        {
            Chest.SetLocked(true);
        }

        public void Bind(BossEncounter encounter)
        {
            Unbind();
            _encounter = encounter;
            Chest.SetLocked(true);
            if (_encounter == null) return;
            if (_encounter.IsDefeated)
            {
                Chest.SetLocked(false);
                return;
            }

            _encounter.BossDefeated += OnBossDefeated;
        }

        private void OnBossDefeated(BossEncounter encounter, int xp)
        {
            Chest.SetLocked(false);
            Unbind();
        }

        private void Unbind()
        {
            if (_encounter != null) _encounter.BossDefeated -= OnBossDefeated;
            _encounter = null;
        }

        private void OnDestroy()
        {
            Unbind();
        }
    }
}
