using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RuinRail.Audio
{
    /// <summary>All authored audio events by id; the service resolves through it and the audit compares it with the required contract.</summary>
    [CreateAssetMenu(menuName = "RuinRail/Audio/Audio Event Catalog", fileName = "AudioEventCatalog")]
    public sealed class AudioEventCatalog : ScriptableObject
    {
        [SerializeField] private List<AudioEventDefinition> _events = new();

        private Dictionary<string, AudioEventDefinition> _byId;

        public IReadOnlyList<AudioEventDefinition> Events => _events;

        public void Configure(IEnumerable<AudioEventDefinition> events)
        {
            _events = events?.Where(e => e != null).ToList() ?? new List<AudioEventDefinition>();
            _byId = null;
        }

        public bool TryGet(string id, out AudioEventDefinition definition)
        {
            _byId ??= _events.Where(e => e != null && !string.IsNullOrEmpty(e.Id)).GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First());
            return _byId.TryGetValue(id ?? string.Empty, out definition);
        }

        public IEnumerable<string> MissingIds(IEnumerable<string> required) => required.Where(id => !TryGet(id, out _));
        public IEnumerable<string> SilentIds(IEnumerable<string> required) => required.Where(id => TryGet(id, out var d) && !d.HasClips);
        public IEnumerable<string> DuplicateIds() => _events.Where(e => e != null).GroupBy(e => e.Id).Where(g => g.Count() > 1).Select(g => g.Key);
    }
}
