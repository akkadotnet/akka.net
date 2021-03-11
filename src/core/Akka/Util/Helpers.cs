using System;
using System.Collections.Generic;
using System.Text;

namespace Akka.Util
{
    internal static class Helpers
    {
        public static T Requiring<T>(this T obj, Func<T, bool> cond, string msg)
        {
            if (!cond(obj))
                throw new ArgumentException($"Requirement failed: {msg}");
            return obj;
        }
    }
}
