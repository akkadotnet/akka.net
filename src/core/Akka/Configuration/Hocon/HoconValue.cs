//-----------------------------------------------------------------------
// <copyright file="HoconValue.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace Akka.Configuration.Hocon
{
    /// <summary>
    /// This class represents the root type for a HOCON (Human-Optimized Config Object Notation)
    /// configuration object.
    /// </summary>
    public class HoconValue : IMightBeAHoconObject
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="HoconValue"/> class.
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
            get
            {
                if (Values.Count == 0)
                    return true;

                var first = Values[0] as HoconObject;
                if (first != null)
                {
                    if (first.Items.Count == 0)
                        return true;
                }
                return false;
            }
        }

        /// <summary>
        /// The list of elements inside this HOCON value
        /// </summary>
        public List<IHoconElement> Values { get; private set; }

        /// <summary>
        /// Wraps this <see cref="HoconValue"/> into a new <see cref="Config"/> object at the specified key.
        /// </summary>
        /// <param name="key">The key designated to be the new root element.</param>
        /// <returns>A <see cref="Config"/> with the given key as the root element.</returns>
        public Config AtKey(string key)
        {
            var o = new HoconObject();
            o.GetOrCreateKey(key);
            o.Items[key] = this;
            var r = new HoconValue();
            r.Values.Add(o);
            return new Config(new HoconRoot(r));
        }

        /// <summary>
        /// Retrieves the <see cref="HoconObject"/> from this <see cref="HoconValue"/>.
        /// </summary>
        /// <returns>The <see cref="HoconObject"/> that represents this <see cref="HoconValue"/>.</returns>
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
        /// Determines if this <see cref="HoconValue"/> is a <see cref="HoconObject"/>.
        /// </summary>
        /// <returns><c>true</c> if this value is a <see cref="HoconObject"/>, <c>false</c> otherwise.</returns>
        public bool IsObject()
        {
            return GetObject() != null;
        }

        /// <summary>
        /// Adds the given element to the list of elements inside this <see cref="HoconValue"/>.
        /// </summary>
        /// <param name="value">The element to add to the list.</param>
        public void AppendValue(IHoconElement value)
        {
            Values.Add(value);
        }

        /// <summary>
        /// Clears the list of elements inside this <see cref="HoconValue"/>.
        /// </summary>
        public void Clear()
        {
            Values.Clear();
        }

        /// <summary>
        /// Creates a fresh list of elements inside this <see cref="HoconValue"/>
        /// and adds the given value to the list.
        /// </summary>
        /// <param name="value">The element to add to the list.</param>
        public void NewValue(IHoconElement value)
        {
            Values.Clear();
            Values.Add(value);
        }

        /// <summary>
        /// Determines whether all the elements inside this <see cref="HoconValue"/>
        /// are a string.
        /// </summary>
        /// <returns>
        ///   <c>true</c>if all elements inside this <see cref="HoconValue"/> are a string; otherwise <c>false</c>.
        /// </returns>
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

        /// <summary>
        /// Retrieves the child object located at the given key.
        /// </summary>
        /// <param name="key">The key used to retrieve the child object.</param>
        /// <returns>The element at the given key.</returns>
        public HoconValue GetChildObject(string key)
        {
            return GetObject().GetKey(key);
        }

        /// <summary>
        /// Retrieves the boolean value from this <see cref="HoconValue"/>.
        /// </summary>
        /// <returns>The boolean value represented by this <see cref="HoconValue"/>.</returns>
        /// <exception cref="System.NotSupportedException">
        /// This exception occurs when the <see cref="HoconValue"/> doesn't
        /// conform to the standard boolean values: "on", "off", "true", or "false"
        /// </exception>
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

        /// <summary>
        /// Retrieves the string value from this <see cref="HoconValue"/>.
        /// </summary>
        /// <returns>The string value represented by this <see cref="HoconValue"/>.</returns>
        public string GetString()
        {
            if (IsString())
            {
                return ConcatString();
            }
            return null; //TODO: throw exception?
        }

        /// <summary>
        /// Retrieves the decimal value from this <see cref="HoconValue"/>.
        /// </summary>
        /// <returns>The decimal value represented by this <see cref="HoconValue"/>.</returns>
        public decimal GetDecimal()
        {
            return decimal.Parse(GetString(), NumberFormatInfo.InvariantInfo);
        }

        /// <summary>
        /// Retrieves the float value from this <see cref="HoconValue"/>.
        /// </summary>
        /// <returns>The float value represented by this <see cref="HoconValue"/>.</returns>
        public float GetFloat()
        {
            return float.Parse(GetString(), NumberFormatInfo.InvariantInfo);
        }

        /// <summary>
        /// Retrieves the double value from this <see cref="HoconValue"/>.
        /// </summary>
        /// <returns>The double value represented by this <see cref="HoconValue"/>.</returns>
        public double GetDouble()
        {
            return double.Parse(GetString(), NumberFormatInfo.InvariantInfo);
        }

        /// <summary>
        /// Retrieves the long value from this <see cref="HoconValue"/>.
        /// </summary>
        /// <returns>The long value represented by this <see cref="HoconValue"/>.</returns>
        public long GetLong()
        {
            return long.Parse(GetString(), NumberFormatInfo.InvariantInfo);
        }

        /// <summary>
        /// Retrieves the integer value from this <see cref="HoconValue"/>.
        /// </summary>
        /// <returns>The integer value represented by this <see cref="HoconValue"/>.</returns>
        public int GetInt()
        {
            return int.Parse(GetString(), NumberFormatInfo.InvariantInfo);
        }

        /// <summary>
        /// Retrieves the byte value from this <see cref="HoconValue"/>.
        /// </summary>
        /// <returns>The byte value represented by this <see cref="HoconValue"/>.</returns>
        public byte GetByte()
        {
            return byte.Parse(GetString(), NumberFormatInfo.InvariantInfo);
        }

        /// <summary>
        /// Retrieves a list of byte values from this <see cref="HoconValue"/>.
        /// </summary>
        /// <returns>A list of byte values represented by this <see cref="HoconValue"/>.</returns>
        public IList<byte> GetByteList()
        {
            return GetArray().Select(v => v.GetByte()).ToList();
        }

        /// <summary>
        /// Retrieves a list of integer values from this <see cref="HoconValue"/>.
        /// </summary>
        /// <returns>A list of integer values represented by this <see cref="HoconValue"/>.</returns>
        public IList<int> GetIntList()
        {
            return GetArray().Select(v => v.GetInt()).ToList();
        }

        /// <summary>
        /// Retrieves a list of long values from this <see cref="HoconValue"/>.
        /// </summary>
        /// <returns>A list of long values represented by this <see cref="HoconValue"/>.</returns>
        public IList<long> GetLongList()
        {
            return GetArray().Select(v => v.GetLong()).ToList();
        }

        /// <summary>
        /// Retrieves a list of boolean values from this <see cref="HoconValue"/>.
        /// </summary>
        /// <returns>A list of boolean values represented by this <see cref="HoconValue"/>.</returns>
        public IList<bool> GetBooleanList()
        {
            return GetArray().Select(v => v.GetBoolean()).ToList();
        }

        /// <summary>
        /// Retrieves a list of float values from this <see cref="HoconValue"/>.
        /// </summary>
        /// <returns>A list of float values represented by this <see cref="HoconValue"/>.</returns>
        public IList<float> GetFloatList()
        {
            return GetArray().Select(v => v.GetFloat()).ToList();
        }

        /// <summary>
        /// Retrieves a list of double values from this <see cref="HoconValue"/>.
        /// </summary>
        /// <returns>A list of double values represented by this <see cref="HoconValue"/>.</returns>
        public IList<double> GetDoubleList()
        {
            return GetArray().Select(v => v.GetDouble()).ToList();
        }

        /// <summary>
        /// Retrieves a list of decimal values from this <see cref="HoconValue"/>.
        /// </summary>
        /// <returns>A list of decimal values represented by this <see cref="HoconValue"/>.</returns>
        public IList<decimal> GetDecimalList()
        {
            return GetArray().Select(v => v.GetDecimal()).ToList();
        }

        /// <summary>
        /// Retrieves a list of string values from this <see cref="HoconValue"/>.
        /// </summary>
        /// <returns>A list of string values represented by this <see cref="HoconValue"/>.</returns>
        public IList<string> GetStringList()
        {
            return GetArray().Select(v => v.GetString()).ToList();
        }

        /// <summary>
        /// Retrieves a list of values from this <see cref="HoconValue"/>.
        /// </summary>
        /// <returns>A list of values represented by this <see cref="HoconValue"/>.</returns>
        public IList<HoconValue> GetArray()
        {
            IEnumerable<HoconValue> x = from arr in Values
                where arr.IsArray()
                from e in arr.GetArray()
                select e;

            return x.ToList();
        }

        /// <summary>
        /// Determines whether this <see cref="HoconValue"/> is an array.
        /// </summary>
        /// <returns>
        ///   <c>true</c> if this <see cref="HoconValue"/> is an array; otherwise <c>false</c>.
        /// </returns>
        public bool IsArray()
        {
            return GetArray() != null;
        }


        [Obsolete("Use GetTimeSpan instead")]
        public TimeSpan GetMillisDuration(bool allowInfinite = true)
        {
            return GetTimeSpan(allowInfinite);
        }

        /// <summary>
        /// Retrieves the time span value from this <see cref="HoconValue"/>.
        /// </summary>
        /// <param name="allowInfinite">A flag used to set inifinite durations.</param>
        /// <returns>The time span value represented by this <see cref="HoconValue"/>.</returns>
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

        /// <summary>
        /// Retrieves the long value, optionally suffixed with a 'b', from this <see cref="HoconValue"/>.
        /// </summary>
        /// <returns>The long value represented by this <see cref="HoconValue"/>.</returns>
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

        /// <summary>
        /// Returns a HOCON string representation of this <see cref="HoconValue"/>.
        /// </summary>
        /// <returns>A HOCON string representation of this <see cref="HoconValue"/>.</returns>
        public override string ToString()
        {
            return ToString(0);
        }

        /// <summary>
        /// Returns a HOCON string representation of this <see cref="HoconValue"/>.
        /// </summary>
        /// <param name="indent">The number of spaces to indent the string.</param>
        /// <returns>A HOCON string representation of this <see cref="HoconValue"/>.</returns>
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

