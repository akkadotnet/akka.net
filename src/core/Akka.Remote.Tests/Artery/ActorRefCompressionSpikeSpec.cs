//-----------------------------------------------------------------------
// <copyright file="ActorRefCompressionSpikeSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Artery;
using Akka.TestKit;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    public class ActorRefCompressionSpikeSpec : AkkaSpec
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

        public ActorRefCompressionSpikeSpec(ITestOutputHelper output) : base(output) { }

        private CompressedActorRef Entry(IActorRef actorRef) => new(actorRef.Path.ToSerializationFormat(), actorRef);

        private void Observe(ActorRefCompression state, IActorRef actorRef)
        {
            for (var i = 0; i < 64; i++)
                state.Observe(default, new InboundActorRef(Entry(actorRef)), _ => throw new InvalidOperationException("Cached ref was resolved again"));
        }

        [Fact(DisplayName = "Should_publish_readable_table_before_ack_and_keep_lookup_pure_when_advertisement_is_pending")]
        public void Should_publish_before_ack()
        {
            var state = new ActorRefCompression(42, 2);
            Observe(state, TestActor);
            var table = Assert.IsType<ActorRefTable>(state.Advertise());
            Assert.True(state.TryResolve(table.Version, 0, out var entry));
            Assert.Equal(TestActor, entry.Resolved);
            Assert.True(state.HasPendingAdvertisement);
            Assert.Same(table, state.Advertise());
            state.Confirm(0);
            Assert.True(state.HasPendingAdvertisement);
            state.Confirm(table.Version);
            Assert.False(state.HasPendingAdvertisement);
            Assert.Null(state.Advertise());
            Observe(state, TestActor);
            Assert.Null(state.Advertise());
        }

        [Fact(DisplayName = "Should_keep_four_old_tables_pending_then_three_when_confirmed_and_never_wrap_version")]
        public void Should_bound_generations()
        {
            var state = new ActorRefCompression(42, 1);
            for (var version = 1; version <= 127; version++)
            {
                Observe(state, CreateTestProbe().Ref);
                var table = Assert.IsType<ActorRefTable>(state.Advertise());
                Assert.Equal(version, table.Version);
                Assert.True(state.TryResolve(table.Version, 0, out _));
                Assert.False(state.TryResolve(table.Version, 1, out _));
                if (version > 4)
                    Assert.True(state.TryResolve((byte)(version - 4), 0, out _));
                state.Confirm(table.Version);
                if (version > 4)
                    Assert.False(state.TryResolve((byte)(version - 4), 0, out _));
            }
            Observe(state, CreateTestProbe().Ref);
            Assert.Null(state.Advertise());
        }

        [Fact(DisplayName = "Should_reject_stale_or_conflicting_installations_and_accept_duplicate_paths")]
        public void Should_install_monotonically()
        {
            var state = new ActorRefCompression(42, 2);
            var first = new ActorRefTable(1, new[] { Entry(TestActor) });
            var next = new ActorRefTable(2, new[] { Entry(TestActor) });
            Assert.True(state.TryInstall(first));
            Assert.True(state.TryInstall(next));
            Assert.False(state.TryInstall(first));
            Assert.True(state.TryInstall(new ActorRefTable(2, new[] { Entry(TestActor) })));
            Assert.False(state.TryInstall(new ActorRefTable(2, new[] { Entry(CreateTestProbe().Ref) })));
            Assert.Same(next, state.Outbound);
        }

        [Fact(DisplayName = "Should_preserve_same_uid_state_and_reset_tables_when_peer_incarnation_changes")]
        public void Should_scope_tables_to_incarnation()
        {
            var address = new Address("akka", "peer", "127.0.0.1", 1234);
            var association = new Association(address);
            association.CompleteHandshake(new UniqueAddress(address, 42));
            var first = association.GetRefCompression(42, 2);
            association.CompleteHandshake(new UniqueAddress(address, 42));
            Assert.Same(first, association.GetRefCompression(42, 2));
            association.CompleteHandshake(new UniqueAddress(address, 43));
            Assert.Null(association.GetRefCompression(42, 2));
            var next = association.GetRefCompression(43, 2);
            Assert.NotSame(first, next);
            Assert.Null(next!.Outbound);
            association.Quarantine(43);
            Assert.Null(association.GetRefCompression(43, 2));
        }

        [Fact(DisplayName = "Should_reject_wrong_origin_target_incarnation_address_and_version_in_controls")]
        public async Task Should_guard_control_tables()
        {
            var system = ActorSystem.Create("control-guards", ConfigurationFactory.ParseString(
                "akka.remote.artery.advanced.ref-compression-spike.interval = 1 hour").WithFallback(Config(true, 1)));
            try
            {
                var transport = (ArteryRemoting)RARP.For(system).Provider.Transport;
                var address = new Address("akka", "peer", "127.0.0.1", 1234);
                var from = new UniqueAddress(address, 42);
                transport.Registry.CompleteHandshake(address, from);
                var compression = transport.Registry.AssociationFor(address).GetRefCompression(42, 2)!;
                var table = new ActorRefTable(1, new[] { Entry(TestActor) });
                Assert.True(compression.TryInstall(table));
                var subscriber = (IControlMessageSubscriber)transport;
                var valid = new ActorRefTableAdvertisement(from, AddressUidExtension.Uid(system), 2, table.Paths());
                subscriber.ControlMessageReceived(41, valid);
                subscriber.ControlMessageReceived(42, valid with { OriginUid = valid.OriginUid + 1 });
                subscriber.ControlMessageReceived(42, valid with { From = new UniqueAddress(new Address("akka", "wrong", "localhost", 1234), 42) });
                subscriber.ControlMessageReceived(42, valid with { Version = 128 });
                subscriber.ControlMessageReceived(42, valid with { Paths = new[] { "not-an-actor-path" } });
                Assert.Same(table, compression.Outbound);
                Observe(compression, TestActor);
                var pending = compression.Advertise()!;
                subscriber.ControlMessageReceived(41, new ActorRefTableAck(from, pending.Version));
                subscriber.ControlMessageReceived(42, new ActorRefTableAck(new UniqueAddress(address, 41), pending.Version));
                Assert.True(compression.HasPendingAdvertisement);
                subscriber.ControlMessageReceived(42, new ActorRefTableAck(from, pending.Version));
                Assert.False(compression.HasPendingAdvertisement);
            }
            finally { await system.Terminate(); }
        }

        [Fact(DisplayName = "Should_demonstrate_receiver_restart_alias_without_receiver_epoch_in_frame")]
        public void Should_expose_restart_design_gap()
        {
            // Witness of an unresolved production requirement, not proof of restart safety:
            // identical sender UID / generation / index can name different refs after receiver restart.
            var before = new ActorRefCompression(42, 1);
            var after = new ActorRefCompression(42, 1);
            Observe(before, TestActor);
            var other = CreateTestProbe().Ref;
            Observe(after, other);
            var first = before.Advertise()!;
            var second = after.Advertise()!;
            Assert.Equal(first.Version, second.Version);
            Assert.True(before.TryResolve(1, 0, out var original));
            Assert.True(after.TryResolve(1, 0, out var restarted));
            Assert.NotEqual(original.Resolved, restarted.Resolved);
        }

        [Fact(DisplayName = "Should_round_trip_native_byte_and_string_array_advertisements_without_custom_formatters")]
        public void Should_serialize_control_tables()
        {
            var serializer = new ArteryControlMessageSerializer((ExtendedActorSystem)Sys);
            var from = new UniqueAddress(new Address("akka", "peer", "localhost", 1234), 42);
            var ad = new ActorRefTableAdvertisement(from, 43, 127, new[] { Entry(TestActor).Path, "akka://peer/user/a#123" });
            var actual = Assert.IsType<ActorRefTableAdvertisement>(serializer.FromBinary(serializer.ToBinary(ad), serializer.Manifest(ad)));
            Assert.Equal(ad.From, actual.From);
            Assert.Equal(ad.OriginUid, actual.OriginUid);
            Assert.Equal(ad.Version, actual.Version);
            Assert.Equal(ad.Paths, actual.Paths);
            var ack = new ActorRefTableAck(from, 127);
            Assert.Equal(ack, serializer.FromBinary(serializer.ToBinary(ack), serializer.Manifest(ack)));
        }

        [Fact(DisplayName = "Should_exclude_unresolved_and_temporary_refs_from_positive_cache")]
        public void Should_exclude_unsafe_cache_entries()
        {
            Assert.False(ActorRefCompression.IsCacheable("akka://test/user/missing#123", ((ExtendedActorSystem)Sys).Provider.ResolveActorRef("akka://test/user/missing#123")));
            var temporary = ActorRefs.Nobody;
            Assert.False(ActorRefCompression.IsCacheable(temporary.Path.ToSerializationFormat(), temporary));
            Assert.True(ActorRefCompression.IsCacheable(TestActor.Path.ToSerializationFormat(), TestActor));
        }

        [Fact(DisplayName = "Should_admit_bounded_candidates_and_age_observations_after_publication")]
        public void Should_bound_candidates_and_age()
        {
            var state = new ActorRefCompression(42, 2);
            for (var i = 0; i < 16; i++)
                Observe(state, CreateTestProbe().Ref);
            var first = state.Advertise()!;
            Assert.Equal(2, first.Paths().Length);
            state.Confirm(first.Version);
            Assert.Null(state.Advertise());
            var popular = CreateTestProbe().Ref;
            Observe(state, popular);
            var next = state.Advertise()!;
            Assert.Single(next.Paths());
            Assert.True(next.TryCompress(popular, out _));
        }

        [Theory(DisplayName = "Should_preserve_payload_and_manifest_when_actor_ref_tags_are_compressed_or_literal")]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task Should_preserve_payload(bool compress, bool absentSender)
        {
            var system = ActorSystem.Create("codec-spike", Config(false, 1));
            try
            {
                var sender = system.ActorOf(Props.Create<Echo>(), "sender");
                var provider = RARP.For(system).Provider;
                var recipient = Assert.IsType<RemoteActorRef>(provider.ResolveActorRef("akka://other@127.0.0.1:1234/user/target#123"));
                var payload = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
                var envelope = new RefOutboundEnvelope(payload, absentSender ? null : sender, recipient, provider.DefaultAddress);
                var table = new ActorRefTable(7, new[] { Entry(sender), Entry(recipient) });
                using var cold = ArteryEnvelopeCodec.EncodeRefs(((ExtendedActorSystem)system).Serialization, 42, envelope, null, null);
                using var frame = ArteryEnvelopeCodec.EncodeRefs(((ExtendedActorSystem)system).Serialization, 42, envelope, null, compress ? table : null);
                var decoded = ArteryEnvelopeCodec.Decode(new ReadOnlySequence<byte>(frame.WrittenMemory.Slice(4)));
                var literal = ArteryEnvelopeCodec.Decode(new ReadOnlySequence<byte>(cold.WrittenMemory.Slice(4)));
                Assert.Equal(literal.Header.SerializerId, decoded.Header.SerializerId);
                Assert.True(literal.TryGetManifest(out var expectedManifest));
                Assert.True(decoded.TryGetManifest(out var manifest));
                Assert.Equal(expectedManifest, manifest);
                Assert.Equal(literal.Payload.ToArray(), decoded.Payload.ToArray());
                Assert.Equal(compress ? ArteryTagKind.Compressed : ArteryTagKind.Literal, decoded.RecipientKind);
                Assert.Equal(absentSender ? ArteryTagKind.Absent : compress ? ArteryTagKind.Compressed : ArteryTagKind.Literal, decoded.SenderKind);
                Assert.Equal(compress ? 7 : 0, decoded.Header.ActorRefTableVersion);
                if (compress)
                    Assert.True(frame.WrittenCount < cold.WrittenCount);
                using var partial = ArteryEnvelopeCodec.EncodeRefs(((ExtendedActorSystem)system).Serialization, 42,
                    envelope, null, new ActorRefTable(8, new[] { Entry(recipient) }));
                var mixed = ArteryEnvelopeCodec.Decode(new ReadOnlySequence<byte>(partial.WrittenMemory.Slice(4)));
                Assert.Equal(ArteryTagKind.Compressed, mixed.RecipientKind);
                Assert.Equal(absentSender ? ArteryTagKind.Absent : ArteryTagKind.Literal, mixed.SenderKind);
                var controlEnvelope = envelope with { Message = new ArteryHeartbeat() };
                using var control = ArteryEnvelopeCodec.EncodeRefs(((ExtendedActorSystem)system).Serialization, 42, controlEnvelope, null, table);
                var controlDecoded = ArteryEnvelopeCodec.Decode(new ReadOnlySequence<byte>(control.WrittenMemory.Slice(4)));
                Assert.Equal(ArteryTagKind.Literal, controlDecoded.RecipientKind);
                Assert.Equal(absentSender ? ArteryTagKind.Absent : ArteryTagKind.Literal, controlDecoded.SenderKind);
            }
            finally { await system.Terminate(); }
        }

        [Theory(DisplayName = "Should_deliver_ordered_messages_and_sender_identity_over_tcp_with_compression_and_mixed_peers")]
        [InlineData(true, true, 1)]
        [InlineData(true, true, 2)]
        [InlineData(true, false, 1)]
        [InlineData(false, true, 2)]
        public async Task Should_deliver_over_tcp(bool enabledA, bool enabledB, int lanes)
        {
            var a = ActorSystem.Create("compression-A", Config(enabledA, lanes));
            var b = ActorSystem.Create("compression-B", Config(enabledB, lanes));
            try
            {
                var usedA = CreateTestProbe(a);
                var usedB = CreateTestProbe(b);
                a.EventStream.Subscribe(usedA.Ref, typeof(ActorRefCompressionUsed));
                b.EventStream.Subscribe(usedB.Ref, typeof(ActorRefCompressionUsed));
                var local = b.ActorOf(Props.Create<Echo>(), "echo");
                var remote = await a.ActorSelection(local.Path.ToSerializationFormatWithAddress(RARP.For(b).Provider.DefaultAddress)).ResolveOne(Timeout);
                var probe = CreateTestProbe(a);
                for (var batch = 0; batch < 2; batch++)
                {
                    for (var i = 0; i < 256; i++)
                        remote.Tell(batch * 256 + i, probe.Ref);
                    for (var i = 0; i < 256; i++)
                    {
                        await probe.ExpectMsgAsync(batch * 256 + i, Timeout);
                        Assert.Equal(remote, probe.LastSender);
                    }
                    if (batch == 0 && enabledA && enabledB)
                    {
                        await AwaitAssertAsync(() =>
                        {
                            Assert.NotNull(((ArteryRemoting)RARP.For(a).Provider.Transport).RefCompressionTableFor(RARP.For(b).Provider.DefaultAddress));
                            Assert.NotNull(((ArteryRemoting)RARP.For(b).Provider.Transport).RefCompressionTableFor(RARP.For(a).Provider.DefaultAddress));
                        }, Timeout);
                    }
                }
                if (enabledA && enabledB)
                {
                    await usedA.ExpectMsgAsync<ActorRefCompressionUsed>(Timeout);
                    await usedB.ExpectMsgAsync<ActorRefCompressionUsed>(Timeout);
                    var transport = (ArteryRemoting)RARP.For(a).Provider.Transport;
                    var association = transport.Registry.AssociationFor(RARP.For(b).Provider.DefaultAddress);
                    var compression = association.GetRefCompression(AddressUidExtension.Uid(b), 256);
                    var generation = association.HandshakeGeneration;
                    association.TripOutboundKillSwitch();
                    await AwaitAssertAsync(() => Assert.True(association.HandshakeGeneration > generation), Timeout);
                    remote.Tell(999, probe.Ref);
                    await probe.ExpectMsgAsync(999, Timeout);
                    Assert.Equal(remote, probe.LastSender);
                    Assert.Same(compression, association.GetRefCompression(AddressUidExtension.Uid(b), 256));
                }
            }
            finally
            {
                await a.Terminate();
                await b.Terminate();
            }
        }

        private static Config Config(bool enabled, int lanes) => ConfigurationFactory.ParseString($"""
            akka.actor.provider = remote
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = 0
            akka.remote.artery.advanced.inbound-lanes = {lanes}
            akka.remote.artery.advanced.outbound-lanes = {lanes}
            akka.remote.artery.advanced.ref-compression-spike.enabled = {enabled.ToString().ToLowerInvariant()}
            akka.remote.artery.advanced.ref-compression-spike.interval = 50ms
            """);

        private sealed class Echo : ReceiveActor
        {
            public Echo() => ReceiveAny(message => Sender.Tell(message));
        }
    }
}
