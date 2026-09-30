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

#nullable enable

        /// <summary>
        /// INTERNAL API
        ///
        /// Splits a type name into the type name and, if present, the assembly name - the same split
        /// <see cref="Type.GetType(string)"/> makes at the first comma outside a generic argument list.
        /// Runs <see cref="StripAssemblyIdentity"/> first, so an assembly-qualified name reduces to its
        /// bare <c>Ns.T, Assembly</c> form before the split.
        /// </summary>
        /// <param name="typeName">The type name to split.</param>
        /// <param name="name">
        /// The type name. Trimmed when <paramref name="typeName"/> carries an assembly name, since it sits
        /// before the separating comma; untouched otherwise, since a HOCON value already arrives pre-trimmed
        /// and a HOCON key does not. Empty when this method returns <c>false</c>.
        /// </param>
        /// <param name="assembly">The trimmed assembly name, or <c>null</c> when <paramref name="typeName"/> carries none.</param>
        /// <returns><c>false</c> when <paramref name="typeName"/> is null, empty or whitespace; <c>true</c> otherwise.</returns>
        internal static bool TrySplitTypeName(string? typeName, out string name, out string? assembly)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                name = string.Empty;
                assembly = null;
                return false;
            }

            // No separator: the whole (stripped) string is the name, untouched - a HOCON value arrives
            // pre-trimmed by the parser, and a HOCON key does not, so trimming it here would let two
            // differently-padded keys collide on the same built-in type once both hit the table.
            var stripped = StripAssemblyIdentity(typeName);
            var separator = IndexOfAssemblySeparator(stripped);
            if (separator < 0)
            {
                name = stripped;
                assembly = null;
            }
            else
            {
                name = stripped.Substring(0, separator).TrimEnd();
                assembly = stripped.Substring(separator + 1).Trim();
            }

            return true;
        }

        /// <summary>
        /// INTERNAL API
        ///
        /// Returns the bare type name when <paramref name="typeName"/> names an Akka.NET type - no assembly
        /// at all, or the <c>Akka</c> assembly in any casing - and <c>null</c> otherwise. This is what every
        /// <c>BuiltIn*</c> table lookup normalizes a HOCON value to before probing its single key per type.
        /// </summary>
        /// <param name="typeName">The type name read out of HOCON.</param>
        internal static string? ToBuiltInAkkaTypeName(string? typeName)
        {
            if (!TrySplitTypeName(typeName, out var name, out var assembly))
                return null;

            return assembly is null || string.Equals(assembly, "Akka", StringComparison.OrdinalIgnoreCase)
                ? name
                : null;
        }

        /// <summary>
        /// INTERNAL API
        ///
        /// The index of the comma that separates a type name from its assembly name - the first comma at
        /// bracket depth zero, so the commas inside a generic type's argument list do not count.
        /// </summary>
        /// <param name="typeName">The (already assembly-identity-stripped) type name to scan.</param>
        /// <returns>The index, or <c>-1</c> when <paramref name="typeName"/> carries no assembly name.</returns>
        internal static int IndexOfAssemblySeparator(string typeName)
        {
            var depth = 0;
            for (var i = 0; i < typeName.Length; i++)
            {
                switch (typeName[i])
                {
                    case '[':
                        depth++;
                        break;
                    case ']':
                        depth--;
                        break;
                    case ',' when depth == 0:
                        return i;
                }
            }

            return -1;
        }

#nullable restore

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

