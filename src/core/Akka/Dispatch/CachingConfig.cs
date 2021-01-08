//-----------------------------------------------------------------------
// <copyright file="CachingConfig.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Akka.Annotations;
using Akka.Configuration;
using Akka.Configuration.Hocon;

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
        private static readonly Config EmptyConfig = ConfigurationFactory.Empty;

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
                : this(valid, exists, EmptyConfig)
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

        private readonly Config _config;
        private readonly ConcurrentDictionary<string, IPathEntry> _entryMap;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="config">TBD</param>
        public CachingConfig(Config config)
        {
            var cachingConfig = config as CachingConfig;
            if (cachingConfig != null)
            {
                _config = cachingConfig._config;
                _entryMap = cachingConfig._entryMap;
            }
            else
            {
                _config = config;
                _entryMap = new ConcurrentDictionary<string, IPathEntry>();
            }
        }

        private IPathEntry GetPathEntry(string path)
        {
            if (!_entryMap.TryGetValue(path, out var pathEntry)) //cache miss
            {
                try
                {
                    if (_config.HasPath(path)) //found something
                    {
                        try
                        {
                            var configValue = _config.GetValue(path);
                            if (configValue == null) //empty
                                pathEntry = EmptyPathEntry;
                            else if (configValue.IsString()) //is a string value
                                pathEntry = new StringPathEntry(true, true, configValue.AtKey("cached"), configValue.GetString());
                            else //some other type of HOCON value
                                pathEntry = new ValuePathEntry(true, true, configValue.AtKey("cached"));
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

        /// <summary>
        /// TBD
        /// </summary>
        public override HoconValue Root
        {
            get { return _config.Root; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="fallback">TBD</param>
        public override Config WithFallback(Config fallback)
        {
            return new CachingConfig(_config.WithFallback(fallback));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="path">TBD</param>
        public override bool HasPath(string path)
        {
            var entry = GetPathEntry(path);
            if (entry.Valid)
                return entry.Exists;
            else //run the real code in order to get exceptions
                return _config.HasPath(path);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override bool IsEmpty
        {
            get { return _config.IsEmpty; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override IEnumerable<KeyValuePair<string, HoconValue>> AsEnumerable()
        {
            return _config.AsEnumerable();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="path">TBD</param>
        /// <param name="default">TBD</param>
        /// <returns>TBD</returns>
        public override bool GetBoolean(string path, bool @default = false)
        {
            return _config.GetBoolean(path, @default);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="path">TBD</param>
        /// <param name="default">TBD</param>
        /// <returns>TBD</returns>
        public override int GetInt(string path, int @default = 0)
        {
            return _config.GetInt(path, @default);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="path">TBD</param>
        /// <param name="default">TBD</param>
        /// <returns>TBD</returns>
        public override long GetLong(string path, long @default = 0)
        {
            return _config.GetLong(path, @default);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="path">TBD</param>
        /// <param name="default">TBD</param>
        /// <returns>TBD</returns>
        public override double GetDouble(string path, double @default = 0)
        {
            return _config.GetDouble(path, @default);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="path">TBD</param>
        /// <param name="default">TBD</param>
        /// <returns>TBD</returns>
        public override string GetString(string path, string @default = null)
        {
            var pathEntry = GetPathEntry(path);
            if (pathEntry is StringPathEntry)
            {
                return ((StringPathEntry)pathEntry).Value;
            }
            else
            {
                return pathEntry.Config.GetString("cached");
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="path">TBD</param>
        /// <param name="default">TBD</param>
        /// <returns>TBD</returns>
        public override decimal GetDecimal(string path, decimal @default = 0)
        {
            return _config.GetDecimal(path, @default);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="path">TBD</param>
        /// <returns>TBD</returns>
        public override IList<bool> GetBooleanList(string path)
        {
            return _config.GetBooleanList(path);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="path">TBD</param>
        /// <returns>TBD</returns>
        public override IList<byte> GetByteList(string path)
        {
            return _config.GetByteList(path);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="path">TBD</param>
        /// <returns>TBD</returns>
        public override long? GetByteSize(string path)
        {
            return _config.GetByteSize(path);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="path">TBD</param>
        /// <returns>TBD</returns>
        public override IList<decimal> GetDecimalList(string path)
        {
            return _config.GetDecimalList(path);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="path">TBD</param>
        /// <returns>TBD</returns>
        public override IList<double> GetDoubleList(string path)
        {
            return _config.GetDoubleList(path);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="path">TBD</param>
        /// <param name="default">TBD</param>
        /// <returns>TBD</returns>
        public override float GetFloat(string path, float @default = 0)
        {
            return _config.GetFloat(path, @default);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="path">TBD</param>
        /// <returns>TBD</returns>
        public override IList<float> GetFloatList(string path)
        {
            return _config.GetFloatList(path);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="path">TBD</param>
        /// <returns>TBD</returns>
        public override IList<int> GetIntList(string path)
        {
            return _config.GetIntList(path);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="path">TBD</param>
        /// <returns>TBD</returns>
        public override IList<long> GetLongList(string path)
        {
            return _config.GetLongList(path);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="path">TBD</param>
        /// <param name="strings"></param>
        /// <returns>TBD</returns>
        public override IList<string> GetStringList(string path, string[] strings)
        {
            return _config.GetStringList(path);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="path">TBD</param>
        /// <param name="default">TBD</param>
        /// <param name="allowInfinite">TBD</param>
        /// <returns>TBD</returns>
        public override TimeSpan GetTimeSpan(string path, TimeSpan? @default = null, bool allowInfinite = true)
        {
            return _config.GetTimeSpan(path, @default, allowInfinite);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="path">TBD</param>
        /// <returns>TBD</returns>
        public override Config GetConfig(string path)
        {
            return _config.GetConfig(path);
        }
    }
}