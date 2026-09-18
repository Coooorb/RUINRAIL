using System;
using System.Collections.Generic;
using RuinRail.Core.Rendering;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Player;
using UnityEngine;

namespace RuinRail.Presentation.Animation
{
    /// <summary>
    /// The held weapon in an actor's hands: one WeaponPivot (rotated through the full 360° by <see cref="PlayerAiming"/>,
    /// which owns the aim), one WeaponSprite under it (the weapon's +X-authored world sprite, grip at the pivot, driven
    /// by <see cref="WeaponVisualDriver"/> for recoil/swing) and one Muzzle at the sprite's firing end, handed to the
    /// ranged weapon components so a projectile leaves the visible barrel.
    ///
    /// Only the loadout's active weapon is ever drawn: the inactive slot has no renderer of its own, so switching
    /// slots swaps the sprite rather than toggling two. Sorting follows art/102: the weapon draws on the Weapons layer
    /// over the body, except when the aim points away from the camera (the N/NE/NW facings), where it drops behind the
    /// body — still above every floor and prop layer. A pure replica without a loadout is fed the replicated weapon id
    /// through <see cref="IHeldWeaponView"/>. Nothing here changes what a weapon does.
    /// </summary>
    public sealed class HeldWeaponVisual : MonoBehaviour, IHeldWeaponView
    {
        public const string PivotName = "WeaponPivot";
        public const string SpriteName = "WeaponSprite";
        public const string MuzzleName = "Muzzle";
        /// <summary>Hand height above the feet, in tiles (the player sheet holds the weapon at hip/chest height).</summary>
        public const float GripHeight = 0.55f;
        /// <summary>Aim y above this (unit vector) reads as "facing away": the weapon goes behind the body.</summary>
        public const float BehindBodyAimY = 0.35f;

        private WeaponLoadout _loadout;
        private Func<string, Sprite> _resolveSprite;
        private PlayerAiming _aiming;
        private WeaponVisualDriver _driver;
        private SpriteRenderer _body;
        private Transform _pivot;
        private SpriteRenderer _sprite;
        private Transform _muzzle;
        private IEquippableWeapon _boundWeapon;
        private string _shownId;
        private readonly List<IEquippableWeapon> _muzzleBound = new();

        public Transform Pivot => _pivot;
        public SpriteRenderer Renderer => _sprite;
        public Transform Muzzle => _muzzle;
        public string ShownWeaponId => _shownId;
        public bool IsVisible => _sprite != null && _sprite.enabled && _sprite.sprite != null;
        public bool IsBehindBody { get; private set; }
        public int Rebinds { get; private set; }
        public IReadOnlyList<IEquippableWeapon> MuzzleBound => _muzzleBound;

        /// <summary>Composes the pivot/sprite/muzzle once and binds the aim, the loadout (may be null for a replica) and the driver.</summary>
        public void Configure(WeaponLoadout loadout, Func<string, Sprite> resolveSprite, PlayerAiming aiming, WeaponVisualDriver driver, SpriteRenderer body)
        {
            if (_loadout != null) _loadout.ActiveSlotChanged -= OnActiveSlotChanged;
            _loadout = loadout;
            _resolveSprite = resolveSprite;
            _aiming = aiming;
            _driver = driver;
            _body = body != null ? body : CharacterVisual.RendererOf(gameObject);
            EnsureHierarchy();
            if (_aiming != null) _aiming.SetAimPivot(_pivot);
            if (_driver != null) _driver.Configure(_loadout, _sprite.transform);
            if (_loadout != null) _loadout.ActiveSlotChanged += OnActiveSlotChanged;
            _boundWeapon = null;
            Refresh();
        }

        private void OnDestroy()
        {
            if (_loadout != null) _loadout.ActiveSlotChanged -= OnActiveSlotChanged;
        }

        private void OnActiveSlotChanged(WeaponSlot _) => Refresh();

        private void EnsureHierarchy()
        {
            if (_pivot != null) return;
            var existing = transform.Find(PivotName);
            _pivot = existing != null ? existing : new GameObject(PivotName).transform;
            _pivot.SetParent(transform, false);
            _pivot.localPosition = new Vector3(0f, GripHeight, 0f);

            var spriteTransform = _pivot.Find(SpriteName);
            if (spriteTransform == null)
            {
                spriteTransform = new GameObject(SpriteName).transform;
                spriteTransform.SetParent(_pivot, false);
            }

            _sprite = spriteTransform.GetComponent<SpriteRenderer>();
            if (_sprite == null) _sprite = spriteTransform.gameObject.AddComponent<SpriteRenderer>();
            _sprite.color = Color.white;
            SpriteSorting.Apply(_sprite, SortingRole.Weapon);

            var muzzle = spriteTransform.Find(MuzzleName);
            _muzzle = muzzle != null ? muzzle : new GameObject(MuzzleName).transform;
            _muzzle.SetParent(spriteTransform, false);
        }

        /// <summary>The definition id of a mounted weapon component, or null.</summary>
        public static string DefinitionIdOf(IEquippableWeapon weapon) => weapon switch
        {
            RangedWeapon r => r.Definition != null ? r.Definition.Id : null,
            BlasterWeapon b => b.Definition != null ? b.Definition.Id : null,
            BowWeapon w => w.Definition != null ? w.Definition.Id : null,
            MeleeWeapon m => m.Definition != null ? m.Definition.Id : null,
            _ => null
        };

        /// <summary>Re-reads the loadout: the active weapon's sprite, and the muzzle on every mounted ranged weapon.</summary>
        public void Refresh()
        {
            if (_loadout == null) return;
            var active = _loadout.ActiveWeapon;
            if (ReferenceEquals(active, _boundWeapon) && Rebinds > 0) return;
            _boundWeapon = active;
            Show(DefinitionIdOf(active));
            BindMuzzles();
            Rebinds++;
        }

        public void ShowWeapon(string definitionId)
        {
            if (_loadout != null) return; // the loadout is the source of truth when there is one
            if (definitionId == _shownId && Rebinds > 0) return;
            Show(definitionId);
            Rebinds++;
        }

        private void Show(string definitionId)
        {
            EnsureHierarchy();
            _shownId = string.IsNullOrEmpty(definitionId) ? null : definitionId;
            var sprite = _shownId != null && _resolveSprite != null ? _resolveSprite(_shownId) : null;
            _sprite.sprite = sprite;
            _sprite.enabled = sprite != null;
            // Muzzle: the sprite's firing end at grip height — where the authored barrel ends (spec 10: +X, muzzle obvious).
            _muzzle.localPosition = sprite != null ? new Vector3(sprite.bounds.max.x, 0f, 0f) : Vector3.zero;
            if (_shownId != null && sprite == null) Debug.LogWarning($"{name}: no held sprite bound for weapon '{_shownId}'.");
        }

        /// <summary>Every mounted ranged/blaster/bow weapon fires from the visible muzzle; re-run when weapons are remounted.</summary>
        private void BindMuzzles()
        {
            _muzzleBound.Clear();
            foreach (var r in GetComponents<RangedWeapon>()) { r.SetMuzzle(_muzzle); _muzzleBound.Add(r); }
            foreach (var b in GetComponents<BlasterWeapon>()) { b.SetMuzzle(_muzzle); _muzzleBound.Add(b); }
            foreach (var w in GetComponents<BowWeapon>()) { w.SetMuzzle(_muzzle); _muzzleBound.Add(w); }
        }

        /// <summary>Pure sorting rule (tests): behind the body when aiming away from the camera, in front otherwise.</summary>
        public static bool BehindBody(Vector2 aimDirection) => aimDirection.y > BehindBodyAimY;

        /// <summary>Applies aim-dependent sorting and the left-facing flip for a given aim (LateUpdate; tests call it directly).</summary>
        public void ApplyAim(Vector2 aim)
        {
            if (_sprite == null) return;
            // Keep the sprite upright-readable: a weapon aimed left is flipped on Y so the top of the gun stays up,
            // which is what the +X authoring convention expects of a 360° pivot.
            _sprite.flipY = aim.x < 0f;
            IsBehindBody = BehindBody(aim);
            var bodyOrder = _body != null ? _body.sortingOrder : SortingConvention.OrderOf(SortingRole.Character, transform.position.y);
            if (IsBehindBody)
            {
                _sprite.sortingLayerName = SortingLayers.Characters;
                _sprite.sortingOrder = bodyOrder - 1;
            }
            else
            {
                _sprite.sortingLayerName = SortingLayers.Weapons;
                _sprite.sortingOrder = SortingConvention.OrderOf(SortingRole.Weapon, transform.position.y);
            }
        }

        private void LateUpdate()
        {
            if (_loadout != null && !ReferenceEquals(_loadout.ActiveWeapon, _boundWeapon)) Refresh();
            ApplyAim(_aiming != null ? _aiming.AimDirection : (Vector2)(_pivot != null ? _pivot.right : Vector3.right));
        }
    }
}
