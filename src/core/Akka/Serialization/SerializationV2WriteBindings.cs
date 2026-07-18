//-----------------------------------------------------------------------
// <copyright file="SerializationV2WriteBindings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using Akka.Annotations;
using Akka.Configuration;

namespace Akka.Serialization
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// Config contract for the write-side V2 serializer migration flags declared under
    /// <c>akka.actor.serialization.v2</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Subsystems that fork an internal serializer to a new V2 (MessagePack) implementation keep
    /// BOTH serializers registered at all times, each under its own stable serializer id, so every
    /// v1.6+ node can READ either wire format regardless of these flags. The flags flip only the
    /// WRITE side: when a subsystem's effective flag is on, the type-to-serializer bindings that
    /// the subsystem declared under <c>akka.actor.serialization.v2.write-bindings.&lt;subsystem&gt;</c>
    /// are re-pointed from the legacy serializer to the V2 serializer during
    /// <see cref="Serialization"/> construction (ActorSystem startup). There is no per-message
    /// cost: the rebinding is a one-time, exact-key overwrite of the affected
    /// <c>akka.actor.serialization-bindings</c> entries, resolved before the system starts.
    /// </para>
    /// <para>
    /// A subsystem opts in by adding, to its own reference configuration:
    /// <code>
    /// akka.actor.serialization.v2.write-bindings {
    ///   my-subsystem {  # key doubles as the flag name: akka.actor.serialization.v2.my-subsystem
    ///     "My.Subsystem.IMarkerInterface, My.Assembly" = my-subsystem-v2  # serializer config name
    ///   }
    /// }
    /// </code>
    /// The subsystem key must be a plain HOCON key (no dots). Effective flag resolution: an
    /// explicit <c>on</c>/<c>off</c> at <c>akka.actor.serialization.v2.&lt;subsystem&gt;</c> always
    /// wins, in both directions; an unset or empty value inherits the master switch
    /// <c>akka.actor.serialization.v2.enabled</c> (default <c>off</c>). The inheritance is
    /// implemented here rather than via HOCON <c>${...}</c> substitution because Akka.NET resolves
    /// substitutions at parse time within a single document, before user config is merged over the
    /// reference config - a substitution would never observe a user's master-switch override.
    /// </para>
    /// <para>
    /// ROLLING UPGRADE RULE: no flag may be enabled until every node in the cluster - and every
    /// remote peer or store that reads this data - runs v1.6+, so that the V2 serializer ids are
    /// registered everywhere before the first V2 payload is written.
    /// </para>
    /// </remarks>
    [InternalApi]
    internal static class SerializationV2WriteBindings
    {
        /// <summary>
        /// Root config path of the write-side V2 flag surface.
        /// </summary>
        public const string ConfigPath = "akka.actor.serialization.v2";

        /// <summary>
        /// Master write-side switch key, relative to <see cref="ConfigPath"/>.
        /// </summary>
        public const string MasterSwitchKey = "enabled";

        /// <summary>
        /// Write-binding declaration section key, relative to <see cref="ConfigPath"/>.
        /// </summary>
        public const string WriteBindingsKey = "write-bindings";

        /// <summary>
        /// Resolves the effective write-side flag for <paramref name="subsystem"/> against
        /// <paramref name="v2Config"/> (the config at <see cref="ConfigPath"/>): an explicit
        /// per-subsystem <c>on</c>/<c>off</c> always wins; an unset or empty value inherits the
        /// <see cref="MasterSwitchKey"/> master switch.
        /// </summary>
        /// <param name="v2Config">The config at <see cref="ConfigPath"/>.</param>
        /// <param name="subsystem">The subsystem flag key, e.g. <c>reliable-delivery</c>.</param>
        /// <returns><c>true</c> when the subsystem should WRITE with its V2 serializer.</returns>
        public static bool IsEnabledFor(Config v2Config, string subsystem)
        {
            var explicitValue = v2Config.GetString(subsystem, null);
            if (string.IsNullOrWhiteSpace(explicitValue))
                return v2Config.GetBoolean(MasterSwitchKey);

            return v2Config.GetBoolean(subsystem);
        }
    }
}
