using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Akka.Configuration.Hocon
{
    public class HoconValue
    {
        private readonly List<IHoconElement> values = new List<IHoconElement>();

        public bool IsEmpty
        {
            get { return values.Count == 0; }
        }

        public void AppendValue(IHoconElement value)
        {
            values.Add(value);
        }

        public void Clear()
        {
            values.Clear();
        }

        public void NewValue(IHoconElement value)
        {
            values.Clear();
            values.Add(value);
        }

        public bool IsString()
        {
            return values.Any() && values.All(v => v.IsString());
        }

        private string ConcatString()
        {
            string concat = string.Join("", values.Select(l => l.GetString())).Trim();

            if (concat == "null")
                return null;

            return concat;
        }

        public HoconObject GetObject()
        {
            //TODO: merge objects?
            var o = values.FirstOrDefault() as HoconObject;
            return o;
        }

        public HoconValue GetChildObject(string key)
        {
            return GetObject().GetKey(key);
        }

        public bool IsObject()
        {
            return GetObject() != null;
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
            IEnumerable<HoconValue> x = from arr in values
                where arr.IsArray()
                from e in arr.GetArray()
                select e;

            return x.ToList();
        }

        public bool IsArray()
        {
            return GetArray() != null;
        }

        //TODO: implement this
        public TimeSpan GetMillisDuration()
        {
            string res = GetString();
            if (res.EndsWith("s"))
            {
                string v = res.Substring(0, res.Length - 1);
                return TimeSpan.FromSeconds(double.Parse(v));
            }

            return TimeSpan.FromSeconds(double.Parse(res));
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
                return string.Format("[{0}]", string.Join(",", GetArray().Select(e => e.ToString())));
            }
            return "aa";
        }

        private string QuoteIfNeeded(string text)
        {
            if (text.ToCharArray().Intersect(" \t".ToCharArray()).Any())
            {
                return "\"" + text + "\"";
            }
            return text;
        }
    }
}