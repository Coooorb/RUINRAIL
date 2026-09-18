using System;
using RuinRail.Core;
using RuinRail.Dungeon.Generation;
using RuinRail.Dungeon.Grid;
using RuinRail.Dungeon.Rooms;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Player;
using UnityEngine;

namespace RuinRail.Dungeon.Runtime
{
    /// <summary>The two sprites a biome's combat door is drawn with.</summary>
    public readonly struct DoorSkinSprites
    {
        public DoorSkinSprites(Sprite open, Sprite locked)
        {
            Open = open;
            Locked = locked;
        }

        public Sprite Open { get; }
        public Sprite Locked { get; }
        public bool IsComplete => Open != null && Locked != null;
    }

    /// <summary>
    /// Physical lock of one door socket: a solid blocker (an EnvironmentObstacle, so projectiles, dashes and enemies
    /// treat it as wall) covering the socket cells while locked. Purely on/off; the runtime decides when.
    ///
    /// A lock never closes on a player standing in the doorway: if a player overlaps the socket cells at the moment
    /// the room locks, the blocker stays pending and engages the instant the doorway is clear. So a teammate who was
    /// mid-doorway when the first player activated the room is neither pushed out nor trapped inside the wall — they
    /// walk through, and the door shuts behind them.
    ///
    /// The door is drawn in the biome's skin: an open housing while traversable, a real shutter/barrier while the
    /// blocker is solid. The visual and the collider change together, so what reads as shut is shut. A sealed spare
    /// socket is wall and gets no door at all.
    /// </summary>
    [RequireComponent(typeof(DoorSocket))]
    public sealed class RoomDoorLock : MonoBehaviour
    {
        private static readonly Collider2D[] Overlaps = new Collider2D[8];

        /// <summary>The biome door skins, registered by the composition root (App) — the runtime never loads assets by path.</summary>
        public static Func<Biome, DoorSkinSprites> SkinResolver { get; set; }

        private DoorSocket _socket;
        private BoxCollider2D _blocker;
        private SpriteRenderer _door;
        private DoorSkinSprites _skin;
        private bool _hasSkin;
        private bool _sealed;
        private bool _locked;
        private bool _pending;

        public DoorSocket Socket => _socket != null ? _socket : _socket = GetComponent<DoorSocket>();
        public bool IsLocked => _locked;
        /// <summary>True while the room wants this door locked but a player is still standing in it.</summary>
        public bool IsPending => _pending;
        /// <summary>True when the blocker is actually solid right now.</summary>
        public bool IsBlocking => _blocker != null && _blocker.enabled;
        /// <summary>The door sprite renderer (null for a sealed socket or before any lock state was applied).</summary>
        public SpriteRenderer DoorRenderer => _door;
        /// <summary>True when the locked shutter is what is drawn (mirrors the solid blocker).</summary>
        public bool IsPlateVisible => _door != null && _door.enabled && _hasSkin && _door.sprite == _skin.Locked && _skin.Locked != null;
        /// <summary>True when the open housing is drawn (traversable doorway).</summary>
        public bool IsOpenVisible => _door != null && _door.enabled && _hasSkin && _door.sprite == _skin.Open && _skin.Open != null;
        public bool IsSealedSocket => _sealed;

        /// <summary>Raised when the lock state actually changes (never for a sealed spare socket): the audio layer plays the door cue from it.</summary>
        public event Action<RoomDoorLock, bool> LockChanged;

        public static RoomDoorLock GetOrAttach(DoorSocket socket)
        {
            var existing = socket.GetComponent<RoomDoorLock>();
            var door = existing != null ? existing : socket.gameObject.AddComponent<RoomDoorLock>();
            door.EnsureVisual();
            return door;
        }

        public void SetLocked(bool locked)
        {
            var changed = _locked != locked;
            _locked = locked;
            if (!locked)
            {
                _pending = false;
                if (_blocker != null) _blocker.enabled = false;
                RefreshVisual();
                if (changed && !_sealed) LockChanged?.Invoke(this, false);
                return;
            }

            EnsureBlocker();
            if (PlayerInDoorway())
            {
                _pending = true;
                _blocker.enabled = false;
            }
            else
            {
                _pending = false;
                _blocker.enabled = true;
            }

            RefreshVisual();
            if (changed && !_sealed) LockChanged?.Invoke(this, true);
        }

        /// <summary>World-space box the blocker covers (the socket cells, one tile deep).</summary>
        public (Vector2 center, Vector2 size) BlockerArea()
        {
            var width = Socket.Width * GridConstants.TileWorldSize;
            var size = DoorDirections.IsHorizontalEdge(Socket.Direction)
                ? new Vector2(width, GridConstants.TileWorldSize)
                : new Vector2(GridConstants.TileWorldSize, width);
            return (transform.position, size);
        }

        /// <summary>A player collider overlapping the socket cells (the only thing a lock must wait for).</summary>
        public bool PlayerInDoorway()
        {
            var (center, size) = BlockerArea();
            var count = Physics2D.OverlapBox(center, size, 0f, ContactFilter2D.noFilter, Overlaps);
            for (var i = 0; i < count; i++)
            {
                if (Overlaps[i] != null && Overlaps[i].GetComponentInParent<PlayerMovement>() != null) return true;
            }

            return false;
        }

        private void FixedUpdate()
        {
            if (!_pending) return;
            if (PlayerInDoorway()) return;
            _pending = false;
            _blocker.enabled = true;
            RefreshVisual();
        }

        private void EnsureBlocker()
        {
            if (_blocker != null) return;
            var go = new GameObject("DoorBlocker");
            go.transform.SetParent(transform, false);
            go.AddComponent<EnvironmentObstacle>();
            _blocker = go.AddComponent<BoxCollider2D>();
            var (_, size) = BlockerArea();
            _blocker.size = size;
            _blocker.offset = Vector2.zero;
        }

        /// <summary>Builds the door renderer for a real (unsealed) socket, in the room's biome skin.</summary>
        public void EnsureVisual()
        {
            if (_door != null) return;
            var root = GetComponentInParent<RoomRoot>();
            _sealed = root != null && RoomExitSealer.IsSealed(root, Socket);
            if (_sealed) return; // wall, not a door
            var biome = root != null && root.Definition != null ? root.Definition.Biome : Biome.RuinedMetro;
            if (SkinResolver != null)
            {
                _skin = SkinResolver(biome);
                _hasSkin = _skin.IsComplete;
            }

            var go = new GameObject("DoorVisual");
            go.transform.SetParent(transform, false);
            // Authored for a North/South socket (the 2-tile run along x); East/West sockets turn it a quarter.
            go.transform.localRotation = DoorDirections.IsHorizontalEdge(Socket.Direction) ? Quaternion.identity : Quaternion.Euler(0f, 0f, 90f);
            // The housing rail is on the wall side: North doors keep it up, South doors flip so the rail faces the wall outside.
            if (Socket.Direction == DoorDirection.South) go.transform.localScale = new Vector3(1f, -1f, 1f);
            if (Socket.Direction == DoorDirection.East) go.transform.localScale = new Vector3(1f, -1f, 1f);
            _door = go.AddComponent<SpriteRenderer>();
            _door.sortingLayerName = RuinRail.Core.Rendering.SortingLayers.LowProps;
            _door.sortingOrder = 20;
            RefreshVisual();
        }

        private void RefreshVisual()
        {
            if (_door == null || !_hasSkin) { if (_door != null) _door.enabled = false; return; }
            _door.sprite = IsBlocking ? _skin.Locked : _skin.Open;
            _door.enabled = _door.sprite != null;
        }

        private void LateUpdate()
        {
            if (_door == null || !_hasSkin) return;
            var wanted = IsBlocking ? _skin.Locked : _skin.Open;
            if (_door.sprite != wanted) { _door.sprite = wanted; _door.enabled = wanted != null; }
        }
    }
}
