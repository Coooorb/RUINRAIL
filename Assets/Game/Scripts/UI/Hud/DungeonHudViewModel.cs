using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Core;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Combat.Weapons.Specials;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using UnityEngine;

namespace RuinRail.UI.Hud
{
    public enum HudResourceKind
    {
        None,
        Ammo,
        Heat,
        Charge
    }

    /// <summary>One weapon slot as the HUD shows it (91 Weapons): name, active emphasis and its own resource.</summary>
    public sealed class HudWeaponState
    {
        public string Name = string.Empty;
        public bool IsActive;
        public HudResourceKind Resource;
        public int Magazine;
        public int Reserve;
        public float Heat01;
        public bool Overheated;
        public float Charge01;
        public bool IsCharging;
        public bool NoAmmo;
        public string SpecialName;
        public float SpecialCooldown01;
        public bool SpecialReady;
        /// <summary>Presentation of the authoritative equipped item (PlayerInventory): its definition icon and instance rarity.</summary>
        public string DefinitionId = string.Empty;
        public Sprite Icon;
        public int Rarity;
        /// <summary>True when the slot shows a resource readout at all: melee weapons show only their icon (91).</summary>
        public bool ShowsResource => Resource != HudResourceKind.None;

        public string ResourceText => Resource switch
        {
            HudResourceKind.Ammo => $"{Magazine} / {Reserve}",
            HudResourceKind.Heat => Overheated ? "OVERHEATED" : $"HEAT {Mathf.RoundToInt(Heat01 * 100f)}%",
            HudResourceKind.Charge => IsCharging ? $"DRAW {Mathf.RoundToInt(Charge01 * 100f)}%" : "READY",
            _ => string.Empty
        };
    }

    public enum HudLifeState
    {
        Alive,
        Downed,
        Dead,
        Disconnected
    }

    /// <summary>A co-op teammate line (91 Top Information): name, HP, life state, Downed timer.</summary>
    public sealed class HudPartyMember
    {
        public string Name = string.Empty;
        public int Hp;
        public int MaxHp;
        public HudLifeState State;
        public float BleedoutRemaining;

        public string StateText => State switch
        {
            HudLifeState.Downed => $"DOWNED {Mathf.CeilToInt(BleedoutRemaining)}s",
            HudLifeState.Dead => "DEAD",
            HudLifeState.Disconnected => "DISCONNECTED",
            _ => $"{Hp}/{MaxHp}"
        };
    }

    /// <summary>Everything the HUD renders, as plain readable values (integers for HP/damage; no critical-hit styling, no score abstraction).</summary>
    public sealed class HudSnapshot
    {
        public int Hp;
        public int MaxHp;
        public bool ShieldVisible;
        public bool DashReady;
        public float DashCooldown01;
        /// <summary>The dash cannot be used at all (player Downed/Dead or no dash component): the icon shows its disabled state.</summary>
        public bool DashDisabled;
        public HudWeaponState Primary = new();
        public HudWeaponState Secondary = new();
        public string ConsumableName = string.Empty;
        public int ConsumableQuantity;
        public Sprite ConsumableIcon;
        public int ConsumableRarity;
        public bool HasConsumable => !string.IsNullOrEmpty(ConsumableName);
        public int Depth;
        public Biome Biome;
        public int Coins;
        /// <summary>An overlay window owns the screen: the low-HP frame stands down so it never tints a menu.</summary>
        public bool VignetteSuppressed;
        public readonly List<HudPartyMember> Party = new();
        /// <summary>Boss bar (55/46): shown only while a boss encounter is active and the boss alive.</summary>
        public bool BossVisible;
        public string BossName = string.Empty;
        public int BossHp;
        public int BossMaxHp;
        public float BossHp01 => BossMaxHp > 0 ? Mathf.Clamp01(BossHp / (float)BossMaxHp) : 0f;
        public string HpText => $"{Hp} / {MaxHp}";
        /// <summary>The biome identity under the minimap; depth lives in the minimap's own compact chip.</summary>
        public string BiomeText => BiomeName(Biome);
        /// <summary>Just the number: the coin token beside it carries the meaning (91 Top Information).</summary>
        public string CoinsText => Coins.ToString();
        public string ConsumableText => string.IsNullOrEmpty(ConsumableName) ? "—" : $"{ConsumableName} x{ConsumableQuantity}";

        public static string BiomeName(Biome biome) => biome switch
        {
            Biome.RuinedMetro => "RUINED METRO",
            Biome.Rustworks => "RUSTWORKS",
            Biome.OvergrownLabs => "OVERGROWN LABS",
            _ => biome.ToString().ToUpperInvariant()
        };
    }

    /// <summary>
    /// Observes the local player's gameplay components, the expedition and the party life roster (119: gameplay
    /// exposes events/state, UI observes) and publishes a readable HudSnapshot. It never mutates gameplay, never
    /// searches the scene, and only re-reads cheap per-frame values (dash cooldown, heat, charge, bleedout) in Tick.
    /// </summary>
    public sealed class DungeonHudViewModel : IDisposable
    {
        private HealthComponent _health;
        private PlayerDash _dash;
        private WeaponLoadout _loadout;
        // Fallback weapon references for a view model bound without a loadout; with a loadout the slots are read live
        // (PlayerRig.MountWeapons replaces the weapon components on every equipment change).
        private IEquippableWeapon _primary;
        private IEquippableWeapon _secondary;
        private LegendarySpecialController _special;
        private PlayerInventory _inventory;
        private ExpeditionService _expedition;
        private CoinWallet _wallet;
        private PartyLifeRoster _roster;
        private PlayerLifeStateComponent _localLife;
        private readonly Dictionary<PlayerLifeStateComponent, HealthComponent> _memberHealth = new();
        private readonly Dictionary<string, string> _displayNames = new(StringComparer.Ordinal);
        private readonly HashSet<string> _disconnected = new(StringComparer.Ordinal);
        private Func<AmmoType, int> _reserve;
        private HealthComponent _bossHealth;
        private Func<bool> _bossActive;

        public HudSnapshot Snapshot { get; } = new();
        public int Publications { get; private set; }

        public event Action<HudSnapshot> Changed;

        // ---- Binding ----

        public void BindPlayer(HealthComponent health, PlayerDash dash, PlayerLifeStateComponent life = null)
        {
            Unbind(ref _health, h => { h.Damaged -= OnHealthChanged; h.Healed -= OnHealthChanged; h.Died -= OnDied; });
            _health = health;
            if (_health != null)
            {
                _health.Damaged += OnHealthChanged;
                _health.Healed += OnHealthChanged;
                _health.Died += OnDied;
            }

            _dash = dash;
            _localLife = life;
            Publish();
        }

        /// <summary>Weapons of the two class-agnostic slots; the reserve resolver reads the player's ammo without owning it.</summary>
        public void BindWeapons(WeaponLoadout loadout, IEquippableWeapon primary, IEquippableWeapon secondary, Func<AmmoType, int> reserve = null, LegendarySpecialController special = null)
        {
            if (_loadout != null) _loadout.ActiveSlotChanged -= OnActiveSlotChanged;
            _loadout = loadout;
            if (_loadout != null) _loadout.ActiveSlotChanged += OnActiveSlotChanged;
            _primary = primary;
            _secondary = secondary;
            _reserve = reserve;
            _special = special;
            Publish();
        }

        public void BindInventory(PlayerInventory inventory)
        {
            if (_inventory != null) { _inventory.EquippedChanged -= OnEquippedChanged; _inventory.BackpackChanged -= Publish; }
            _inventory = inventory;
            if (_inventory != null) { _inventory.EquippedChanged += OnEquippedChanged; _inventory.BackpackChanged += Publish; }
            Publish();
        }

        public void BindExpedition(ExpeditionService expedition)
        {
            if (_expedition != null) { _expedition.DepthEntered -= OnDepth; _expedition.ExpeditionStarted -= OnDepth; _expedition.ExpeditionEnded -= OnEnded; }
            if (_wallet != null) _wallet.Changed -= OnCoins;
            _expedition = expedition;
            _wallet = null;
            if (_expedition != null)
            {
                _expedition.DepthEntered += OnDepth;
                _expedition.ExpeditionStarted += OnDepth;
                _expedition.ExpeditionEnded += OnEnded;
                if (_expedition.State != null) AttachWallet(_expedition.State);
            }

            Publish();
        }

        public void BindParty(PartyLifeRoster roster)
        {
            if (_roster != null) _roster.MemberStateChanged -= OnMemberStateChanged;
            foreach (var pair in _memberHealth) { pair.Value.Damaged -= OnHealthChanged; pair.Value.Healed -= OnHealthChanged; }
            _memberHealth.Clear();
            _roster = roster;
            if (_roster != null)
            {
                _roster.MemberStateChanged += OnMemberStateChanged;
                foreach (var member in _roster.Members)
                {
                    var health = member != null ? member.GetComponent<HealthComponent>() : null;
                    if (health == null) continue;
                    _memberHealth[member] = health;
                    health.Damaged += OnHealthChanged;
                    health.Healed += OnHealthChanged;
                }
            }

            Publish();
        }

        /// <summary>Display names come from the session roster (sanitized); the participant id is the fallback.</summary>
        public void SetDisplayName(string participantId, string displayName)
        {
            if (string.IsNullOrEmpty(participantId)) return;
            _displayNames[participantId] = displayName ?? participantId;
            Publish();
        }

        /// <summary>85: a teammate in reconnect grace shows as DISCONNECTED until reclaimed or expired.</summary>
        public void SetMemberConnected(string participantId, bool connected)
        {
            if (string.IsNullOrEmpty(participantId)) return;
            if (connected) _disconnected.Remove(participantId); else _disconnected.Add(participantId);
            Publish();
        }

        /// <summary>
        /// Binds the boss of the current depth: name + authoritative health, visible from the moment the encounter is
        /// active until the boss is dead. Health events (including replicated ones on clients) refresh it; nothing here
        /// touches the fight.
        /// </summary>
        public void BindBoss(string displayName, HealthComponent health, Func<bool> isActive)
        {
            Unbind(ref _bossHealth, h => { h.Damaged -= OnBossHealth; h.Healed -= OnBossHealth; h.Died -= OnBossDied; });
            _bossHealth = health;
            _bossActive = isActive;
            Snapshot.BossName = displayName ?? string.Empty;
            if (_bossHealth != null)
            {
                _bossHealth.Damaged += OnBossHealth;
                _bossHealth.Healed += OnBossHealth;
                _bossHealth.Died += OnBossDied;
            }

            RefreshBoss();
            Raise();
        }

        private void OnBossHealth(int _) { if (RefreshBoss()) Raise(); }
        private void OnBossDied() { if (RefreshBoss()) Raise(); }

        private bool RefreshBoss()
        {
            var before = (Snapshot.BossVisible, Snapshot.BossHp, Snapshot.BossMaxHp);
            var active = _bossHealth != null && _bossHealth.IsAlive && (_bossActive == null || _bossActive());
            Snapshot.BossVisible = active;
            Snapshot.BossHp = _bossHealth != null ? _bossHealth.CurrentHealth : 0;
            Snapshot.BossMaxHp = _bossHealth != null ? _bossHealth.MaxHealth : 0;
            return before != (Snapshot.BossVisible, Snapshot.BossHp, Snapshot.BossMaxHp);
        }

        /// <summary>
        /// An overlay window (inventory, pause, merchant, weapon cache) is up: the low-HP danger frame stands down so
        /// it never tints a menu, and returns the moment the last window closes.
        /// </summary>
        public void SetOverlayOpen(bool open)
        {
            if (Snapshot.VignetteSuppressed == open) return;
            Snapshot.VignetteSuppressed = open;
            Raise();
        }

        // ---- Per-frame cheap refresh (no allocation, no searches) ----

        public void Tick()
        {
            var changed = false;
            if (_health != null && (Snapshot.Hp != _health.CurrentHealth || Snapshot.MaxHp != _health.MaxHealth))
            {
                // Max HP moves through PlayerStats -> ResizeMaxHealth without a health event; the readout follows it.
                Snapshot.Hp = _health.CurrentHealth;
                Snapshot.MaxHp = _health.MaxHealth;
                changed = true;
            }

            if (_dash != null)
            {
                var cooldown = _dash.CurrentDashCooldown;
                var remaining = _dash.CooldownRemaining;
                var ready = _dash.CanDash;
                var frac = cooldown > 0f ? Mathf.Clamp01(remaining / cooldown) : 0f;
                var disabled = !_dash.enabled || (_localLife != null && !_localLife.CanAct);
                changed |= ready != Snapshot.DashReady || disabled != Snapshot.DashDisabled || Mathf.Abs(frac - Snapshot.DashCooldown01) > 0.001f;
                Snapshot.DashReady = ready;
                Snapshot.DashCooldown01 = frac;
                Snapshot.DashDisabled = disabled;
            }
            else if (!Snapshot.DashDisabled)
            {
                Snapshot.DashDisabled = true;
                changed = true;
            }

            changed |= RefreshWeapon(Snapshot.Primary, WeaponOf(WeaponSlot.Primary), WeaponSlot.Primary);
            changed |= RefreshWeapon(Snapshot.Secondary, WeaponOf(WeaponSlot.Secondary), WeaponSlot.Secondary);
            changed |= RefreshConsumable();
            changed |= RefreshParty();
            changed |= RefreshBoss();
            if (changed) Raise();
        }

        // ---- Snapshot assembly ----

        private void Publish()
        {
            if (_health != null)
            {
                Snapshot.Hp = _health.CurrentHealth;
                Snapshot.MaxHp = _health.MaxHealth;
            }

            RefreshWeapon(Snapshot.Primary, WeaponOf(WeaponSlot.Primary), WeaponSlot.Primary);
            RefreshWeapon(Snapshot.Secondary, WeaponOf(WeaponSlot.Secondary), WeaponSlot.Secondary);

            RefreshConsumable();

            if (_expedition?.State != null)
            {
                Snapshot.Depth = _expedition.State.Depth;
                Snapshot.Biome = _expedition.State.Biome;
                Snapshot.Coins = _expedition.State.CarriedCoins;
            }

            RefreshParty();
            Raise();
        }

        /// <summary>The active stack: a use decrements the instance's quantity without an inventory event, so the per-frame tick re-reads it (cheap: one dictionary lookup).</summary>
        private bool RefreshConsumable()
        {
            var before = (Snapshot.ConsumableName, Snapshot.ConsumableQuantity, Snapshot.ConsumableIcon, Snapshot.ConsumableRarity);
            var consumable = _inventory?.GetEquipped(EquippedSlot.ActiveConsumable);
            Snapshot.ConsumableName = consumable != null ? NameOf(consumable) : string.Empty;
            Snapshot.ConsumableQuantity = consumable?.Quantity ?? 0;
            Snapshot.ConsumableIcon = consumable != null ? DefinitionOf(consumable)?.Icon : null;
            Snapshot.ConsumableRarity = consumable != null ? (int)consumable.Rarity : 0;
            return before != (Snapshot.ConsumableName, Snapshot.ConsumableQuantity, Snapshot.ConsumableIcon, Snapshot.ConsumableRarity);
        }

        private string NameOf(ItemInstance item)
        {
            var definition = DefinitionOf(item);
            return definition != null && !string.IsNullOrEmpty(definition.DisplayName) ? definition.DisplayName : item.DefinitionId;
        }

        private ItemDefinition DefinitionOf(ItemInstance item) => item != null && _inventory != null ? _inventory.Resolve(item.DefinitionId) : null;

        /// <summary>
        /// The weapon component currently mounted in a slot. With a loadout bound this is read live on every refresh, so
        /// the HUD follows PlayerRig.MountWeapons (which destroys and re-adds the components on EquippedChanged) instead
        /// of holding a reference to a destroyed component; the bound references only serve a loadout-less binding.
        /// </summary>
        private IEquippableWeapon WeaponOf(WeaponSlot slot)
        {
            if (_loadout != null) return _loadout.GetSlot(slot);
            return slot == WeaponSlot.Primary ? _primary : _secondary;
        }

        private static EquippedSlot InventorySlotOf(WeaponSlot slot) => slot == WeaponSlot.Primary ? EquippedSlot.PrimaryWeapon : EquippedSlot.SecondaryWeapon;

        private bool RefreshWeapon(HudWeaponState state, IEquippableWeapon weapon, WeaponSlot slot)
        {
            var before = (state.Name, state.IsActive, state.Resource, state.Magazine, state.Reserve, Mathf.RoundToInt(state.Heat01 * 100f), state.Overheated, Mathf.RoundToInt(state.Charge01 * 100f), state.IsCharging, state.NoAmmo, state.SpecialName, Mathf.RoundToInt(state.SpecialCooldown01 * 100f), state.SpecialReady, state.DefinitionId, state.Icon, state.Rarity);
            if (weapon is UnityEngine.Object unityWeapon && unityWeapon == null) weapon = null;   // a destroyed component is no weapon
            state.IsActive = _loadout != null && _loadout.ActiveSlot == slot && weapon != null;
            var item = _inventory?.GetEquipped(InventorySlotOf(slot));
            state.DefinitionId = item?.DefinitionId ?? string.Empty;
            state.Icon = item != null ? DefinitionOf(item)?.Icon : null;
            state.Rarity = item != null ? (int)item.Rarity : 0;
            state.SpecialName = null;
            state.SpecialCooldown01 = 0f;
            state.SpecialReady = false;
            switch (weapon)
            {
                case RangedWeapon ranged:
                    state.Name = ranged.Definition != null ? ranged.Definition.DisplayName : "—";
                    state.Resource = HudResourceKind.Ammo;
                    state.Magazine = ranged.MagazineAmmo;
                    state.Reserve = ranged.Definition != null && _reserve != null ? _reserve(ranged.Definition.AmmoType) : 0;
                    state.NoAmmo = state.Magazine <= 0 && state.Reserve <= 0;
                    break;
                case BlasterWeapon blaster:
                    state.Name = blaster.Definition != null ? blaster.Definition.DisplayName : "—";
                    state.Resource = HudResourceKind.Heat;
                    state.Heat01 = blaster.Heat.HeatFraction;
                    state.Overheated = blaster.Heat.IsOverheated;
                    state.NoAmmo = false;
                    break;
                case BowWeapon bow:
                    state.Name = bow.Definition != null ? bow.Definition.DisplayName : "—";
                    state.Resource = HudResourceKind.Charge;
                    state.Charge01 = bow.ChargeFraction;
                    state.IsCharging = bow.IsCharging;
                    state.NoAmmo = false;
                    break;
                case MeleeWeapon melee:
                    state.Name = melee.Definition != null ? melee.Definition.DisplayName : "—";
                    state.Resource = HudResourceKind.None;
                    state.NoAmmo = false;
                    break;
                default:
                    state.Name = weapon == null ? "—" : weapon.GetType().Name;
                    state.Resource = HudResourceKind.None;
                    break;
            }

            if (state.IsActive && _special != null && _special.Special != null && _special.State != null && _special.IsWeaponEquipped)
            {
                state.SpecialName = _special.Special.Id;
                state.SpecialCooldown01 = _special.State.CooldownSeconds > 0f ? Mathf.Clamp01(_special.State.CooldownRemaining / _special.State.CooldownSeconds) : 0f;
                state.SpecialReady = _special.State.IsReady;
            }

            var after = (state.Name, state.IsActive, state.Resource, state.Magazine, state.Reserve, Mathf.RoundToInt(state.Heat01 * 100f), state.Overheated, Mathf.RoundToInt(state.Charge01 * 100f), state.IsCharging, state.NoAmmo, state.SpecialName, Mathf.RoundToInt(state.SpecialCooldown01 * 100f), state.SpecialReady, state.DefinitionId, state.Icon, state.Rarity);
            return !before.Equals(after);
        }

        private bool RefreshParty()
        {
            var members = _roster != null ? _roster.Members.Where(m => m != null && m != _localLife).ToList() : new List<PlayerLifeStateComponent>();
            var changed = members.Count != Snapshot.Party.Count;
            while (Snapshot.Party.Count < members.Count) Snapshot.Party.Add(new HudPartyMember());
            while (Snapshot.Party.Count > members.Count) Snapshot.Party.RemoveAt(Snapshot.Party.Count - 1);
            for (var i = 0; i < members.Count; i++)
            {
                var member = members[i];
                var line = Snapshot.Party[i];
                var health = _memberHealth.TryGetValue(member, out var h) ? h : member.GetComponent<HealthComponent>();
                var name = _displayNames.TryGetValue(member.ParticipantId ?? string.Empty, out var display) ? display : member.ParticipantId;
                var state = _disconnected.Contains(member.ParticipantId ?? string.Empty) ? HudLifeState.Disconnected : member.State switch
                {
                    PlayerLifeState.Downed => HudLifeState.Downed,
                    PlayerLifeState.Dead => HudLifeState.Dead,
                    _ => HudLifeState.Alive
                };
                var bleedout = member.IsDowned ? member.BleedoutRemaining : 0f;
                changed |= line.Name != name || line.State != state || line.Hp != (health != null ? health.CurrentHealth : 0) || line.MaxHp != (health != null ? health.MaxHealth : 0) || Mathf.CeilToInt(line.BleedoutRemaining) != Mathf.CeilToInt(bleedout);
                line.Name = name;
                line.State = state;
                line.Hp = health != null ? health.CurrentHealth : 0;
                line.MaxHp = health != null ? health.MaxHealth : 0;
                line.BleedoutRemaining = bleedout;
            }

            return changed;
        }

        private void Raise()
        {
            Publications++;
            Changed?.Invoke(Snapshot);
        }

        // ---- Event handlers ----

        private void OnHealthChanged(int _) => Publish();
        private void OnDied() => Publish();
        private void OnActiveSlotChanged(WeaponSlot _) => Publish();
        private void OnEquippedChanged(EquippedSlot _, ItemInstance __) => Publish();
        private void OnCoins(CoinReceipt _) => Publish();
        private void OnEnded(ExpeditionSummary _) => Publish();
        private void OnMemberStateChanged(PlayerLifeStateComponent member, PlayerLifeState from, PlayerLifeState to)
        {
            if (!_memberHealth.ContainsKey(member))
            {
                var health = member.GetComponent<HealthComponent>();
                if (health != null)
                {
                    _memberHealth[member] = health;
                    health.Damaged += OnHealthChanged;
                    health.Healed += OnHealthChanged;
                }
            }

            Publish();
        }

        private void OnDepth(ExpeditionState state)
        {
            AttachWallet(state);
            Publish();
        }

        private void AttachWallet(ExpeditionState state)
        {
            if (_wallet == state.CarriedWallet) return;
            if (_wallet != null) _wallet.Changed -= OnCoins;
            _wallet = state.CarriedWallet;
            if (_wallet != null) _wallet.Changed += OnCoins;
        }

        private static void Unbind<T>(ref T field, Action<T> unsubscribe) where T : class
        {
            if (field != null) unsubscribe(field);
            field = null;
        }

        public void Dispose()
        {
            BindPlayer(null, null);
            BindWeapons(null, null, null);
            BindInventory(null);
            BindExpedition(null);
            BindParty(null);
            BindBoss(null, null, null);
        }
    }
}
