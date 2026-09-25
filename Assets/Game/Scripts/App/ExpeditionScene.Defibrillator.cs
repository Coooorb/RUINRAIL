using System.Linq;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Items.Consumables;
using RuinRail.Gameplay.Player;
using RuinRail.Networking;

namespace RuinRail.App
{
    /// <summary>
    /// The Defibrillator's runtime hook (84 / items 31). The consumable's revive request used to reach nothing: the rig
    /// configured its consumables without a revive requester, so every use was refused as an unsupported effect while
    /// the item could still be looted — the same "provider exists, no caller composes it" shape as the other dead seams.
    ///
    /// Solo and host: the party's revive authority revives the closest fully Dead teammate in reach, once, and the use
    /// spends the unit only when that happened (solo has no teammate, so a use is refused at no cost). A co-op client
    /// asks the host, which validates against its own copy of the member (alive, carrying that Revive consumable) and
    /// its own party; the client spends the unit only on the host's acceptance.
    /// </summary>
    public sealed partial class ExpeditionScene
    {
        public const string RevivedNotice = "TEAMMATE REVIVED";
        public const string NoOneToReviveNotice = "NO FALLEN TEAMMATE IN REACH";

        /// <summary>Defibrillator uses the host accepted (diagnostics / proof).</summary>
        public int DefibrillatorRevives { get; private set; }

        private bool RequestDefibrillatorRevive(ReviveRequest request)
        {
            var life = _rig?.Player != null ? _rig.Player.GetComponent<PlayerLifeStateComponent>() : null;
            if (request == null || life == null || !life.CanAct) return false;
            if (Mode == CoopRunMode.Client)
            {
                // The host decides; the unit is spent when its verdict arrives (OnClientReviveResult), never here.
                _coopClient?.RequestRevive(request.ConsumableId, _expedition?.State?.TransactionId);
                return false;
            }

            var revived = _services?.ReviveAuthority is PartyReviveAuthority authority && authority.ReviveWithDefibrillator(life, request);
            if (revived) DefibrillatorRevives++;
            Notify(revived ? RevivedNotice : NoOneToReviveNotice, !revived);
            return revived;
        }

        /// <summary>Host: a member's Defibrillator use, validated against the host's own state of that member.</summary>
        private void OnClientReviveRequested(ulong clientId, ReviveRequestMessage request)
        {
            if (request == null || _coopHost == null) return;
            var result = new ReviveResultMessage { TransactionId = request.TransactionId, ConsumableId = request.ConsumableId };
            var entity = _party?.Presence.Entities.TryGetValue(clientId, out var e) == true ? e.GameObject : null;
            var life = entity != null ? entity.GetComponent<PlayerLifeStateComponent>() : null;
            var mirror = MirrorInventoryOf(clientId);
            // The definition comes from the host's catalog: the member names an item, the host supplies its numbers.
            var definition = _app?.Configs?.Resolve(request.ConsumableId) as ConsumableDefinition;
            var carried = mirror != null && definition != null && (mirror.GetEquipped(EquippedSlot.ActiveConsumable)?.DefinitionId == definition.Id
                || mirror.BackpackSlots.Any(i => i != null && i.DefinitionId == definition.Id && i.Quantity > 0));
            if (life == null || !life.CanAct) result.Reason = "the user cannot act";
            else if (definition == null || definition.EffectKind != ConsumableEffectKind.Revive) result.Reason = "not a revive consumable";
            else if (!carried) result.Reason = "the member does not carry it";
            else if (_services?.ReviveAuthority is PartyReviveAuthority authority)
            {
                var target = authority.FindDeadTeammateInReach(life);
                result.Accepted = target != null && authority.ReviveWithDefibrillator(life, new ReviveRequest(definition.Id, definition.ReviveHealthPercent));
                result.RevivedParticipantId = result.Accepted ? target.ParticipantId : string.Empty;
                if (!result.Accepted) result.Reason = "no fallen teammate in reach";
            }

            if (result.Accepted) DefibrillatorRevives++;
            _coopHost.SendReviveResult(clientId, result);
        }

        /// <summary>Client: the host's verdict. Only an accepted use spends the unit, exactly one.</summary>
        private void OnClientReviveResult(ReviveResultMessage result)
        {
            if (result == null) return;
            if (result.Accepted)
            {
                DefibrillatorRevives++;
                SpendOne(_expedition?.State?.Inventory, result.ConsumableId);
            }

            Notify(result.Accepted ? RevivedNotice : NoOneToReviveNotice, !result.Accepted);
        }

        private static void SpendOne(PlayerInventory inventory, string definitionId)
        {
            if (inventory == null || string.IsNullOrEmpty(definitionId)) return;
            var active = inventory.GetEquipped(EquippedSlot.ActiveConsumable);
            if (active != null && active.DefinitionId == definitionId && active.Quantity > 0)
            {
                active.SetQuantity(active.Quantity - 1);
                if (active.Quantity == 0) inventory.Unequip(EquippedSlot.ActiveConsumable);
                return;
            }

            var slots = inventory.BackpackSlots;
            for (var i = 0; i < slots.Count; i++)
            {
                if (slots[i] == null || slots[i].DefinitionId != definitionId || slots[i].Quantity <= 0) continue;
                slots[i].SetQuantity(slots[i].Quantity - 1);
                if (slots[i].Quantity == 0) inventory.RemoveFromBackpack(i);
                return;
            }
        }
    }
}
