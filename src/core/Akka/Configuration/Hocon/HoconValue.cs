﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace Akka.Configuration.Hocon
{
    /// <summary>
    /// Root type of HOCON configuration object
    /// </summary>
    public class HoconValue : IMightBeAHoconObject
    {
        /// <summary>
        /// Default constructor
        /// </summary>
        public HoconValue()
        {
            Values = new List<IHoconElement>();
        }

        /// <summary>
        /// Returns true if this HOCON value doesn't contain any elements
        /// </summary>
        public bool IsEmpty
        {
            get { return Values.Count == 0; }
        }

        /// <summary>
        /// The list of elements inside this HOCON value
        /// </summary>
        public List<IHoconElement> Values { get; private set; }

        /// <summary>
        /// Wraps this <see cref="HoconValue"/> into a new <see cref="Config"/> object at the specified key.
        /// </summary>
        public Config AtKey(string key)
        {
            var o = new HoconObject();
            o.GetOrCreateKey(key);
            o.Items[key] = this;
            var r = new HoconValue();
            r.Values.Add(o);
            return new Config(new HoconRoot(r));
        }

        public HoconObject GetObject()
        {
            //TODO: merge objects?
            IHoconElement raw = Values.FirstOrDefault();
            var o = raw as HoconObject;
            var sub = raw as IMightBeAHoconObject;
            if (o != null) return o;
            if (sub != null && sub.IsObject()) return sub.GetObject();
            return null;
        }

        /// <summary>
        /// Determines if this <see cref="HoconValue"/> is a <see cref="HoconObject"/>
        /// </summary>
        /// <returns><c>true</c> if this value is a HOCON object, <c>false</c> otherwise.</returns>
        public bool IsObject()
        {
            return GetObject() != null;
        }

        public void AppendValue(IHoconElement value)
        {
            Values.Add(value);
        }

        public void Clear()
        {
            Values.Clear();
        }

        public void NewValue(IHoconElement value)
        {
            Values.Clear();
            Values.Add(value);
        }

        public bool IsString()
        {
            return Values.Any() && Values.All(v => v.IsString());
        }

        private string ConcatString()
        {
            string concat = string.Join("", Values.Select(l => l.GetString())).Trim();

            if (concat == "null")
                return null;

            return concat;
        }

        public HoconValue GetChildObject(string key)
        {
            return GetObject().GetKey(key);
        }

        public bool GetBoolean()
        {
            string v = GetString();
            switch (v)
            {
                case "on":
                    return true;
                case "off":
                    return false;
                case "true":
                    return true;
                case "false":
                    return false;
                default:
                    throw new NotSupportedException("Unknown boolean format: " + v);
            }
        }

        public string GetString()
        {
            if (IsString())
            {
                return ConcatString();
            }
            return null; //TODO: throw exception?
        }

        public decimal GetDecimal()
        {
            return decimal.Parse(GetString(), NumberFormatInfo.InvariantInfo);
        }

        public float GetFloat()
        {
            return float.Parse(GetString(), NumberFormatInfo.InvariantInfo);
        }

        public double GetDouble()
        {
            return double.Parse(GetString(), NumberFormatInfo.InvariantInfo);
        }

        public long GetLong()
        {
            return long.Parse(GetString(), NumberFormatInfo.InvariantInfo);
        }

        public int GetInt()
        {
            return int.Parse(GetString(), NumberFormatInfo.InvariantInfo);
        }

        public byte GetByte()
        {
            return byte.Parse(GetString(), NumberFormatInfo.InvariantInfo);
        }

        public IList<byte> GetByteList()
        {
            return GetArray().Select(v => v.GetByte()).ToList();
        }

        public IList<int> GetIntList()
        {
            return GetArray().Select(v => v.GetInt()).ToList();
        }

        public IList<long> GetLongList()
        {
            return GetArray().Select(v => v.GetLong()).ToList();
        }

        public IList<bool> GetBooleanList()
        {
            return GetArray().Select(v => v.GetBoolean()).ToList();
        }

        public IList<float> GetFloatList()
        {
            return GetArray().Select(v => v.GetFloat()).ToList();
        }

        public IList<double> GetDoubleList()
        {
            return GetArray().Select(v => v.GetDouble()).ToList();
        }

        public IList<decimal> GetDecimalList()
        {
            return GetArray().Select(v => v.GetDecimal()).ToList();
        }

        public IList<string> GetStringList()
        {
            return GetArray().Select(v => v.GetString()).ToList();
        }

        public IList<HoconValue> GetArray()
        {
            IEnumerable<HoconValue> x = from arr in Values
                where arr.IsArray()
                from e in arr.GetArray()
                select e;

            return x.ToList();
        }

        public bool IsArray()
        {
            return GetArray() != null;
        }


        [Obsolete("Use GetTimeSpan instead")]
        public TimeSpan GetMillisDuration(bool allowInfinite = true)
        {
            return GetTimeSpan(allowInfinite);
        }

        public TimeSpan GetTimeSpan(bool allowInfinite = true)
        {
            string res = GetString();
            if (res.EndsWith("ms"))
            //TODO: Add support for ns, us, and non abbreviated versions (second, seconds and so on) see https://github.com/typesafehub/config/blob/master/HOCON.md#duration-format
            {
                var v = res.Substring(0, res.Length - 2);
                return TimeSpan.FromMilliseconds(ParsePositiveValue(v));
            }
            if (res.EndsWith("s"))
            {
                var v = res.Substring(0, res.Length - 1);
                return TimeSpan.FromSeconds(ParsePositiveValue(v));
            }
            if(res.EndsWith("m"))
            {
                var v = res.Substring(0, res.Length - 1);
                return TimeSpan.FromMinutes(ParsePositiveValue(v));
            }
            if(res.EndsWith("h"))
            {
                var v = res.Substring(0, res.Length - 1);
                return TimeSpan.FromHours(ParsePositiveValue(v));
            }
            if (res.EndsWith("d"))
            {
                var v = res.Substring(0, res.Length - 1);
                return TimeSpan.FromDays(ParsePositiveValue(v));
            }
            if(allowInfinite && res.Equals("infinite", StringComparison.OrdinalIgnoreCase))  //Not in Hocon spec
            {
                return Timeout.InfiniteTimeSpan;
            }

            return TimeSpan.FromMilliseconds(ParsePositiveValue(res));
        }

        private static double ParsePositiveValue(string v)
        {
            var value = double.Parse(v, NumberFormatInfo.InvariantInfo);
            if(value < 0)
                throw new FormatException("Expected a positive value instead of " + value);
            return value;
        }

        public long? GetByteSize()
        {
            var res = GetString();
            if (res.EndsWith("b"))
            {
                var v = res.Substring(0, res.Length - 1);
                return long.Parse(v);
            }

            return long.Parse(res);
        }

        public override string ToString()
        {
            return ToString(0);
        }

        public virtual string ToString(int indent)
        {
            if (IsString())
            {
                string text = QuoteIfNeeded(GetString());
                return text;
            }
            if (IsObject())
            {
                var i = new string(' ', indent*2);
                return string.Format("{{\r\n{1}{0}}}", i, GetObject().ToString(indent + 1));
            }
            if (IsArray())
            {
                return string.Format("[{0}]", string.Join(",", GetArray().Select(e => e.ToString(indent + 1))));
            }
            return "<<unknown value>>";
        }

        private string QuoteIfNeeded(string text)
        {
            if(text == null) return "";
            if(text.ToCharArray().Intersect(" \t".ToCharArray()).Any())
            {
                return "\"" + text + "\"";
            }
            return text;
        }
    }
}