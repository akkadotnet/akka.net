using System;
using System.Collections.Generic;

namespace Akka.Configuration.Hocon
{
    public class HoconArray : List<HoconValue>, IHoconElement
    {
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

        public override string ToString()
        {
            return "[" + string.Join(",", this) + "]";
        }
    }
}