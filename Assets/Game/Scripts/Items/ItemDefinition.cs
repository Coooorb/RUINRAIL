using UnityEngine;

namespace RuinRail.Gameplay.Items
{
    public abstract class ItemDefinition : ScriptableObject
    {
        [SerializeField] private string _id;
        [SerializeField] private string _displayName;
        [SerializeField] private ItemCategory _category;
        [SerializeField] private bool _isStackable;
        [SerializeField] private int _maxStack = 1;

        /// <summary>
        /// TASK 185-C: the inventory/HUD icon for this item.
        ///
        /// Presentation only — nothing about identity, stacking or balance reads it, and it is deliberately the last
        /// serialized field so existing assets deserialize unchanged with a null icon. The UI falls back to the item's
        /// name when this is unset, so an item with no icon yet is still usable rather than invisible.
        /// </summary>
        [SerializeField] private Sprite _icon;

        public string Id => _id;
        public string DisplayName => _displayName;
        public ItemCategory Category => _category;
        public bool IsStackable => _isStackable;
        public int MaxStack => IsStackable ? UnityEngine.Mathf.Max(1, _maxStack) : 1;

        /// <summary>The item's icon, or null while its art is outstanding.</summary>
        public Sprite Icon => _icon;

        public bool HasIcon => _icon != null;

#if UNITY_EDITOR
        /// <summary>Editor-only binding used by the art pipeline. Never called at runtime.</summary>
        public void EditorSetIcon(Sprite icon)
        {
            _icon = icon;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
