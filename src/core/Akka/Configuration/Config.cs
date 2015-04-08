//-----------------------------------------------------------------------
// <copyright file="Config.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Configuration.Hocon;

namespace Akka.Configuration
{
    public class Config
    {
        public Config()
        {
        }

        public Config(HoconRoot root)
        {
            if (root.Value == null)
                throw new ArgumentNullException("root.Value");

            Root = root.Value;
            Substitutions = root.Substitutions;
        }

        public Config(Config source, Config fallback)
        {
            if (source == null)
                throw new ArgumentNullException("source");

            Root = source.Root;
            Fallback = fallback;
        }

        public Config Fallback { get; private set; }

        /// <summary>
        ///     Lets the caller know if this root node contains any values
        /// </summary>
        public virtual bool IsEmpty
        {
            get { return Root == null || Root.IsEmpty; }
        }

        /// <summary>
        ///     Returns the root node of this configuration section
        /// </summary>
        public virtual HoconValue Root { get; private set; }

        public IEnumerable<HoconSubstitution> Substitutions { get; set; }

        protected Config Copy()
        {
            //deep clone
            return new Config
            {
                Fallback = Fallback != null ? Fallback.Copy() : null,
                Root = Root,
                Substitutions = Substitutions
            };
        }

        private HoconValue GetNode(string path)
        {
            string[] elements = path.Split('.');
            HoconValue currentNode = Root;
            if (currentNode == null)
            {
                throw new Exception("Current node should not be null");
            }
            foreach (string key in elements)
            {
                currentNode = currentNode.GetChildObject(key);
                if (currentNode == null)
                {
                    if (Fallback != null)
                        return Fallback.GetNode(path);

                    return null;
                }
            }
            return currentNode;
        }

        public virtual bool GetBoolean(string path, bool @default = false)
        {
            HoconValue value = GetNode(path);
            if (value == null)
                return @default;

            return value.GetBoolean();
        }

        public virtual long? GetByteSize(string path)
        {
            HoconValue value = GetNode(path);
            if (value == null) return null;
            return value.GetByteSize();
        }

        public virtual int GetInt(string path, int @default = 0)
        {
            HoconValue value = GetNode(path);
            if (value == null)
                return @default;

            return value.GetInt();
        }

        public virtual long GetLong(string path, long @default = 0)
        {
            HoconValue value = GetNode(path);
            if (value == null)
                return @default;

            return value.GetLong();
        }

        public virtual string GetString(string path, string @default = null)
        {
            HoconValue value = GetNode(path);
            if (value == null)
                return @default;

            return value.GetString();
        }

        public virtual float GetFloat(string path, float @default = 0)
        {
            HoconValue value = GetNode(path);
            if (value == null)
                return @default;

            return value.GetFloat();
        }

        public virtual decimal GetDecimal(string path, decimal @default = 0)
        {
            HoconValue value = GetNode(path);
            if (value == null)
                return @default;

            return value.GetDecimal();
        }

        public virtual double GetDouble(string path, double @default = 0)
        {
            HoconValue value = GetNode(path);
            if (value == null)
                return @default;

            return value.GetDouble();
        }

        public virtual IList<Boolean> GetBooleanList(string path)
        {
            HoconValue value = GetNode(path);
            return value.GetBooleanList();
        }

        public virtual IList<decimal> GetDecimalList(string path)
        {
            HoconValue value = GetNode(path);
            return value.GetDecimalList();
        }

        public virtual IList<float> GetFloatList(string path)
        {
            HoconValue value = GetNode(path);
            return value.GetFloatList();
        }

        public virtual IList<double> GetDoubleList(string path)
        {
            HoconValue value = GetNode(path);
            return value.GetDoubleList();
        }

        public virtual IList<int> GetIntList(string path)
        {
            HoconValue value = GetNode(path);
            return value.GetIntList();
        }

        public virtual IList<long> GetLongList(string path)
        {
            HoconValue value = GetNode(path);
            return value.GetLongList();
        }

        public virtual IList<byte> GetByteList(string path)
        {
            HoconValue value = GetNode(path);
            return value.GetByteList();
        }

        public virtual IList<string> GetStringList(string path)
        {
            HoconValue value = GetNode(path);
            if (value == null) return new string[0];
            return value.GetStringList();
        }

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
        /// Return a <see cref="HoconValue"/> from a specific path.
        /// </summary>
        /// <param name="path">The path for which we're loading a value.</param>
        /// <returns>The <see cref="HoconValue"/> found at the location if one exists, otherwise <c>null</c>.</returns>
        public HoconValue GetValue(string path)
        {
            HoconValue value = GetNode(path);
            return value;
        }

        [Obsolete("Use GetTimeSpan instead")]
        public TimeSpan GetMillisDuration(string path, TimeSpan? @default = null, bool allowInfinite = true)
        {
            return GetTimeSpan(path, @default, allowInfinite);
        }

        public virtual TimeSpan GetTimeSpan(string path, TimeSpan? @default = null, bool allowInfinite = true)
        {
            HoconValue value = GetNode(path);
            if (value == null)
                return @default.GetValueOrDefault();

            return value.GetTimeSpan(allowInfinite);
        }

        public override string ToString()
        {
            if (Root == null)
                return "";

            return Root.ToString();
        }

        public virtual Config WithFallback(Config fallback)
        {
            if (fallback == this)
                throw new ArgumentException("Config can not have itself as fallback", "fallback");

            Config clone = Copy();

            Config current = clone;
            while (current.Fallback != null)
            {
                current = current.Fallback;
            }
            current.Fallback = fallback;

            return clone;
        }


        /// <summary>
        /// Determine if a HOCON configuration element exists at the specified location
        /// </summary>
        /// <param name="path">The location to check for a configuration value.</param>
        /// <returns><c>true</c> if a value was found, <c>false</c> otherwise.</returns>
        public virtual bool HasPath(string path)
        {
            HoconValue value = GetNode(path);
            return value != null;
        }

        public static Config operator +(Config config, string fallback)
        {
            Config fallbackConfig = ConfigurationFactory.ParseString(fallback);
            return config.WithFallback(fallbackConfig);
        }

        public static Config operator +(string configHocon, Config fallbackConfig)
        {
            Config config = ConfigurationFactory.ParseString(configHocon);
            return config.WithFallback(fallbackConfig);
        }

        public static implicit operator Config(string str)
        {
            Config config = ConfigurationFactory.ParseString(str);
            return config;
        }

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
    }

    public static class ConfigExtensions
    {
        public static Config SafeWithFallback(this Config config, Config fallback)
        {
            return config == null
                ? fallback
                : ReferenceEquals(config, fallback)
                    ? config
                    : config.WithFallback(fallback);
        }

        /// <summary>
        ///     Convenience method for determining if <see cref="Config" /> has any usable content period.
        /// </summary>
        /// <returns>true if the <see cref="Config" /> is null or <see cref="Config.IsEmpty" /> return true; false otherwise.</returns>
        public static bool IsNullOrEmpty(this Config config)
        {
            return config == null || config.IsEmpty;
        }
    }
}

