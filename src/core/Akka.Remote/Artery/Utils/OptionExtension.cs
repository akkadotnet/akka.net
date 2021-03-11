using System;
using System.Collections.Generic;
using System.Text;
using Akka.Util;

namespace Akka.Remote.Artery.Utils
{
    internal static class OptionExtension
    {
        public static bool Contains<T>(this Option<T> option, T value)
            => option.HasValue && option.Value.Equals(value);
    }
}
