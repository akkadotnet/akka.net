//-----------------------------------------------------------------------
// <copyright file="CachingConfig.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Akka.Annotations;
using Hocon;
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

        private readonly Config _config;
        private readonly ConcurrentDictionary<string, IPathEntry> _entryMap;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="config">TBD</param>
        public CachingConfig(Config config) : base(Empty)
        {
            if (config is CachingConfig cachingConfig)
            {
                _config = new Config(cachingConfig._config);
                _entryMap = cachingConfig._entryMap;
            }
            else
            {
                _config = new Config(config);
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
        public override Config WithFallback(Config fallback)
        {
            if (fallback.IsNullOrEmpty())
                return this; // no-op

            if (ReferenceEquals(fallback, _config))
                throw new ArgumentException("Config can not have itself as fallback", nameof(fallback));

            if (_config.IsEmpty)
                return new CachingConfig(fallback);

            return new CachingConfig(_config.WithFallback(fallback));
        }

        /// <inheritdoc />
        public override bool HasPath(string path)
        {
            var entry = GetPathEntry(path);
            if (entry.Valid)
                return entry.Exists;
            else //run the real code in order to get exceptions
                return _config.HasPath(path);
        }

        /// <inheritdoc />
        public override bool IsEmpty
        {
            get { return _config.IsEmpty; }
        }

        /// <inheritdoc />
        public override IEnumerable<KeyValuePair<string, HoconField>> AsEnumerable()
        {
            return _config.AsEnumerable();
        }

        /// <inheritdoc />
        public override Config GetConfig(string path)
        {
            return _config.GetConfig(path);
        }

        /// <inheritdoc />
        public override Config GetConfig(HoconPath path)
        {
            return _config.GetConfig(path);
        }

        #region Value getter functions

        /// <inheritdoc />
        public override bool GetBoolean(string path)
        {
            return _config.GetBoolean(path);
        }

        /// <inheritdoc />
        public override bool GetBoolean(HoconPath path)
        {
            return _config.GetBoolean(path);
        }

        /// <inheritdoc />
        public override bool GetBoolean(string path, bool @default = false)
        {
            return _config.GetBoolean(path, @default);
        }

        /// <inheritdoc />
        public override bool GetBoolean(HoconPath path, bool @default = false)
        {
            return _config.GetBoolean(path, @default);
        }

        /// <inheritdoc />
        public override int GetInt(string path)
        {
            return _config.GetInt(path);
        }

        /// <inheritdoc />
        public override int GetInt(HoconPath path)
        {
            return _config.GetInt(path);
        }

        /// <inheritdoc />
        public override int GetInt(string path, int @default = 0)
        {
            return _config.GetInt(path, @default);
        }

        /// <inheritdoc />
        public override int GetInt(HoconPath path, int @default = 0)
        {
            return _config.GetInt(path, @default);
        }

        /// <inheritdoc />
        public override long GetLong(string path)
        {
            return _config.GetLong(path);
        }

        /// <inheritdoc />
        public override long GetLong(HoconPath path)
        {
            return _config.GetLong(path);
        }

        /// <inheritdoc />
        public override long GetLong(string path, long @default = 0)
        {
            return _config.GetLong(path, @default);
        }

        /// <inheritdoc />
        public override long GetLong(HoconPath path, long @default = 0)
        {
            return _config.GetLong(path, @default);
        }

        /// <inheritdoc />
        public override double GetDouble(string path)
        {
            return _config.GetDouble(path);
        }

        /// <inheritdoc />
        public override double GetDouble(HoconPath path)
        {
            return _config.GetDouble(path);
        }

        /// <inheritdoc />
        public override double GetDouble(string path, double @default = 0)
        {
            return _config.GetDouble(path, @default);
        }

        /// <inheritdoc />
        public override double GetDouble(HoconPath path, double @default = 0)
        {
            return _config.GetDouble(path, @default);
        }

        /// <inheritdoc />
        public override string GetString(string path)
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

        /// <inheritdoc />
        public override string GetString(HoconPath path)
        {
            return GetString(path.ToString());
        }

        /// <inheritdoc />
        public override string GetString(string path, string @default = null)
        {
            var pathEntry = GetPathEntry(path);
            if (pathEntry is StringPathEntry)
            {
                return ((StringPathEntry) pathEntry).Value;
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

        /// <inheritdoc />
        public override decimal GetDecimal(string path)
        {
            return _config.GetDecimal(path);
        }

        /// <inheritdoc />
        public override decimal GetDecimal(HoconPath path)
        {
            return _config.GetDecimal(path);
        }

        /// <inheritdoc />
        public override decimal GetDecimal(string path, decimal @default = 0)
        {
            return _config.GetDecimal(path, @default);
        }

        /// <inheritdoc />
        public override decimal GetDecimal(HoconPath path, decimal @default = 0)
        {
            return _config.GetDecimal(path, @default);
        }

        /// <inheritdoc />
        public override IList<bool> GetBooleanList(string path)
        {
            return _config.GetBooleanList(path);
        }

        /// <inheritdoc />
        public override IList<bool> GetBooleanList(HoconPath path)
        {
            return _config.GetBooleanList(path);
        }

        /// <inheritdoc />
        public override IList<bool> GetBooleanList(string path, IList<bool> @default)
        {
            return _config.GetBooleanList(path, @default);
        }

        /// <inheritdoc />
        public override IList<bool> GetBooleanList(HoconPath path, IList<bool> @default)
        {
            return _config.GetBooleanList(path, @default);
        }

        /// <inheritdoc />
        public override IList<byte> GetByteList(string path)
        {
            return _config.GetByteList(path);
        }

        /// <inheritdoc />
        public override IList<byte> GetByteList(HoconPath path)
        {
            return _config.GetByteList(path);
        }

        /// <inheritdoc />
        public override IList<byte> GetByteList(string path, IList<byte> @default)
        {
            return _config.GetByteList(path, @default);
        }

        /// <inheritdoc />
        public override IList<byte> GetByteList(HoconPath path, IList<byte> @default)
        {
            return _config.GetByteList(path, @default);
        }

        /// <inheritdoc />
        public override long? GetByteSize(string path)
        {
            return _config.GetByteSize(path);
        }

        /// <inheritdoc />
        public override long? GetByteSize(HoconPath path)
        {
            return _config.GetByteSize(path);
        }

        /// <inheritdoc />
        public override IList<decimal> GetDecimalList(string path)
        {
            return _config.GetDecimalList(path);
        }

        /// <inheritdoc />
        public override IList<decimal> GetDecimalList(HoconPath path)
        {
            return _config.GetDecimalList(path);
        }

        /// <inheritdoc />
        public override IList<decimal> GetDecimalList(string path, IList<decimal> @default)
        {
            return _config.GetDecimalList(path, @default);
        }

        /// <inheritdoc />
        public override IList<decimal> GetDecimalList(HoconPath path, IList<decimal> @default)
        {
            return _config.GetDecimalList(path, @default);
        }

        /// <inheritdoc />
        public override IList<double> GetDoubleList(string path)
        {
            return _config.GetDoubleList(path);
        }

        /// <inheritdoc />
        public override IList<double> GetDoubleList(HoconPath path)
        {
            return _config.GetDoubleList(path);
        }

        /// <inheritdoc />
        public override IList<double> GetDoubleList(string path, IList<double> @default)
        {
            return _config.GetDoubleList(path, @default);
        }

        /// <inheritdoc />
        public override IList<double> GetDoubleList(HoconPath path, IList<double> @default)
        {
            return _config.GetDoubleList(path, @default);
        }

        /// <inheritdoc />
        public override float GetFloat(string path)
        {
            return _config.GetFloat(path);
        }

        /// <inheritdoc />
        public override float GetFloat(HoconPath path)
        {
            return _config.GetFloat(path);
        }

        /// <inheritdoc />
        public override float GetFloat(string path, float @default)
        {
            return _config.GetFloat(path, @default);
        }

        /// <inheritdoc />
        public override float GetFloat(HoconPath path, float @default)
        {
            return _config.GetFloat(path, @default);
        }

        /// <inheritdoc />
        public override IList<float> GetFloatList(string path)
        {
            return _config.GetFloatList(path);
        }

        /// <inheritdoc />
        public override IList<float> GetFloatList(HoconPath path)
        {
            return _config.GetFloatList(path);
        }

        /// <inheritdoc />
        public override IList<float> GetFloatList(string path, IList<float> @default)
        {
            return _config.GetFloatList(path, @default);
        }

        /// <inheritdoc />
        public override IList<float> GetFloatList(HoconPath path, IList<float> @default)
        {
            return _config.GetFloatList(path, @default);
        }

        /// <inheritdoc />
        public override IList<int> GetIntList(string path)
        {
            return _config.GetIntList(path);
        }

        /// <inheritdoc />
        public override IList<int> GetIntList(HoconPath path)
        {
            return _config.GetIntList(path);
        }

        /// <inheritdoc />
        public override IList<int> GetIntList(string path, IList<int> @default)
        {
            return _config.GetIntList(path, @default);
        }

        /// <inheritdoc />
        public override IList<int> GetIntList(HoconPath path, IList<int> @default)
        {
            return _config.GetIntList(path, @default);
        }

        /// <inheritdoc />
        public override IList<long> GetLongList(string path)
        {
            return _config.GetLongList(path);
        }

        /// <inheritdoc />
        public override IList<long> GetLongList(HoconPath path)
        {
            return _config.GetLongList(path);
        }

        /// <inheritdoc />
        public override IList<long> GetLongList(string path, IList<long> @default)
        {
            return _config.GetLongList(path, @default);
        }

        /// <inheritdoc />
        public override IList<long> GetLongList(HoconPath path, IList<long> @default)
        {
            return _config.GetLongList(path, @default);
        }

        /// <inheritdoc />
        public override IList<string> GetStringList(string path)
        {
            return _config.GetStringList(path);
        }

        /// <inheritdoc />
        public override IList<string> GetStringList(HoconPath path)
        {
            return _config.GetStringList(path);
        }

        /// <inheritdoc />
        public override IList<string> GetStringList(string path, IList<string> @default)
        {
            return _config.GetStringList(path, @default);
        }

        /// <inheritdoc />
        public override IList<string> GetStringList(HoconPath path, IList<string> @default)
        {
            return _config.GetStringList(path, @default);
        }

        /// <inheritdoc />
        public override TimeSpan GetTimeSpan(string path, bool allowInfinite = true)
        {
            return _config.GetTimeSpan(path, null, allowInfinite);
        }

        /// <inheritdoc />
        public override TimeSpan GetTimeSpan(HoconPath path, bool allowInfinite = true)
        {
            return _config.GetTimeSpan(path, null, allowInfinite);
        }

        /// <inheritdoc />
        public override TimeSpan GetTimeSpan(string path, TimeSpan? @default = null, bool allowInfinite = true)
        {
            return _config.GetTimeSpan(path, @default, allowInfinite);
        }

        /// <inheritdoc />
        public override TimeSpan GetTimeSpan(HoconPath path, TimeSpan? @default = null, bool allowInfinite = true)
        {
            return _config.GetTimeSpan(path, @default, allowInfinite);
        }

        #endregion

    }
}

