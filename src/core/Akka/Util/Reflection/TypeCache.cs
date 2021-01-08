//-----------------------------------------------------------------------
// <copyright file="TypeCache.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Akka.Util.Reflection
{
    /// <summary>
    /// TBD
    /// </summary>
    public static class TypeCache
    {
        private static readonly ConcurrentDictionary<string, Type> TypeMap = new ConcurrentDictionary<string, Type>(new[] { new KeyValuePair<string, Type>("null", null),  });

        /// <summary>
        /// Gets the <see cref="T:System.Type"/> with the specified name, performing a case-sensitive search and throw an exception if the type is not found.
        /// </summary>
        /// 
        /// <returns>
        /// The type with the specified name. If the type is not found, an exception is thrown.
        /// </returns>
        /// <param name="typeName">
        /// The assembly-qualified name of the type to get. See <see cref="P:System.Type.AssemblyQualifiedName"/>.
        /// If the type is in Akka.dll or in Mscorlib.dll, it is sufficient to supply the type name qualified by its namespace.
        /// </param>
        /// <returns>TBD</returns>
        public static Type GetType(string typeName)
        {
            return TypeMap.GetOrAdd(typeName, GetTypeInternal);
        }

        private static Type GetTypeInternal(string typeName)
        {
            return Type.GetType(typeName, true);
        }
    }
}
