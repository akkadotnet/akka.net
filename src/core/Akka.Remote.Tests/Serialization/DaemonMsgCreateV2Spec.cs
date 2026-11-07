//-----------------------------------------------------------------------
// <copyright file="DaemonMsgCreateV2Spec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Remote.Serialization;
using Akka.Routing;
using Akka.Serialization;
using Akka.TestKit;
using FluentAssertions;
using VerifyXunit;
using Xunit;

namespace Akka.Remote.Tests.Serialization;

[Collection(DynamicTypeLoadingCollection.Name)]
public sealed class DaemonMsgCreateV2Spec : AkkaSpec
{
    public DaemonMsgCreateV2Spec(ITestOutputHelper output) : base(output)
    {
    }

    // Captured before implementation, using legacy serializer git blob
    // 66aa3fc10476bdc3fc5909e56c99c94c9b949937 (upstream dev 2d6a06797).
    // This port uses the kit's corpus/mixed-binding helpers without inheriting its
    // unconditional native-roundtrip contract: remote actor construction remains dynamic.
    internal static IReadOnlyList<V2PortCase> Corpus { get; } = new[]
    {
        new V2PortCase(new DaemonMsgCreate(Props.Create<FixtureActor>(), new Deploy(), "/remote/frozen", ActorRefs.NoSender), "", "default") { V2Manifest = "a" },
        new V2PortCase(new DaemonMsgCreate(Props.Create(typeof(ArgumentActor), "argument", 17, null), new Deploy(),
            "/remote/arguments", ActorRefs.NoSender), "", "arguments") { V2Manifest = "a" },
        new V2PortCase(new DaemonMsgCreate(Props.Create<FixtureActor>().WithDeploy(new Deploy("props-path",
                ConfigurationFactory.ParseString("props-value = 1"), new RoundRobinPool(3),
                new RemoteScope(new Address("akka.tcp", "props-target", "127.0.0.1", 2552)), "props-dispatcher").WithStashCapacity(11)),
            new Deploy("message-path", ConfigurationFactory.ParseString("message-value = 2"), FromConfig.Instance,
                new RemoteScope(new Address("akka.tcp", "message-target", "127.0.0.1", 2553)), "message-dispatcher").WithStashCapacity(7),
            "/remote/two-deploys", ActorRefs.NoSender), "", "two-deploys") { V2Manifest = "a" },
        new V2PortCase(new DaemonMsgCreate(Props.Create<FixtureActor>(), new Deploy().WithStashCapacity(0),
            "/remote/zero-stash", ActorRefs.NoSender), "", "zero-stash") { V2Manifest = "a" },
        new V2PortCase(new DaemonMsgCreate(new Props(new Deploy("props-null", null, NoRouter.Instance,
                Deploy.NoScopeGiven, Deploy.NoDispatcherGiven), typeof(FixtureActor), Array.Empty<object>()),
            new Deploy("message-null", null, NoRouter.Instance, Deploy.NoScopeGiven, Deploy.NoDispatcherGiven),
            "/remote/null-config", ActorRefs.NoSender), "", "null-config") { V2Manifest = "a" }
    }.Select(c => c with { AssertEquivalent = AssertMessage }).ToArray();

    public static IEnumerable<object[]> FixtureNames => Corpus.Select(c => new object[] { c.Name });

    [Theory(DisplayName = "Should_PreserveHistoricalBytes_When_LegacySerializes")]
    [MemberData(nameof(FixtureNames))]
    public async Task Should_PreserveHistoricalBytes_When_LegacySerializes(string name)
    {
        var sample = Corpus.Single(c => c.Name == name);
        var serializer = new DaemonMsgCreateSerializer((ExtendedActorSystem)Sys);
        serializer.Identifier.Should().Be(3);
        serializer.Manifest(sample.Message).Should().BeEmpty();
        await Verifier.Verify(HexDumpFormatter.Format(name, "DaemonMsgCreate", "", 3, serializer.ToBinary(sample.Message)))
            .UseDirectory("Snapshots").UseFileName($"DaemonMsgCreateV2Spec.{name}.legacy");
        AssertMessage(sample.Message, Sys.Serialization.Deserialize(ReadFixture(name, "legacy"), 3, ""));
    }

    [Theory(DisplayName = "Should_FreezeGeneratedBytes_When_V2Serializes")]
    [MemberData(nameof(FixtureNames))]
    public async Task Should_FreezeGeneratedBytes_When_V2Serializes(string name)
    {
        var sample = Corpus.Single(c => c.Name == name);
        var serializer = new DaemonMsgCreateMessagePackSerializer((ExtendedActorSystem)Sys);
        serializer.Manifest(sample.Message).Should().Be(sample.V2Manifest);
        await Verifier.Verify(HexDumpFormatter.Format(name, "DaemonMsgCreate", "a", 43, serializer.ToBinary(sample.Message)))
            .UseDirectory("Snapshots").UseFileName($"DaemonMsgCreateV2Spec.{name}.v2");
        AssertMessage(sample.Message, Sys.Serialization.Deserialize(ReadFixture(name, "v2"), 43, "a"));
    }

    [Fact(DisplayName = "Should_PreserveEveryField_When_GeneratedBuffersAreSegmented")]
    public void Should_PreserveEveryField_When_GeneratedBuffersAreSegmented()
    {
        var serializer = new DaemonMsgCreateMessagePackSerializer((ExtendedActorSystem)Sys);
        foreach (var sample in Corpus)
        {
            var bytes = serializer.ToBinary(sample.Message);
            var buffer = new ArrayBufferWriter<byte>();
            serializer.Serialize(sample.Message, buffer).Should().Be(bytes.Length);
            buffer.WrittenSpan.ToArray().Should().Equal(bytes);
            for (var split = 0; split <= bytes.Length; split++)
                AssertMessage(sample.Message, serializer.Deserialize(Split(bytes, split), "a"));
        }
        var withSupervisor = new DaemonMsgCreate(Props.Create<FixtureActor>(), new Deploy(), "/remote/supervisor", TestActor);
        var received = (DaemonMsgCreate)serializer.FromBinary(serializer.ToBinary(withSupervisor), "a");
        received.Supervisor.Should().Be(TestActor);
    }

    [Theory(DisplayName = "Should_PreserveBindingsAndBothReaders_When_GlobalSelectionChanges")]
    [InlineData(false, false, 3)]
    [InlineData(true, false, 43)]
    [InlineData(true, true, 3)]
    public async Task Should_PreserveBindingsAndBothReaders_When_GlobalSelectionChanges(bool enabled, bool pinned, int writerId)
    {
        var config = ConfigurationFactory.ParseString($"akka.actor.serialization-v2 = {enabled.ToString().ToLowerInvariant()}");
        if (pinned) config = MixedBindingPair.BindTo("daemon-create", typeof(DaemonMsgCreate)).WithFallback(config);
        var node = ActorSystem.Create("daemon-binding", config);
        InitializeLogger(node);
        try
        {
            node.Serialization.GetSerializerById(3).Should().BeOfType<DaemonMsgCreateSerializer>();
            node.Serialization.GetSerializerById(43).Should().BeOfType<DaemonMsgCreateMessagePackSerializer>();
            foreach (var sample in Corpus)
            {
                node.Serialization.FindSerializerFor(sample.Message).Identifier.Should().Be(writerId);
                foreach (var id in new[] { 3, 43 })
                {
                    var reader = node.Serialization.GetSerializerById(id);
                    AssertMessage(sample.Message, node.Serialization.Deserialize(reader.ToBinary(sample.Message), id, id == 3 ? "" : "a"));
                }
            }
        }
        finally
        {
            await node.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact(DisplayName = "Should_ExchangeBothFormats_When_PeersSelectOppositeWriters")]
    public async Task Should_ExchangeBothFormats_When_PeersSelectOppositeWriters()
    {
        await using var pair = MixedBindingPair.Create("daemon-mixed",
            ConfigurationFactory.ParseString("akka.actor.serialization-v2 = on"),
            ConfigurationFactory.ParseString("akka.actor.serialization-v2 = off"));
        pair.AssertRoundTripsBothWays(Corpus, 43, 3);
    }

    [Fact(DisplayName = "Should_RejectBeforeOutputOrPayloadWork_When_DynamicTypeLoadingIsDisabled")]
    public async Task Should_RejectBeforeOutputOrPayloadWork_When_DynamicTypeLoadingIsDisabled()
    {
        var setup = ActorSystemSetup.Create(SerializationSetup.Create(system => ImmutableHashSet.Create(
            SerializerDetails.Create("daemon-user", new UserSerializer(system), ImmutableHashSet.Create(typeof(UserArgument))))));
        var node = ActorSystem.Create("daemon-strict", setup);
        InitializeLogger(node);
        try
        {
            var payload = (UserSerializer)node.Serialization.GetSerializerById(9843);
            var serializer = node.Serialization.GetSerializerById(43);
            var message = new DaemonMsgCreate(Props.Create(typeof(ArgumentActor), "custom", 1, new UserArgument("user")),
                new Deploy(), "/remote/custom", ActorRefs.NoSender);
            var bytes = serializer.ToBinary(message);
            payload.Writes.Should().Be(1);
            AssertMessage(message, serializer.FromBinary(bytes, "a"));
            payload.Reads.Should().Be(1);
            var previousWrites = payload.Writes;
            var previousReads = payload.Reads;
            var hadSwitch = AppContext.TryGetSwitch("Akka.DynamicTypeLoading", out var previous);
            AppContext.SetSwitch("Akka.DynamicTypeLoading", false);
            try
            {
                var serialization = new Akka.Serialization.Serialization((ExtendedActorSystem)node);
                var strict = (SerializerV2)serialization.GetSerializerById(43);
                var buffer = new ArrayBufferWriter<byte>();
                buffer.Write(new byte[] { 0x5a });
                Action manifest = () => strict.Manifest(message);
                Action array = () => strict.ToBinary(message);
                Action write = () => strict.Serialize(message, buffer);
                Action hint = () => strict.SizeHint(message);
                Action read = () => strict.Deserialize(new ReadOnlySequence<byte>(bytes), "a");
                foreach (var operation in new[] { manifest, array, write, hint, read })
                    operation.Should().Throw<SerializationException>().WithMessage("*Akka.DynamicTypeLoading*no native actor factory*");
                buffer.WrittenSpan.ToArray().Should().Equal(0x5a);
                payload.Writes.Should().Be(previousWrites);
                payload.Reads.Should().Be(previousReads);
            }
            finally
            {
                AppContext.SetSwitch("Akka.DynamicTypeLoading", !hadSwitch || previous);
            }
        }
        finally
        {
            await node.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact(DisplayName = "Should_PreserveCustomBufferSerializers_When_ConstructorArgumentsUseIndependentBindings")]
    public async Task Should_PreserveCustomBufferSerializers_When_ConstructorArgumentsUseIndependentBindings()
    {
        var setup = ActorSystemSetup.Create(SerializationSetup.Create(system => ImmutableHashSet.Create(
            SerializerDetails.Create("daemon-user-buffer", new UserBufferSerializer(system), ImmutableHashSet.Create(typeof(UserArgument))))));
        var node = ActorSystem.Create("daemon-user-buffer", setup);
        InitializeLogger(node);
        try
        {
            var payload = new UserArgument("custom-buffer");
            var message = new DaemonMsgCreate(Props.Create(typeof(ArgumentActor), "custom", 1, payload),
                new Deploy(), "/remote/custom-buffer", ActorRefs.NoSender);
            node.Serialization.FindSerializerFor(payload).Identifier.Should().Be(9844);
            foreach (var id in new[] { 3, 43 })
            {
                var serializer = node.Serialization.GetSerializerById(id);
                AssertMessage(message, node.Serialization.Deserialize(serializer.ToBinary(message), id, id == 3 ? "" : "a"));
            }
        }
        finally
        {
            await node.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact(DisplayName = "Should_RejectInvalidMessagesAndRecords_When_UsingGeneratedDeploymentCodec")]
    public void Should_RejectInvalidMessagesAndRecords_When_UsingGeneratedDeploymentCodec()
    {
        var serializer = new DaemonMsgCreateMessagePackSerializer((ExtendedActorSystem)Sys);
        var writer = new ArrayBufferWriter<byte>();
        Action wrongType = () => serializer.Serialize(new object(), writer);
        Action wrongManifest = () => serializer.Deserialize(ReadOnlySequence<byte>.Empty, "");
        Action malformed = () => serializer.Deserialize(new ReadOnlySequence<byte>(new byte[] { 0x81 }), "a");
        wrongType.Should().Throw<ArgumentException>();
        wrongManifest.Should().Throw<SerializationException>();
        malformed.Should().Throw<Exception>();
        writer.WrittenCount.Should().Be(0);
    }

    private static void AssertMessage(object expectedObject, object actualObject)
    {
        actualObject.Should().BeOfType<DaemonMsgCreate>();
        var expected = (DaemonMsgCreate)expectedObject;
        var actual = (DaemonMsgCreate)actualObject;
        actual.Path.Should().Be(expected.Path);
        actual.Props.Type.Should().Be(expected.Props.Type);
        actual.Props.Arguments.Should().Equal(expected.Props.Arguments);
        AssertDeploy(expected.Props.Deploy, actual.Props.Deploy);
        AssertDeploy(expected.Deploy, actual.Deploy);
        if (expected.Supervisor is null)
            actual.Supervisor.Path.Name.Should().Be("deadLetters");
        else
            actual.Supervisor.Path.Should().Be(expected.Supervisor.Path);
    }

    private static void AssertDeploy(Deploy expected, Deploy actual)
    {
        actual.Path.Should().Be(expected.Path);
        actual.Config.ToString().Should().Be((expected.Config ?? Config.Empty).ToString());
        actual.RouterConfig.Should().BeEquivalentTo(expected.RouterConfig, options => options.RespectingRuntimeTypes());
        actual.Scope.Should().Be(expected.Scope);
        actual.Dispatcher.Should().Be(expected.Dispatcher);
        actual.StashCapacity.Should().Be(expected.StashCapacity > 0 ? expected.StashCapacity : Deploy.NoStashSize);
    }

    private static byte[] ReadFixture(string name, string kind, [CallerFilePath] string source = "") =>
        File.ReadLines(Path.Combine(Path.GetDirectoryName(source)!, "Snapshots", $"DaemonMsgCreateV2Spec.{name}.{kind}.verified.txt"))
            .Where(line => line.Length > 6 && Uri.IsHexDigit(line[0]) && line[4] == ' ')
            .SelectMany(line => line.Split('|')[0][6..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Select(hex => Convert.ToByte(hex, 16)).ToArray();

    private static ReadOnlySequence<byte> Split(byte[] bytes, int offset)
    {
        var first = new Segment(bytes.AsMemory(0, offset));
        var last = first.Append(bytes.AsMemory(offset));
        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory) => Memory = memory;
        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;
            return next;
        }
    }

    private sealed record UserArgument(string Value);

    private sealed class UserSerializer(ExtendedActorSystem system) : SerializerWithStringManifest(system)
    {
        public int Writes { get; private set; }
        public int Reads { get; private set; }
        public override int Identifier => 9843;
        public override string Manifest(object obj) => "user";
        public override byte[] ToBinary(object obj)
        {
            Writes++;
            return Encoding.UTF8.GetBytes(((UserArgument)obj).Value);
        }
        public override object FromBinary(byte[] bytes, string manifest)
        {
            Reads++;
            return new UserArgument(Encoding.UTF8.GetString(bytes));
        }
    }

    private sealed class UserBufferSerializer(ExtendedActorSystem system) : SerializerV2(system)
    {
        public override int Identifier => 9844;
        public override string Manifest(object obj) => "user-buffer";
        public override int Serialize(object obj, IBufferWriter<byte> writer)
        {
            var bytes = Encoding.UTF8.GetBytes(((UserArgument)obj).Value);
            writer.Write(bytes);
            return bytes.Length;
        }
        public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest) =>
            new UserArgument(Encoding.UTF8.GetString(bytes.ToArray()));
    }

    public sealed class FixtureActor : UntypedActor
    {
        protected override void OnReceive(object message)
        {
            Sender.Tell(message);
        }
    }

    public sealed class ArgumentActor : UntypedActor
    {
        public ArgumentActor(string text, int number, object? optional)
        {
        }
        protected override void OnReceive(object message)
        {
            Sender.Tell(message);
        }
    }
}
