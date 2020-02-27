//-----------------------------------------------------------------------
// <copyright file="FlatConfig.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using Hocon;

namespace Akka.Dispatch
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class CachingConfig : Config
    {
        private readonly ConcurrentDictionary<string, HoconElement> _cache = new ConcurrentDictionary<string, HoconElement>();

        public CachingConfig(HoconElement root) : base(root)
        {
            if (root is CachingConfig caching)
                _cache = caching._cache;
        }

        public override HoconElement GetValue(string path)
        {
            if (_cache.TryGetValue(path, out var result))
                return result;

            result = Root.GetObject().GetValue(HoconPath.Parse(path));
            _cache[path] = result;
            return result;
        }

        public override HoconElement GetValue(HoconPath path)
        {
            var fullPath = path.ToString();
            if (_cache.TryGetValue(fullPath, out var result))
                return result;

            result = Root.GetObject().GetValue(path);
            _cache[fullPath] = result;
            return result;
        }

        public override bool TryGetValue(string path, out HoconElement result)
        {
            if (_cache.TryGetValue(path, out result))
                return true;

            if (HoconPath.TryParse(path, out var hoconPath))
                if (Root.TryGetObject(out var obj))
                    if (obj.TryGetValue(hoconPath, out result))
                    {
                        _cache[path] = result;
                        return true;
                    }

            return false;
        }

        public override bool TryGetValue(HoconPath path, out HoconElement result)
        {
            var fullPath = path.ToString();
            if (_cache.TryGetValue(fullPath, out result))
                return true;

            if (Root.TryGetObject(out var obj))
                if (obj.TryGetValue(path, out result))
                {
                    _cache[fullPath] = result;
                    return true;
                }
            return false;
        }
    }
}