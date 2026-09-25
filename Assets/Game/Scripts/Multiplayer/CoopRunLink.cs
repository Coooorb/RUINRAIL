using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace RuinRail.Networking
{
    /// <summary>
    /// The session's one expedition channel (a registered network prefab, spawned by the host as soon as it listens and
    /// kept across scene loads): carries the host's depth payload (<see cref="NetworkDungeonSync"/> on the same object),
    /// the reliable expedition records and the unreliable enemy-motion / shot streams.
    ///
    /// Enemies, bosses, loot and rooms are deliberately not network objects of their own: one registered prefab plus
    /// definition ids is the replication model (EnemySpawnRecord / EnemyNetState), so no per-enemy prefab can be
    /// missing from a release build's prefab list. Every client→host record is checked against
    /// <see cref="CoopKinds.IsClientToHost"/> and tagged with the sender NGO reports — never with an id the payload
    /// claims — so a client can only ever speak for itself.
    /// </summary>
    [RequireComponent(typeof(NetworkDungeonSync))]
    public sealed class CoopRunLink : NetworkBehaviour, ICoopBus
    {
        private NetworkDungeonSync _depth;

        /// <summary>The spawned link of this process (null outside a live session).</summary>
        public static CoopRunLink Current { get; private set; }

        public static event Action<CoopRunLink> Available;
        public static event Action<CoopRunLink> Lost;

        /// <summary>The bus's host role is NGO's server role (a listening host process).</summary>
        bool ICoopBus.IsHost => IsServer;
        public ulong LocalClientId => NetworkManager != null ? NetworkManager.LocalClientId : 0;

        public IReadOnlyList<ulong> RemoteClients => IsServer && NetworkManager != null
            ? NetworkManager.ConnectedClientsIds.Where(id => id != NetworkManager.ServerClientId).ToList()
            : (IReadOnlyList<ulong>)Array.Empty<ulong>();

        public double NetworkTime => NetworkManager != null ? NetworkManager.ServerTime.Time : Time.timeAsDouble;

        public int MessagesSent { get; private set; }
        public int MessagesReceived { get; private set; }
        public long BytesSent { get; private set; }
        public long BytesReceived { get; private set; }
        public int RejectedWrongDirection { get; private set; }

        public event Action<ulong, string, string> Received;
        public event Action<EnemyNetState[]> EnemyStatesReceived;
        public event Action<ShotNetRecord> ShotReceived;
        public event Action<DungeonSyncPayload> DepthPayloadChanged;

        public DungeonSyncPayload DepthPayload => Depth != null ? Depth.Payload : default;
        private NetworkDungeonSync Depth => _depth != null ? _depth : _depth = GetComponent<NetworkDungeonSync>();

        public override void OnNetworkSpawn()
        {
            // The expedition spans the Shelter and every depth: the link must survive the scene loads in between.
            if (transform.parent == null) DontDestroyOnLoad(gameObject);
            if (Depth != null) Depth.PayloadChanged += OnDepthChanged;
            Current = this;
            Debug.Log($"COOP-LINK spawned on {(IsServer ? "host" : "client")} {LocalClientId}.");
            Available?.Invoke(this);
        }

        public override void OnNetworkDespawn()
        {
            if (Depth != null) Depth.PayloadChanged -= OnDepthChanged;
            if (Current == this) Current = null;
            Lost?.Invoke(this);
        }

        private void OnDepthChanged(DungeonSyncPayload payload) => DepthPayloadChanged?.Invoke(payload);

        public void PublishDepth(in DungeonSyncPayload payload) => Depth.Publish(payload);

        // ---------------------------------------------------------------- reliable records

        public void SendToHost(string kind, string json)
        {
            if (IsServer || !IsSpawned) return;
            Count(kind, json);
            ToHostRpc(kind, json ?? string.Empty);
        }

        public void SendToClients(string kind, string json)
        {
            if (!IsServer || !IsSpawned || RemoteClients.Count == 0) return;
            Count(kind, json);
            ToClientRpc(kind, json ?? string.Empty, RpcTarget.NotServer);
        }

        public void SendToClient(ulong clientId, string kind, string json)
        {
            if (!IsServer || !IsSpawned || clientId == NetworkManager.ServerClientId) return;
            if (!NetworkManager.ConnectedClientsIds.Contains(clientId)) return;
            Count(kind, json);
            ToClientRpc(kind, json ?? string.Empty, RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        private void Count(string kind, string json)
        {
            MessagesSent++;
            BytesSent += (kind?.Length ?? 0) + (json?.Length ?? 0);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void ToHostRpc(string kind, string json, RpcParams rpcParams = default)
        {
            if (!CoopKinds.IsClientToHost(kind)) { RejectedWrongDirection++; return; }
            MessagesReceived++;
            BytesReceived += (kind?.Length ?? 0) + (json?.Length ?? 0);
            Received?.Invoke(rpcParams.Receive.SenderClientId, kind, json);
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void ToClientRpc(string kind, string json, RpcParams rpcParams)
        {
            if (IsServer) return;
            if (CoopKinds.IsClientToHost(kind)) { RejectedWrongDirection++; return; }
            MessagesReceived++;
            BytesReceived += (kind?.Length ?? 0) + (json?.Length ?? 0);
            Received?.Invoke(NetworkManager.ServerClientId, kind, json);
        }

        // ---------------------------------------------------------------- high-rate streams

        public void SendEnemyStates(EnemyNetState[] states)
        {
            if (!IsServer || !IsSpawned || states == null || states.Length == 0 || RemoteClients.Count == 0) return;
            BytesSent += states.Length * 50;
            EnemyStatesRpc(states);
        }

        [Rpc(SendTo.NotServer, Delivery = RpcDelivery.Unreliable)]
        private void EnemyStatesRpc(EnemyNetState[] states)
        {
            BytesReceived += (states?.Length ?? 0) * 50;
            EnemyStatesReceived?.Invoke(states);
        }

        public void SendShot(ShotNetRecord shot)
        {
            if (!IsSpawned) return;
            shot.Shooter = LocalClientId;
            if (IsServer)
            {
                if (RemoteClients.Count > 0) ShotToClientsRpc(shot, RpcTarget.NotServer);
                return;
            }

            ShotToHostRpc(shot);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone, Delivery = RpcDelivery.Unreliable)]
        private void ShotToHostRpc(ShotNetRecord shot, RpcParams rpcParams = default)
        {
            // The shooter is who NGO says sent it, whatever the record claims.
            shot.Shooter = rpcParams.Receive.SenderClientId;
            ShotReceived?.Invoke(shot);
            var others = RemoteClients.Where(id => id != shot.Shooter).ToList();
            if (others.Count > 0) ShotToClientsRpc(shot, RpcTarget.Group(others.ToArray(), RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, Delivery = RpcDelivery.Unreliable)]
        private void ShotToClientsRpc(ShotNetRecord shot, RpcParams rpcParams)
        {
            if (IsServer) return;
            ShotReceived?.Invoke(shot);
        }
    }

    /// <summary>
    /// Registers the link prefab on a NetworkManager and spawns one link whenever that manager starts serving. Both
    /// peers must register the same prefabs (NGO compares the prefab list on connect), so every composition that
    /// builds a NetworkManager — the shipped live path and the built-player proof — installs it the same way.
    /// </summary>
    public static class CoopLinkSpawner
    {
        private static readonly HashSet<int> Installed = new();

        public static bool Register(NetworkManager manager, GameObject linkPrefab)
        {
            if (manager == null || linkPrefab == null || linkPrefab.GetComponent<NetworkObject>() == null) return false;
            if (!manager.NetworkConfig.Prefabs.Contains(linkPrefab)) manager.AddNetworkPrefab(linkPrefab);
            return true;
        }

        public static void Install(NetworkManager manager, GameObject linkPrefab)
        {
            if (!Register(manager, linkPrefab)) return;
            if (!Installed.Add(manager.GetInstanceID())) return;
            manager.OnServerStarted += () => Spawn(manager, linkPrefab);
        }

        /// <summary>Spawns the session's link (host only, once per session).</summary>
        public static CoopRunLink Spawn(NetworkManager manager, GameObject linkPrefab)
        {
            if (manager == null || !manager.IsServer || linkPrefab == null) return null;
            if (CoopRunLink.Current != null && CoopRunLink.Current.IsSpawned) return CoopRunLink.Current;
            var instance = UnityEngine.Object.Instantiate(linkPrefab);
            instance.name = "CoopRunLink";
            UnityEngine.Object.DontDestroyOnLoad(instance);
            var networkObject = instance.GetComponent<NetworkObject>();
            networkObject.Spawn(false);
            return instance.GetComponent<CoopRunLink>();
        }
    }
}
