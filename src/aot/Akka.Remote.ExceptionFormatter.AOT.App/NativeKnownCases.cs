using System;
using System.IO;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.IO.Buffers;
using Akka.Pattern;
using Akka.Remote;
using Akka.Remote.Transport;

internal static class NativeKnownCases
{
    internal static Exception[] Build(IActorRef actor)
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
}
