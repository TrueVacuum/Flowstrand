using System;
using System.Collections.Generic;

namespace Flowstrand
{
    public sealed class FlowBlackboard
    {
        private readonly Dictionary<string, object> _values =
            new Dictionary<string, object>(StringComparer.Ordinal);

        public IEnumerable<string> Keys => _values.Keys;

        public bool Contains(string key)
        {
            return !string.IsNullOrEmpty(key) && _values.ContainsKey(key);
        }

        public void Set<T>(string key, T value)
        {
            ValidateKey(key);
            _values[key] = value;
        }

        public T Get<T>(string key)
        {
            if (!TryGet(key, out T value))
            {
                throw new KeyNotFoundException(
                    $"Blackboard value '{key}' does not exist or is not a {typeof(T).Name}.");
            }

            return value;
        }

        public T GetOrDefault<T>(string key, T fallback = default)
        {
            return TryGet(key, out T value) ? value : fallback;
        }

        public bool TryGet<T>(string key, out T value)
        {
            if (!string.IsNullOrEmpty(key) &&
                _values.TryGetValue(key, out object storedValue) &&
                storedValue is T typedValue)
            {
                value = typedValue;
                return true;
            }

            value = default;
            return false;
        }

        public bool Remove(string key)
        {
            return !string.IsNullOrEmpty(key) && _values.Remove(key);
        }

        public void Clear()
        {
            _values.Clear();
        }

        private static void ValidateKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("A blackboard key cannot be empty.", nameof(key));
            }
        }
    }
}
