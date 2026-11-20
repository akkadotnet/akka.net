// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>

#nullable enable
using System;
using System.Buffers;
using System.Collections.Immutable;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Serialization;
using Akka.Serialization.V2;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation.StreamRef;
using Akka.Streams.Serialization;
using Akka.TestKit;
using Akka.Util;
using MessagePack;
using Xunit;
using AkkaSerialization = Akka.Serialization.Serialization;

namespace Akka.Streams.Tests.Serialization;

public sealed class TypedStreamRefFormatterSpec : AkkaSpec
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(10);

    public TypedStreamRefFormatterSpec(ITestOutputHelper output) : base(output)
    {
    }

    [Theory(DisplayName = "Should_TransferAndMaterializeTypedRefs_When_AFormattedOfferCrossesRemoting")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_TransferAndMaterializeTypedRefs_When_AFormattedOfferCrossesRemoting(bool artery)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var sender = CreateRemoteSystem("typed-refs-sender", artery);
        ActorSystem? receiver = null;
        try
        {
            receiver = CreateRemoteSystem("typed-refs-receiver", artery);
            var probe = CreateTestProbe(receiver);
            var address = ((ExtendedActorSystem)receiver).Provider.DefaultAddress;
            var destination = await sender.ActorSelection(probe.Ref.Path.ToSerializationFormatWithAddress(address))
                .ResolveOne(OperationTimeout, cancellationToken);

            TypedRefOrder[] offeredOrders = [new(1), new(2)];
            TypedRefOrder[] returnedOrders = [new(3), new(4)];
            var senderMaterializer = sender.Materializer();
            var sourceRef = await Source.From(offeredOrders).RunWith(StreamRefs.SourceRef<TypedRefOrder>(), senderMaterializer)
                .WaitAsync(OperationTimeout, cancellationToken);
            var (sinkRefTask, receivedAtSink) = StreamRefs.SinkRef<TypedRefOrder>()
                .ToMaterialized(Sink.Seq<TypedRefOrder>(), Keep.Both).Run(senderMaterializer);
            var sinkRef = await sinkRefTask.WaitAsync(OperationTimeout, cancellationToken);
            var offer = new TypedRefOffer(sourceRef, sinkRef);
            Assert.IsType<TypedRefSerializer>(sender.Serialization.FindSerializerFor(offer));
            Assert.IsType<TypedRefSerializer>(receiver.Serialization.FindSerializerFor(offer));

            destination.Tell(offer);
            var received = await probe.ExpectMsgAsync<TypedRefOffer>(OperationTimeout, cancellationToken: cancellationToken);

            Assert.NotSame(sourceRef, received.SourceRef);
            Assert.NotSame(sinkRef, received.SinkRef);
            var receiverMaterializer = receiver.Materializer();
            var receivedFromSource = received.SourceRef.Source.RunWith(Sink.Seq<TypedRefOrder>(), receiverMaterializer);
            Source.From(returnedOrders).RunWith(received.SinkRef.Sink, receiverMaterializer);
            Assert.Equal(offeredOrders, await receivedFromSource.WaitAsync(OperationTimeout, cancellationToken));
            Assert.Equal(returnedOrders, await receivedAtSink.WaitAsync(OperationTimeout, cancellationToken));
        }
        finally
        {
            await Task.WhenAll(ShutdownAsync(sender), receiver is null ? Task.CompletedTask : ShutdownAsync(receiver));
        }
    }

    [Fact(DisplayName = "Should_EncodeOneActorPath_When_FormattingABuiltInTypedRef")]
    public void Should_EncodeOneActorPath_When_FormattingABuiltInTypedRef()
    {
        var system = (ExtendedActorSystem)Sys;
        var unsupported = new CustomRef();
        CheckFormatter<ISourceRef<TypedRefOrder>>(new SourceRefFormatter<TypedRefOrder>(system),
            new SourceRefImpl<TypedRefOrder>(TestActor), unsupported,
            static value => Assert.IsType<SourceRefImpl<TypedRefOrder>>(value).InitialPartnerRef);
        CheckFormatter<ISinkRef<TypedRefOrder>>(new SinkRefFormatter<TypedRefOrder>(system),
            new SinkRefImpl<TypedRefOrder>(TestActor), unsupported,
            static value => Assert.IsType<SinkRefImpl<TypedRefOrder>>(value).InitialPartnerRef);
    }

    [Theory(DisplayName = "Should_RoundTripDistinctClosedRefs_When_AnOptionalSourceRefIsPresentOrNil")]
    [InlineData(false)]
    [InlineData(true)]
    public void Should_RoundTripDistinctClosedRefs_When_AnOptionalSourceRefIsPresentOrNil(bool present)
    {
        var serializer = new TypedRefSerializer((ExtendedActorSystem)Sys);
        var offer = new TypedRefOffer(new SourceRefImpl<TypedRefOrder>(TestActor), new SinkRefImpl<TypedRefOrder>(TestActor),
            present ? new SourceRefImpl<int>(TestActor) : null);

        var bytes = serializer.ToBinary(offer);
        var reader = new MessagePackReader(bytes);
        var actorPath = AkkaSerialization.SerializedActorPath(TestActor);
        Assert.Equal(3, reader.ReadMapHeader());
        Assert.Equal(1, reader.ReadInt32());
        Assert.Equal(actorPath, reader.ReadString());
        Assert.Equal(2, reader.ReadInt32());
        Assert.Equal(actorPath, reader.ReadString());
        Assert.Equal(3, reader.ReadInt32());
        if (present)
            Assert.Equal(actorPath, reader.ReadString());
        else
            Assert.True(reader.TryReadNil());
        Assert.True(reader.End);

        var decoded = Assert.IsType<TypedRefOffer>(serializer.FromBinary(bytes, serializer.Manifest(offer)));
        Assert.Equal(bytes.Length, serializer.SizeHint(offer));
        Assert.Same(TestActor, Assert.IsType<SourceRefImpl<TypedRefOrder>>(decoded.SourceRef).InitialPartnerRef);
        if (present)
            Assert.Same(TestActor, Assert.IsType<SourceRefImpl<int>>(decoded.OptionalSourceRef).InitialPartnerRef);
        else
            Assert.Null(decoded.OptionalSourceRef);
    }

    [Fact(DisplayName = "Should_UseTheSerializerFormatter_When_BothScopesRegisterTheSameType")]
    public void Should_UseTheSerializerFormatter_When_BothScopesRegisterTheSameType()
    {
        var system = (ExtendedActorSystem)Sys;
        var serializer = new TypedRefOverrideSerializer(system);
        var offer = new TypedRefOverrideOffer(new CustomRef());

        var bytes = serializer.ToBinary(offer);
        var received = Assert.IsType<TypedRefOverrideOffer>(serializer.FromBinary(bytes, serializer.Manifest(offer)));

        Assert.Equal(bytes.Length, serializer.SizeHint(offer));
        Assert.Same(system.DeadLetters, Assert.IsType<SourceRefImpl<TypedRefOrder>>(received.SourceRef).InitialPartnerRef);
    }

    private static ActorSystem CreateRemoteSystem(string name, bool artery)
    {
        var config = ConfigurationFactory.ParseString($$"""
            akka.actor.provider = remote
            akka.actor.serialize-messages = off
            akka.remote.artery.enabled = {{(artery ? "on" : "off")}}
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = 0
            akka.remote.dot-netty.tcp.hostname = "127.0.0.1"
            akka.remote.dot-netty.tcp.port = 0
            """).WithFallback(AkkaSpecConfig);
        return ActorSystem.Create(name, ActorSystemSetup.Create(BootstrapSetup.Create().WithConfig(config),
            SerializationSetup.Create(system => ImmutableHashSet.Create(TypedRefSerializer.CreateSerializerDetails(system)))));
    }

    private void CheckFormatter<T>(IAkkaMessagePackFormatter<T> formatter, T reference, T unsupported,
        Func<T, IActorRef> endpoint)
    {
        var bytes = Write(formatter, reference);
        var reader = new MessagePackReader(bytes);
        Assert.Equal(AkkaSerialization.SerializedActorPath(TestActor), reader.ReadString());
        Assert.True(reader.End);
        Assert.Equal(bytes.Length, formatter.SizeOf(reference));
        Assert.Same(TestActor, endpoint(Read(formatter, bytes)));

        Assert.Throws<SerializationException>(() => Write(formatter, unsupported));
        Assert.Throws<SerializationException>(() => formatter.SizeOf(unsupported));
        Assert.Throws<SerializationException>(() => Read(formatter, new byte[] { 0xc0 }));
    }

    private static ReadOnlyMemory<byte> Write<T>(IAkkaMessagePackFormatter<T> formatter, T reference)
    {
        var output = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(output);
        formatter.Write(ref writer, reference);
        writer.Flush();
        return output.WrittenMemory;
    }

    private static T Read<T>(IAkkaMessagePackFormatter<T> formatter, ReadOnlyMemory<byte> bytes)
    {
        var reader = new MessagePackReader(bytes);
        var value = formatter.Read(ref reader);
        Assert.True(reader.End);
        return value;
    }

    private sealed class CustomRef : ISourceRef<TypedRefOrder>, ISinkRef<TypedRefOrder>
    {
        public Source<TypedRefOrder, NotUsed> Source => throw new NotSupportedException();
        public Sink<TypedRefOrder, NotUsed> Sink => throw new NotSupportedException();
        public ISurrogate ToSurrogate(ActorSystem system) => throw new NotSupportedException();
    }
}

internal interface ITypedRefProtocol
{
}

[AkkaSerializable(Manifest = "order")]
internal sealed record TypedRefOrder([property: AkkaField(1)] int Number) : ITypedRefProtocol;

[AkkaSerializable(Manifest = "typed-refs")]
[AkkaMessageFormatter<ISourceRef<TypedRefOrder>, SourceRefFormatter<TypedRefOrder>>]
[AkkaMessageFormatter<ISinkRef<TypedRefOrder>, SinkRefFormatter<TypedRefOrder>>]
[AkkaMessageFormatter<ISourceRef<int>, SourceRefFormatter<int>>]
internal sealed record TypedRefOffer(
    [property: AkkaField(1)] ISourceRef<TypedRefOrder> SourceRef,
    [property: AkkaField(2)] ISinkRef<TypedRefOrder> SinkRef,
    [property: AkkaField(3)] ISourceRef<int>? OptionalSourceRef = null) : ITypedRefProtocol;

[AkkaSerializer<ITypedRefProtocol>("typed-refs", 18310)]
internal sealed partial class TypedRefSerializer : AkkaSerializer
{
    public static partial SerializerDetails CreateSerializerDetails(ExtendedActorSystem system);
}

internal interface ITypedRefOverrideProtocol
{
}

[AkkaSerializable(Manifest = "typed-ref-override")]
[AkkaMessageFormatter<ISourceRef<TypedRefOrder>, SourceRefFormatter<TypedRefOrder>>]
internal sealed record TypedRefOverrideOffer(
    [property: AkkaField(1)] ISourceRef<TypedRefOrder> SourceRef) : ITypedRefOverrideProtocol;

[AkkaSerializer<ITypedRefOverrideProtocol>("typed-refs-override", 18311)]
[AkkaSerializerFormatter<ISourceRef<TypedRefOrder>, MarkerSourceRefFormatter>]
internal sealed partial class TypedRefOverrideSerializer : AkkaSerializer
{
    public static partial SerializerDetails CreateSerializerDetails(ExtendedActorSystem system);
}

internal sealed class MarkerSourceRefFormatter(ExtendedActorSystem system) : IAkkaMessagePackFormatter<ISourceRef<TypedRefOrder>>
{
    public void Write(ref MessagePackWriter writer, ISourceRef<TypedRefOrder> value) => writer.Write(42);

    public ISourceRef<TypedRefOrder> Read(ref MessagePackReader reader)
    {
        Assert.Equal(42, reader.ReadInt32());
        return new SourceRefImpl<TypedRefOrder>(system.DeadLetters);
    }

    public int SizeOf(ISourceRef<TypedRefOrder> value) => MessagePackSizes.SizeOfInt32(42);
}
