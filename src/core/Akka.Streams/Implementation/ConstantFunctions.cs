using System;

namespace Akka.Streams.Implementation
{
    internal static class ConstantFunctions
    {
        public static Func<T, long> OneLong<T>() => _ => 1L;
    }
}
