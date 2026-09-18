using System;
using RuinRail.Core.Input;
using UnityEngine;

namespace RuinRail.Gameplay.Player
{
    public sealed class PlayerInput : MonoBehaviour
    {
        public IPlayerInputReader Reader { get; private set; }

        private void Awake()
        {
            Reader = new PlayerInputReader();
        }

        private void OnEnable()
        {
            Reader.Enable();
        }

        private void OnDisable()
        {
            Reader.Disable();
        }

        private void OnDestroy()
        {
            (Reader as IDisposable)?.Dispose();
        }
    }
}
