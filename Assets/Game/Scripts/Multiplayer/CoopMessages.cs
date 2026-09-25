using System;
using System.Collections.Generic;
using RuinRail.Gameplay.Items;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RuinRail.Networking
{
    /// <summary>
    /// The message kinds of the co-op expedition channel. Low-rate, must-arrive state travels as reliable JSON records
    /// (below); high-rate presentation (enemy motion, shots) travels as compact unreliable structs. Every kind has one
    /// direction: <c>req.*</c>, <c>lobby.member</c>, <c>depth.ready</c>, <c>inv.*</c> and <c>proof.report</c> go to the
    /// host; everything else goes from the host to clients. A process never trusts a kind arriving from the wrong side.
    /// </summary>
    public static class CoopKinds
    {
        // session / lobby (Shelter)
        public const string LobbyMember = "lobby.member";
        public const string LobbyState = "lobby.state";
        public const string RunStart = "run.start";
        public const string RunEnded = "run.ended";
        public const string ReconnectToken = "run.token";

        // depth lifecycle
        public const string DepthReady = "depth.ready";
        public const string GameplayRelease = "depth.release";

        // world replication (host -> clients)
        public const string EnemySpawn = "enemy.spawn";
        public const string EnemyGone = "enemy.gone";
        public const string BossState = "boss.state";
        public const string RoomState = "room.state";
        public const string LootSpawn = "loot.spawn";
        public const string LootGone = "loot.gone";
        public const string LootMove = "loot.move";
        public const string Xp = "xp";
        public const string TransitOpen = "transit.open";
        public const string TransitVotes = "transit.votes";
        public const string TransitResolved = "transit.resolved";
        public const string Notice = "notice";

        // requests (client -> host)
        public const string Hit = "req.hit";
        public const string Impact = "req.impact";
        public const string Heal = "req.heal";
        public const string Vote = "req.vote";
        public const string Buy = "req.buy";
        public const string Sell = "req.sell";
        public const string CacheChoose = "req.cache";
        public const string Drop = "req.drop";
        public const string InventorySnapshot = "inv.snapshot";
        public const string Resync = "req.resync";
        /// <summary>A Defibrillator use (84): the host revives the closest Dead teammate in the sender's reach.</summary>
        public const string Revive = "req.revive";

        // replies (host -> one client)
        public const string Grant = "res.grant";
        public const string Revoke = "res.revoke";
        public const string TradeResult = "res.trade";
        public const string CacheResult = "res.cache";
        public const string ReviveResult = "res.revive";
        /// <summary>The host's verdict that this member's validated hit took an enemy's last health (Adrenaline / Flow State).</summary>
        public const string Kill = "res.kill";

        // built-player proof orchestration
        public const string ProofCommand = "proof.cmd";
        public const string ProofReport = "proof.report";

        private static readonly HashSet<string> ToHost = new(StringComparer.Ordinal)
        {
            LobbyMember, DepthReady, Hit, Impact, Heal, Vote, Buy, Sell, CacheChoose, Drop, InventorySnapshot, Resync, Revive, ProofReport
        };

        /// <summary>True for the kinds a client may send; the host drops anything else arriving from a client.</summary>
        public static bool IsClientToHost(string kind) => kind != null && ToHost.Contains(kind);
    }

    // ---------------------------------------------------------------- session

    [Serializable]
    public sealed class LobbyMemberMessage
    {
        public bool Ready;
        public string DisplayName;
        public InventorySnapshot Loadout;
        /// <summary>Permanent attribute ranks (player/13), indexed by SkillId, so the host's copy of this member has the same stats.</summary>
        public int[] SkillRanks = Array.Empty<int>();
    }

    [Serializable]
    public sealed class LobbyStateMessage
    {
        public List<LobbyLine> Members = new();

        [Serializable]
        public sealed class LobbyLine
        {
            public ulong ClientId;
            public string Name;
            public bool IsHost;
            public bool Ready;
            public bool ValidLoadout;
        }
    }

    /// <summary>The host's authoritative expedition start: every peer starts its own transaction from this exactly once.</summary>
    [Serializable]
    public sealed class RunStartMessage
    {
        public string StartId;
        public int RunSeed;
        public int Biome;
        public int PartySize;
        public int Depth = 1;
        public string ContentVersion;
        public List<RunMember> Members = new();

        [Serializable]
        public sealed class RunMember
        {
            public ulong ClientId;
            public string ParticipantId;
            public string DisplayName;
            public bool IsHost;
        }

        public RunMember MemberFor(ulong clientId) => Members.Find(m => m.ClientId == clientId);
    }

    [Serializable]
    public sealed class RunEndedMessage
    {
        /// <summary>0 = extracted (Return), 1 = failed (wipe / host quit).</summary>
        public int Outcome;
        public string Reason;
    }

    [Serializable]
    public sealed class ReconnectTokenMessage
    {
        public string Token;
        public string ParticipantId;
    }

    [Serializable]
    public sealed class DepthReadyMessage
    {
        public int Depth;
        public bool Success;
        public string LayoutFingerprint;
        public string Error;
        public int Rooms;
    }

    [Serializable]
    public sealed class GameplayReleaseMessage
    {
        public int Depth;
        public int ReadyPeers;
        public bool TimedOut;
    }

    // ---------------------------------------------------------------- world

    public enum CoopActorKind
    {
        Normal = 0,
        Elite = 1,
        Boss = 2
    }

    [Serializable]
    public sealed class EnemySpawnMessage
    {
        public uint NetId;
        public int Kind;
        public string DefinitionId;
        public float X;
        public float Y;
        public int Depth;
        public int RoomNode;
        public int Health;
        public int MaxHealth;
        public int Phase;
        public Vector2 Position => new(X, Y);
    }

    [Serializable]
    public sealed class EnemyGoneMessage
    {
        public uint NetId;
        public bool Died;
        public int Kind;
        public int Xp;
    }

    [Serializable]
    public sealed class BossStateMessage
    {
        public uint NetId;
        public int RoomNode;
        public int Phase;
        public bool Started;
        public bool Defeated;
        public int Health;
        public int MaxHealth;
        public uint Version;
    }

    [Serializable]
    public sealed class RoomStateMessage
    {
        public int Depth;
        public int NodeId;
        public int State;
        public int EntryCount;
        public int EnemiesSpawned;
        public int EnemiesDefeated;
        public int EnemiesRemaining;
        public bool DoorsLocked;
        public string[] Resolved = Array.Empty<string>();
        public int[] MerchantSold = Array.Empty<int>();
        public bool BossCacheLocked;
        public bool TransitActive;
        public int EventPhase = -1;
        public uint Version;
    }

    [Serializable]
    public sealed class LootSpawnMessage
    {
        public uint LootId;
        public int Depth;
        public int Coins;
        public ItemInstanceSnapshot Item;
        public float X;
        public float Y;
        public Vector2 Position => new(X, Y);
    }

    [Serializable]
    public sealed class LootGoneMessage
    {
        public uint LootId;
        public ulong TakenBy = ulong.MaxValue;
    }

    [Serializable]
    public sealed class LootMoveMessage
    {
        public uint LootId;
        public float X;
        public float Y;
    }

    [Serializable]
    public sealed class XpMessage
    {
        public int Amount;
        public int Kind;
    }

    [Serializable]
    public sealed class TransitOpenMessage
    {
        public int Depth;
        public int BossXp;
        public string[] Living = Array.Empty<string>();
        public string[] Dead = Array.Empty<string>();
    }

    [Serializable]
    public sealed class TransitVotesMessage
    {
        public string[] Voters = Array.Empty<string>();
        public int[] Choices = Array.Empty<int>();
        public string[] Living = Array.Empty<string>();
        public int Submissions;
    }

    [Serializable]
    public sealed class TransitResolvedMessage
    {
        public int Depth;
        public int Choice;
    }

    [Serializable]
    public sealed class NoticeMessage
    {
        public string Text;
        public bool IsProblem;
    }

    // ---------------------------------------------------------------- requests

    [Serializable]
    public sealed class HitRequestMessage
    {
        public uint NetId;
        public int Amount;
        public int DamageKind;
        public float StaggerPower;
        public float DirX;
        public float DirY;
        public uint Sequence;
    }

    [Serializable]
    public sealed class ImpactRequestMessage
    {
        public uint NetId;
        public float DirX;
        public float DirY;
        public float Knockback;
        public float StaggerPower;
        public int DamageKind;
    }

    [Serializable]
    public sealed class HealRequestMessage
    {
        public int Amount;
    }

    /// <summary>A member's Defibrillator use; the host resolves the consumable from its own catalog, never the sender's numbers.</summary>
    [Serializable]
    public sealed class ReviveRequestMessage
    {
        public string TransactionId;
        public string ConsumableId;
    }

    /// <summary>The host's verdict on a Defibrillator use: the member spends the unit only on Accepted.</summary>
    [Serializable]
    public sealed class ReviveResultMessage
    {
        public string TransactionId;
        public string ConsumableId;
        public bool Accepted;
        public string RevivedParticipantId;
        public string Reason;
    }

    [Serializable]
    public sealed class KillMessage
    {
        public bool Melee;
    }

    [Serializable]
    public sealed class VoteRequestMessage
    {
        public int Depth;
        public int Choice;
    }

    [Serializable]
    public sealed class TradeRequestMessage
    {
        public string TransactionId;
        public int Depth;
        public int RoomNode;
        public int OfferIndex = -1;
        public string InstanceId;
        public int Quantity;
    }

    [Serializable]
    public sealed class InventorySnapshotMessage
    {
        public uint Version;
        public InventorySnapshot Inventory;
        public int[] SkillRanks = Array.Empty<int>();
    }

    [Serializable]
    public sealed class GrantMessage
    {
        public string TransactionId;
        public string Source;
        public ItemInstanceSnapshot Item;
    }

    [Serializable]
    public sealed class RevokeMessage
    {
        public string TransactionId;
        public string InstanceId;
        public int Quantity;
    }

    [Serializable]
    public sealed class TradeResultMessage
    {
        public string TransactionId;
        public bool IsBuy;
        public int Verdict;
        public string Error;
        public string ItemName;
        public int Coins;
        public int RoomNode;
        public int OfferIndex;
    }

    [Serializable]
    public sealed class CacheResultMessage
    {
        public string TransactionId;
        public bool Accepted;
        public bool AlreadyTaken;
        public string ItemName;
        public int RoomNode;
    }

    [Serializable]
    public sealed class ProofMessage
    {
        public string Step;
        public string Arg;
        public float X;
        public float Y;
        public float Seconds;
        public int Number;
        public string Json;
    }

    // ---------------------------------------------------------------- high-rate structs

    /// <summary>A shot as presentation for the other peers: zero damage, same path, same art.</summary>
    [Serializable]
    public struct ShotNetRecord : INetworkSerializable
    {
        public Vector2 Origin;
        public Vector2 Direction;
        public float Speed;
        public float Range;
        public int Team;
        public ulong Shooter;
        public FixedString32Bytes Visual;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Origin);
            serializer.SerializeValue(ref Direction);
            serializer.SerializeValue(ref Speed);
            serializer.SerializeValue(ref Range);
            serializer.SerializeValue(ref Team);
            serializer.SerializeValue(ref Shooter);
            serializer.SerializeValue(ref Visual);
        }
    }

    /// <summary>JSON helpers for the reliable records (JsonUtility: the same serializer the save system uses).</summary>
    public static class CoopJson
    {
        public static string Write<T>(T value) => value == null ? "{}" : JsonUtility.ToJson(value);

        public static T Read<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                return JsonUtility.FromJson<T>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Co-op message could not be read as {typeof(T).Name}: {e.Message}");
                return null;
            }
        }
    }
}
