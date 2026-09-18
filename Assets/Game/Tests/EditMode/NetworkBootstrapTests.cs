using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using RuinRail.Networking;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>TASK 091: lifecycle states, service boundary with fakes, host-authority contract, dependency rules.</summary>
    public class NetworkBootstrapTests
    {
        [SetUp]
        public void SetUp() => FakeMultiplayerServices.ResetRegistry();

        private static (NetworkSessionController controller, FakeMultiplayerServices services, FakeNetworkDriver driver) Build()
        {
            var services = new FakeMultiplayerServices();
            var driver = new FakeNetworkDriver();
            return (new NetworkSessionController(services, driver), services, driver);
        }

        private static T Wait<T>(Task<T> task)
        {
            task.Wait();
            return task.Result;
        }

        // ---- Acceptance 1 + 2: offline / host / client modes without services in solo ----

        [Test]
        public void Offline_IsTheDefault_AndNeverTouchesServices()
        {
            var (controller, services, driver) = Build();
            services.IsAvailable = false;
            controller.StartOffline();
            Assert.AreEqual(NetworkLifecycleState.Offline, controller.State);
            Assert.AreEqual(NetworkRole.Offline, controller.Role);
            Assert.IsTrue(controller.IsHostAuthority, "Solo is authoritative over its own game.");
            Assert.AreEqual(0, services.InitializeCalls);
            Assert.IsFalse(driver.IsListening);
        }

        [Test]
        public void Host_CreatesAPrivateSessionWithAJoinCode_AndStartsTheHostEndpoint()
        {
            var (controller, services, driver) = Build();
            var states = new System.Collections.Generic.List<NetworkLifecycleState>();
            controller.StateChanged += (_, s) => states.Add(s);
            var result = Wait(controller.HostAsync(new SessionRequest(3)));

            Assert.IsTrue(result.Success, result.Message);
            Assert.AreEqual(NetworkLifecycleState.Connected, controller.State);
            Assert.AreEqual(NetworkRole.Host, controller.Role);
            Assert.IsTrue(controller.IsHostAuthority);
            Assert.IsNotNull(controller.Session);
            Assert.IsTrue(controller.Session.IsHost);
            Assert.AreEqual(6, controller.Session.JoinCode.Length);
            Assert.AreEqual(3, controller.Session.MaxPlayers);
            Assert.AreEqual(1, driver.HostStarts);
            Assert.AreEqual(1, services.InitializeCalls);
            CollectionAssert.AreEqual(new[] { NetworkLifecycleState.Initializing, NetworkLifecycleState.Hosting, NetworkLifecycleState.Connected }, states);
        }

        [Test]
        public void Client_JoinsByCode_AndCannotDecideAnything_ClientRoleIsNeverAuthority()
        {
            var (host, _, _) = Build();
            Wait(host.HostAsync(new SessionRequest(2)));
            var (client, _, clientDriver) = Build();

            var result = Wait(client.JoinAsync(host.Session.JoinCode.ToLowerInvariant()));
            Assert.IsTrue(result.Success, result.Message);
            Assert.AreEqual(NetworkLifecycleState.Connected, client.State);
            Assert.AreEqual(NetworkRole.Client, client.Role);
            Assert.IsFalse(client.IsHostAuthority);
            Assert.AreEqual(1, clientDriver.ClientStarts);
            Assert.AreEqual(host.Session.SessionId, client.Session.SessionId);
            Assert.AreEqual(2, client.Session.PlayerCount);

            foreach (AuthoritativeDomain domain in System.Enum.GetValues(typeof(AuthoritativeDomain)))
            {
                Assert.IsFalse(HostAuthorityContract.CanDecide(client.Authority, domain), domain.ToString());
                Assert.IsTrue(HostAuthorityContract.CanDecide(host.Authority, domain), domain.ToString());
                Assert.Throws<AuthorityViolationException>(() => HostAuthorityContract.Require(client.Authority, domain));
                Assert.DoesNotThrow(() => HostAuthorityContract.Require(host.Authority, domain));
                Assert.DoesNotThrow(() => HostAuthorityContract.Require(LocalAuthorityContext.Instance, domain));
            }
        }

        [Test]
        public void PartyLimit_IsThree_AndAFullSessionRefusesTheFourth()
        {
            var (host, _, _) = Build();
            Wait(host.HostAsync(new SessionRequest(99)));
            Assert.AreEqual(3, host.Session.MaxPlayers, "80: maximum 3 players total.");
            var (c1, _, _) = Build();
            var (c2, _, _) = Build();
            var (c3, _, _) = Build();
            Assert.IsTrue(Wait(c1.JoinAsync(host.Session.JoinCode)).Success);
            Assert.IsTrue(Wait(c2.JoinAsync(host.Session.JoinCode)).Success);
            var fourth = Wait(c3.JoinAsync(host.Session.JoinCode));
            Assert.IsFalse(fourth.Success);
            Assert.AreEqual(ServiceErrorKind.SessionFull, fourth.Error);
            Assert.AreEqual(NetworkLifecycleState.Failed, c3.State);
            Assert.IsTrue(new SessionRequest(3).IsPrivate, "No public matchmaking: sessions are always private.");
        }

        // ---- Acceptance 4: failures, leaving, no host migration ----

        [Test]
        public void InvalidCode_ServiceOutage_AndTransportFailure_EndInFailed_AndLeaveReturnsOffline()
        {
            var (client, services, driver) = Build();
            var invalid = Wait(client.JoinAsync("NOPE12"));
            Assert.AreEqual(ServiceErrorKind.InvalidCode, invalid.Error);
            Assert.AreEqual(NetworkLifecycleState.Failed, client.State);
            Assert.AreEqual(ServiceErrorKind.InvalidCode, client.LastError.Error);
            Assert.IsFalse(driver.IsListening);

            services.IsAvailable = false;
            var outage = Wait(client.HostAsync(new SessionRequest()));
            Assert.AreEqual(ServiceErrorKind.Unavailable, outage.Error);
            Assert.AreEqual(NetworkLifecycleState.Failed, client.State);
            services.IsAvailable = true;

            driver.FailNextWith = ServiceErrorKind.Timeout;
            var transport = Wait(client.HostAsync(new SessionRequest()));
            Assert.AreEqual(ServiceErrorKind.Timeout, transport.Error);
            Assert.IsNull(services.CurrentSession, "A failed host start leaves the service session too.");

            Assert.IsTrue(Wait(client.HostAsync(new SessionRequest())).Success, "Recoverable: a later attempt can succeed.");
            Assert.IsTrue(Wait(client.LeaveAsync()).Success);
            Assert.AreEqual(NetworkLifecycleState.Offline, client.State);
            Assert.AreEqual(NetworkRole.Offline, client.Role);
            Assert.IsNull(client.Session);
            Assert.IsFalse(driver.IsListening);
            Assert.AreEqual(1, driver.Shutdowns);
        }

        [Test]
        public void LostConnection_EndsTheSession_WithoutHostMigration()
        {
            var (host, _, _) = Build();
            Wait(host.HostAsync(new SessionRequest()));
            var (client, _, clientDriver) = Build();
            var joined = Wait(client.JoinAsync(host.Session.JoinCode));
            Assert.IsTrue(joined.Success, $"{joined.Error} {joined.Message}");
            Assert.AreEqual(NetworkLifecycleState.Connected, client.State);

            clientDriver.Drop();
            Assert.AreEqual(NetworkLifecycleState.Failed, client.State);
            Assert.AreEqual(NetworkRole.Offline, client.Role, "A client never becomes host when the connection drops.");
            Assert.IsNull(client.Session);
            Assert.IsFalse(NetworkSessionController.SupportsHostMigration);
            Assert.IsFalse(NetworkSessionController.SupportsPublicMatchmaking);
            Assert.IsFalse(NetworkSessionController.SupportsDedicatedServer);
            Assert.IsNull(typeof(NetworkSessionController).GetMethod("StartServer"), "No dedicated-server path.");
            Assert.IsNull(typeof(INetworkDriver).GetMethod("StartServer"));
            Assert.IsFalse(typeof(NetworkSessionController).GetMethods().Any(m => m.Name.ToLowerInvariant().Contains("migrat") || m.Name.ToLowerInvariant().Contains("matchmak")));
        }

        [Test]
        public void HostingTwice_OrJoiningWhileConnected_IsRefusedWithoutChangingState()
        {
            var (host, _, driver) = Build();
            Wait(host.HostAsync(new SessionRequest()));
            var again = Wait(host.HostAsync(new SessionRequest()));
            Assert.AreEqual(ServiceErrorKind.InvalidState, again.Error);
            Assert.AreEqual(NetworkLifecycleState.Connected, host.State);
            Assert.AreEqual(1, driver.HostStarts);
            Assert.AreEqual(ServiceErrorKind.InvalidState, Wait(host.JoinAsync("ABCDEF")).Error);
            Assert.Throws<System.InvalidOperationException>(() => host.StartOffline());
        }

        // ---- Acceptance 2: dependency rules ----

        [Test]
        public void GameplayAssemblies_DoNotReferenceNetcodeOrServices_OnlyGameNetworkDoes()
        {
            string Read(string path) => File.ReadAllText(path);
            foreach (var asmdef in new[] { "Assets/Game/Scripts/Core/Game.Core.asmdef", "Assets/Game/Scripts/Game.Gameplay.asmdef", "Assets/Game/Scripts/Dungeon/Game.Dungeon.asmdef", "Assets/Game/Scripts/Persistence/Game.Persistence.asmdef" }.Where(File.Exists))
            {
                var text = Read(asmdef);
                StringAssert.DoesNotContain("Netcode", text, asmdef);
                StringAssert.DoesNotContain("Unity.Services", text, asmdef);
                StringAssert.DoesNotContain("Game.Network", text, $"{asmdef}: gameplay never depends on the network assembly.");
            }

            var network = Read("Assets/Game/Scripts/Multiplayer/Game.Networking.asmdef");
            StringAssert.Contains("Unity.Netcode.Runtime", network);
            StringAssert.Contains("Unity.Services.Multiplayer", network);
            StringAssert.Contains("Game.Gameplay", network);
        }

        [Test]
        public void Bootstrap_ComposesOfflineByDefault_WithoutAGlobalSingleton()
        {
            var go = new GameObject("NetworkBootstrap");
            try
            {
                var bootstrap = go.AddComponent<NetworkBootstrap>();
                var controller = bootstrap.Configure(new FakeMultiplayerServices(), new FakeNetworkDriver());
                Assert.AreEqual(NetworkLifecycleState.Offline, controller.State);
                Assert.IsNull(typeof(NetworkBootstrap).GetProperty("Instance"), "No global singleton.");
                Assert.IsFalse(typeof(NetworkBootstrap).GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public).Any());
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
