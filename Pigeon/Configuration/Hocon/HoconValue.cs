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

                //if (concat == "true")
                //    return true;

                //if (concat == "false")
                //    return false;

                //long tmpLong;
                //if (long.TryParse(concat, NumberStyles.Integer, NumberFormatInfo.InvariantInfo, out tmpLong))
                //{
                //    return tmpLong;
                //}

                //double tmpDouble;
                //if (double.TryParse(concat,NumberStyles.Float,NumberFormatInfo.InvariantInfo,out tmpDouble))
                //{
                //    return tmpDouble;
                //}

                return concat;
            }
            return values.FirstOrDefault();
        }

        public HoconObject GetObject()
        {
            var o = values.FirstOrDefault() as HoconObject;
            return o;
        }

        public HoconObject GetChildObject(string key)
        {
            return GetObject().Children.FirstOrDefault(c => c.Key == key);
        }

        internal bool IsObject()
        {
            return GetObject() != null;
        }
    }
}
