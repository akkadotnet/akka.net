//-----------------------------------------------------------------------
// <copyright file="TypeExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.RegularExpressions;
using Akka.Annotations;

namespace Akka.Util
{
    /// <summary>
    /// Class TypeExtensions.
    /// </summary>
    public static class TypeExtensions
    {
        /// <summary>
        /// Returns true if <paramref name="type" /> implements/inherits <typeparamref name="T" />.
        /// <example><para>typeof(object[]).Implements&lt;IEnumerable&gt;() --&gt; true</para></example>
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="type">The type.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
        public static bool Implements<T>(this Type type)
        {
            return Implements(type, typeof(T));
        }

        /// <summary>
        /// Returns true if <paramref name="type" /> implements/inherits <paramref name="moreGeneralType" />.
        /// <example><para>typeof(object[]).Implements(typeof(IEnumerable)) --&gt; true</para></example>
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="moreGeneralType">Type of the more general.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
        public static bool Implements(this Type type, Type moreGeneralType)
        {
            return moreGeneralType.IsAssignableFrom(type);
        }

        private static readonly ConcurrentDictionary<Type, string> ShortenedTypeNames = new ConcurrentDictionary<Type, string>();

        private static readonly Regex cleanAssemblyVersionRegex = new Regex(
            @"(, Version=([\d\.]+))?(, Culture=[^,\] \t]+)?(, PublicKeyToken=(null|[\da-f]+))?",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>
        /// INTERNAL API
        /// Utility to be used by implementers to create a manifest from the type.
        /// The manifest is used to look up the type on deserialization.
        /// </summary>
        /// <param name="type">TBD</param>
        /// <returns>Returns the type qualified name including namespace and assembly, but not assembly version.</returns>
        [InternalApi]
        public static string TypeQualifiedName(this Type type)
        {
            string shortened;
            if (ShortenedTypeNames.TryGetValue(type, out shortened))
            {
                return shortened;
            }

            shortened = cleanAssemblyVersionRegex.Replace(type.AssemblyQualifiedName, string.Empty);
            ShortenedTypeNames.TryAdd(type, shortened);
            
            return shortened;
        }
    }
}

