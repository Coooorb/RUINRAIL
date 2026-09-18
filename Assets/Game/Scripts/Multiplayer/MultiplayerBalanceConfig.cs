using UnityEngine;

namespace RuinRail.Networking
{
    /// <summary>Shared multiplayer tunables (85): the reconnect grace is the only approved number here.</summary>
    [CreateAssetMenu(fileName = "MultiplayerBalanceConfig", menuName = "RuinRail/Multiplayer/Multiplayer Balance Config")]
    public sealed class MultiplayerBalanceConfig : ScriptableObject
    {
        [Tooltip("85: initial reconnect grace target ~60 s; a disconnected character stays represented and at risk this long.")]
        [SerializeField, Min(0f)] private float _reconnectGraceSeconds = 60f;

        public float ReconnectGraceSeconds => _reconnectGraceSeconds;
    }
}
