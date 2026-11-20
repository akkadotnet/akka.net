// -----------------------------------------------------------------------
// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// -----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.IO.Buffers;
using Akka.Pattern;
using Akka.Remote.Serialization;
using Akka.Remote.Tests.Serialization;
using Akka.Remote.Transport;
using Akka.Serialization;
using Akka.TestKit;
using MessagePack;
using Xunit;

namespace Akka.Remote.Tests;

[Collection(DynamicTypeLoadingCollection.Name)]
public sealed class ExceptionMessagePackFormatterSpec : AkkaSpec
{
    private readonly ExceptionMessagePackFormatter _formatter;

    public ExceptionMessagePackFormatterSpec(ITestOutputHelper output) : base(output)
    {
        _formatter = new ExceptionMessagePackFormatter((ExtendedActorSystem)Sys);
    }

    [Fact(DisplayName = "Should_PreserveEveryKnownException_When_ClosedFactoriesReadTheGeneratedFormat")]
    public void Should_PreserveEveryKnownException_When_ClosedFactoriesReadTheGeneratedFormat()
    {
        WithDynamicLoading(false, () =>
        {
            var count = 0;
            foreach (var expected in KnownCases(TestActor))
            {
                expected.Source = "known-source";
                expected.HelpLink = "https://example.com/exception";
                expected.Data["state"] = "data-value";
                expected.Data[7] = 11L;
                AssertState(expected, Read(Write(expected)));
                count++;
            }
            Assert.Equal(36, count);
        });
    }

    [Fact(DisplayName = "Should_PreserveRawMessagesAndRichState_When_ExceptionsHaveBeenThrown")]
    public void Should_PreserveRawMessagesAndRichState_When_ExceptionsHaveBeenThrown()
    {
        WithDynamicLoading(false, () =>
        {
            var actor = TestActor;
            Exception[] values =
            [
                new ArgumentException("argument message", "parameter", new Exception("inner")),
                new ArgumentNullException("parameter", "null message"),
                new ArgumentOutOfRangeException("parameter", 123, "range message"),
                new ActorInitializationException(actor, "initialization", new Exception("inner")),
                new DeathPactException(actor),
                new PreRestartException(actor, new Exception("restart"), new Exception("cause"), new byte[] { 1, 2, 3 }),
                new PostRestartException(actor, new Exception("cause"), new Exception("original")),
                new OpenCircuitException("open", new Exception("inner"), TimeSpan.FromTicks(1234567))
            ];
            foreach (var value in values)
            {
                var thrown = ThrowAndCatch(value);
                Assert.NotNull(thrown.StackTrace);
                AssertState(thrown, Read(Write(thrown)));
            }
        });
    }

    [Fact(DisplayName = "Should_UseTheRegisteredNativePayloadSerializer_When_RichStateContainsUserData")]
    public void Should_UseTheRegisteredNativePayloadSerializer_When_RichStateContainsUserData()
    {
        var serializer = new NativeStateSerializer((ExtendedActorSystem)Sys);
        Sys.Serialization.AddSerializer("g8-state", serializer);
        Sys.Serialization.AddSerializationMap(typeof(NativeState), serializer);
        WithDynamicLoading(false, () =>
        {
            var state = new NativeState(123);
            var expected = new PreRestartException(TestActor, new Exception("restart"), new Exception("cause"), state);
            expected.Data["custom"] = state;
            AssertState(expected, Read(Write(expected)));
            Assert.Equal(state, Assert.IsType<PreRestartException>(Read(Write(expected))).OptionalMessage);
        });
    }

    [Fact(DisplayName = "Should_PreserveActorRefSentinels_When_RichExceptionStateRoundTrips")]
    public void Should_PreserveActorRefSentinels_When_RichExceptionStateRoundTrips()
    {
        WithDynamicLoading(false, () =>
        {
            foreach (var actor in new[] { ActorRefs.Nobody, Sys.DeadLetters, TestActor, ActorRefs.NoSender })
            {
                var expected = new ActorInitializationException(actor, "actor state", new Exception("inner"));
                AssertState(expected, Read(Write(expected)));
            }
        });
    }

    [Fact(DisplayName = "Should_RetainCustomSerializableFields_When_TheJitFallbackIsEnabled")]
    public void Should_RetainCustomSerializableFields_When_TheJitFallbackIsEnabled()
    {
        WithDynamicLoading(true, () =>
        {
            var expected = ThrowAndCatch(new CustomException("custom", 42, new Exception("nested")));
            expected.HelpLink = "https://example.com/custom";
            expected.Data["custom-data"] = "retained";
            var actual = Assert.IsType<CustomException>(Read(Write(expected)));
            Assert.Equal(expected.Message, actual.Message);
            Assert.Equal(expected.Source, actual.Source);
            Assert.Equal(expected.StackTrace, actual.StackTrace);
            Assert.Equal(expected.Number, actual.Number);
            Assert.Equal(expected.Note, actual.Note);
            Assert.Equal(typeof(UnregisteredState), actual.DeclaredOptionalType);
            AssertState(expected, actual);
        });
    }

    [Fact(DisplayName = "Should_RetainForwardedTypeResolution_When_ReadingAJitFallback")]
    public void Should_RetainForwardedTypeResolution_When_ReadingAJitFallback()
    {
        WithDynamicLoading(true, () =>
        {
            var captured = (ExceptionWire)Codec().Deserialize(new ReadOnlySequence<byte>(Write(new CustomException("forwarded", 9, null))), "exception-fragment-v1");
            var wire = captured with
            {
                JitTypeName = typeof(CustomException).FullName + ", Missing.Exception.Assembly",
                JitForwardedFrom = typeof(CustomException).Assembly.GetName().Name
            };
            var actual = Assert.IsType<CustomException>(Read(Codec().ToBinary(wire)));
            Assert.Equal("forwarded", actual.Message);
            Assert.Equal(9, actual.Number);
        });
    }

    [Fact(DisplayName = "Should_UseExplicitSerializableDispatch_When_CustomJitExceptionsImplementIt")]
    public void Should_UseExplicitSerializableDispatch_When_CustomJitExceptionsImplementIt()
    {
        WithDynamicLoading(true, () =>
        {
            var expected = new ExplicitSerializableException("explicit", 99);
            var actual = Assert.IsType<ExplicitSerializableException>(Read(Write(expected)));
            Assert.Equal(expected.Value, actual.Value);
            AssertState(expected, actual);
            var buffer = new ArrayBufferWriter<byte>();
            Assert.Throws<SerializationException>(() => WriteTo(new MissingSerializationConstructorException(), buffer));
            Assert.Equal(0, buffer.WrittenCount);
        });
    }

    [Theory(DisplayName = "Should_RejectUnsupportedStateBeforeCallerOutput_When_DynamicLoadingIsDisabled")]
    [InlineData("root")]
    [InlineData("inner")]
    [InlineData("rich-exception")]
    [InlineData("rich-payload")]
    [InlineData("data-payload")]
    public void Should_RejectUnsupportedStateBeforeCallerOutput_When_DynamicLoadingIsDisabled(string shape)
    {
        WithDynamicLoading(false, () =>
        {
            Exception value = shape switch
            {
                "root" => new CustomException("custom", 1, null),
                "inner" => new Exception("outer", new CustomException("custom", 1, null)),
                "rich-exception" => new PreRestartException(TestActor, new CustomException("custom", 1, null), new Exception("cause"), null),
                "rich-payload" => new PreRestartException(TestActor, new Exception("restart"), new Exception("cause"), new UnregisteredState(1)),
                _ => new Exception("data")
            };
            if (shape == "data-payload")
                value.Data["unknown"] = new UnregisteredState(1);
            var buffer = new ArrayBufferWriter<byte>();
            Assert.Throws<SerializationException>(() => WriteTo(value, buffer));
            Assert.Equal(0, buffer.WrittenCount);
        });
    }

    [Fact(DisplayName = "Should_RejectUnknownNativeAndJitDescriptors_When_DynamicLoadingIsDisabled")]
    public void Should_RejectUnknownNativeAndJitDescriptors_When_DynamicLoadingIsDisabled()
    {
        var jit = WithDynamicLoading(true, () => Write(new CustomException("custom", 1, null)));
        WithDynamicLoading(false, () =>
        {
            Assert.Throws<SerializationException>(() => Read(jit));
            Assert.Throws<SerializationException>(() => Read(Codec().ToBinary(EmptyWire(999999))));
            var unknownField = EmptyWire(119) with
            {
                Fields = [new ExceptionFieldWire("Actor", 999999, new ExceptionValueWire(0, null, null, null, null), null)]
            };
            Assert.Throws<SerializationException>(() => Read(Codec().ToBinary(unknownField)));
        });
    }

    [Fact(DisplayName = "Should_RejectTruncatedData_When_ReadingTheExceptionValue")]
    public void Should_RejectTruncatedData_When_ReadingTheExceptionValue()
    {
        var bytes = Write(new Exception("truncated"));
        for (var length = 0; length < bytes.Length; length++)
        {
            var truncated = bytes.AsSpan(0, length).ToArray();
            Assert.ThrowsAny<Exception>(() => Read(truncated));
        }
        Assert.Equal(SerializerV2.UnknownSize, _formatter.SizeOf(new Exception("size")));
    }

    [Fact(DisplayName = "Should_KeepCallerOutputEmpty_When_ARegisteredPayloadFailsAfterWriting")]
    public void Should_KeepCallerOutputEmpty_When_ARegisteredPayloadFailsAfterWriting()
    {
        var serializer = new ThrowingStateSerializer((ExtendedActorSystem)Sys);
        Sys.Serialization.AddSerializer("g8-throwing-state", serializer);
        Sys.Serialization.AddSerializationMap(typeof(ThrowingState), serializer);
        WithDynamicLoading(false, () =>
        {
            var expected = new PreRestartException(TestActor, new Exception("restart"), new Exception("cause"), new ThrowingState());
            var output = new ArrayBufferWriter<byte>();
            Assert.Throws<SerializationException>(() => WriteTo(expected, output));
            Assert.True(serializer.WrotePayloadBytes);
            Assert.Equal(0, output.WrittenCount);
        });
    }

    [Fact(DisplayName = "Should_RejectCyclicExceptionState_When_PreparingTheValue")]
    public void Should_RejectCyclicExceptionState_When_PreparingTheValue()
    {
        WithDynamicLoading(false, () =>
        {
            var expected = new Exception("cycle");
            expected.Data["self"] = expected;
            var output = new ArrayBufferWriter<byte>();
            Assert.Throws<SerializationException>(() => WriteTo(expected, output));
            Assert.Equal(0, output.WrittenCount);
        });
    }

    private byte[] Write(Exception value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        WriteTo(value, buffer);
        return buffer.WrittenSpan.ToArray();
    }

    private void WriteTo(Exception value, IBufferWriter<byte> buffer)
    {
        var writer = new MessagePackWriter(buffer);
        _formatter.Write(ref writer, value);
        writer.Flush();
    }

    private Exception Read(byte[] bytes)
    {
        var reader = new MessagePackReader(new ReadOnlySequence<byte>(bytes));
        var result = _formatter.Read(ref reader);
        Assert.True(reader.End);
        return result;
    }

    private ExceptionWireCodec Codec() => new((ExtendedActorSystem)Sys);
    private static ExceptionWire EmptyWire(int descriptor) => new(descriptor, "message", null, null, null, [], null,
        typeof(ActorInitializationException).FullName, -1, null, null, 0, null, [], null);

    private static void AssertState(Exception expected, Exception actual)
    {
        Assert.Equal(expected.GetType(), actual.GetType());
        Assert.Equal(expected.Message, actual.Message);
        Assert.Equal(expected.Source, actual.Source);
        Assert.Equal(expected.StackTrace, actual.StackTrace);
        Assert.Equal(expected.HelpLink, actual.HelpLink);
        Assert.Equal(expected.HResult, actual.HResult);
        if (expected.InnerException is null)
            Assert.Null(actual.InnerException);
        else
            AssertState(expected.InnerException, actual.InnerException!);
        Assert.Equal(expected.Data.Count, actual.Data.Count);
        foreach (var key in expected.Data.Keys)
            Assert.Equal(expected.Data[key], actual.Data[key]);
        switch (expected)
        {
            case PreRestartException pre:
                var actualPre = Assert.IsType<PreRestartException>(actual);
                Assert.Equal(pre.Actor, actualPre.Actor);
                AssertState(pre.RestartException, actualPre.RestartException);
                Assert.Equal(pre.OptionalMessage, actualPre.OptionalMessage);
                break;
            case PostRestartException post:
                var actualPost = Assert.IsType<PostRestartException>(actual);
                Assert.Equal(post.Actor, actualPost.Actor);
                AssertState(post.OriginalCause, actualPost.OriginalCause);
                break;
            case ActorInitializationException initialization:
                Assert.Equal(initialization.Actor, Assert.IsType<ActorInitializationException>(actual).Actor);
                break;
            case DeathPactException death:
                Assert.Equal(death.DeadActor, Assert.IsType<DeathPactException>(actual).DeadActor);
                break;
            case OpenCircuitException open:
                Assert.Equal(open.RemainingDuration, Assert.IsType<OpenCircuitException>(actual).RemainingDuration);
                break;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static T ThrowAndCatch<T>(T value) where T : Exception
    {
        try
        {
            throw value;
        }
        catch (T caught)
        {
            return caught;
        }
    }

    private static void WithDynamicLoading(bool enabled, Action action) => WithDynamicLoading(enabled, () => { action(); return true; });
    private static T WithDynamicLoading<T>(bool enabled, Func<T> action)
    {
        var had = AppContext.TryGetSwitch("Akka.DynamicTypeLoading", out var previous);
        AppContext.SetSwitch("Akka.DynamicTypeLoading", enabled);
        try
        {
            return action();
        }
        finally
        {
            AppContext.SetSwitch("Akka.DynamicTypeLoading", !had || previous);
        }
    }

    internal static Exception[] KnownCases(IActorRef actor)
    {
        var inner = new Exception("inner message");
        return
        [
            new Exception("message", inner), new ApplicationException("message", inner),
            new InvalidOperationException("message", inner), new ArgumentException("message", "parameter", inner),
            new ArgumentNullException("parameter", "message"), new ArgumentOutOfRangeException("parameter", 123, "message"),
            new TimeoutException("message", inner), new NotSupportedException("message", inner),
            new NotImplementedException("message", inner), new SerializationException("message", inner),
            new IOException("message", inner), new FileNotFoundException("message", "file", inner),
            new ActorInterruptedException("message", inner), new ActorNotFoundException("message", inner),
            new InvalidActorNameException("message", inner), new LoggerInitializationException("message", inner),
            new StashOverflowException("message", inner), new ConfigurationException("message", inner),
            new RejectedExecutionException("message", inner), new IllegalStateException("message", inner),
            new RemoteTransportException("message", inner), new AkkaProtocolException("message", inner),
            new InvalidAssociationException("message", inner), new ActorKilledException("message"),
            new AskTimeoutException("message"), new IllegalActorNameException("message"),
            new IllegalActorStateException("message"), new InvalidMessageException("message"),
            new SchedulerException("message"), new BufferPoolAllocationException("message"), new UserCalledFailException(),
            new ActorInitializationException(actor, "message", inner), new DeathPactException(actor),
            new PreRestartException(actor, new Exception("restart"), inner, "optional"),
            new PostRestartException(actor, inner, new Exception("original")),
            new OpenCircuitException("message", inner, TimeSpan.FromTicks(1234567))
        ];
    }

    [Serializable]
    public sealed class CustomException : Exception
    {
        public CustomException(string message, int number, Exception? inner) : base(message, inner)
        {
            Number = number;
        }
        private CustomException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
            Number = info.GetInt32("Number");
            Note = info.GetString("Note")!;
            foreach (var field in info)
            {
                if (field.Name == "Optional")
                    DeclaredOptionalType = field.ObjectType;
            }
        }
        public int Number { get; }
        public string Note { get; } = "custom-note";
        public Type? DeclaredOptionalType { get; }
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue("Number", Number);
            info.AddValue("Note", Note);
            info.AddValue("Optional", null, typeof(UnregisteredState));
        }
    }

    public sealed record NativeState(int Value);
    public sealed record UnregisteredState(int Value);
    [Serializable]
    public sealed class ExplicitSerializableException : Exception, ISerializable
    {
        public ExplicitSerializableException(string message, int value) : base(message)
        {
            Value = value;
        }
        private ExplicitSerializableException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
            Value = info.GetInt32("ExplicitValue");
        }
        public int Value { get; }
        void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue("ExplicitValue", Value);
        }
    }
    private sealed class MissingSerializationConstructorException : Exception
    {
    }
    private sealed record ThrowingState;
    private sealed class ThrowingStateSerializer(ExtendedActorSystem system) : SerializerV2(system)
    {
        public bool WrotePayloadBytes { get; private set; }
        public override int Identifier => 876542;
        public override string Manifest(object obj) => "throwing-state";
        public override int Serialize(object obj, IBufferWriter<byte> writer)
        {
            writer.Write(new byte[8192]);
            WrotePayloadBytes = true;
            throw new SerializationException("Deliberate payload failure after bytes were written.");
        }
        public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest)
            => throw new NotSupportedException("The failure fixture only exercises writes.");
    }
    private sealed class NativeStateSerializer(ExtendedActorSystem system) : SerializerV2(system)
    {
        public override int Identifier => 876543;
        public override string Manifest(object obj) => "native-state";
        public override int Serialize(object obj, IBufferWriter<byte> writer)
        {
            var bytes = BitConverter.GetBytes(((NativeState)obj).Value);
            writer.Write(bytes);
            return bytes.Length;
        }
        public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest) => new NativeState(BitConverter.ToInt32(bytes.ToArray()));
    }
}
