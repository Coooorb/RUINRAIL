using RuinRail.Core.Rendering;
using UnityEngine;

namespace RuinRail.Presentation
{
    /// <summary>Applies the art/102 convention to any renderer (sprite, tilemap, particle, canvas) once; y-sorted roles get a <see cref="YSorter"/>.</summary>
    public static class SpriteSorting
    {
        public static void Apply(Renderer renderer, SortingRole role, float feetY = 0f)
        {
            if (renderer == null) return;
            renderer.sortingLayerName = SortingConvention.LayerOf(role);
            renderer.sortingOrder = SortingConvention.OrderOf(role, feetY);
        }

        public static void Apply(Canvas canvas, SortingRole role, int order = 0)
        {
            if (canvas == null) return;
            canvas.sortingLayerName = SortingConvention.LayerOf(role);
            canvas.sortingOrder = SortingConvention.BaseOrderOf(role) + order;
        }

        /// <summary>Assigns the role and, for y-sorted roles, attaches/configures a <see cref="YSorter"/> so the order follows the feet.</summary>
        public static YSorter Attach(Renderer renderer, SortingRole role, float feetOffset = 0f)
        {
            Apply(renderer, role, renderer.transform.position.y + feetOffset);
            if (!SortingConvention.IsYSorted(role)) return null;
            var sorter = renderer.GetComponent<YSorter>();
            if (sorter == null) sorter = renderer.gameObject.AddComponent<YSorter>();
            sorter.Configure(renderer, role, feetOffset);
            return sorter;
        }
    }

    /// <summary>Keeps a y-sorted renderer's order at −(feet y in pixels): lower on screen draws on top; whole pixels so nothing flickers within a row.</summary>
    public sealed class YSorter : MonoBehaviour
    {
        [SerializeField] private Renderer _renderer;
        [SerializeField] private SortingRole _role = SortingRole.Character;
        [SerializeField] private float _feetOffset;

        public SortingRole Role => _role;
        public float FeetY => transform.position.y + _feetOffset;

        public void Configure(Renderer renderer, SortingRole role, float feetOffset)
        {
            _renderer = renderer;
            _role = role;
            _feetOffset = feetOffset;
            Refresh();
        }

        public void Refresh()
        {
            if (_renderer == null) _renderer = GetComponent<Renderer>();
            if (_renderer == null) return;
            var order = SortingConvention.OrderOf(_role, FeetY);
            if (_renderer.sortingOrder != order) _renderer.sortingOrder = order;
        }

        private void LateUpdate() => Refresh();
    }
}
