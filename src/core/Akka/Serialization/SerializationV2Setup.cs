//-----------------------------------------------------------------------
// <copyright file="SerializationV2Setup.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using Akka.Actor;
using Akka.Actor.Setup;

namespace Akka.Serialization
{
    /// <summary>
    /// The Serialization V2 switch, the programmatic equivalent of <c>akka.actor.serialization-v2</c>. When a
    /// <see cref="SerializationV2Setup"/> is present, its <see cref="Enabled"/> value wins over the HOCON setting.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Off by default. When on, each built-in serializer that has a V2 version takes over the bindings of the legacy
    /// serializer it replaces, so this node writes those messages in the V2 format. The legacy serializers stay
    /// registered, so reads never depend on the switch: a node reads both formats whether it is on or off.
    /// Turn it on only once every node runs a version of Akka.NET that can read the V2 formats.
    /// </para>
    /// <para>
    /// A binding set in <c>akka.actor.serialization-bindings</c> or by a <see cref="SerializationSetup"/> always wins
    /// over the switch, so one subsystem can stay on its legacy serializer by binding its types to the legacy alias.
    /// Serializers that became native <see cref="SerializerV2"/> implementations under their old id are not affected.
    /// </para>
    /// <para>
    /// Like any <see cref="Setup"/>, a second <see cref="SerializationV2Setup"/> passed to
    /// <see cref="ActorSystemSetup.And{T}"/> replaces the first.
    /// </para>
    /// </remarks>
    public sealed class SerializationV2Setup : Setup
    {
        private SerializationV2Setup(bool enabled)
        {
            Enabled = enabled;
        }

        /// <summary>
        /// <c>true</c> to move built-in bindings to their V2 serializers; <c>false</c> to keep every binding on its
        /// legacy serializer, whatever <c>akka.actor.serialization-v2</c> says.
        /// </summary>
        public bool Enabled { get; }

        /// <summary>
        /// Creates a <see cref="SerializationV2Setup"/> that turns the Serialization V2 switch on or off.
        /// </summary>
        /// <param name="enabled"><c>true</c> to turn the switch on; <c>false</c> to force it off.</param>
        public static SerializationV2Setup Create(bool enabled) => new(enabled);
    }
}
