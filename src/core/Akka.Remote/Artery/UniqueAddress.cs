//-----------------------------------------------------------------------
// <copyright file="UniqueAddress.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using Akka.Actor;
using Akka.Serialization.V2;

namespace Akka.Remote.Artery
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// An <see cref="Actor.Address"/> paired with the 64-bit UID of the <c>ActorSystem</c>
    /// incarnation bound to it, used to distinguish a restarted remote system (same host/port,
    /// different UID) from the system that previously occupied that address.
    ///
    /// This is Artery's own type, distinct from <c>Akka.Cluster.UniqueAddress</c> — <c>Akka.Remote</c>
    /// cannot reference <c>Akka.Cluster</c> (Pekko likewise keeps <c>akka.remote.UniqueAddress</c>
    /// separate from the cluster one). See
    /// <c>openspec/changes/artery-tcp-remoting/design.md</c> ("Handshake + association/UID (gate G2)").
    ///
    /// <para>
    /// <see cref="AkkaSerializableAttribute"/>-annotated (source-generated sourcegen gap fix
    /// #8331 lets an <c>[AkkaSerializable]</c> value type be used as a required/optional nested
    /// field): handled directly by <see cref="ArteryControlMessageSerializer"/>'s generated
    /// nested-object path, composing the built-in <see cref="AddressFormatter"/> escape hatch for
    /// <see cref="Address"/> -- see <c>FieldlessAndStructFieldSpec.GapUniqueAddress</c> for the
    /// validated shape this mirrors.
    /// </para>
    /// </summary>
    /// <param name="Address">The remote (or local) address this UID is bound to.</param>
    /// <param name="Uid">
    /// The 64-bit UID of the <c>ActorSystem</c> incarnation bound to <paramref name="Address"/>.
    /// </param>
    [AkkaSerializable]
    internal readonly record struct UniqueAddress(
        [property: AkkaField(1)] Address Address,
        [property: AkkaField(2)] long Uid)
    {
        /// <inheritdoc/>
        public override string ToString() => $"{Address}#{Uid}";
    }
}
