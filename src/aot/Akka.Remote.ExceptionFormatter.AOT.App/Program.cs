#nullable enable
using System.Buffers;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.AOT.Shared;
using Akka.Configuration;
using Akka.Event;
using Akka.Pattern;
using Akka.Remote.Serialization;
using Akka.Serialization;
using Akka.Serialization.V2;
using MessagePack;

Require(!RuntimeFeature.IsDynamicCodeSupported, "Native execution is required.");
Require(AppContext.TryGetSwitch("Akka.DynamicTypeLoading", out var dynamicLoading) && !dynamicLoading,
    "Dynamic type loading must be explicitly disabled.");
var watchdog = new LogWatchdogFilter();
var setup = ActorSystemSetup.Create(BootstrapSetup.Create(), new LogFilterSetup([watchdog]),
    SerializationSetup.Create(system => ImmutableHashSet.Create(NativeStateCodec.CreateSerializerDetails(system))));
var system = ActorSystem.Create("g8-native", setup);
try
{
    watchdog.ThrowIfAnyProblems("g8-native", "startup");
    var codec = new ExceptionPacketCodec((ExtendedActorSystem)system);
    var formatter = new ExceptionMessagePackFormatter((ExtendedActorSystem)system);
    var count = 0;
    foreach (var expected in NativeKnownCases.Build(system.DeadLetters))
    {
        expected.Source = "native-source";
        expected.HelpLink = "https://example.com/native";
        expected.Data["state"] = "retained";
        expected.Data[7] = 11L;
        var packet = new ExceptionPacket(expected);
        var bytes = codec.ToBinary(packet);
        Compare(expected, ((ExceptionPacket)codec.Deserialize(new ReadOnlySequence<byte>(bytes), "exception-packet")).Cause);
        var buffer = new ArrayBufferWriter<byte>();
        Require(codec.Serialize(packet, buffer) == bytes.Length && buffer.WrittenCount == bytes.Length, "Generated writer count differs.");
        Require(buffer.WrittenSpan.SequenceEqual(bytes), "Generated native bytes differ.");
        count++;
    }
    Require(count == 36, "Incomplete known factory corpus.");
    Console.WriteLine("[g8-native] known-factories=36 verified");
    var absent = new NullableExceptionPacket(null);
    Require(((NullableExceptionPacket)codec.Deserialize(new ReadOnlySequence<byte>(codec.ToBinary(absent)), "nullable-exception-packet")).Cause is null,
        "Nullable formatter fields must retain nil without invoking a constructor.");
    var rich = new PreRestartException(system.DeadLetters, ThrowAndCatch(new Exception("restart")),
        ThrowAndCatch(new ArgumentException("raw message", "parameter")), new NativeState(42));
    rich.Data["user"] = new NativeState(99);
    Compare(rich, ((ExceptionPacket)codec.Deserialize(new ReadOnlySequence<byte>(codec.ToBinary(new ExceptionPacket(rich))), "exception-packet")).Cause);
    var thrown = ThrowAndCatch(new OpenCircuitException("open", new Exception("inner"), TimeSpan.FromTicks(1234567)));
    Compare(thrown, ((ExceptionPacket)codec.Deserialize(new ReadOnlySequence<byte>(codec.ToBinary(new ExceptionPacket(thrown))), "exception-packet")).Cause);
    var nobody = new ActorInitializationException(ActorRefs.Nobody, "nobody", new Exception("inner"));
    Compare(nobody, ((ExceptionPacket)codec.Deserialize(new ReadOnlySequence<byte>(codec.ToBinary(new ExceptionPacket(nobody))), "exception-packet")).Cause);

    Exception[] rejected =
    [
        new UnknownException(),
        new Exception("inner", new UnknownException()),
        new PreRestartException(system.DeadLetters, new UnknownException(), new Exception("cause"), null),
        new PreRestartException(system.DeadLetters, new Exception("restart"), new Exception("cause"), new UnregisteredState(1))
    ];
    foreach (var value in rejected)
    {
        var buffer = new ArrayBufferWriter<byte>();
        try
        {
            Write(formatter, value, buffer);
            throw new InvalidOperationException("Expected native rejection did not occur.");
        }
        catch (SerializationException)
        {
            Require(buffer.WrittenCount == 0, "Rejected state partially wrote caller output.");
        }
    }
    var malformed = Encode(formatter, new Exception("unknown descriptor"));
    Require(malformed[0] == 0x8f && malformed[1] == 1 && malformed[2] == 1, "Unexpected wire descriptor prefix.");
    var rejectedReaders = 0;
    foreach (var descriptor in new byte[] { 0, 127 })
    {
        malformed[2] = descriptor;
        try
        {
            ReadValue(formatter, malformed);
            throw new InvalidOperationException("Unknown native descriptor was accepted.");
        }
        catch (SerializationException)
        {
            rejectedReaders++;
        }
    }
    Require(rejectedReaders == 2, "Unknown descriptor checks did not both run.");
    watchdog.ThrowIfAnyProblems("g8-native", "serialization");
    Console.WriteLine("[g8-native] OK known=36 rich=3 null-field=1 native-user-payload=true rejected=6");
}
finally
{
    await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
}
watchdog.ThrowIfAnyProblems("g8-native", "shutdown");

static byte[] Encode(ExceptionMessagePackFormatter formatter, Exception value)
{
    var buffer = new ArrayBufferWriter<byte>();
    Write(formatter, value, buffer);
    return buffer.WrittenSpan.ToArray();
}
static void Write(ExceptionMessagePackFormatter formatter, Exception value, IBufferWriter<byte> output)
{
    var writer = new MessagePackWriter(output);
    formatter.Write(ref writer, value);
    writer.Flush();
}
static Exception ReadValue(ExceptionMessagePackFormatter formatter, byte[] bytes)
{
    var reader = new MessagePackReader(new ReadOnlySequence<byte>(bytes));
    return formatter.Read(ref reader);
}
static void Compare(Exception expected, Exception actual)
{
    Require(expected.GetType() == actual.GetType(), "Exception runtime type differs.");
    Require(expected.Message == actual.Message, "Exception message differs.");
    Require(expected.Source == actual.Source, "Exception source differs.");
    Require(expected.StackTrace == actual.StackTrace, "Exception stack differs.");
    Require(expected.HelpLink == actual.HelpLink && expected.HResult == actual.HResult, "Exception base state differs.");
    if (expected.InnerException is { } inner)
        Compare(inner, actual.InnerException ?? throw new InvalidOperationException("Missing inner exception."));
    else
        Require(actual.InnerException is null, "Unexpected inner exception.");
    Require(expected.Data.Count == actual.Data.Count, "Exception data count differs.");
    foreach (var key in expected.Data.Keys)
        Require(Equals(expected.Data[key], actual.Data[key]), "Exception data differs.");
    switch (expected)
    {
        case PreRestartException pre:
            var actualPre = (PreRestartException)actual;
            Require(Equals(pre.Actor, actualPre.Actor) && Equals(pre.OptionalMessage, actualPre.OptionalMessage), "PreRestart rich state differs.");
            Compare(pre.RestartException, actualPre.RestartException);
            break;
        case PostRestartException post:
            Require(Equals(post.Actor, ((PostRestartException)actual).Actor), "PostRestart actor differs.");
            Compare(post.OriginalCause, ((PostRestartException)actual).OriginalCause);
            break;
        case ActorInitializationException initialization:
            Require(Equals(initialization.Actor, ((ActorInitializationException)actual).Actor),
                $"Initialization actor differs: [{initialization.Actor}] vs [{((ActorInitializationException)actual).Actor}].");
            break;
        case DeathPactException death:
            Require(Equals(death.DeadActor, ((DeathPactException)actual).DeadActor), "DeathPact actor differs.");
            break;
        case OpenCircuitException open:
            Require(open.RemainingDuration == ((OpenCircuitException)actual).RemainingDuration, "Duration differs.");
            break;
    }
}
[MethodImpl(MethodImplOptions.NoInlining)]
static T ThrowAndCatch<T>(T value) where T : Exception
{
    try { throw value; }
    catch (T caught) { return caught; }
}
static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

public interface IExceptionPacket
{
}
[AkkaSerializable(Manifest = "exception-packet")]
public sealed record ExceptionPacket([property: AkkaField(1)] Exception Cause) : IExceptionPacket;
[AkkaSerializable(Manifest = "nullable-exception-packet")]
public sealed record NullableExceptionPacket([property: AkkaField(1)] Exception? Cause) : IExceptionPacket;
[AkkaSerializer<IExceptionPacket>("g8-native-packet", 876544)]
[AkkaSerializerFormatter<Exception, ExceptionMessagePackFormatter>]
public sealed partial class ExceptionPacketCodec : AkkaSerializer
{
    public static partial SerializerDetails CreateSerializerDetails(ExtendedActorSystem system);
}
public interface INativeState
{
}
[AkkaSerializable(Manifest = "native-state")]
public sealed record NativeState([property: AkkaField(1)] int Value) : INativeState;
[AkkaSerializer<INativeState>("g8-native-state", 876543)]
public sealed partial class NativeStateCodec : AkkaSerializer
{
    public static partial SerializerDetails CreateSerializerDetails(ExtendedActorSystem system);
}
public sealed record UnregisteredState(int Value);
public sealed class UnknownException : Exception
{
}
