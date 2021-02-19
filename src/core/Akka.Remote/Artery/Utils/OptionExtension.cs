using System;
using System.Collections.Generic;
using System.Text;
using Akka.Util;

namespace Akka.Remote.Artery.Utils
{
    public static class OptionExtension
    {
        public static bool Contains<T>(this Util.IOptionVal<T> option, T value)
            => option.HasValue && option.Value.Equals(value);
    }
}
