using RuinRail.Gameplay.Enemies.Attacks;
using UnityEngine;

namespace RuinRail.Gameplay.Enemies.Elites
{
    /// <summary>
    /// Elite mini-boss actor: the shared moveset FSM with one fixed moveset from start to finish
    /// (45_ELITES: no phases, no random modifiers).
    /// </summary>
    public sealed class EliteController : MovesetActorController
    {
        [SerializeField] private EliteDefinition _definition;

        public EliteDefinition Definition => _definition;

        protected override IMovesetActorDefinition ActorDefinition => _definition;

        public void SetDefinition(EliteDefinition definition)
        {
            _definition = definition;
            ApplyDefinition();
        }
    }
}
