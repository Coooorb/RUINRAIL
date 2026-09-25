using System.Collections.Generic;
using RuinRail.Gameplay.Items.Passives;
using UnityEngine;

namespace RuinRail.App
{
    /// <summary>
    /// Drives the Legendary equipment passives of one body every frame (their cooldowns and timed buffs, items/28 + 34).
    /// The registrar was composed but never ticked, so Anchored's 8 s cooldown never elapsed after its first negation and
    /// every timed passive froze once triggered. Scaled time: a paused solo world pauses the passives with it.
    /// </summary>
    public sealed class EquipmentPassiveTicker : MonoBehaviour
    {
        private readonly List<EquipmentPassiveRegistrar> _registrars = new();

        public static EquipmentPassiveTicker On(GameObject body)
        {
            var ticker = body.GetComponent<EquipmentPassiveTicker>();
            return ticker != null ? ticker : body.AddComponent<EquipmentPassiveTicker>();
        }

        public void Add(EquipmentPassiveRegistrar registrar)
        {
            if (registrar != null && !_registrars.Contains(registrar)) _registrars.Add(registrar);
        }

        public void Remove(EquipmentPassiveRegistrar registrar) => _registrars.Remove(registrar);

        public int Count => _registrars.Count;

        private void Update()
        {
            var dt = Time.deltaTime;
            if (dt <= 0f) return;
            foreach (var registrar in _registrars) registrar.Tick(dt);
        }
    }
}
