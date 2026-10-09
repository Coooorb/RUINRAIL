using System.Collections;
using System.Threading.Tasks;
using NUnit.Framework;
using RuinRail.Networking;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests.PlayMode
{
    /// <summary>
    /// The live Sessions package starts NetworkManager itself while a Relay session is created/joined. The driver
    /// must adopt that endpoint: refusing it made every live HOST CO-OP tear its own session down into Failed, with
    /// "Leave your current session…" on screen and LEAVE SESSION disabled.
    /// </summary>
    public sealed class NgoNetworkDriverAdoptionTests
    {
        private GameObject _go;
        private NetworkManager _manager;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("NgoDriverAdoptionFixture");
            _manager = _go.AddComponent<NetworkManager>();
            var transport = _go.AddComponent<UnityTransport>();
            transport.SetConnectionData("127.0.0.1", (ushort)Random.Range(42000, 43000), "127.0.0.1");
            _manager.NetworkConfig = new NetworkConfig { NetworkTransport = transport, EnableSceneManagement = false };
        }

        [TearDown]
        public void TearDown()
        {
            if (_manager != null && _manager.IsListening) _manager.Shutdown();
            Object.DestroyImmediate(_go);
        }

        private static T Wait<T>(Task<T> task)
        {
            task.Wait();
            return task.Result;
        }

        [UnityTest]
        public IEnumerator HostAlreadyStartedBySessions_IsAdopted_AndTheTerminalReachesInSession()
        {
            Assert.IsTrue(_manager.StartHost(), "fixture: the Sessions-started host endpoint");
            yield return null;

            var driver = new NgoNetworkDriver(_manager);
            Assert.IsFalse(driver.StartClient().Success, "a host endpoint is never adopted as a client");

            var controller = new NetworkSessionController(new FakeMultiplayerServices(), driver);
            var terminal = new MultiplayerTerminalService(controller);
            var error = Wait(terminal.HostCoopAsync());

            Assert.IsTrue(error.IsNone, $"hosting must succeed over the running endpoint, got: {error.Message}");
            Assert.AreEqual(TerminalState.InSession, terminal.State);
            Assert.IsTrue(terminal.IsInSession, "LEAVE SESSION is enabled while in session");
            Assert.IsTrue(_manager.IsHost, "the adopted endpoint keeps running");

            var left = Wait(terminal.LeaveAsync());
            Assert.IsTrue(left.IsNone);
            Assert.AreEqual(TerminalState.Idle, terminal.State);
            Assert.IsTrue(_manager.ShutdownInProgress || !_manager.IsListening, "leaving shuts the endpoint down");
            yield return null;
            Assert.IsFalse(_manager.IsListening, "NGO completes the shutdown on the next frame");
            Assert.AreEqual(NetworkLifecycleState.Offline, controller.State, "HOST CO-OP is available again after leaving");
        }
    }
}
