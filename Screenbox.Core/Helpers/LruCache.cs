using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Screenbox.Core.Helpers;

/// <summary>
/// Represents a fixed-capacity cache that evicts the least recently used entry when it is full.
/// </summary>
/// <remarks>This type is not thread-safe. Callers must synchronize access.</remarks>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TValue">The type of the cached values.</typeparam>
/// <example>
/// <code>
/// LruCache&lt;long, string&gt; cache = new(capacity: 2);
/// cache.Set(1, "one");
/// cache.Set(2, "two");
/// cache.TryGetValue(1, out _); // Marks 1 as recently used.
/// cache.Set(3, "three");       // Evicts 2.
/// </code>
/// </example>
public sealed class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> _nodes;
    private readonly LinkedList<KeyValuePair<TKey, TValue>> _usageOrder = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="LruCache{TKey, TValue}"/> class.
    /// </summary>
    /// <param name="capacity">The maximum number of entries to keep. Must be positive.</param>
    public LruCache(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
        _nodes = new Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>>(capacity);
    }

    /// <summary>
    /// Gets the number of cached entries.
    /// </summary>
    public int Count => _nodes.Count;

    /// <summary>
    /// Gets the value associated with the specified key and marks the entry as the most recently used.
    /// </summary>
    /// <param name="key">The key to look up.</param>
    /// <param name="value">When this method returns, the cached value if the key was found.</param>
    /// <returns><see langword="true"/> if the key was found; otherwise, <see langword="false"/>.</returns>
    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        if (!_nodes.TryGetValue(key, out LinkedListNode<KeyValuePair<TKey, TValue>>? node))
        {
            value = default;
            return false;
        }

        _usageOrder.Remove(node);
        _usageOrder.AddFirst(node);
        value = node.Value.Value;
        return true;
    }

    /// <summary>
    /// Adds or replaces the value for the specified key, evicting the least recently used entry if the cache is full.
    /// </summary>
    /// <param name="key">The key of the entry.</param>
    /// <param name="value">The value to cache.</param>
    public void Set(TKey key, TValue value)
    {
        if (_nodes.TryGetValue(key, out LinkedListNode<KeyValuePair<TKey, TValue>>? existingNode))
        {
            existingNode.Value = new KeyValuePair<TKey, TValue>(key, value);
            _usageOrder.Remove(existingNode);
            _usageOrder.AddFirst(existingNode);
            return;
        }

        if (_nodes.Count >= _capacity && _usageOrder.Last is { } leastRecentlyUsed)
        {
            _usageOrder.RemoveLast();
            _nodes.Remove(leastRecentlyUsed.Value.Key);
        }

        _nodes[key] = _usageOrder.AddFirst(new KeyValuePair<TKey, TValue>(key, value));
    }

    /// <summary>
    /// Removes all entries from the cache.
    /// </summary>
    public void Clear()
    {
        _nodes.Clear();
        _usageOrder.Clear();
    }
}
