using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Core.Input;
using RuinRail.Gameplay.Expedition;
using UnityEngine;

namespace RuinRail.Gameplay.Player
{
    /// <summary>
    /// Dead spectator follow (85): while this player is Dead the follow target is a living teammate (Interact cycles);
    /// otherwise the player follows itself. The camera rig reads FollowPosition, which is never a free position.
    /// </summary>
    public sealed class DeadSpectatorFollow : MonoBehaviour
    {
        private PlayerLifeStateComponent _life;
        private IPlayerInputReader _inputReader;
        private SpectatorTargetSelector _selector;

        public bool IsSpectating => _life != null && _life.IsDead && _selector != null && _selector.Current != null;
        public SpectatorTargetSelector Selector => _selector;
        public Transform FollowTarget => IsSpectating ? _selector.Current.transform : transform;
        public Vector2 FollowPosition => FollowTarget.position;

        public void SetInputReader(IPlayerInputReader reader)
        {
            if (_inputReader != null) _inputReader.Interact -= OnInteract;
            _inputReader = reader;
            if (_inputReader != null) _inputReader.Interact += OnInteract;
        }

        private void Awake()
        {
            _life = GetComponent<PlayerLifeStateComponent>();
            if (_inputReader == null) SetInputReader(GetComponent<PlayerInput>()?.Reader);
            if (_life != null) _life.StateChanged += OnStateChanged;
            EnsureSelector();
        }

        private void OnDestroy()
        {
            SetInputReader(null);
            if (_life != null) _life.StateChanged -= OnStateChanged;
        }

        private void EnsureSelector()
        {
            if (_selector == null && _life != null && _life.Roster != null) _selector = new SpectatorTargetSelector(_life.Roster, _life);
        }

        private void OnStateChanged(PlayerLifeStateComponent _, PlayerLifeState from, PlayerLifeState to)
        {
            EnsureSelector();
            if (to == PlayerLifeState.Dead) _selector?.Refresh();
        }

        /// <summary>Cycles to the next living teammate (Interact while Dead; no other input is consumed).</summary>
        public void CycleNext()
        {
            EnsureSelector();
            if (_life == null || !_life.IsDead || _selector == null) return;
            _selector.CycleNext();
        }

        private void OnInteract() => CycleNext();

        private void Update()
        {
            if (IsSpectating || (_life != null && _life.IsDead)) _selector?.Refresh();
        }
    }
}
