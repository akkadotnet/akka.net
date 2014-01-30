using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Pigeon.Configuration.Hocon
{
    public class HoconArray : List<object>
    {
        public override string ToString()
        {
            return "[" + string.Join(",", this) + "]";
        }
    }
}
