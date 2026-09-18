using UnityEngine;

namespace RuinRail.Persistence
{
    /// <summary>
    /// End-of-frame flush for the autosave: writes only when a safe point marked the slot dirty this frame, so several
    /// mutations in one frame become one write and a quiet frame costs nothing. Also flushes on application pause/quit.
    /// </summary>
    public sealed class AutosaveFlusher : MonoBehaviour
    {
        private AutosaveService _autosave;

        public void SetAutosave(AutosaveService autosave)
        {
            _autosave = autosave;
        }

        private void LateUpdate()
        {
            if (_autosave != null && _autosave.IsDirty) _autosave.Flush();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) _autosave?.Flush();
        }

        private void OnApplicationQuit()
        {
            _autosave?.Flush();
        }
    }
}
