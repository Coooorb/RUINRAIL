using Unity.Netcode;
using UnityEngine;

namespace RuinRail.Networking
{
    /// <summary>
    /// Scene-level composition root for networking (not a global singleton): builds the session controller over the
    /// project's service boundary. Offline by default, so the solo game runs without any service; the live Unity
    /// adapters are used only when a NetworkManager is present and live services are enabled.
    /// </summary>
    public sealed class NetworkBootstrap : MonoBehaviour
    {
        [SerializeField] private NetworkManager _networkManager;
        [Tooltip("Use Unity Multiplayer Services + NGO. Off = in-memory fake (offline development, tests).")]
        [SerializeField] private bool _useLiveServices = true;

        private NetworkSessionController _controller;
        private IMultiplayerServices _services;
        private INetworkDriver _driver;

        public NetworkSessionController Controller => _controller ??= Build();
        public IMultiplayerServices Services => _services;
        public INetworkDriver Driver => _driver;

        /// <summary>Explicit composition for tests/boot code (skips the serialized wiring).</summary>
        public NetworkSessionController Configure(IMultiplayerServices services, INetworkDriver driver)
        {
            _services = services;
            _driver = driver;
            _controller = new NetworkSessionController(services, driver);
            _controller.StartOffline();
            return _controller;
        }

        private NetworkSessionController Build()
        {
            if (_networkManager == null) _networkManager = GetComponent<NetworkManager>();
            var live = _useLiveServices && _networkManager != null;
            _services = live ? new UnityMultiplayerServices() : new FakeMultiplayerServices();
            _driver = live ? new NgoNetworkDriver(_networkManager) : new FakeNetworkDriver();
            _controller = new NetworkSessionController(_services, _driver);
            _controller.StartOffline();
            return _controller;
        }

        private void Awake()
        {
            _ = Controller;
        }
    }
}
