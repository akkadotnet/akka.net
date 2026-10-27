//-----------------------------------------------------------------------
// <copyright file="CachingConfig.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        /// Cached result of looking up a configuration path whose value is not stored as a string.
        /// </summary>
        public struct ValuePathEntry : IPathEntry
        {
            /// <summary>
            /// Creates a cached path result with an associated configuration value.
            /// </summary>
            /// <param name="valid">Whether checking path existence completed without an error; reading its value can still fail and be cached as empty.</param>
            /// <param name="exists">Whether the path exists in the wrapped configuration.</param>
            /// <param name="config">The cached configuration value, or an empty configuration when none is available.</param>
            public ValuePathEntry(bool valid, bool exists, Config config) : this()
            {
                Config = config;
                Exists = exists;
                Valid = valid;
            }

            /// <summary>
            /// Creates a cached path result without an associated configuration value.
            /// </summary>
            /// <param name="valid">Whether checking path existence completed without an error; reading its value can still fail and be cached as empty.</param>
            /// <param name="exists">Whether the path exists in the wrapped configuration.</param>
            public ValuePathEntry(bool valid, bool exists)
                : this(valid, exists, EmptyConfig)
            {
            }

            /// <summary>
            /// Whether checking path existence completed without an error. Reading the value can still fail and be cached as empty.
            /// </summary>
            public bool Valid { get; private set; }
            /// <summary>
            /// Whether the path exists in the wrapped configuration.
            /// </summary>
            public bool Exists { get; private set; }
            /// <summary>
            /// The cached configuration value associated with the path.
            /// </summary>
            public Config Config { get; private set; }
        }

        /// <summary>
        /// Cached result of looking up a configuration path whose value is a string.
        /// </summary>
        public struct StringPathEntry : IPathEntry
        {
            /// <summary>
            /// Creates a cached string path result with its associated configuration value.
            /// </summary>
            /// <param name="valid">Whether checking path existence completed without an error; reading its value can still fail and be cached as empty.</param>
            /// <param name="exists">Whether the path exists in the wrapped configuration.</param>
            /// <param name="config">The cached configuration value for the path.</param>
            /// <param name="value">The string value read from the path.</param>
            public StringPathEntry(bool valid, bool exists, Config config, string value) : this()
            {
                Config = config;
                Exists = exists;
                Valid = valid;
                Value = value;
            }

            /// <summary>
            /// Whether checking path existence completed without an error. Reading the value can still fail and be cached as empty.
            /// </summary>
            public bool Valid { get; private set; }
            /// <summary>
            /// Whether the path exists in the wrapped configuration.
            /// </summary>
            public bool Exists { get; private set; }
            /// <summary>
            /// The cached configuration value associated with the path.
            /// </summary>
            public Config Config { get; private set; }

            /// <summary>
            /// The string value read from the path.
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
        /// Wraps a configuration and caches path-existence and string-value lookups. Wrapping another
        /// <see cref="CachingConfig"/> reuses its underlying configuration and lookup cache.
        /// </summary>
        /// <param name="config">The configuration whose path lookups should be cached.</param>
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
        /// Gets the root HOCON value from the wrapped configuration.
        /// </summary>
        public override HoconValue Root
        {
            get { return _config.Root; }
        }

        /// <summary>
        /// Returns a caching wrapper around this configuration combined with the supplied fallback.
        /// </summary>
        /// <param name="fallback">The configuration to use as a fallback.</param>
        public override Config WithFallback(Config fallback)
        {
            return new CachingConfig(_config.WithFallback(fallback));
        }

        /// <summary>
        /// Checks whether the wrapped configuration contains the specified path, caching the lookup result.
        /// </summary>
        /// <param name="path">The configuration path to check.</param>
        public override bool HasPath(string path)
        {
            var entry = GetPathEntry(path);
            if (entry.Valid)
                return entry.Exists;
            else //run the real code in order to get exceptions
                return _config.HasPath(path);
        }

        /// <summary>
        /// Gets whether the wrapped configuration is empty.
        /// </summary>
        public override bool IsEmpty
        {
            get { return _config.IsEmpty; }
        }

        /// <summary>
        /// Enumerates the key-value pairs exposed by the wrapped configuration.
        /// </summary>
        /// <returns>The wrapped configuration's key-value pairs.</returns>
        public override IEnumerable<KeyValuePair<string, HoconValue>> AsEnumerable()
        {
            return _config.AsEnumerable();
        }

        /// <summary>
        /// Returns a boolean value from the configuration at the specified path, or the default value if not found.
        /// </summary>
        /// <param name="path">The configuration path.</param>
        /// <param name="default">The default value to return if the path is not found.</param>
        /// <returns>The boolean value at the specified path, or the default value.</returns>
        public override bool GetBoolean(string path, bool @default = false)
        {
            return _config.GetBoolean(path, @default);
        }

        /// <summary>
        /// Returns an integer value from the configuration at the specified path, or the default value if not found.
        /// </summary>
        /// <param name="path">The configuration path.</param>
        /// <param name="default">The default value to return if the path is not found.</param>
        /// <returns>The integer value at the specified path, or the default value.</returns>
        public override int GetInt(string path, int @default = 0)
        {
            return _config.GetInt(path, @default);
        }

        /// <summary>
        /// Returns a long value from the configuration at the specified path, or the default value if not found.
        /// </summary>
        /// <param name="path">The configuration path.</param>
        /// <param name="default">The default value to return if the path is not found.</param>
        /// <returns>The long value at the specified path, or the default value.</returns>
        public override long GetLong(string path, long @default = 0)
        {
            return _config.GetLong(path, @default);
        }

        /// <summary>
        /// Returns a double value from the configuration at the specified path, or the default value if not found.
        /// </summary>
        /// <param name="path">The configuration path.</param>
        /// <param name="default">The default value to return if the path is not found.</param>
        /// <returns>The double value at the specified path, or the default value.</returns>
        public override double GetDouble(string path, double @default = 0)
        {
            return _config.GetDouble(path, @default);
        }

        /// <summary>
        /// Returns a string value from the configuration at the specified path, or the default value if not found.
        /// </summary>
        /// <param name="path">The configuration path.</param>
        /// <param name="default">The default value to return if the path is not found.</param>
        /// <returns>The string value at the specified path, or the default value.</returns>
        public override string GetString(string path, string @default = null)
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

        /// <summary>
        /// Returns a decimal value from the configuration at the specified path, or the default value if not found.
        /// </summary>
        /// <param name="path">The configuration path.</param>
        /// <param name="default">The default value to return if the path is not found.</param>
        /// <returns>The decimal value at the specified path, or the default value.</returns>
        public override decimal GetDecimal(string path, decimal @default = 0)
        {
            return _config.GetDecimal(path, @default);
        }

        /// <summary>
        /// Returns a list of boolean values from the configuration at the specified path.
        /// </summary>
        /// <param name="path">The configuration path.</param>
        /// <returns>The list of boolean values at the specified path.</returns>
        public override IList<bool> GetBooleanList(string path)
        {
            return _config.GetBooleanList(path);
        }

        /// <summary>
        /// Returns a list of byte values from the configuration at the specified path.
        /// </summary>
        /// <param name="path">The configuration path.</param>
        /// <returns>The list of byte values at the specified path.</returns>
        public override IList<byte> GetByteList(string path)
        {
            return _config.GetByteList(path);
        }

        /// <summary>
        /// Returns the byte size value from the configuration at the specified path, or null if not found.
        /// </summary>
        /// <param name="path">The configuration path.</param>
        /// <returns>The byte size value at the specified path, or null if not found.</returns>
        public override long? GetByteSize(string path)
        {
            return _config.GetByteSize(path);
        }

        /// <summary>
        /// Returns a list of decimal values from the configuration at the specified path.
        /// </summary>
        /// <param name="path">The configuration path.</param>
        /// <returns>The list of decimal values at the specified path.</returns>
        public override IList<decimal> GetDecimalList(string path)
        {
            return _config.GetDecimalList(path);
        }

        /// <summary>
        /// Returns a list of double values from the configuration at the specified path.
        /// </summary>
        /// <param name="path">The configuration path.</param>
        /// <returns>The list of double values at the specified path.</returns>
        public override IList<double> GetDoubleList(string path)
        {
            return _config.GetDoubleList(path);
        }

        /// <summary>
        /// Returns a float value from the configuration at the specified path, or the default value if not found.
        /// </summary>
        /// <param name="path">The configuration path.</param>
        /// <param name="default">The default value to return if the path is not found.</param>
        /// <returns>The float value at the specified path, or the default value.</returns>
        public override float GetFloat(string path, float @default = 0)
        {
            return _config.GetFloat(path, @default);
        }

        /// <summary>
        /// Returns a list of float values from the configuration at the specified path.
        /// </summary>
        /// <param name="path">The configuration path.</param>
        /// <returns>The list of float values at the specified path.</returns>
        public override IList<float> GetFloatList(string path)
        {
            return _config.GetFloatList(path);
        }

        /// <summary>
        /// Returns a list of integer values from the configuration at the specified path.
        /// </summary>
        /// <param name="path">The configuration path.</param>
        /// <returns>The list of integer values at the specified path.</returns>
        public override IList<int> GetIntList(string path)
        {
            return _config.GetIntList(path);
        }

        /// <summary>
        /// Returns a list of long values from the configuration at the specified path.
        /// </summary>
        /// <param name="path">The configuration path.</param>
        /// <returns>The list of long values at the specified path.</returns>
        public override IList<long> GetLongList(string path)
        {
            return _config.GetLongList(path);
        }

        /// <summary>
        /// Returns a list of string values from the configuration at the specified path.
        /// </summary>
        /// <param name="path">The configuration path.</param>
        /// <param name="strings">Unused parameter (for compatibility).</param>
        /// <returns>The list of string values at the specified path.</returns>
        public override IList<string> GetStringList(string path, string[] strings)
        {
            return _config.GetStringList(path);
        }

        /// <summary>
        /// Returns a <see cref="TimeSpan"/> value from the configuration at the specified path, or the default value if not found.
        /// </summary>
        /// <param name="path">The configuration path.</param>
        /// <param name="default">The default value to return if the path is not found.</param>
        /// <param name="allowInfinite">Whether to allow infinite values.</param>
        /// <returns>The <see cref="TimeSpan"/> value at the specified path, or the default value.</returns>
        public override TimeSpan GetTimeSpan(string path, TimeSpan? @default = null, bool allowInfinite = true)
        {
            return _config.GetTimeSpan(path, @default, allowInfinite);
        }

        /// <summary>
        /// Returns a <see cref="Config"/> object from the configuration at the specified path.
        /// </summary>
        /// <param name="path">The configuration path.</param>
        /// <returns>The <see cref="Config"/> object at the specified path.</returns>
        public override Config GetConfig(string path)
        {
            return _config.GetConfig(path);
        }
    }
}
