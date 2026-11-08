// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
#nullable enable
using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Dispatch.SysMsg;
using Akka.Pattern;
using Akka.Remote.Serialization;
using Akka.Remote.Serialization.Proto.Msg;
using Akka.Remote.Tests.Serialization;
using Akka.TestKit;
using Akka.Serialization;
using Akka.Util;
using FluentAssertions;
using Google.Protobuf;
using VerifyXunit;
using Xunit;

namespace Akka.Remote.Tests;

[Collection(DynamicTypeLoadingCollection.Name)]
public sealed class LegacyExceptionNativeReadSpec : AkkaSpec
{
    public LegacyExceptionNativeReadSpec(ITestOutputHelper output)
        : base(ActorSystem.Create("legacy-exceptions", AkkaSpecConfig), output)
    {
    }

    internal static IEnumerable<(string Name, object Message)> Corpus()
    {
        foreach (var exception in ExceptionMessagePackFormatterSpec.KnownCases(ActorRefs.Nobody))
        {
            exception.Source = "legacy-source";
            exception.HelpLink = "legacy-help-is-not-serialized";
            exception.Data["legacy-data-is-not-serialized"] = 17;
            yield return ($"status-{exception.GetType().Name}", new Status.Failure(exception));
        }
        yield return ("system-create", new Create(new ActorInitializationException(ActorRefs.Nobody, "initialization", new Exception("inner"))));
        yield return ("system-pre-restart", new Recreate(new PreRestartException(ActorRefs.Nobody,
            new Exception("restart"), new Exception("cause"), "optional")));
        yield return ("system-post-restart", new Recreate(new PostRestartException(ActorRefs.Nobody,
            new Exception("cause"), new Exception("original"))));
        yield return ("status-json-state", new Status.Failure(new PreRestartException(ActorRefs.Nobody,
            new Exception("restart"), new Exception("cause"), new UnregisteredState(17))));
    }

    // Original records captured before this fix, from ExceptionSupport source git blob
    // ab1ac13716307407ad93b85c807c55ee07388e28 (unchanged in G8 base c37da8e93).
    public static IEnumerable<object[]> NativeFixtureNames => Corpus()
        .Where(sample => sample.Name is not "status-OpenCircuitException" and not "status-json-state")
        .Select(sample => new object[] { sample.Name });

    [Theory(DisplayName = "Should_PreserveOriginalWireBytes_When_LegacyWritersSerialize")]
    [InlineData("system-create")]
    [InlineData("system-pre-restart")]
    [InlineData("system-post-restart")]
    [InlineData("status-Exception")]
    [InlineData("status-OpenCircuitException")]
    [InlineData("status-json-state")]
    public async Task Should_PreserveOriginalWireBytes_When_LegacyWritersSerialize(string name)
    {
        var sample = Corpus().Single(sample => sample.Name == name);
        var serializer = Sys.Serialization.FindSerializerFor(sample.Message);
        serializer.Identifier.Should().Be(sample.Message is Status.Failure ? 16 : 22);
        await Verifier.Verify(HexDumpFormatter.Format(name, HexDumpFormatter.FriendlyTypeName(sample.Message.GetType()),
                serializer.Manifest(sample.Message), serializer.Identifier, serializer.ToBinary(sample.Message)))
            .UseDirectory("Snapshots").UseFileName($"LegacyExceptionNativeReadSpec.{name}");
    }

    [Theory(DisplayName = "Should_PreserveHistoricalExceptionMeaning_When_NativeFactoriesReadFrozenRecords")]
    [MemberData(nameof(NativeFixtureNames))]
    public void Should_PreserveHistoricalExceptionMeaning_When_NativeFactoriesReadFrozenRecords(string name)
    {
        var bytes = ReadFixture(name);
        var sample = Corpus().Single(sample => sample.Name == name);
        var serializer = Sys.Serialization.FindSerializerFor(sample.Message);
        var expected = WithDynamicLoading(true, () => Sys.Serialization.Deserialize(bytes, serializer.Identifier, serializer.Manifest(sample.Message)));
        var actual = WithDynamicLoading(false, () => Sys.Serialization.Deserialize(bytes, serializer.Identifier, serializer.Manifest(sample.Message)));
        AssertException(Cause(expected), Cause(actual));
        if (actual is Status.Failure failure) failure.State.Should().BeNull();
    }

    [Theory(DisplayName = "Should_RejectHistoricalJsonFieldsWithContext_When_NativePayloadReadersAreUnavailable")]
    [InlineData("status-OpenCircuitException", "RemainingDuration")]
    [InlineData("status-json-state", "OptionalMessage")]
    public void Should_RejectHistoricalJsonFieldsWithContext_When_NativePayloadReadersAreUnavailable(string name, string field)
    {
        Action read = () => WithDynamicLoading(false, () => Sys.Serialization.Deserialize(ReadFixture(name), 16, "STF"));
        read.Should().Throw<SerializationException>().WithMessage($"*field [{field}]*serializer [1]*Akka.DynamicTypeLoading*");
    }

    [Fact(DisplayName = "Should_RetainOldFormatLosses_When_FrozenExceptionsContainAdditionalState")]
    public void Should_RetainOldFormatLosses_When_FrozenExceptionsContainAdditionalState()
    {
        var decoded = WithDynamicLoading(false, () => (Status.Failure)Sys.Serialization.Deserialize(ReadFixture("status-Exception"), 16, "STF"));
        decoded.Cause.HResult.Should().Be(int.MinValue);
        decoded.Cause.Data.Count.Should().Be(0);
        decoded.Cause.HelpLink.Should().BeEmpty();
        var argument = WithDynamicLoading(false, () => (Status.Failure)Sys.Serialization.Deserialize(ReadFixture("status-ArgumentException"), 16, "STF"));
        // The old writer stores Message (already decorated), then the old constructor decorates
        // it again. Replacing that with G8's raw-message behavior would change old-wire meaning.
        argument.Cause.Message.Should().Be("message (Parameter 'parameter') (Parameter 'parameter')");
    }

    [Theory(DisplayName = "Should_ResolveOnlyKnownHistoricalIdentities_When_DynamicTypeLoadingIsDisabled")]
    [InlineData("System.Exception, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089", "")]
    [InlineData("System.Exception, System.Private.CoreLib, Version=1.0.0.0", "")]
    [InlineData("System.Exception, Missing.Historical.Assembly", "mscorlib")]
    public void Should_ResolveOnlyKnownHistoricalIdentities_When_DynamicTypeLoadingIsDisabled(string name, string forwarded)
    {
        var proto = StatusFailure.Parser.ParseFrom(ReadFixture("status-Exception")).Cause;
        proto.TypeName = name;
        proto.TypeForwardedFrom = forwarded;
        var support = new ExceptionSupport((ExtendedActorSystem)Sys);
        var result = WithDynamicLoading(false, () => support.ExceptionFromProto(proto));
        result.Should().BeOfType<Exception>().Which.Message.Should().Be("message");
    }

    [Theory(DisplayName = "Should_RejectUnknownNativeExceptionIdentities_When_TheDescriptorIsNotInTheFiniteSet")]
    [InlineData("Unknown.Exception, Unknown.Assembly")]
    [InlineData("System.Exception, Unknown.Assembly")]
    [InlineData("System.String, System.Private.CoreLib")]
    public void Should_RejectUnknownNativeExceptionIdentities_When_TheDescriptorIsNotInTheFiniteSet(string name)
    {
        var proto = StatusFailure.Parser.ParseFrom(ReadFixture("status-Exception")).Cause;
        proto.TypeName = name;
        proto.TypeForwardedFrom = "";
        var support = new ExceptionSupport((ExtendedActorSystem)Sys);
        Action read = () => WithDynamicLoading(false, () => support.ExceptionFromProto(proto));
        read.Should().Throw<SerializationException>().WithMessage("*no native factory*Akka.DynamicTypeLoading*");
    }

    [Fact(DisplayName = "Should_RejectMissingDescriptors_When_TheRecordContainsExceptionState")]
    public void Should_RejectMissingDescriptors_When_TheRecordContainsExceptionState()
    {
        var support = new ExceptionSupport((ExtendedActorSystem)Sys);
        WithDynamicLoading(false, () => support.ExceptionFromProto(new ExceptionData())).Should().BeNull();
        Action read = () => WithDynamicLoading(false, () => support.ExceptionFromProto(new ExceptionData { Message = "lost cause" }));
        read.Should().Throw<SerializationException>().WithMessage("*state but no exception type descriptor*Akka.DynamicTypeLoading*");
    }

    [Fact(DisplayName = "Should_UseCustomPayloadReaders_When_ApplicationsOverrideTheProtobufAlias")]
    public async Task Should_UseCustomPayloadReaders_When_ApplicationsOverrideTheProtobufAlias()
    {
        var setup = ActorSystemSetup.Create(SerializationSetup.Create(system => ImmutableHashSet.Create(
            SerializerDetails.Create("proto", new CustomProtoReader(system), ImmutableHashSet<Type>.Empty))));
        var node = ActorSystem.Create("legacy-proto-override", setup);
        InitializeLogger(node);
        try
        {
            var actual = WithDynamicLoading(false, () => (Status.Failure)node.Serialization.Deserialize(ReadFixture("status-PostRestartException"), 16, "STF"));
            actual.Cause.Should().BeOfType<PostRestartException>().Which.OriginalCause.Message.Should().Be("application override");
            ((CustomProtoReader)node.Serialization.GetSerializerById(2)).Reads.Should().Be(1);
        }
        finally
        {
            await node.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact(DisplayName = "Should_DelegateProtobufSubclasses_When_TheManifestNamesExceptionData")]
    public async Task Should_DelegateProtobufSubclasses_When_TheManifestNamesExceptionData()
    {
        var setup = ActorSystemSetup.Create(SerializationSetup.Create(system => ImmutableHashSet.Create(
            SerializerDetails.Create("proto", new DerivedProtoReader(system), ImmutableHashSet<Type>.Empty))));
        var node = ActorSystem.Create("legacy-proto-subclass", setup);
        InitializeLogger(node);
        try
        {
            var actual = WithDynamicLoading(false, () => (Status.Failure)node.Serialization.Deserialize(ReadFixture("status-PostRestartException"), 16, "STF"));
            actual.Cause.Should().BeOfType<PostRestartException>().Which.OriginalCause.Message.Should().Be("subclass override");
        }
        finally
        {
            await node.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact(DisplayName = "Should_RetainCustomJitConstructionAndForwarding_When_DynamicLoadingIsEnabled")]
    public void Should_RetainCustomJitConstructionAndForwarding_When_DynamicLoadingIsEnabled()
    {
        var support = new ExceptionSupport((ExtendedActorSystem)Sys);
        var proto = support.ExceptionToProto(new CustomException("custom", 42));
        var original = WithDynamicLoading(true, () => support.ExceptionFromProto(proto));
        original.Should().BeOfType<CustomException>().Which.Number.Should().Be(42);
        proto.TypeName = typeof(CustomException).FullName + ", Missing.Exception.Assembly";
        proto.TypeForwardedFrom = typeof(CustomException).Assembly.GetName().Name;
        WithDynamicLoading(true, () => support.ExceptionFromProto(proto)).Should().BeOfType<CustomException>().Which.Number.Should().Be(42);
        Action native = () => WithDynamicLoading(false, () => support.ExceptionFromProto(proto));
        native.Should().Throw<SerializationException>().WithMessage("*no native factory*");
    }

    internal static byte[] ReadFixture(string name, [CallerFilePath] string source = "") =>
        File.ReadLines(Path.Combine(Path.GetDirectoryName(source)!, "Snapshots", $"LegacyExceptionNativeReadSpec.{name}.verified.txt"))
            .Where(line => line.Length > 6 && Uri.IsHexDigit(line[0]) && line[4] == ' ')
            .SelectMany(line => line.Split('|')[0][6..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Select(hex => Convert.ToByte(hex, 16)).ToArray();

    private static Exception Cause(object message) => message switch
    {
        Status.Failure failure => failure.Cause,
        Create create => create.Failure,
        Recreate recreate => recreate.Cause,
        _ => throw new ArgumentException("Not an exception-bearing fixture.", nameof(message))
    };

    private static void AssertException(Exception? expected, Exception? actual)
    {
        if (expected is null)
        {
            actual.Should().BeNull();
            return;
        }
        actual.Should().NotBeNull();
        actual!.GetType().Should().Be(expected.GetType());
        actual.Message.Should().Be(expected.Message);
        actual.Source.Should().Be(expected.Source);
        actual.StackTrace.Should().Be(expected.StackTrace);
        actual.HResult.Should().Be(expected.HResult);
        actual.HelpLink.Should().Be(expected.HelpLink);
        actual.Data.Count.Should().Be(expected.Data.Count);
        AssertException(expected.InnerException, actual.InnerException);
        if (expected is ActorInitializationException actor)
            (((ActorInitializationException)actual).Actor?.Path.ToSerializationFormat()).Should().Be(actor.Actor?.Path.ToSerializationFormat());
        if (expected is DeathPactException death)
            ((DeathPactException)actual).DeadActor.Path.Should().Be(death.DeadActor.Path);
        if (expected is PreRestartException pre)
        {
            AssertException(pre.RestartException, ((PreRestartException)actual).RestartException);
            ((PreRestartException)actual).OptionalMessage.Should().BeEquivalentTo(pre.OptionalMessage);
        }
        if (expected is PostRestartException post)
            AssertException(post.OriginalCause, ((PostRestartException)actual).OriginalCause);
        if (expected is ArgumentException argument)
            ((ArgumentException)actual).ParamName.Should().Be(argument.ParamName);
        if (expected is ArgumentOutOfRangeException range)
            ((ArgumentOutOfRangeException)actual).ActualValue.Should().Be(range.ActualValue);
        if (expected is FileNotFoundException file)
        {
            ((FileNotFoundException)actual).FileName.Should().Be(file.FileName);
            ((FileNotFoundException)actual).FusionLog.Should().Be(file.FusionLog);
        }
    }

    private static T WithDynamicLoading<T>(bool enabled, Func<T> action)
    {
        var hadSwitch = AppContext.TryGetSwitch("Akka.DynamicTypeLoading", out var previous);
        AppContext.SetSwitch("Akka.DynamicTypeLoading", enabled);
        try { return action(); }
        finally { AppContext.SetSwitch("Akka.DynamicTypeLoading", !hadSwitch || previous); }
    }

    public sealed record UnregisteredState(int Value);

    private sealed class CustomProtoReader(ExtendedActorSystem system) : SerializerWithStringManifest(system)
    {
        public int Reads { get; private set; }
        public override int Identifier => 2;
        public override string Manifest(object obj) => "override";
        public override byte[] ToBinary(object obj) => throw new NotSupportedException();
        public override object FromBinary(byte[] bytes, string manifest)
        {
            manifest.Should().Be(typeof(ExceptionData).TypeQualifiedName());
            Reads++;
            return new Exception("application override");
        }
    }

    private sealed class DerivedProtoReader(ExtendedActorSystem system) : ProtobufSerializer(system)
    {
        public override int Identifier => 2;
        // Owning the manifest avoids depending on a warm TypeCache in this native-safe override.
        public override object FromBinary(byte[] bytes, string manifest) => new Exception("subclass override");
        public override object FromBinary(byte[] bytes, Type type) => new Exception("subclass override");
    }

    [Serializable]
    public sealed class CustomException : Exception
    {
        public CustomException(string message, int number) : base(message) => Number = number;
        private CustomException(SerializationInfo info, StreamingContext context) : base(info, context) => Number = info.GetInt32("Number");
        public int Number { get; }
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue("Number", Number);
            base.GetObjectData(info, context);
        }
    }
}
