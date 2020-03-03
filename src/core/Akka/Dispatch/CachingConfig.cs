//-----------------------------------------------------------------------
// <copyright file="CachingConfig.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Akka.Annotations;
using Hocon; using Akka.Configuration;

namespace Akka.Dispatch
{
    /// <summary>
    /// INTERNAL API
    /// 
    /// A <see cref="CachingConfig"/> is a <see cref="Config"/> that wraps another <see cref="Config"/> and is used to
    /// cache path lookup and string retrieval, which we happen to do in some critical paths of the actor creation
    /// and mailbox selection code.
    /// 
    /// All other <see cref="Config"/> operations are delegated to the wrapped <see cref="Config"/>.
    /// </summary>
    [InternalApi]
    class CachingConfig : Config
    {
        #region PathEntry definitions

        interface IPathEntry
        {
            bool Valid { get; }
            bool Exists { get; }
            Config Config { get; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public struct ValuePathEntry : IPathEntry
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="valid">TBD</param>
            /// <param name="exists">TBD</param>
            /// <param name="config">TBD</param>
            public ValuePathEntry(bool valid, bool exists, Config config) : this()
            {
                Config = config;
                Exists = exists;
                Valid = valid;
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="valid">TBD</param>
            /// <param name="exists">TBD</param>
            public ValuePathEntry(bool valid, bool exists)
                : this(valid, exists, Config.Empty)
            {
            }

            /// <summary>
            /// TBD
            /// </summary>
            public bool Valid { get; private set; }
            /// <summary>
            /// TBD
            /// </summary>
            public bool Exists { get; private set; }
            /// <summary>
            /// TBD
            /// </summary>
            public Config Config { get; private set; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public struct StringPathEntry : IPathEntry
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="valid">TBD</param>
            /// <param name="exists">TBD</param>
            /// <param name="config">TBD</param>
            /// <param name="value">TBD</param>
            public StringPathEntry(bool valid, bool exists, Config config, string value) : this()
            {
                Config = config;
                Exists = exists;
                Valid = valid;
                Value = value;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public bool Valid { get; private set; }
            /// <summary>
            /// TBD
            /// </summary>
            public bool Exists { get; private set; }
            /// <summary>
            /// TBD
            /// </summary>
            public Config Config { get; private set; }

            /// <summary>
            /// TBD
            /// </summary>
            public string Value { get; private set; }
        }

        static readonly IPathEntry InvalidPathEntry = new ValuePathEntry(false, true);
        static readonly IPathEntry NonExistingPathEntry = new ValuePathEntry(true, false);
        static readonly IPathEntry EmptyPathEntry = new ValuePathEntry(true, true);

        #endregion

        private readonly ConcurrentDictionary<string, IPathEntry> _entryMap;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="config">TBD</param>
        public CachingConfig(Config config) : base(config)
        {
            if (config is CachingConfig caching)
                _entryMap = caching._entryMap;
            else
                _entryMap = new ConcurrentDictionary<string, IPathEntry>();
        }

        public CachingConfig(HoconRoot root) : base(root)
        {
            _entryMap = new ConcurrentDictionary<string, IPathEntry>();
        }

        private IPathEntry GetPathEntry(string path)
        {
            if (!_entryMap.TryGetValue(path, out var pathEntry)) //cache miss
            {
                try
                {
                    if (TryGetNode(path, out var configValue)) //found something
                    {
                        try
                        {
                            if (configValue == null) //empty
                                pathEntry = EmptyPathEntry;
                            else if (configValue.Type == HoconType.String) //is a string value
                                pathEntry = new StringPathEntry(true, true, new Config(configValue.AtKey("cached")), configValue.GetString());
                            else //some other type of HOCON value
                                pathEntry = new ValuePathEntry(true, true, new Config(configValue.AtKey("cached")));
                        }
                        catch (Exception)
                        {
                            pathEntry = EmptyPathEntry;
                        }
                    }
                    else //couldn't find the path
                        pathEntry = NonExistingPathEntry;
                }
                catch (Exception) //configuration threw some sort of error
                {
                    pathEntry = InvalidPathEntry;
                }

                if (_entryMap.TryAdd(path, pathEntry))
                    return pathEntry;
                return _entryMap[path];
            }

            //cache hit
            return pathEntry;
        }

        private IPathEntry GetPathEntry(HoconPath path)
        {
            return GetPathEntry(path.ToString());
        }

        /// <inheritdoc />
        public override bool HasPath(string path)
        {
            var entry = GetPathEntry(path);
            if (entry.Valid)
                return entry.Exists;
            else //run the real code in order to get exceptions
                return base.HasPath(path);
        }

        /// <inheritdoc />
        public override string GetString(string path)
        {
            var pathEntry = GetPathEntry(path);
            if (pathEntry is StringPathEntry entry)
            {
                return entry.Value;
            }
            else
            {
                return pathEntry.Config.GetString("cached");
            }
        }

        /// <inheritdoc />
        public override string GetString(HoconPath path)
        {
            return GetString(path.ToString());
        }

        /// <inheritdoc />
        public override string GetString(string path, string @default = null)
        {
            var pathEntry = GetPathEntry(path);
            if (pathEntry is StringPathEntry entry)
            {
                return entry.Value;
            }
            else
            {
                return pathEntry.Config.GetString("cached", @default);
            }
        }

        /// <inheritdoc />
        public override string GetString(HoconPath path, string @default = null)
        {
            return GetString(path.ToString(), @default);
        }
    }
}
