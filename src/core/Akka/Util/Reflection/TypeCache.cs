//-----------------------------------------------------------------------
// <copyright file="TypeCache.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Akka.Util.Reflection
{
    /// <summary>
    /// TBD
    /// </summary>
    public static class TypeCache
    {
        private static readonly ConcurrentDictionary<string, Type> TypeMap = new(new[] { new KeyValuePair<string, Type>("null", null),  });

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
        [RequiresUnreferencedCode("Resolves typeName by reflection when it is not already cached. The trimmer cannot tell which type that is, so it may have been trimmed away. Guard with TryGetCached plus AkkaFeatures.IsDynamicTypeLoadingSupported instead of calling this directly on a cache miss.")]
        public static Type GetType(string typeName)
        {
            return TypeMap.GetOrAdd(typeName, GetTypeInternal);
        }

        /// <summary>
        /// Looks up <paramref name="typeName"/> without falling back to reflection, so a caller can check for
        /// an already-cached type before deciding whether reflection is even allowed.
        /// </summary>
        internal static bool TryGetCached(string typeName, out Type type) => TypeMap.TryGetValue(typeName, out type);

        /// <summary>INTERNAL API, for tests: forget every resolved type, so a test can't pass on a cache another test warmed.</summary>
        internal static void Clear()
        {
            TypeMap.Clear();
            TypeMap["null"] = null;
        }

        [RequiresUnreferencedCode("Calls Type.GetType(string, bool). The trimmer cannot tell which type that is, so it may have been trimmed away.")]
        private static Type GetTypeInternal(string typeName)
        {
            return Type.GetType(typeName, true);
        }
    }
}
