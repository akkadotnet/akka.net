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
        private List<object> values = new List<object>();
        public void AppendValue(object value)
        {
            this.values.Add(value);
        }
        public void NewValue(object value)
        {
            this.values.Clear();
            this.values.Add(value);
        }

        public object GetValue()
        {
            if (values.Any() && values.All(v => v is string))
            {
                var concat = string.Join("", values).Trim();

                if (concat == "null")
                    return null;

                return concat;
            }
            return values.FirstOrDefault();
        }

        public HoconObject GetObject()
        {
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
            var v = GetValue().ToString();
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
            return GetValue() as string;
        }

        public decimal GetDecimal()
        {
            return decimal.Parse(GetValue().ToString(), NumberFormatInfo.InvariantInfo);
        }

        public float GetFloat()
        {
            return float.Parse(GetValue().ToString(), NumberFormatInfo.InvariantInfo);
        }

        public double GetDouble()
        {
            return double.Parse(GetValue().ToString(), NumberFormatInfo.InvariantInfo);
        }

        public long GetLong()
        {
            return long.Parse(GetValue().ToString(), NumberFormatInfo.InvariantInfo);
        }

        public int GetInt()
        {
            return int.Parse(GetValue().ToString(), NumberFormatInfo.InvariantInfo);
        }

        public byte GetByte()
        {
            return byte.Parse(GetValue().ToString(), NumberFormatInfo.InvariantInfo);
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
            var x = from arr in this.values.OfType<HoconArray>()
                    from e in arr
                    select e;

            return x.ToList();
        }

        public bool IsArray()
        {
            return this.GetArray() != null;
        }
    }
}
