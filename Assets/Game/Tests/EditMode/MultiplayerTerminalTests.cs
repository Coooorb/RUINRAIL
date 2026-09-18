using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using RuinRail.Networking;

namespace RuinRail.Tests
{
    /// <summary>TASK 092: SOLO / HOST / JOIN terminal flow, join codes, capacity, error model, idempotency; live check NOT RUN without cloud.</summary>
    public class MultiplayerTerminalTests
    {
        [SetUp]
        public void SetUp() => FakeMultiplayerServices.ResetRegistry();

        private static (MultiplayerTerminalService terminal, FakeMultiplayerServices services, FakeNetworkDriver driver) Build()
        {
            var services = new FakeMultiplayerServices();
            var driver = new FakeNetworkDriver();
            var controller = new NetworkSessionController(services, driver);
            controller.StartOffline();
            return (new MultiplayerTerminalService(controller), services, driver);
        }

        private static T Wait<T>(Task<T> task)
        {
            task.Wait();
            return task.Result;
        }

        [Test]
        public void JoinCodes_AreSixAlphanumerics_CaseAndSeparatorInsensitive()
        {
            Assert.AreEqual("ABC123", JoinCode.Normalize(" abc-123 "));
            Assert.IsTrue(JoinCode.IsWellFormed("abc 123"));
            Assert.IsFalse(JoinCode.IsWellFormed("ABC12"));
            Assert.IsFalse(JoinCode.IsWellFormed("ABC1234"));
            Assert.IsFalse(JoinCode.IsWellFormed(""));
            Assert.IsFalse(JoinCode.IsWellFormed(null));
            Assert.IsFalse(JoinCode.IsWellFormed("ÄBC123"));
        }

        [Test]
        public void Solo_NeedsNoServices_AndIsIdempotent()
        {
            var (terminal, services, driver) = Build();
            services.IsAvailable = false;
            Assert.IsTrue(Wait(terminal.SelectSoloAsync()).IsNone);
            Assert.AreEqual(TerminalState.Solo, terminal.State);
            Assert.AreEqual(TerminalMode.Solo, terminal.Mode);
            Assert.AreEqual(1, terminal.PartySize);
            Assert.IsTrue(Wait(terminal.SelectSoloAsync()).IsNone);
            Assert.AreEqual(0, services.InitializeCalls);
            Assert.IsFalse(driver.IsListening);
        }

        [Test]
        public void HostCoop_ExposesAJoinCode_ClientsJoinByIt_AndAFourthIsRefusedWithAReadableError()
        {
            var (host, _, _) = Build();
            var changes = 0;
            host.Changed += _ => changes++;
            Assert.IsTrue(Wait(host.HostCoopAsync()).IsNone);
            Assert.AreEqual(TerminalState.InSession, host.State);
            Assert.IsNotNull(host.JoinCodeToShare);
            Assert.IsTrue(JoinCode.IsWellFormed(host.JoinCodeToShare));
            Assert.AreEqual(1, host.PartySize);
            Assert.AreEqual(3, host.MaxPartySize);
            Assert.Greater(changes, 0);

            var (c1, _, _) = Build();
            var (c2, _, _) = Build();
            var (c3, _, _) = Build();
            Assert.IsTrue(Wait(c1.JoinCoopAsync(host.JoinCodeToShare.ToLowerInvariant())).IsNone);
            Assert.IsTrue(Wait(c2.JoinCoopAsync(" " + host.JoinCodeToShare + " ")).IsNone);
            Assert.AreEqual(TerminalState.InSession, c2.State);
            Assert.IsNull(c2.JoinCodeToShare, "Only the host shares the code.");
            Assert.AreEqual(3, c2.PartySize);

            var full = Wait(c3.JoinCoopAsync(host.JoinCodeToShare));
            Assert.AreEqual(ServiceErrorKind.SessionFull, full.Kind);
            StringAssert.Contains("full", full.Message);
            Assert.IsFalse(full.IsRetryable);
            Assert.AreEqual(TerminalState.Error, c3.State);
            Assert.IsFalse(c3.IsInSession);
            Assert.AreEqual(0, c3.PartySize);
        }

        [Test]
        public void InvalidExpiredOrMalformedCodes_AndServiceFailures_ReturnReadableErrors_WithoutCorruptingState()
        {
            var (terminal, services, driver) = Build();

            var malformed = Wait(terminal.JoinCoopAsync("12"));
            Assert.AreEqual(ServiceErrorKind.InvalidCode, malformed.Kind);
            Assert.AreEqual(0, services.InitializeCalls, "Malformed codes never reach the services.");
            Assert.AreEqual(TerminalState.Idle, terminal.State, "Validation failure keeps the terminal state.");
            Assert.AreEqual(malformed.Message, terminal.LastError.Message);

            var expired = Wait(terminal.JoinCoopAsync("ZZZZ99"));
            Assert.AreEqual(ServiceErrorKind.InvalidCode, expired.Kind);
            StringAssert.Contains("expired", expired.Message);
            Assert.AreEqual(TerminalState.Error, terminal.State);
            Assert.IsFalse(driver.IsListening);
            Assert.IsNull(terminal.Controller.Session);

            services.IsAvailable = false;
            var outage = Wait(terminal.HostCoopAsync());
            Assert.AreEqual(ServiceErrorKind.Unavailable, outage.Kind);
            StringAssert.Contains("solo", outage.Message);
            Assert.IsTrue(outage.IsRetryable);
            services.IsAvailable = true;

            services.FailNextWith = ServiceErrorKind.Unknown;
            var unknown = Wait(terminal.HostCoopAsync());
            Assert.AreEqual(ServiceErrorKind.Unknown, unknown.Kind);
            Assert.IsNotEmpty(unknown.Message);

            // Recover: solo works, hosting works afterwards.
            Assert.IsTrue(Wait(terminal.SelectSoloAsync()).IsNone);
            Assert.IsTrue(Wait(terminal.HostCoopAsync()).IsNone);
            Assert.AreEqual(TerminalState.InSession, terminal.State);
        }

        [Test]
        public void CreateJoinLeave_AreIdempotent_AndSwitchingModesLeavesFirst()
        {
            var (host, services, driver) = Build();
            Assert.IsTrue(Wait(host.HostCoopAsync()).IsNone);
            var code = host.JoinCodeToShare;
            Assert.IsTrue(Wait(host.HostCoopAsync()).IsNone, "Repeating HOST while hosting is a no-op.");
            Assert.AreEqual(code, host.JoinCodeToShare);
            Assert.AreEqual(1, driver.HostStarts);

            var busy = Wait(host.JoinCoopAsync("ABC123"));
            Assert.AreEqual(ServiceErrorKind.InvalidState, busy.Kind, "Joining while hosting is refused.");
            Assert.AreEqual(TerminalState.InSession, host.State, "...without leaving the session.");

            Assert.IsTrue(Wait(host.LeaveAsync()).IsNone);
            Assert.AreEqual(TerminalState.Idle, host.State);
            Assert.IsNull(host.JoinCodeToShare);
            Assert.IsTrue(Wait(host.LeaveAsync()).IsNone, "Leaving twice is harmless.");
            Assert.IsNull(services.CurrentSession);

            Assert.IsTrue(Wait(host.HostCoopAsync()).IsNone);
            Assert.AreNotEqual(code, host.JoinCodeToShare, "A new session has a new code; the old one no longer exists.");
            var (client, _, _) = Build();
            Assert.AreEqual(ServiceErrorKind.InvalidCode, Wait(client.JoinCoopAsync(code)).Kind, "Old (expired) code is rejected.");
            Assert.IsTrue(Wait(client.JoinCoopAsync(host.JoinCodeToShare)).IsNone);

            Assert.IsTrue(Wait(client.SelectSoloAsync()).IsNone, "Switching to SOLO leaves the session first.");
            Assert.AreEqual(TerminalState.Solo, client.State);
            Assert.IsFalse(client.IsInSession);
            Assert.AreEqual(1, host.PartySize, "Host sees the party shrink again.");
        }

        [Test]
        public void LostConnectionInSession_SurfacesAsARetryableError_AndTheTerminalRecovers()
        {
            var (host, _, _) = Build();
            Wait(host.HostCoopAsync());
            var (client, _, clientDriver) = Build();
            Wait(client.JoinCoopAsync(host.JoinCodeToShare));

            clientDriver.Drop();
            Assert.AreEqual(TerminalState.Error, client.State);
            Assert.AreEqual(ServiceErrorKind.Timeout, client.LastError.Kind);
            Assert.IsTrue(client.LastError.IsRetryable);
            Assert.IsFalse(client.IsInSession);
            Assert.IsTrue(Wait(client.JoinCoopAsync(host.JoinCodeToShare)).IsNone, "Rejoin by code after a drop.");
            Assert.AreEqual(TerminalState.InSession, client.State);
        }

        [Test]
        public void NoPublicDiscovery_OnlyJoinCodes()
        {
            var members = typeof(MultiplayerTerminalService).GetMembers().Select(m => m.Name.ToLowerInvariant()).ToList();
            Assert.IsFalse(members.Any(m => m.Contains("browse") || m.Contains("quickplay") || m.Contains("matchmak") || m.Contains("publicsession")));
            Assert.IsTrue(new SessionRequest().IsPrivate);
        }

        /// <summary>
        /// Live Sessions/Relay integration: only runs when RUINRAIL_LIVE_SERVICES=1 and a Unity Cloud project link is
        /// available; otherwise it is reported as NOT RUN (ignored), never as a fake pass.
        /// </summary>
        [Test]
        public void LiveSessionsRelay_IntegrationCheck_OrNotRun()
        {
            if (Environment.GetEnvironmentVariable("RUINRAIL_LIVE_SERVICES") != "1")
            {
                Assert.Ignore("NOT RUN: live Unity Multiplayer Services (Sessions/Relay) check requires RUINRAIL_LIVE_SERVICES=1 and cloud credentials; fakes cover the state machine.");
            }

            var live = new UnityMultiplayerServices();
            var controller = new NetworkSessionController(live, new FakeNetworkDriver());
            var result = Wait(controller.HostAsync(new SessionRequest()));
            Assert.IsTrue(result.Success, result.Message);
            Assert.IsTrue(JoinCode.IsWellFormed(controller.Session.JoinCode));
            Wait(controller.LeaveAsync());
        }
    }

}
