//-----------------------------------------------------------------------
// <copyright file="Config.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Configuration.Hocon;
using Akka.Util.Internal;

namespace Akka.Configuration
{
    /// <summary>
    /// This class represents the main configuration object used by Akka.NET
    /// when configuring objects within the system. To put it simply, it's
    /// the internal representation of a HOCON (Human-Optimized Config Object Notation)
    /// configuration string.
    /// </summary>
    public class Config
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Config"/> class.
        /// </summary>
        public Config()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Config"/> class.
        /// </summary>
        /// <param name="root">The root node to base this configuration.</param>
        /// <exception cref="ArgumentNullException">This exception is thrown if the given <paramref name="root"/> value is undefined.</exception>
        public Config(HoconRoot root)
        {
            if (root.Value == null)
                throw new ArgumentNullException(nameof(root), "The root value cannot be null.");

            Value = root.Value;
            Root = root.Value;
            Substitutions = root.Substitutions;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Config"/> class.
        /// </summary>
        /// <param name="source">The configuration to use as the primary source.</param>
        /// <param name="fallback">The configuration to use as a secondary source.</param>
        /// <exception cref="ArgumentNullException">This exception is thrown if the given <paramref name="source"/> is undefined.</exception>
        public Config(Config source, Config fallback)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source), "The source configuration cannot be null.");

            Value = source.Value;
            Root = source.Root;
            Fallback = fallback;
        }

        /// <summary>
        /// The configuration used as a secondary source.
        /// </summary>
        public Config Fallback { get; private set; }

        /// <summary>
        /// Determines if this root node contains any values
        /// </summary>
        public virtual bool IsEmpty
        {
            get { return Root == null || Root.IsEmpty; }
        }

        private HoconValue Value { get; set; }

        /// <summary>
        /// The root node of this configuration section
        /// </summary>
        public virtual HoconValue Root { get; private set; }

        /// <summary>
        /// An enumeration of substitutions values
        /// </summary>
        public IEnumerable<HoconSubstitution> Substitutions { get; set; }

        /// <summary>
        /// Generates a deep clone of the current configuration.
        /// </summary>
        /// <returns>A deep clone of the current configuration</returns>
        protected Config Copy(Config fallback = null)
        {
            //deep clone
            return new Config
            {
                Fallback = Fallback != null ? Fallback.Copy(fallback) : fallback,
                Root = Root,
                Value = Value,
                Substitutions = Substitutions
            };
        }

        private HoconValue GetNode(string path)
        {
            var parsedPath = path.SplitDottedPathHonouringQuotes();
            HoconValue currentNode = Root;
            if (currentNode == null)
            {
                throw new InvalidOperationException("Current node should not be null");
            }
            foreach (string key in parsedPath)
            {
                currentNode = currentNode.GetChildObject(key);
                if (currentNode == null) return null;
            }
            return currentNode;
        }

        /// <summary>
        /// Retrieves a boolean value from the specified path in the configuration.
        /// </summary>
        /// <param name="path">The path that contains the value to retrieve.</param>
        /// <param name="default">The default value to return if the value doesn't exist.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if the current node is undefined.</exception>
        /// <returns>The boolean value defined in the specified path.</returns>
        public virtual bool GetBoolean(string path, bool @default = false)
        {
            HoconValue value = GetNode(path);
            if (value == null)
                return @default;

            return value.GetBoolean();
        }

        /// <summary>
        /// Retrieves a long value, optionally suffixed with a 'b', from the specified path in the configuration.
        /// </summary>
        /// <param name="path">The path that contains the value to retrieve.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if the current node is undefined.</exception>
        /// <returns>The long value defined in the specified path.</returns>
        public virtual long? GetByteSize(string path)
        {
            HoconValue value = GetNode(path);
            if (value == null) return null;
            return value.GetByteSize();
        }

        /// <summary>
        /// Retrieves a long value, optionally suffixed with a 'b', from the specified path in the configuration.
        /// </summary>
        /// <param name="path">The path that contains the value to retrieve.</param>
        /// <param name="def">Default return value if none provided.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if the current node is undefined.</exception>
        /// <returns>The long value defined in the specified path.</returns>
        public virtual long? GetByteSize(string path, long? def = null)
        {
            HoconValue value = GetNode(path);
            if (value == null) return def;
            return value.GetByteSize();
        }

        /// <summary>
        /// Retrieves an integer value from the specified path in the configuration.
        /// </summary>
        /// <param name="path">The path that contains the value to retrieve.</param>
        /// <param name="default">The default value to return if the value doesn't exist.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if the current node is undefined.</exception>
        /// <returns>The integer value defined in the specified path.</returns>
        public virtual int GetInt(string path, int @default = 0)
        {
            HoconValue value = GetNode(path);
            if (value == null)
                return @default;

            return value.GetInt();
        }

        /// <summary>
        /// Retrieves a long value from the specified path in the configuration.
        /// </summary>
        /// <param name="path">The path that contains the value to retrieve.</param>
        /// <param name="default">The default value to return if the value doesn't exist.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if the current node is undefined.</exception>
        /// <returns>The long value defined in the specified path.</returns>
        public virtual long GetLong(string path, long @default = 0)
        {
            HoconValue value = GetNode(path);
            if (value == null)
                return @default;

            return value.GetLong();
        }

        /// <summary>
        /// Retrieves a string value from the specified path in the configuration.
        /// </summary>
        /// <param name="path">The path that contains the value to retrieve.</param>
        /// <param name="default">The default value to return if the value doesn't exist.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if the current node is undefined.</exception>
        /// <returns>The string value defined in the specified path.</returns>
        public virtual string GetString(string path, string @default = null)
        {
            HoconValue value = GetNode(path);
            if (value == null)
                return @default;

            return value.GetString();
        }

        /// <summary>
        /// Retrieves a float value from the specified path in the configuration.
        /// </summary>
        /// <param name="path">The path that contains the value to retrieve.</param>
        /// <param name="default">The default value to return if the value doesn't exist.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if the current node is undefined.</exception>
        /// <returns>The float value defined in the specified path.</returns>
        public virtual float GetFloat(string path, float @default = 0)
        {
            HoconValue value = GetNode(path);
            if (value == null)
                return @default;

            return value.GetFloat();
        }

        /// <summary>
        /// Retrieves a decimal value from the specified path in the configuration.
        /// </summary>
        /// <param name="path">The path that contains the value to retrieve.</param>
        /// <param name="default">The default value to return if the value doesn't exist.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if the current node is undefined.</exception>
        /// <returns>The decimal value defined in the specified path.</returns>
        public virtual decimal GetDecimal(string path, decimal @default = 0)
        {
            HoconValue value = GetNode(path);
            if (value == null)
                return @default;

            return value.GetDecimal();
        }

        /// <summary>
        /// Retrieves a double value from the specified path in the configuration.
        /// </summary>
        /// <param name="path">The path that contains the value to retrieve.</param>
        /// <param name="default">The default value to return if the value doesn't exist.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if the current node is undefined.</exception>
        /// <returns>The double value defined in the specified path.</returns>
        public virtual double GetDouble(string path, double @default = 0)
        {
            HoconValue value = GetNode(path);
            if (value == null)
                return @default;

            return value.GetDouble();
        }

        /// <summary>
        /// Retrieves a list of boolean values from the specified path in the configuration.
        /// </summary>
        /// <param name="path">The path that contains the values to retrieve.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if the current node is undefined.</exception>
        /// <returns>The list of boolean values defined in the specified path.</returns>
        public virtual IList<Boolean> GetBooleanList(string path)
        {
            HoconValue value = GetNode(path);
            return value.GetBooleanList();
        }

        /// <summary>
        /// Retrieves a list of decimal values from the specified path in the configuration.
        /// </summary>
        /// <param name="path">The path that contains the values to retrieve.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if the current node is undefined.</exception>
        /// <returns>The list of decimal values defined in the specified path.</returns>
        public virtual IList<decimal> GetDecimalList(string path)
        {
            HoconValue value = GetNode(path);
            return value.GetDecimalList();
        }

        /// <summary>
        /// Retrieves a list of float values from the specified path in the configuration.
        /// </summary>
        /// <param name="path">The path that contains the values to retrieve.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if the current node is undefined.</exception>
        /// <returns>The list of float values defined in the specified path.</returns>
        public virtual IList<float> GetFloatList(string path)
        {
            HoconValue value = GetNode(path);
            return value.GetFloatList();
        }

        /// <summary>
        /// Retrieves a list of double values from the specified path in the configuration.
        /// </summary>
        /// <param name="path">The path that contains the values to retrieve.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if the current node is undefined.</exception>
        /// <returns>The list of double values defined in the specified path.</returns>
        public virtual IList<double> GetDoubleList(string path)
        {
            HoconValue value = GetNode(path);
            return value.GetDoubleList();
        }

        /// <summary>
        /// Retrieves a list of int values from the specified path in the configuration.
        /// </summary>
        /// <param name="path">The path that contains the values to retrieve.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if the current node is undefined.</exception>
        /// <returns>The list of int values defined in the specified path.</returns>
        public virtual IList<int> GetIntList(string path)
        {
            HoconValue value = GetNode(path);
            return value.GetIntList();
        }

        /// <summary>
        /// Retrieves a list of long values from the specified path in the configuration.
        /// </summary>
        /// <param name="path">The path that contains the values to retrieve.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if the current node is undefined.</exception>
        /// <returns>The list of long values defined in the specified path.</returns>
        public virtual IList<long> GetLongList(string path)
        {
            HoconValue value = GetNode(path);
            return value.GetLongList();
        }

        /// <summary>
        /// Retrieves a list of byte values from the specified path in the configuration.
        /// </summary>
        /// <param name="path">The path that contains the values to retrieve.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if the current node is undefined.</exception>
        /// <returns>The list of byte values defined in the specified path.</returns>
        public virtual IList<byte> GetByteList(string path)
        {
            HoconValue value = GetNode(path);
            return value.GetByteList();
        }

        /// <summary>
        /// Retrieves a list of string values from the specified path in the configuration.
        /// </summary>
        /// <param name="path">The path that contains the values to retrieve.</param>
        /// <param name="strings"></param>
        /// <exception cref="InvalidOperationException">This exception is thrown if the current node is undefined.</exception>
        /// <returns>The list of string values defined in the specified path.</returns>
        public virtual IList<string> GetStringList(string path)
        {
            HoconValue value = GetNode(path);
            if (value == null) return new List<string>();
            return value.GetStringList();
        }

        /// <summary>
        /// Retrieves a list of string values from the specified path in the configuration.
        /// </summary>
        /// <param name="path">The path that contains the values to retrieve.</param>
        /// <param name="defaultPaths">Default paths that will be returned to the user.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if the current node is undefined.</exception>
        /// <returns>The list of string values defined in the specified path.</returns>
        public virtual IList<string> GetStringList(string path, string[] defaultPaths)
        {
            HoconValue value = GetNode(path);
            if (value == null) return defaultPaths;
            return value.GetStringList();
        }

        /// <summary>
        /// Retrieves a new configuration from the current configuration
        /// with the root node being the supplied path.
        /// </summary>
        /// <param name="path">The path that contains the configuration to retrieve.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if the current node is undefined.</exception>
        /// <returns>A new configuration with the root node being the supplied path.</returns>
        public virtual Config GetConfig(string path)
        {
            HoconValue value = GetNode(path);
            if (Fallback != null)
            {
                Config f = Fallback.GetConfig(path);
                if (value == null && f == null)
                    return null;
                if (value == null)
                    return f;

                return new Config(new HoconRoot(value)).WithFallback(f);
            }

            if (value == null)
                return null;

            return new Config(new HoconRoot(value));
        }

        /// <summary>
        /// Retrieves a <see cref="HoconValue"/> from a specific path.
        /// </summary>
        /// <param name="path">The path that contains the value to retrieve.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if the current node is undefined.</exception>
        /// <returns>The <see cref="HoconValue"/> found at the location if one exists, otherwise <c>null</c>.</returns>
        public HoconValue GetValue(string path)
        {
            HoconValue value = GetNode(path);
            return value;
        }

        /// <summary>
        /// Retrieves a <see cref="TimeSpan"/> value from the specified path in the configuration.
        /// </summary>
        /// <param name="path">The path that contains the value to retrieve.</param>
        /// <param name="default">The default value to return if the value doesn't exist.</param>
        /// <param name="allowInfinite"><c>true</c> if infinite timespans are allowed; otherwise <c>false</c>.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if the current node is undefined.</exception>
        /// <returns>The <see cref="TimeSpan"/> value defined in the specified path.</returns>
        public virtual TimeSpan GetTimeSpan(string path, TimeSpan? @default = null, bool allowInfinite = true)
        {
            HoconValue value = GetNode(path);
            if (value == null)
                return @default.GetValueOrDefault();

            return value.GetTimeSpan(allowInfinite);
        }

        /// <summary>
        /// Converts the current configuration to a string.
        /// </summary>
        /// <returns>A string containing the current configuration.</returns>
        public override string ToString()
        {
            if (Value == null)
                return "";

            return Value.ToString();
        }

        /// <summary>
        /// Converts the current configuration to a string 
        /// </summary>
        /// <param name="includeFallback">if true returns string with current config combined with fallback key-values else only current config key-values</param>
        /// <returns>TBD</returns>
        public string ToString(bool includeFallback)
        {
            if (includeFallback == false)
                return ToString();
            return Root.ToString();
        }

        /// <summary>
        /// Configure the current configuration with a secondary source.
        /// </summary>
        /// <param name="fallback">The configuration to use as a secondary source.</param>
        /// <exception cref="ArgumentException">This exception is thrown if the given <paramref name="fallback"/> is a reference to this instance.</exception>
        /// <returns>The current configuration configured with the specified fallback.</returns>
        public virtual Config WithFallback(Config fallback)
        {
            if (fallback == this)
                throw new ArgumentException("Config can not have itself as fallback", nameof(fallback));
            if (fallback == null)
                return this;
            if (IsEmpty)
                return fallback;

            var mergedRoot = fallback.Root.GetObject().MergeImmutable(Root.GetObject());
            var newRoot = new HoconValue();
            newRoot.AppendValue(mergedRoot);
            var mergedConfig = Copy(fallback);
            mergedConfig.Root = newRoot;
            return mergedConfig;
        }

        /// <summary>
        /// Determine if a HOCON configuration element exists at the specified location
        /// </summary>
        /// <param name="path">The location to check for a configuration value.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if the current node is undefined.</exception>
        /// <returns><c>true</c> if a value was found, <c>false</c> otherwise.</returns>
        public virtual bool HasPath(string path)
        {
            HoconValue value = GetNode(path);
            return value != null;
        }

        /// <summary>
        /// Adds the supplied configuration string as a fallback to the supplied configuration.
        /// </summary>
        /// <param name="config">The configuration used as the source.</param>
        /// <param name="fallback">The string used as the fallback configuration.</param>
        /// <returns>The supplied configuration configured with the supplied fallback.</returns>
        public static Config operator +(Config config, string fallback)
        {
            Config fallbackConfig = ConfigurationFactory.ParseString(fallback);
            return config.WithFallback(fallbackConfig);
        }

        /// <summary>
        /// Adds the supplied configuration as a fallback to the supplied configuration string.
        /// </summary>
        /// <param name="configHocon">The configuration string used as the source.</param>
        /// <param name="fallbackConfig">The configuration used as the fallback.</param>
        /// <returns>A configuration configured with the supplied fallback.</returns>
        public static Config operator +(string configHocon, Config fallbackConfig)
        {
            Config config = ConfigurationFactory.ParseString(configHocon);
            return config.WithFallback(fallbackConfig);
        }

        /// <summary>
        /// Performs an implicit conversion from <see cref="System.String"/> to <see cref="Config"/>.
        /// </summary>
        /// <param name="str">The string that contains a configuration.</param>
        /// <returns>A configuration based on the supplied string.</returns>
        public static implicit operator Config(string str)
        {
            Config config = ConfigurationFactory.ParseString(str);
            return config;
        }

        /// <summary>
        /// Retrieves an enumerable key value pair representation of the current configuration.
        /// </summary>
        /// <returns>The current configuration represented as an enumerable key value pair.</returns>
        public virtual IEnumerable<KeyValuePair<string, HoconValue>> AsEnumerable()
        {
            var used = new HashSet<string>();
            Config current = this;
            while (current != null)
            {
                foreach (var kvp in current.Root.GetObject().Items)
                {
                    if (!used.Contains(kvp.Key))
                    {
                        yield return kvp;
                        used.Add(kvp.Key);
                    }
                }
                current = current.Fallback;
            }
        }

        /// <summary>
        /// A static "Empty" configuration we can use instead of <c>null</c> in some key areas.
        /// </summary>
        public static readonly Config Empty = ConfigurationFactory.Empty;
    }

    /// <summary>
    /// This class contains convenience methods for working with <see cref="Config"/>.
    /// </summary>
    public static class ConfigExtensions
    {
        /// <summary>
        /// Retrieves the current configuration or the fallback
        /// configuration if the current one is null.
        /// </summary>
        /// <param name="config">The configuration used as the source.</param>
        /// <param name="fallback">The configuration to use as a secondary source.</param>
        /// <returns>The current configuration or the fallback configuration if the current one is null.</returns>
        public static Config SafeWithFallback(this Config config, Config fallback)
        {
            return config == null
                ? fallback
                : ReferenceEquals(config, fallback)
                    ? config
                    : config.WithFallback(fallback);
        }

        /// <summary>
        /// Determines if the supplied configuration has any usable content period.
        /// </summary>
        /// <param name="config">The configuration used as the source.</param>
        /// <returns><c>true></c> if the <see cref="Config" /> is null or <see cref="Config.IsEmpty" />; otherwise <c>false</c>.</returns>
        public static bool IsNullOrEmpty(this Config config)
        {
            return config == null || config.IsEmpty;
        }
    }
}

