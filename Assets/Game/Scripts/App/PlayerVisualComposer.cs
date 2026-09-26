using RuinRail.Core.Input;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Player;
using RuinRail.Presentation.Animation;
using UnityEngine;

namespace RuinRail.App
{
    /// <summary>
    /// The one composition of a player's presentation: body (CharacterVisual + PlayerAnimationDriver) and held weapon
    /// (WeaponVisualDriver + HeldWeaponVisual), bound to whatever gameplay components the object carries. Used by the
    /// solo expedition for the local player, by the presence factory for every session member, and — through
    /// <see cref="RuinRail.Networking.NetworkPlayerObject.VisualComposer"/> — by every replicated player object on
    /// every peer. Idempotent: composing twice never stacks a second renderer or driver.
    /// </summary>
    public static class PlayerVisualComposer
    {
        public static HeldWeaponVisual Compose(GameObject player, GameContentCatalog content, IPlayerInputReader reader = null)
        {
            if (player == null || content == null) return null;
            var body = CharacterVisual.Attach(player, content.AnimationSetFor(CharacterVisual.PlayerActorId));

            var animation = player.GetComponent<PlayerAnimationDriver>();
            if (animation == null)
            {
                animation = player.AddComponent<PlayerAnimationDriver>();
                animation.Configure(body, player.GetComponent<PlayerLifeStateComponent>(), player.GetComponent<PlayerDash>(), player.GetComponent<PlayerAiming>(),
                    reader ?? player.GetComponent<PlayerInput>()?.Reader, player.GetComponent<Rigidbody2D>());
            }

            // Taking damage reads on the survivor itself: a short red tint of the body on every applied hit
            // (HealthComponent.Damaged — replicated health drives it on remote peers too). No stagger tint: the player's
            // own impact reactions are the camera and the damage indicator's business.
            if (body != null && player.GetComponent<RuinRail.Presentation.Vfx.HitFlash>() == null && player.GetComponent<RuinRail.Gameplay.Combat.HealthComponent>() != null)
            {
                var flash = player.AddComponent<RuinRail.Presentation.Vfx.HitFlash>();
                flash.Configure(content.Feedback, player.GetComponent<RuinRail.Gameplay.Combat.HealthComponent>(), null, body.Renderer);
                flash.UsePlayerProfile();
            }

            var loadout = player.GetComponent<WeaponLoadout>();
            var weaponDriver = player.GetComponent<WeaponVisualDriver>();
            if (weaponDriver == null) weaponDriver = player.AddComponent<WeaponVisualDriver>();

            var held = player.GetComponent<HeldWeaponVisual>();
            if (held == null) held = player.AddComponent<HeldWeaponVisual>();
            held.Configure(loadout, content.WeaponSpriteFor, player.GetComponent<PlayerAiming>(), weaponDriver, body != null ? body.Renderer : null);
            return held;
        }
    }
}
