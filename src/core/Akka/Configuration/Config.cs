﻿using System;
using System.Collections.Generic;
using Akka.Configuration.Hocon;

namespace Akka.Configuration
{
    public class Config
    {
        private Config _fallback;
        private HoconValue _node;
        private IEnumerable<HoconSubstitution> _substitutions;


        public Config()
        {
        }

        public Config(HoconRoot root)
        {
            if (root.Value == null)
                throw new ArgumentNullException("root.Value");

            _node = root.Value;
            _substitutions = root.Substitutions;
        }

        public Config(Config source, Config fallback)
        {
            if (source == null)
                throw new ArgumentNullException("source");

            _node = source._node;
            _fallback = fallback;
        }

        /// <summary>
        ///     Lets the caller know if this root node contains any values
        /// </summary>
        public bool IsEmpty
        {
            get { return _node == null || _node.IsEmpty; }
        }

        /// <summary>
        ///     Returns the root node of this configuration section
        /// </summary>
        public HoconValue Root
        {
            get { return _node; }
        }

        protected Config Copy()
        {
            //deep clone
            return new Config
            {
                _fallback = _fallback != null ? _fallback.Copy() : null,
                _node = _node,
                _substitutions = _substitutions
            };
        }

        private HoconValue GetNode(string path)
        {
            string[] elements = path.Split('.');
            HoconValue currentNode = _node;
            if (currentNode == null)
            {
                throw new Exception("Current node should not be null");
            }
            foreach (string key in elements)
            {
                currentNode = currentNode.GetChildObject(key);
                if (currentNode == null)
                {
                    if (_fallback != null)
                        return _fallback.GetNode(path);

                    return null;
                }
            }
            return currentNode;
        }

        public bool GetBoolean(string path, bool @default = false)
        {
            HoconValue value = GetNode(path);
            if (value == null)
                return @default;

            return value.GetBoolean();
        }

        public long? GetByteSize(string path)
        {
            HoconValue value = GetNode(path);
            if (value == null) return null;
            return value.GetByteSize();
        }

        public int GetInt(string path, int @default = 0)
        {
            HoconValue value = GetNode(path);
            if (value == null)
                return @default;

            return value.GetInt();
        }

        public long GetLong(string path, long @default = 0)
        {
            HoconValue value = GetNode(path);
            if (value == null)
                return @default;

            return value.GetLong();
        }

        public string GetString(string path, string @default = null)
        {
            HoconValue value = GetNode(path);
            if (value == null)
                return @default;

            return value.GetString();
        }

        public float GetFloat(string path, float @default = 0)
        {
            HoconValue value = GetNode(path);
            if (value == null)
                return @default;

            return value.GetFloat();
        }

        public decimal GetDecimal(string path, decimal @default = 0)
        {
            HoconValue value = GetNode(path);
            if (value == null)
                return @default;

            return value.GetDecimal();
        }

        public double GetDouble(string path, double @default = 0)
        {
            HoconValue value = GetNode(path);
            if (value == null)
                return @default;

            return value.GetDouble();
        }

        public IList<Boolean> GetBooleanList(string path)
        {
            HoconValue value = GetNode(path);
            return value.GetBooleanList();
        }

        public IList<decimal> GetDecimalList(string path)
        {
            HoconValue value = GetNode(path);
            return value.GetDecimalList();
        }

        public IList<float> GetFloatList(string path)
        {
            HoconValue value = GetNode(path);
            return value.GetFloatList();
        }

        public IList<double> GetDoubleList(string path)
        {
            HoconValue value = GetNode(path);
            return value.GetDoubleList();
        }

        public IList<int> GetIntList(string path)
        {
            HoconValue value = GetNode(path);
            return value.GetIntList();
        }

        public IList<long> GetLongList(string path)
        {
            HoconValue value = GetNode(path);
            return value.GetLongList();
        }

        public IList<byte> GetByteList(string path)
        {
            HoconValue value = GetNode(path);
            return value.GetByteList();
        }

        public IList<string> GetStringList(string path)
        {
            HoconValue value = GetNode(path);
            if (value == null) return new string[0];
            return value.GetStringList();
        }

        public Config GetConfig(string path)
        {
            HoconValue value = GetNode(path);
            if (_fallback != null)
            {
                Config f = _fallback.GetConfig(path);
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

        public TimeSpan GetTimeSpan(string path, TimeSpan? @default = null, bool allowInfinite = true)
        {
            HoconValue value = GetNode(path);
            if (value == null)
                return @default.GetValueOrDefault();

            return value.GetTimeSpan(allowInfinite);
        }

        public override string ToString()
        {
            if (_node == null)
                return "";

            return _node.ToString();
        }

        public Config WithFallback(Config fallback)
        {
            if (fallback == this)
                throw new ArgumentException("Config can not have itself as fallback", "fallback");

            Config clone = Copy();

            Config current = clone;
            while (current._fallback != null)
            {
                current = current._fallback;
            }
            current._fallback = fallback;

            return clone;
        }


        public bool HasPath(string path)
        {
            HoconValue value = GetNode(path);
            return value != null;
        }

        public static Config operator +(Config config, string fallback)
        {
            var fallbackConfig = ConfigurationFactory.ParseString(fallback);
            return config.WithFallback(fallbackConfig);
        }

        public static Config operator +(string configHocon, Config fallbackConfig)
        {
            var config = ConfigurationFactory.ParseString(configHocon);
            return config.WithFallback(fallbackConfig);
        }

        public static implicit operator Config(string str)
        {
            var config = ConfigurationFactory.ParseString(str);
            return config;
        }

        public IEnumerable<KeyValuePair<string, HoconValue>> AsEnumerable()
        {
            var used = new HashSet<string>();
            var current = this;
            while (current != null)
            {
                foreach (var kvp in current.Root.GetObject().AsEnumerable())
                {
                    if (!used.Contains(kvp.Key))
                    {
                        yield return kvp;
                        used.Add(kvp.Key);
                    }
                }
                current = current._fallback;
            }
        }
    }

    public static class ConfigExtensions
    {
        public static Config SafeWithFallback(this Config config, Config fallback)
        {
            return config == null ? fallback 
                : ReferenceEquals(config,fallback) ? config 
                : config.WithFallback(fallback);
        }
    }
}