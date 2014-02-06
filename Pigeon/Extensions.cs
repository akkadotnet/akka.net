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
    }
}
