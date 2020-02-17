using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using Hocon;

namespace Akka.Configuration
{
    public sealed class FlatConfig : Config
    {
        private ConcurrentDictionary<string, HoconValue> _cache = new ConcurrentDictionary<string, HoconValue>();

        public FlatConfig(HoconRoot root, Config fallback) : base(root, fallback)
        {
            if (root is FlatConfig caching)
                _cache = caching._cache;
            else
                _cache = new ConcurrentDictionary<string, HoconValue>();
        }

        public FlatConfig(HoconRoot root) : base(root)
        {
            if (root is FlatConfig caching)
                _cache = caching._cache;
            else
                _cache = new ConcurrentDictionary<string, HoconValue>();
        }

        protected override HoconValue GetNode(string path)
        {
            if (_cache.TryGetValue(path, out var result))
                return result;
            result = base.GetNode(path);
            _cache[path] = result;
            return result;
        }

        protected override HoconValue GetNode(HoconPath path)
        {
            var fullPath = path.ToString();
            if (_cache.TryGetValue(fullPath, out var result))
                return result;
            result = base.GetNode(path);
            _cache[fullPath] = result;
            return result;
        }

        protected override bool TryGetNode(string path, out HoconValue result)
        {
            if (_cache.TryGetValue(path, out result))
                return true;

            if (!base.TryGetNode(path, out result))
                return false;

            _cache[path] = result;
            return true;
        }

        protected override bool TryGetNode(HoconPath path, out HoconValue result)
        {
            var fullPath = path.ToString();
            if (_cache.TryGetValue(fullPath, out result))
                return true;

            if (!base.TryGetNode(path, out result))
                return false;

            _cache[fullPath] = result;
            return true;
        }
    }
}
