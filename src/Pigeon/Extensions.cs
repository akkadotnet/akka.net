using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon
{
    public static class Extensions
    {
        public static T AsInstanceOf<T>(this object self)
        {
            return (T)self;
        }

        public static T[] Add<T>(this T[] self,T add)
        {
            return self.Union(Enumerable.Repeat(add, 1)).ToArray();
        }
    }
}
