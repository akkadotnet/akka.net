//-----------------------------------------------------------------------
// <copyright file="RuntimeGenerics.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Akka.Util;

namespace Akka.Streams.Implementation
{
    /// <summary>
    /// INTERNAL API
    /// <para>
    /// The reflective fallback for the types the materializer builds at island boundaries (#8731).
    /// Every port, publisher and subscriber that Akka.Streams creates itself knows its element type
    /// statically and builds these types directly; this path is only reached for an
    /// <see cref="Inlet"/>, <see cref="Outlet"/>, <see cref="IUntypedPublisher"/> or
    /// <see cref="IUntypedSubscriber"/> implemented outside Akka.Streams. Callers must check
    /// <see cref="AkkaFeatures.IsDynamicTypeLoadingSupported"/> first, and throw
    /// <see cref="NotSupported"/> when it is off.
    /// </para>
    /// </summary>
    internal static class RuntimeGenerics
    {
        private const string Reason =
            "Builds a closed generic stream type for an element type discovered at runtime. Only used for port, publisher or subscriber types implemented outside Akka.Streams.";

        [RequiresDynamicCode(Reason)]
        [RequiresUnreferencedCode(Reason)]
        public static object Instantiate(Type genericDefinition, Type elementType, params object[] args)
            => Activator.CreateInstance(genericDefinition.MakeGenericType(elementType), args)!;

        /// <summary>
        /// The first generic argument of <paramref name="instance"/>'s type - the element type of an
        /// <see cref="Inlet{T}"/> or <see cref="Outlet{T}"/>.
        /// </summary>
        public static Type FirstGenericArgument(object instance) => instance.GetType().GetGenericArguments().First();

        /// <summary>
        /// The <c>T</c> of the single <paramref name="openInterface"/> (<c>ISubscriber&lt;&gt;</c> or
        /// <c>IPublisher&lt;&gt;</c>) that <paramref name="instance"/> implements.
        /// </summary>
        [RequiresUnreferencedCode(Reason)]
        public static Type ElementType(object instance, Type openInterface)
            => instance.GetType()
                .GetInterfaces()
                .Single(i => i.IsGenericType && i.GetGenericTypeDefinition() == openInterface)
                .GetGenericArguments()
                .First();

        public static NotSupportedException NotSupported(object instance)
            => new($"Akka.Streams cannot build the boundary types for [{instance.GetType()}] without runtime " +
                   "generic instantiation, which is disabled (the [Akka.DynamicTypeLoading] feature switch " +
                   "is off, as it is under Native AOT). Use the port, publisher and subscriber types that " +
                   "ship with Akka.Streams (Inlet<T>, Outlet<T>, Source.FromPublisher, Sink.FromSubscriber).");
    }
}
