//-----------------------------------------------------------------------
// <copyright file="MessageTemplateParser.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;

namespace Akka.Event
{
    /// <summary>
    /// Parses message templates to extract property names for semantic logging.
    /// Supports both positional templates ({0}, {1}) and named templates ({PropertyName}).
    /// Uses ThreadStatic caching for performance.
    /// </summary>
    internal static class MessageTemplateParser
    {
        [ThreadStatic]
        private static LruCache<int, ParsedTemplate>? _cache;

        private const int MaxCacheSize = 1000;

        private static LruCache<int, ParsedTemplate> Cache
        {
            get
            {
                if (_cache == null)
                    _cache = new LruCache<int, ParsedTemplate>(MaxCacheSize);
                return _cache;
            }
        }

        /// <summary>
        /// Gets the property names from a message template.
        /// For positional templates like "{0} and {1}", returns ["0", "1"].
        /// For named templates like "{UserId} logged in", returns ["UserId"].
        /// Results are cached for performance.
        /// </summary>
        /// <param name="template">The message template string</param>
        /// <returns>List of property names</returns>
        public static IReadOnlyList<string> GetPropertyNames(string template)
        {
            if (string.IsNullOrEmpty(template))
                return Array.Empty<string>();

            var hash = template.GetHashCode();

            // Try cache first
            if (Cache.TryGet(hash, out var cached) && cached.Template == template)
                return cached.PropertyNames;

            // Parse and cache
            var propertyNames = ParseTemplate(template);
            var parsed = new ParsedTemplate(template, propertyNames);
            Cache.Add(hash, parsed);

            return propertyNames;
        }

        /// <summary>
        /// Parses a message template to extract property names.
        /// </summary>
        private static IReadOnlyList<string> ParseTemplate(string template)
        {
            var properties = new List<string>();
            var length = template.Length;
            var i = 0;

            while (i < length)
            {
                var openBrace = template.IndexOf('{', i);
                if (openBrace == -1)
                    break;

                // Check for escaped brace {{
                if (openBrace + 1 < length && template[openBrace + 1] == '{')
                {
                    i = openBrace + 2;
                    continue;
                }

                var closeBrace = template.IndexOf('}', openBrace + 1);
                if (closeBrace == -1)
                    break; // Malformed template, stop parsing

                // Note: We do NOT check for }} here. The }} escape sequence only applies to literal
                // text during formatting, not during property name extraction. After finding a valid
                // placeholder {Name}, any subsequent } is a literal character, not an escape.
                // For example: "{UserId}}" has placeholder "UserId" followed by literal "}"

                // Extract property name
                var propertyLength = closeBrace - openBrace - 1;
                if (propertyLength > 0)
                {
                    var propertyName = template.Substring(openBrace + 1, propertyLength).Trim();

                    // Remove format specifiers (e.g., {Value:N2} -> Value)
                    var colonIndex = propertyName.IndexOf(':');
                    if (colonIndex > 0)
                        propertyName = propertyName.Substring(0, colonIndex).Trim();

                    // Remove alignment specifiers (e.g., {Value,10} -> Value)
                    var commaIndex = propertyName.IndexOf(',');
                    if (commaIndex > 0)
                        propertyName = propertyName.Substring(0, commaIndex).Trim();

                    if (!string.IsNullOrEmpty(propertyName))
                        properties.Add(propertyName);
                }

                i = closeBrace + 1;
            }

            return properties.ToArray();
        }
    }

    /// <summary>
    /// Represents a parsed message template with property names.
    /// </summary>
    internal sealed class ParsedTemplate
    {
        public string Template { get; }
        public IReadOnlyList<string> PropertyNames { get; }

        public ParsedTemplate(string template, IReadOnlyList<string> propertyNames)
        {
            Template = template;
            PropertyNames = propertyNames;
        }
    }

    /// <summary>
    /// Simple LRU (Least Recently Used) cache implementation.
    /// </summary>
    internal sealed class LruCache<TKey, TValue> where TKey : notnull
    {
        private readonly int _maxSize;
        private readonly Dictionary<TKey, LinkedListNode<CacheEntry>> _cache;
        private readonly LinkedList<CacheEntry> _lruList;

        public LruCache(int maxSize)
        {
            _maxSize = maxSize;
            _cache = new Dictionary<TKey, LinkedListNode<CacheEntry>>(maxSize);
            _lruList = new LinkedList<CacheEntry>();
        }

        /// <summary>
        /// Tries to get a value from the cache.
        /// If found, moves the entry to the front (most recently used).
        /// </summary>
        public bool TryGet(TKey key, out TValue value)
        {
            if (_cache.TryGetValue(key, out var node))
            {
                // Move to front (most recently used)
                _lruList.Remove(node);
                _lruList.AddFirst(node);

                value = node.Value.Value;
                return true;
            }

            value = default!;
            return false;
        }

        /// <summary>
        /// Adds a value to the cache.
        /// If at capacity, evicts the least recently used entry.
        /// </summary>
        public void Add(TKey key, TValue value)
        {
            // If key already exists, update it
            if (_cache.TryGetValue(key, out var existingNode))
            {
                _lruList.Remove(existingNode);
                _cache.Remove(key);
            }
            // Evict oldest if at capacity
            else if (_cache.Count >= _maxSize)
            {
                var oldest = _lruList.Last;
                if (oldest != null)
                {
                    _lruList.RemoveLast();
                    _cache.Remove(oldest.Value.Key);
                }
            }

            // Add new entry
            var entry = new CacheEntry(key, value);
            var node = _lruList.AddFirst(entry);
            _cache[key] = node;
        }

        private struct CacheEntry
        {
            public TKey Key { get; }
            public TValue Value { get; }

            public CacheEntry(TKey key, TValue value)
            {
                Key = key;
                Value = value;
            }
        }
    }
}
