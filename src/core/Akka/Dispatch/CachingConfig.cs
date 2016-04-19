//-----------------------------------------------------------------------
// <copyright file="CachingConfig.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

        public struct ValuePathEntry : IPathEntry
        {
            public ValuePathEntry(bool valid, bool exists, Config config) : this()
            {
                Config = config;
                Exists = exists;
                Valid = valid;
            }

            public ValuePathEntry(bool valid, bool exists)
                : this(valid, exists, EmptyConfig)
            {
            }

            public bool Valid { get; private set; }
            public bool Exists { get; private set; }
            public Config Config { get; private set; }
        }

        public struct StringPathEntry : IPathEntry
        {
            public StringPathEntry(bool valid, bool exists, Config config, string value) : this()
            {
                Config = config;
                Exists = exists;
                Valid = valid;
                Value = value;
            }

            public bool Valid { get; private set; }
            public bool Exists { get; private set; }
            public Config Config { get; private set; }

            public string Value { get; private set; }
        }

        static readonly IPathEntry InvalidPathEntry = new ValuePathEntry(false, true);
        static readonly IPathEntry NonExistingPathEntry = new ValuePathEntry(true, false);
        static readonly IPathEntry EmptyPathEntry = new ValuePathEntry(true, true);

        #endregion

        private readonly Config _config;
        private readonly ConcurrentDictionary<string, IPathEntry> _entryMap;

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
            IPathEntry pathEntry;
            if (!_entryMap.TryGetValue(path, out pathEntry)) //cache miss
            {
                try
                {
                    if (_config.HasPath(path)) //found something
                    {
                        try
                        {
                            var configValue = _config.GetValue(path);
                            if (configValue == null) //empty
                            {
                                pathEntry = EmptyPathEntry;
                            }
                            else if (configValue.IsString()) //is a string value
                            {
                                pathEntry = new StringPathEntry(true, true, configValue.AtKey("cached"), configValue.GetString());
                            }
                            else //some other type of HOCON value
                            {
                                pathEntry = new ValuePathEntry(true, true, configValue.AtKey("cached"));
                            }
                        }
                        catch (Exception)
                        {
                            pathEntry = EmptyPathEntry;
                        }
                    }
                    else //couldn't find the path
                    {
                        pathEntry = NonExistingPathEntry;
                    }
                }
                catch (Exception) //configuration threw some sort of error
                {
                    pathEntry = InvalidPathEntry;
                }

                if (_entryMap.TryAdd(path, pathEntry))
                {
                    return pathEntry;
                }
                else
                {
                    return _entryMap[path];
                }
            }
            
            //cache hit
            return pathEntry;
        }

        public override HoconValue Root
        {
            get { return _config.Root; }
        }

        public override Config WithFallback(Config fallback)
        {
            return new CachingConfig(_config.WithFallback(fallback));
        }

        public override bool HasPath(string path)
        {
            var entry = GetPathEntry(path);
            if (entry.Valid)
                return entry.Exists;
            else //run the real code in order to get exceptions
                return _config.HasPath(path);
        }

        public override bool IsEmpty
        {
            get { return _config.IsEmpty; }
        }

        public override IEnumerable<KeyValuePair<string, HoconValue>> AsEnumerable()
        {
            return _config.AsEnumerable();
        }

        public override bool GetBoolean(string path, bool @default = false)
        {
            return _config.GetBoolean(path, @default);
        }

        public override int GetInt(string path, int @default = 0)
        {
            return _config.GetInt(path, @default);
        }

        public override long GetLong(string path, long @default = 0)
        {
            return _config.GetLong(path, @default);
        }

        public override double GetDouble(string path, double @default = 0)
        {
            return _config.GetDouble(path, @default);
        }

        public override string GetString(string path, string @default = null)
        {
            var pathEntry = GetPathEntry(path);
            if (pathEntry is StringPathEntry)
            {
                return ((StringPathEntry) pathEntry).Value;
            }
            else
            {
                return pathEntry.Config.GetString("cached");
            }
        }

        public override decimal GetDecimal(string path, decimal @default = 0)
        {
            return _config.GetDecimal(path, @default);
        }

        public override IList<bool> GetBooleanList(string path)
        {
            return _config.GetBooleanList(path);
        }

        public override IList<byte> GetByteList(string path)
        {
            return _config.GetByteList(path);
        }

        public override long? GetByteSize(string path)
        {
            return _config.GetByteSize(path);
        }

        public override IList<decimal> GetDecimalList(string path)
        {
            return _config.GetDecimalList(path);
        }

        public override IList<double> GetDoubleList(string path)
        {
            return _config.GetDoubleList(path);
        }

        public override float GetFloat(string path, float @default = 0)
        {
            return _config.GetFloat(path, @default);
        }

        public override IList<float> GetFloatList(string path)
        {
            return _config.GetFloatList(path);
        }

        public override IList<int> GetIntList(string path)
        {
            return _config.GetIntList(path);
        }

        public override IList<long> GetLongList(string path)
        {
            return _config.GetLongList(path);
        }

        public override IList<string> GetStringList(string path)
        {
            return _config.GetStringList(path);
        }

        public override TimeSpan GetTimeSpan(string path, TimeSpan? @default = null, bool allowInfinite = true)
        {
            return _config.GetTimeSpan(path, @default, allowInfinite);
        }

        public override Config GetConfig(string path)
        {
            return _config.GetConfig(path);
        }
    }
}

