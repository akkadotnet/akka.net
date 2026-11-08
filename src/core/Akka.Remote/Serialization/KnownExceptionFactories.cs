// -----------------------------------------------------------------------
// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// -----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.IO.Buffers;
using Akka.Pattern;
using Akka.Remote.Transport;
using Akka.Util;

namespace Akka.Remote.Serialization;

/// <summary>Closed native exception factories; descriptor numbers are permanent wire identities.</summary>
internal static class KnownExceptionFactories
{
    internal sealed record Entry(int Descriptor, Type Type, Func<SerializationInfo, StreamingContext, Exception> Create);

    private static readonly Entry[] Entries =
    [
        new(1, typeof(Exception), NewException),
        new(2, typeof(ApplicationException), NewApplicationException),
        new(3, typeof(InvalidOperationException), NewInvalidOperationException),
        new(4, typeof(ArgumentException), NewArgumentException),
        new(5, typeof(ArgumentNullException), NewArgumentNullException),
        new(6, typeof(ArgumentOutOfRangeException), NewArgumentOutOfRangeException),
        new(7, typeof(TimeoutException), NewTimeoutException),
        new(8, typeof(NotSupportedException), NewNotSupportedException),
        new(9, typeof(NotImplementedException), NewNotImplementedException),
        new(10, typeof(SerializationException), NewSerializationException),
        new(11, typeof(IOException), NewIOException),
        new(12, typeof(FileNotFoundException), NewFileNotFoundException),
        new(100, typeof(ActorInterruptedException), NewActorInterruptedException),
        new(101, typeof(ActorNotFoundException), NewActorNotFoundException),
        new(102, typeof(InvalidActorNameException), NewInvalidActorNameException),
        new(103, typeof(LoggerInitializationException), NewLoggerInitializationException),
        new(104, typeof(StashOverflowException), NewStashOverflowException),
        new(105, typeof(ConfigurationException), NewConfigurationException),
        new(106, typeof(RejectedExecutionException), NewRejectedExecutionException),
        new(107, typeof(IllegalStateException), NewIllegalStateException),
        new(108, typeof(RemoteTransportException), NewRemoteTransportException),
        new(109, typeof(AkkaProtocolException), NewAkkaProtocolException),
        new(110, typeof(InvalidAssociationException), NewInvalidAssociationException),
        new(111, typeof(ActorKilledException), NewActorKilledException),
        new(112, typeof(AskTimeoutException), NewAskTimeoutException),
        new(113, typeof(IllegalActorNameException), NewIllegalActorNameException),
        new(114, typeof(IllegalActorStateException), NewIllegalActorStateException),
        new(115, typeof(InvalidMessageException), NewInvalidMessageException),
        new(116, typeof(SchedulerException), NewSchedulerException),
        new(117, typeof(BufferPoolAllocationException), NewBufferPoolAllocationException),
        new(118, typeof(UserCalledFailException), NewUserCalledFailException),
        new(119, typeof(ActorInitializationException), NewActorInitializationException),
        new(120, typeof(DeathPactException), NewDeathPactException),
        new(121, typeof(PreRestartException), NewPreRestartException),
        new(122, typeof(PostRestartException), NewPostRestartException),
        new(123, typeof(OpenCircuitException), NewOpenCircuitException),
    ];

    private static readonly Dictionary<Type, Entry> ByType = Entries.ToDictionary(entry => entry.Type);
    private static readonly Dictionary<int, Entry> ByDescriptor = Entries.ToDictionary(entry => entry.Descriptor);
    private static readonly Dictionary<string, Entry> ByLegacyName = Entries.ToDictionary(entry => entry.Type.FullName!, StringComparer.Ordinal);

    internal static bool TryFind(Type type, out Entry entry) => ByType.TryGetValue(type, out entry!);
    internal static bool TryFind(int descriptor, out Entry entry) => ByDescriptor.TryGetValue(descriptor, out entry!);

    // The old protobuf format carries CLR names rather than G8's descriptors. Resolve only
    // the same finite factory set, including historical mscorlib identities for BCL types.
    internal static bool TryFindLegacy(string name, string? forwardedFrom, out Entry entry)
    {
        entry = null!;
        if (!TypeExtensions.TrySplitTypeName(name, out var typeName, out var assembly) ||
            !ByLegacyName.TryGetValue(typeName, out var candidate))
            return false;
        if (!MatchesAssembly(candidate.Type, assembly) &&
            (assembly is null || string.IsNullOrEmpty(forwardedFrom) || !MatchesAssembly(candidate.Type, forwardedFrom)))
            return false;
        entry = candidate;
        return true;
    }

    private static bool MatchesAssembly(Type type, string? assembly)
    {
        if (assembly is null)
            return type.Assembly == typeof(Exception).Assembly || type.Assembly == typeof(KnownExceptionFactories).Assembly;
        return string.Equals(assembly, type.Assembly.GetName().Name, StringComparison.OrdinalIgnoreCase) ||
               (type.Assembly == typeof(Exception).Assembly && string.Equals(assembly, "mscorlib", StringComparison.OrdinalIgnoreCase));
    }

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern Exception NewException(SerializationInfo info, StreamingContext context);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern ApplicationException NewApplicationException(SerializationInfo info, StreamingContext context);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern InvalidOperationException NewInvalidOperationException(SerializationInfo info, StreamingContext context);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern ArgumentException NewArgumentException(SerializationInfo info, StreamingContext context);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern ArgumentNullException NewArgumentNullException(SerializationInfo info, StreamingContext context);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern ArgumentOutOfRangeException NewArgumentOutOfRangeException(SerializationInfo info, StreamingContext context);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern TimeoutException NewTimeoutException(SerializationInfo info, StreamingContext context);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern NotSupportedException NewNotSupportedException(SerializationInfo info, StreamingContext context);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern NotImplementedException NewNotImplementedException(SerializationInfo info, StreamingContext context);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern SerializationException NewSerializationException(SerializationInfo info, StreamingContext context);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern IOException NewIOException(SerializationInfo info, StreamingContext context);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern FileNotFoundException NewFileNotFoundException(SerializationInfo info, StreamingContext context);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern ActorInterruptedException NewActorInterruptedException(SerializationInfo info, StreamingContext context);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern ActorNotFoundException NewActorNotFoundException(SerializationInfo info, StreamingContext context);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern InvalidActorNameException NewInvalidActorNameException(SerializationInfo info, StreamingContext context);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern LoggerInitializationException NewLoggerInitializationException(SerializationInfo info, StreamingContext context);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern StashOverflowException NewStashOverflowException(SerializationInfo info, StreamingContext context);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern ConfigurationException NewConfigurationException(SerializationInfo info, StreamingContext context);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern RejectedExecutionException NewRejectedExecutionException(SerializationInfo info, StreamingContext context);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern IllegalStateException NewIllegalStateException(SerializationInfo info, StreamingContext context);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern RemoteTransportException NewRemoteTransportException(SerializationInfo info, StreamingContext context);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern AkkaProtocolException NewAkkaProtocolException(SerializationInfo info, StreamingContext context);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern InvalidAssociationException NewInvalidAssociationException(SerializationInfo info, StreamingContext context);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern ActorKilledException NewActorKilledException(SerializationInfo info, StreamingContext context);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern AskTimeoutException NewAskTimeoutException(SerializationInfo info, StreamingContext context);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern IllegalActorNameException NewIllegalActorNameException(SerializationInfo info, StreamingContext context);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern IllegalActorStateException NewIllegalActorStateException(SerializationInfo info, StreamingContext context);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern InvalidMessageException NewInvalidMessageException(SerializationInfo info, StreamingContext context);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern SchedulerException NewSchedulerException(SerializationInfo info, StreamingContext context);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern BufferPoolAllocationException NewBufferPoolAllocationException(SerializationInfo info, StreamingContext context);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern UserCalledFailException NewUserCalledFailException(SerializationInfo info, StreamingContext context);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern ActorInitializationException NewActorInitializationException(SerializationInfo info, StreamingContext context);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern DeathPactException NewDeathPactException(SerializationInfo info, StreamingContext context);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern PreRestartException NewPreRestartException(SerializationInfo info, StreamingContext context);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern PostRestartException NewPostRestartException(SerializationInfo info, StreamingContext context);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern OpenCircuitException NewOpenCircuitException(SerializationInfo info, StreamingContext context);

}
