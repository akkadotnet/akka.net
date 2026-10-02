//-----------------------------------------------------------------------
// <copyright file="MixedBindingPair.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.TestKit;
using FluentAssertions;

namespace Akka.Serialization
{
    /// <summary>Which way a message travels between the two nodes of a <see cref="MixedBindingPair"/>.</summary>
    public enum MixedBindingDirection
    {
        /// <summary>The node bound to V2 writes; the node pinned to legacy reads.</summary>
        V2ToLegacy,

        /// <summary>The node pinned to legacy writes; the node bound to V2 reads.</summary>
        LegacyToV2
    }

    /// <summary>What crossed the wire in one <see cref="MixedBindingPair.Send"/>.</summary>
    public sealed class MixedBindingExchange
    {
        internal MixedBindingExchange(
            MixedBindingDirection direction, Serializer writer, string manifest, byte[] bytes, object received)
        {
            Direction = direction;
            Writer = writer;
            Manifest = manifest;
            Bytes = bytes;
            Received = received;
        }

        /// <summary>The way the message travelled.</summary>
        public MixedBindingDirection Direction { get; }

        /// <summary>The serializer the writing node picked for the message.</summary>
        public Serializer Writer { get; }

        /// <summary>The manifest the writer gave the message.</summary>
        public string Manifest { get; }

        /// <summary>The bytes on the wire.</summary>
        public byte[] Bytes { get; }

        /// <summary>The message the reading node made of them.</summary>
        public object Received { get; }
    }

    /// <summary>
    /// Two actor systems that disagree about how a subsystem's messages are serialized, as the nodes of a cluster
    /// do mid-way through a rolling upgrade: one binds the types to the V2 serializer, the other pins them to the
    /// legacy one. A message written by either node must be readable by the other.
    /// </summary>
    /// <remarks>
    /// The helper takes the config of each side and knows nothing about how a port turns V2 on. Before the global
    /// switch exists, pass explicit bindings (<see cref="BindTo"/>): the V2 side binds the corpus types to the V2 alias,
    /// the legacy side to the legacy alias. Once the switch exists, pass the switch's own config to the V2 side and
    /// <see cref="BindTo"/> to the legacy side. Nodes are in-process and exchange bytes directly through their
    /// <see cref="Serialization"/> extensions; this needs no remoting, and the same configs can be used on remote nodes.
    /// </remarks>
    public sealed class MixedBindingPair : IAsyncDisposable
    {
        private MixedBindingPair(ActorSystem v2Node, ActorSystem legacyNode)
        {
            V2Node = v2Node;
            LegacyNode = legacyNode;
        }

        /// <summary>The node whose config turns V2 on.</summary>
        public ActorSystem V2Node { get; }

        /// <summary>The node whose config pins the types to legacy.</summary>
        public ActorSystem LegacyNode { get; }

        /// <summary>
        /// Starts the two nodes. Each config falls back to <see cref="AkkaSpec.AkkaSpecConfig"/>, then to the defaults.
        /// </summary>
        /// <param name="name">A system name prefix; the nodes are named <c>name-v2</c> and <c>name-legacy</c>.</param>
        /// <param name="v2Side">Config of the node bound to V2.</param>
        /// <param name="legacySide">Config of the node pinned to legacy.</param>
        /// <param name="serialization">
        /// A <see cref="SerializationSetup"/> both nodes start with, for a port whose serializers aren't in a module's
        /// table. Leave null when they are.
        /// </param>
        public static MixedBindingPair Create(
            string name, Config v2Side, Config legacySide, SerializationSetup? serialization = null)
            => Create(name, Setup(v2Side, serialization), Setup(legacySide, serialization));

        /// <summary>
        /// Starts the two nodes from setups, for ports that register serializers through a <see cref="SerializationSetup"/>.
        /// A setup's own bindings (<see cref="SerializerDetails.UseFor"/>) win over HOCON bindings, so give the
        /// setup rows no bound types and bind through the config.
        /// </summary>
        public static MixedBindingPair Create(string name, ActorSystemSetup v2Side, ActorSystemSetup legacySide)
        {
            var v2 = ActorSystem.Create(name + "-v2", v2Side);
            try
            {
                return new MixedBindingPair(v2, ActorSystem.Create(name + "-legacy", legacySide));
            }
            catch
            {
                _ = v2.Terminate();
                throw;
            }
        }

        /// <summary>
        /// HOCON that binds <paramref name="types"/> to the serializer registered as <paramref name="alias"/>, in
        /// <c>akka.actor.serialization-bindings</c>. Pass closed generic types through your own config instead: their
        /// names don't spell as simple HOCON keys.
        /// </summary>
        public static Config BindTo(string alias, params Type[] types)
        {
            var rows = types.Select(t => $@"""{t.FullName}, {t.Assembly.GetName().Name}"" = {alias}");
            return ConfigurationFactory.ParseString($"akka.actor.serialization-bindings {{\n{string.Join("\n", rows)}\n}}");
        }

        /// <summary>
        /// Writes <paramref name="message"/> on one node and reads it on the other, by serializer id and manifest -
        /// the way a remote node would. The writer is whatever that node's own bindings pick.
        /// </summary>
        public MixedBindingExchange Send(MixedBindingDirection direction, object message)
        {
            var (writerNode, readerNode) = direction == MixedBindingDirection.V2ToLegacy
                ? (V2Node, LegacyNode)
                : (LegacyNode, V2Node);

            var serializer = writerNode.Serialization.FindSerializerFor(message);
            var manifest = Serialization.ManifestFor(serializer, message);
            var bytes = serializer.ToBinary(message);
            var received = readerNode.Serialization.Deserialize(bytes, serializer.Identifier, manifest);
            return new MixedBindingExchange(direction, serializer, manifest, bytes, received);
        }

        /// <summary>
        /// Sends every message both ways and checks: each writer is the serializer its node's config says (the V2
        /// node writes <paramref name="expectedV2Id"/>, the legacy node writes <paramref name="expectedLegacyId"/>),
        /// the manifests agree, and the reader gets back what was sent.
        /// </summary>
        /// <param name="cases">The messages and their legacy manifests; the same corpus <see cref="V2Port"/> takes.</param>
        /// <param name="expectedV2Id">The V2 serializer id.</param>
        /// <param name="expectedLegacyId">The legacy serializer id.</param>
        /// <returns>Every exchange, in the order sent.</returns>
        public IReadOnlyList<MixedBindingExchange> AssertRoundTripsBothWays(
            IEnumerable<V2PortCase> cases, int expectedV2Id, int expectedLegacyId)
        {
            var exchanges = new List<MixedBindingExchange>();
            foreach (var c in cases)
            {
                var toLegacy = Send(MixedBindingDirection.V2ToLegacy, c.Message);
                toLegacy.Writer.Identifier.Should().Be(expectedV2Id, WhyNot($"the V2 node should write {c} with the V2 serializer"));
                toLegacy.Manifest.Should().Be(c.Manifest, WhyNot($"the V2 node's manifest for {c}"));
                V2PortSpecs.AssertEquivalent(c, toLegacy.Received, "V2 node to legacy node");

                var toV2 = Send(MixedBindingDirection.LegacyToV2, c.Message);
                toV2.Writer.Identifier.Should().Be(expectedLegacyId, WhyNot($"the legacy node should write {c} with the legacy serializer"));
                toV2.Manifest.Should().Be(c.Manifest, WhyNot($"the legacy node's manifest for {c}"));
                V2PortSpecs.AssertEquivalent(c, toV2.Received, "legacy node to V2 node");

                exchanges.Add(toLegacy);
                exchanges.Add(toV2);
            }

            return exchanges;
        }

        /// <summary>Terminates both nodes.</summary>
        public async ValueTask DisposeAsync()
        {
            await Task.WhenAll(V2Node.Terminate(), LegacyNode.Terminate()).ConfigureAwait(false);
        }

        private static ActorSystemSetup Setup(Config config, SerializationSetup? serialization)
        {
            var setup = ActorSystemSetup.Create(BootstrapSetup.Create().WithConfig(config.WithFallback(AkkaSpec.AkkaSpecConfig)));
            return serialization is null ? setup : setup.And(serialization);
        }

        // see V2PortSpecs: FluentAssertions formats "because" with string.Format
        private static string WhyNot(string reason) => reason.Replace("{", "{{").Replace("}", "}}");
    }
}
