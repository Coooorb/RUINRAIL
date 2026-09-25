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

        /// <summary>
        /// Replaces the device reader with a scripted one (proof/diagnostic runs: the built-player co-op peer harness
        /// drives its own player without a keyboard). It changes only where this owner's intents come from — the host
        /// still validates every intent it receives (82), so nothing about authority moves with it.
        /// </summary>
        public void UseReader(IPlayerInputReader reader)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            if (ReferenceEquals(reader, Reader)) return;
            Reader.Disable();
            (Reader as IDisposable)?.Dispose();
            Reader = reader;
            if (isActiveAndEnabled) Reader.Enable();
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
