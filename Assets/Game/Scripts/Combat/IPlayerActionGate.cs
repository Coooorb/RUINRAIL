using UnityEngine;

namespace RuinRail.Gameplay.Combat
{
    /// <summary>
    /// Life-state gate for player actions (84/15): while Downed or Dead a player cannot attack, dash, use items, open the
    /// inventory or interact. Implemented by the player life-state component; consumers ask through ActionGateLookup.
    /// </summary>
    public interface IPlayerActionGate
    {
        bool CanAct { get; }
    }

    /// <summary>Lazy per-consumer lookup of the owner's action gate (searches parents so child weapon objects are covered).</summary>
    public sealed class ActionGateLookup
    {
        private IPlayerActionGate _gate;
        private bool _searched;

        public bool CanAct(Component owner)
        {
            if (!_searched || (_gate == null && owner != null))
            {
                _gate = owner != null ? owner.GetComponentInParent<IPlayerActionGate>() : null;
                _searched = true;
            }

            return _gate == null || _gate.CanAct;
        }
    }
}
