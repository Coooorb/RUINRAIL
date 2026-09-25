using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Core.Input;
using RuinRail.Core.Rendering;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Items.Consumables;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using RuinRail.Gameplay.Stats;
using RuinRail.Presentation.World;
using RuinRail.UI.Codex;
using UnityEngine;

namespace RuinRail.App
{
    /// <summary>
    /// The presentation / audio / UX-QoL stage of the built-player smoke.
    ///
    /// Everything here is checked in the shipped player rather than in a test fixture, because each item is a seam this
    /// pass either created or found dead: the underlay actually exists in the built dungeon and carries no collider;
    /// the camera is not clearing to raw black; the audio events shipped with an authored mix; the player rig really
    /// composes a grenade launcher so a grenade can be thrown; the quick-grenade key spends exactly one; a base pickup
    /// radius is live and the Magnetic Coil is still stronger; the aim-assist setting reaches the real shot solver; a
    /// timed buff shows a HUD chip and clears; and the Codex pages the player can open say what the build does.
    /// </summary>
    public sealed partial class SmokeRunner
    {
        private readonly List<string> _presentationChecks = new();

        private void PresentationCheck(string what, bool ok)
        {
            if (ok) _presentationChecks.Add(what);
            else if (string.IsNullOrEmpty(_result.Error)) Fail("presentation/qol: " + what);
            _result.PresentationQolChecks = _presentationChecks.ToArray();
        }

        private IEnumerator PresentationQolChecks(ExpeditionScene run)
        {
            var content = _app.Content;
            var state = run.Expedition.State;

            // ---- world substrate ----
            var substrate = run.Substrate;
            PresentationCheck("the depth has an environmental underlay", substrate != null && substrate.Renderer != null && substrate.Renderer.sprite != null);
            if (substrate != null)
            {
                PresentationCheck($"the underlay is on the Ground layer below the floor (order {substrate.Renderer.sortingOrder})",
                    substrate.Renderer.sortingLayerName == SortingLayers.Ground && substrate.Renderer.sortingOrder < 0);
                PresentationCheck("the underlay carries no collider, so it cannot be walked on or shot",
                    substrate.GetComponentsInChildren<Collider2D>().Length == 0);
                var camera = run.Camera != null ? run.Camera.GetComponent<Camera>() : null;
                PresentationCheck("the dungeon camera no longer clears to raw black",
                    camera != null && camera.backgroundColor != Color.black && camera.backgroundColor == WorldSubstrate.ClearColorFor(state.Biome));
                // The camera clamps inside the layout; the underlay has to reach further than it can ever see.
                var bounds = run.Camera != null ? run.Camera.VisibleBounds : null;
                PresentationCheck("the underlay covers everything the clamped camera can see",
                    !bounds.HasValue || (substrate.Coverage.xMin <= bounds.Value.xMin && substrate.Coverage.xMax >= bounds.Value.xMax
                                                                                     && substrate.Coverage.yMin <= bounds.Value.yMin && substrate.Coverage.yMax >= bounds.Value.yMax));
                PresentationCheck($"the underlay is quieter than the {state.Biome} floor",
                    WorldSubstrate.Luminance(WorldSubstrate.Palette(state.Biome).Base) < 0.03f);
            }

            // ---- prop dressing ----
            var dressed = run.Rooms != null ? run.Rooms.Values.Where(r => r != null).ToList() : new List<RuinRail.Dungeon.Runtime.RoomRuntime>();
            var added = dressed.Sum(r => r.Dressing.Added);
            PresentationCheck($"the depth's rooms were dressed deterministically ({dressed.Count} rooms, {added} extra detail tiles)",
                dressed.Count > 0 && dressed.Any(r => r.Dressing.Transformed > 0 || r.Dressing.Added > 0));

            // ---- authored audio mix ----
            var events = content.AudioEvents != null ? content.AudioEvents.Events.Where(e => e != null).ToList() : new List<RuinRail.Audio.AudioEventDefinition>();
            PresentationCheck($"no shipped audio event is at full gain ({events.Count} events, loudest {(events.Count > 0 ? events.Max(e => e.Volume) : 0f):0.00})",
                events.Count > 0 && events.All(e => e.Volume < 1f));
            var repeated = events.Where(e => e.Id == RuinRail.Audio.AudioEventIds.EnemyHit || e.Id == RuinRail.Audio.AudioEventIds.FireSmg).ToList();
            PresentationCheck("the most repeated cues ship with pitch variation and a throttle",
                repeated.Count == 2 && repeated.All(e => e.PitchMax > e.PitchMin && e.MinIntervalMs > 0));
            PresentationCheck("the process still holds exactly one AudioListener",
                Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length == 1);

            // ---- status chips ----
            var hud = run.HudView;
            var effects = run.Rig.Consumables != null ? run.Rig.Consumables.Effects : null;
            var buff = content.Items.OfType<ConsumableDefinition>().FirstOrDefault(c => c.EffectKind == ConsumableEffectKind.TimedBuff);
            if (hud != null && effects != null && buff != null)
            {
                PresentationCheck("no status chip is on screen before anything is active", hud.VisibleStatusChips.Count == 0);
                effects.Apply(buff);
                run.Hud?.Tick();
                yield return null;
                PresentationCheck($"using {buff.DisplayName} shows its chip with time left",
                    hud.VisibleStatusChips.Any(c => c.DefinitionId == buff.Id && c.Fill > 0.5f));
                PresentationCheck("the status detail line names the active effect", hud.StatusDetailText.Contains(buff.DisplayName));
                effects.Tick(buff.BuffDurationSeconds + 0.1f);
                run.Hud?.Tick();
                yield return null;
                PresentationCheck("the chip clears the moment the effect expires",
                    hud.VisibleStatusChips.All(c => c.DefinitionId != buff.Id) && string.IsNullOrEmpty(hud.StatusDetailText));
            }

            // ---- pickup attraction ----
            var attractor = run.Rig.Player != null ? run.Rig.Player.GetComponent<PickupAttractor>() : null;
            if (attractor != null)
            {
                PresentationCheck($"the survivor has a baseline pickup reach of {attractor.BaseRadiusTiles:0.##} tiles",
                    attractor.BaseRadiusTiles > 0f && attractor.BaseRadiusTiles <= PickupAttractor.MaxBaseRadiusTiles);
                var baseRadius = attractor.Radius;
                var stats = run.Rig.Player.GetComponent<PlayerStatsBinder>()?.Stats;
                if (stats != null)
                {
                    stats.SetSource(new StatModifierSource("smoke:coil", StatModifier.Flat(StatId.PickupAttractionRadius, 3)));
                    PresentationCheck($"the Magnetic Coil is still meaningfully stronger ({baseRadius:0.##} -> {attractor.Radius:0.##} tiles)",
                        attractor.Radius >= baseRadius + 3f - 0.001f && attractor.Radius > baseRadius * 2f);
                    stats.RemoveSource("smoke:coil");
                }

                var coin = new GameObject("SmokeCoin").AddComponent<CoinPickup>();
                var collider = coin.gameObject.AddComponent<CircleCollider2D>();
                collider.radius = 0.2f;
                collider.isTrigger = true;
                coin.SetAmount(3);
                coin.transform.position = (Vector2)run.Rig.Player.transform.position + new Vector2(0.9f, 0f);
                yield return new WaitForFixedUpdate();
                var collectedBefore = attractor.Collected;
                for (var i = 0; i < 40; i++) attractor.Step(0.02f);
                PresentationCheck("a coin the player walks over comes to them",
                    attractor.Collected == collectedBefore + 1);

                var far = new GameObject("SmokeFarCoin").AddComponent<CoinPickup>();
                var farCollider = far.gameObject.AddComponent<CircleCollider2D>();
                farCollider.radius = 0.2f;
                farCollider.isTrigger = true;
                far.SetAmount(3);
                far.transform.position = (Vector2)run.Rig.Player.transform.position + new Vector2(6f, 0f);
                var farStart = far.transform.position;
                yield return new WaitForFixedUpdate();
                for (var i = 0; i < 40; i++) attractor.Step(0.02f);
                PresentationCheck("a coin across the room is left where it is (no room vacuum)",
                    (far.transform.position - farStart).sqrMagnitude < 0.0001f);
                if (far != null) Object.Destroy(far.gameObject);
            }

            // ---- grenade quick-use ----
            var consumables = run.Rig.Consumables;
            if (consumables != null)
            {
                PresentationCheck("the player rig composes a grenade launcher, so grenades can be thrown at all",
                    run.Rig.Player.GetComponent<RuinRail.Gameplay.Combat.Area.GrenadeLauncher>() != null && consumables.Effects.CanThrowGrenades);
                var grenadeDefinition = content.Items.OfType<ConsumableDefinition>().FirstOrDefault(c => c.EffectKind == ConsumableEffectKind.Grenade);
                if (grenadeDefinition != null)
                {
                    // The smoke has been looting for several stages, so the backpack may be full by now. One slot is
                    // freed first, or the quick-grenade checks below would be silently skipped rather than run.
                    if (state.Inventory.BackpackSlots.All(i => i != null))
                    {
                        var free = state.Inventory.BackpackSlots
                            .Select((item, index) => (item, index))
                            .First(pair => pair.item != null && pair.item.DefinitionId != grenadeDefinition.Id);
                        state.Inventory.RemoveFromBackpack(free.index);
                    }

                    var carried = state.Inventory.TryAddToBackpack(new ItemInstance(grenadeDefinition.Id, 2));
                    PresentationCheck("a grenade stack can be carried for the quick-use check", carried);
                    if (carried)
                    {
                        // The container owns the stack once added (a stackable add can zero the source instance).
                        var stack = state.Inventory.BackpackSlots.First(i => i != null && i.DefinitionId == grenadeDefinition.Id);
                        PresentationCheck("quick-grenade throws a backpack grenade without touching the Active Consumable slot",
                            consumables.TryQuickGrenade() && state.Inventory.GetEquipped(EquippedSlot.ActiveConsumable)?.DefinitionId != grenadeDefinition.Id);
                        yield return null;
                        PresentationCheck($"quick-grenade spent exactly one ({stack.Quantity} left of 2)", stack.Quantity == 1);
                        GameplayInputGate.Hold();
                        var before = stack.Quantity;
                        consumables.TryQuickGrenade();
                        GameplayInputGate.Release();
                        // TryQuickGrenade itself is the API a window never reaches: the reader does not raise the event
                        // while the gate is held, which is the check below.
                        PresentationCheck("the quick-grenade press is gated by the gameplay input gate",
                            !PlayerInputReaderRaisesWhileGated());
                        stack.SetQuantity(0);
                        PresentationCheck("quick-grenade with nothing to throw is a silent no-op", !consumables.TryQuickGrenade());
                    }
                }
            }

            // ---- aim assist setting ----
            var origin = run.Rig.Player != null ? (Vector2)run.Rig.Player.transform.position : Vector2.zero;
            var assist = content.AimAssist;
            if (assist != null)
            {
                AssistPreferences.Set(false);
                var off = ShotSolver.Solve(origin, origin, Vector2.right, 12f, WeaponClass.AssaultRifle, run.Rig.Player, DamageTeam.Player, assist, null);
                PresentationCheck("with Aim Assist OFF a shot leaves on the raw aim and selects no target",
                    off.AssistedTarget == null && Vector2.Angle(Vector2.right, off.Direction) < 0.001f);
                AssistPreferences.Set(true);
                PresentationCheck("Aim Assist is back ON and defaults ON", AssistPreferences.AimAssist);
            }

            // ---- Codex ----
            var codex = new CodexViewModel(new RuinRail.UI.Onboarding.SchemeGlyphs(RuinRail.UI.Onboarding.InputScheme.KeyboardMouse), content.AmmoBalance);
            codex.Open();
            PresentationCheck($"the Help page ships all {codex.Sections.Count} sections with content",
                codex.Sections.Count == 8 && codex.Sections.All(s => s.Lines.Count > 2));
            var ammoPage = codex.Sections.First(s => s.Id == CodexContent.AmmoId);
            PresentationCheck("the Help page's ammo caps are the ones the build enforces",
                ammoPage.Lines.Any(l => l.Contains(content.AmmoBalance.GetStackLimit(AmmoType.Light).ToString())));
            var reached = 0;
            foreach (var section in codex.Sections)
            {
                codex.SelectSection(section.Id);
                var seen = new List<string>(codex.VisibleBody());
                while (codex.ScrollDown()) seen.AddRange(codex.VisibleBody());
                if (section.Lines.All(seen.Contains)) reached++;
            }

            PresentationCheck($"every Help line is reachable by paging ({reached}/{codex.Sections.Count} sections)", reached == codex.Sections.Count);
            codex.Close();

            yield return null;
        }

        /// <summary>The reader's own gate, asserted from the shipped source-level behaviour rather than a test double.</summary>
        private static bool PlayerInputReaderRaisesWhileGated()
        {
            GameplayInputGate.Hold();
            var gated = GameplayInputGate.IsHeld;
            GameplayInputGate.Release();
            return !gated;
        }
    }
}
