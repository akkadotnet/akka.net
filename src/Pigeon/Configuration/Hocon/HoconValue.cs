using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Configuration.Hocon
{
    public class HoconValue
    {
        private List<IHoconElement> values = new List<IHoconElement>();
        public void AppendValue(IHoconElement value)
        {
            this.values.Add(value);
        }
        public void Clear()
        {
            this.values.Clear();
        }
        public void NewValue(IHoconElement value)
        {
            this.values.Clear();
            this.values.Add(value);
        }

        public bool IsString()
        {
            return values.Any() && values.All(v => v.IsString());
        }

        private string ConcatString()
        {
            var concat = string.Join("", values.Select(l => l.GetString())).Trim();

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
            var v = GetString();
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
            return this.GetArray().Select(v => v.GetByte()).ToList();
        }

        public IList<int> GetIntList()
        {
            return this.GetArray().Select(v => v.GetInt()).ToList();
        }

        public IList<long> GetLongList()
        {
            return this.GetArray().Select(v => v.GetLong()).ToList();
        }

        public IList<bool> GetBooleanList()
        {
            return this.GetArray().Select(v => v.GetBoolean()).ToList();
        }

        public IList<float> GetFloatList()
        {
            return this.GetArray().Select(v => v.GetFloat()).ToList();
        }

        public IList<double> GetDoubleList()
        {
            return this.GetArray().Select(v => v.GetDouble()).ToList();
        }

        public IList<decimal> GetDecimalList()
        {
            return this.GetArray().Select(v => v.GetDecimal()).ToList();
        }

        public IList<string> GetStringList()
        {
            return this.GetArray().Select(v => v.GetString()).ToList();
        }

        public IList<HoconValue> GetArray()
        {
            var x = from arr in this.values
                    where arr.IsArray()
                    from e in arr.GetArray()
                    select e;

            return x.ToList();
        }

        public bool IsArray()
        {
            return this.GetArray() != null;
        }

        //TODO: implement this
        public TimeSpan GetMillisDuration()
        {
            var res = this.GetString();
            if (res.EndsWith("s"))
            {
                var v = res.Substring(0, res.Length - 1);
                return TimeSpan.FromSeconds(double.Parse(v));
            }

            return TimeSpan.FromSeconds(double.Parse(res));
        }

        public override string ToString()
        {
            return ToString(0);
        }

        public virtual string ToString(int indent)
        {
            if (this.IsString())
            {
                var text = QuoteIfNeeded(this.GetString());
                return text;
            }
            if (this.IsObject())
            {
                var i = new string(' ', indent * 2);
                return string.Format("{{\r\n{1}{0}}}",i, this.GetObject().ToString(indent+1));
            }
            if (this.IsArray())
            {
                return string.Format("[{0}]", string.Join(",", this.GetArray().Select(e => e.ToString())));
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
