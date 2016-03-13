using System;
using System.Collections.Concurrent;

namespace Akka.Util.Reflection
{
    public static class TypeCache
    {
        private static readonly ConcurrentDictionary<string, Type> TypeMap = new ConcurrentDictionary<string, Type>();

        public static Type GetType(string typeName)
        {
            return TypeMap.GetOrAdd(typeName, item => Type.GetType(item));
        }
    }
}