using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Pigeon.Configuration.Hocon
{
    public class HoconArray : List<HoconValue> , IHoconElement
    {
        public override string ToString()
        {
            return "[" + string.Join(",", this) + "]";
        }

        public bool IsString()
        {
            return false;
        }
        public string GetString()
        {
            throw new NotImplementedException();
        }
        public bool IsArray()
        {
            return true;
        }

        public IList<HoconValue> GetArray()
        {
            return this;
        }
    }
}
