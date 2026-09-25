using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Gameplay.Player;
using Unity.Netcode;
using UnityEngine;

namespace RuinRail.Networking
{
    /// <summary>
    /// Finds the replicated player objects of the live session on this peer: the one this process owns and the one
    /// every member owns. The run's composition root uses it to attach its local rig to the owned object and to adopt
    /// the other members' replicas — it never creates a player object itself on a client (82).
    /// </summary>
    public static class CoopPlayerDirectory
    {
        public static IEnumerable<NetworkPlayerObject> All()
        {
            var manager = NetworkManager.Singleton;
            if (manager == null || manager.SpawnManager == null) yield break;
            foreach (var networkObject in manager.SpawnManager.SpawnedObjectsList)
            {
                if (networkObject == null) continue;
                var player = networkObject.GetComponent<NetworkPlayerObject>();
                if (player != null) yield return player;
            }
        }

        /// <summary>The player object this process owns (null until the host spawned it and it replicated here).</summary>
        public static GameObject Owned() => All().FirstOrDefault(p => p.IsOwner)?.gameObject;

        /// <summary>The player object a member owns, as this peer sees it.</summary>
        public static GameObject Of(ulong clientId) => All().FirstOrDefault(p => p.OwnerClientId == clientId)?.gameObject;

        public static ulong OwnerOf(GameObject player)
        {
            var net = player != null ? player.GetComponent<NetworkPlayerObject>() : null;
            return net != null ? net.OwnerClientId : ulong.MaxValue;
        }

        public static int Count => All().Count();

        /// <summary>Spawned network objects on this peer (leak check across depth transitions and teardown).</summary>
        public static int SpawnedObjects()
        {
            var manager = NetworkManager.Singleton;
            return manager != null && manager.SpawnManager != null ? manager.SpawnManager.SpawnedObjectsList.Count : 0;
        }

        public static bool IsListening => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
    }

    /// <summary>
    /// Client-side party view (82): "spawning" a member means adopting the replica the host already spawned for it.
    /// Nothing is ever instantiated or despawned here — the host owns every player object's lifetime.
    /// </summary>
    public sealed class ReplicatedPlayerFactory : IPlayerEntityFactory
    {
        private readonly Func<ulong, GameObject> _find;
        private readonly PartyLifeRoster _roster;

        public ReplicatedPlayerFactory(Func<ulong, GameObject> find, PartyLifeRoster roster)
        {
            _find = find ?? CoopPlayerDirectory.Of;
            _roster = roster;
        }

        public int Adopted { get; private set; }

        public GameObject Spawn(PlayerIdentity identity, bool isLocalOwner)
        {
            var go = _find(identity.ClientId);
            if (go == null) return null;
            var life = go.GetComponent<PlayerLifeStateComponent>();
            if (life != null && _roster != null && life.Roster != _roster) life.SetRoster(_roster);
            Adopted++;
            return go;
        }

        public void Despawn(GameObject entity)
        {
            // Replicas are the host's: leaving the run on a client never destroys a network object.
        }
    }

    /// <summary>Wraps a player factory with a composition step run on every spawned member before the party sees it.</summary>
    public sealed class ComposingPlayerFactory : IPlayerEntityFactory
    {
        private readonly IPlayerEntityFactory _inner;
        private readonly Action<GameObject, PlayerIdentity, bool> _compose;

        public ComposingPlayerFactory(IPlayerEntityFactory inner, Action<GameObject, PlayerIdentity, bool> compose)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _compose = compose;
        }

        public GameObject Spawn(PlayerIdentity identity, bool isLocalOwner)
        {
            var go = _inner.Spawn(identity, isLocalOwner);
            if (go != null) _compose?.Invoke(go, identity, isLocalOwner);
            return go;
        }

        public void Despawn(GameObject entity) => _inner.Despawn(entity);
    }
}
