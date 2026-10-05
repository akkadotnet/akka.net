//-----------------------------------------------------------------------
// <copyright file="Construct.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Diagnostics.CodeAnalysis;

namespace Akka.Streams
{
    /// <summary>
    /// Provides reflection-based helpers for constructing closed generic types.
    /// </summary>
    public static class Construct
    {
        /// <summary>
        /// Closes a generic type definition with one type argument and invokes a constructor on the resulting type.
        /// </summary>
        /// <param name="genericType">The open generic type definition to close.</param>
        /// <param name="genericParam">The type argument used to close <paramref name="genericType"/>.</param>
        /// <param name="constructorArgs">Arguments passed to the constructor selected by <see cref="Activator.CreateInstance(Type, object[])"/>.</param>
        /// <returns>An instance of the constructed closed generic type.</returns>
        [Obsolete("Builds a closed generic type at runtime, which is not supported under Native AOT; will be removed in 1.7. Obsolete since v1.6.0")]
        [RequiresDynamicCode("Builds a closed generic type at runtime with MakeGenericType; Native AOT may not have compiled it.")]
        [RequiresUnreferencedCode("Constructs a closed generic type through Activator; the trimmer cannot see which constructor is needed.")]
        public static object Instantiate(this Type genericType, Type genericParam, params object[] constructorArgs)
        {
            var gen = genericType.MakeGenericType(genericParam);
            return Activator.CreateInstance(gen, constructorArgs);
        }

        /// <summary>
        /// Closes a generic type definition with the supplied type arguments and invokes a constructor on the resulting type.
        /// </summary>
        /// <param name="genericType">The open generic type definition to close.</param>
        /// <param name="genericParams">The type arguments used to close <paramref name="genericType"/>.</param>
        /// <param name="constructorArgs">Arguments passed to the constructor selected by <see cref="Activator.CreateInstance(Type, object[])"/>.</param>
        /// <returns>An instance of the constructed closed generic type.</returns>
        [Obsolete("Builds a closed generic type at runtime, which is not supported under Native AOT; will be removed in 1.7. Obsolete since v1.6.0")]
        [RequiresDynamicCode("Builds a closed generic type at runtime with MakeGenericType; Native AOT may not have compiled it.")]
        [RequiresUnreferencedCode("Constructs a closed generic type through Activator; the trimmer cannot see which constructor is needed.")]
        public static object Instantiate(this Type genericType, Type[] genericParams, params object[] constructorArgs)
        {
            var gen = genericType.MakeGenericType(genericParams);
            return Activator.CreateInstance(gen, constructorArgs);
        }
    }
}
