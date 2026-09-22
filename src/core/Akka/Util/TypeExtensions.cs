//-----------------------------------------------------------------------
// <copyright file="TypeExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
    public static partial class TypeExtensions
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

        private static readonly ConcurrentDictionary<Type, string> ShortenedTypeNames = new();

        /// <summary>
        /// Matches the assembly identity components of an assembly-qualified type name - the parts that pin a
        /// particular build of an assembly rather than naming the assembly itself.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Alternation rather than a fixed sequence, so a component is stripped wherever it sits and whether or
        /// not its comma is followed by a space. The value pattern stops at <c>,</c>, <c>[</c> and <c>]</c>, so
        /// the type arguments inside a generic name keep their own brackets while their identity components are
        /// stripped in the same pass.
        /// </para>
        /// <para>
        /// Source-generated rather than <see cref="RegexOptions.Compiled"/>: compiled regexes emit IL at
        /// runtime, which Native AOT cannot do, so under AOT they silently fall back to the interpreter.
        /// </para>
        /// </remarks>
        [GeneratedRegex(
            @",\s*(?:Version|Culture|PublicKeyToken|ProcessorArchitecture|Retargetable|ContentType)\s*=\s*[^,\[\]]*",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
        private static partial Regex AssemblyIdentityRegex();

        /// <summary>
        /// INTERNAL API
        ///
        /// Strips the assembly identity components - <c>Version</c>, <c>Culture</c>, <c>PublicKeyToken</c>,
        /// <c>ProcessorArchitecture</c>, <c>Retargetable</c> and <c>ContentType</c> - out of an
        /// assembly-qualified type name, leaving the type name and the simple assembly name behind.
        /// </summary>
        /// <param name="typeName">A type name, assembly-qualified or not.</param>
        internal static string StripAssemblyIdentity(string typeName)
            => AssemblyIdentityRegex().Replace(typeName, string.Empty);

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
            if (ShortenedTypeNames.TryGetValue(type, out var shortened))
            {
                return shortened;
            }

            shortened = StripAssemblyIdentity(type.AssemblyQualifiedName);
            ShortenedTypeNames.TryAdd(type, shortened);

            return shortened;
        }
    }
}

