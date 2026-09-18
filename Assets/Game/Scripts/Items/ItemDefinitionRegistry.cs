using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace RuinRail.Gameplay.Items
{
    public sealed class ItemDefinitionRegistry
    {
        private static readonly Regex IdPattern = new("^[a-z0-9]+(_[a-z0-9]+)*$", RegexOptions.Compiled);

        private readonly Dictionary<string, ItemDefinition> _byId = new();
        private readonly List<string> _problems = new();

        public IReadOnlyList<string> Problems => _problems;
        public int Count => _byId.Count;
        public IEnumerable<ItemDefinition> Definitions => _byId.Values;

        public static bool IsValidId(string id)
        {
            return !string.IsNullOrEmpty(id) && IdPattern.IsMatch(id);
        }

        public bool TryRegister(ItemDefinition definition)
        {
            if (definition == null)
            {
                _problems.Add("Null definition.");
                return false;
            }

            if (!IsValidId(definition.Id))
            {
                _problems.Add($"Invalid or missing id on definition asset '{definition.name}': '{definition.Id}'.");
                return false;
            }

            if (_byId.TryGetValue(definition.Id, out var existing))
            {
                _problems.Add($"Duplicate id '{definition.Id}' on assets '{existing.name}' and '{definition.name}'.");
                return false;
            }

            _byId.Add(definition.Id, definition);
            return true;
        }

        public bool TryGet(string id, out ItemDefinition definition)
        {
            return _byId.TryGetValue(id, out definition);
        }

        public static ItemDefinitionRegistry Build(IEnumerable<ItemDefinition> definitions)
        {
            var registry = new ItemDefinitionRegistry();
            foreach (var definition in definitions)
            {
                registry.TryRegister(definition);
            }

            return registry;
        }
    }
}
