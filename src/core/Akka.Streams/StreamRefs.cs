//-----------------------------------------------------------------------
// <copyright file="StreamRefs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Pattern;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Akka.Util;

namespace Akka.Streams
{
    /// <summary>
    /// A <see cref="ISinkRef{TIn}"/> allows sharing a "reference" to a <see cref="Sink{TIn,TMat}"/> with others,
    /// with the main purpose of crossing a network boundary. Usually obtaining a SinkRef would be done via Actor messaging,
    /// in which one system asks a remote one, to accept some data from it, and the remote one decides to accept the
    /// request to send data in a back-pressured streaming fashion -- using a sink ref.
    /// 
    /// To create a <see cref="ISinkRef{TIn}"/> you have to materialize the <see cref="Sink"/> that you want to obtain
    /// a reference to by attaching it to a <see cref="StreamRefs.SinkRef{T}"/>.
    /// 
    /// Stream refs can be seen as Reactive Streams over network boundaries.
    /// 
    /// For additional configuration see `reference.conf` as well as <see cref="StreamRefAttributes"/>.
    /// </summary>
    /// <seealso cref="ISourceRef{TOut}"/>
    public interface ISinkRef<TIn>
    {
        Sink<TIn, NotUsed> Sink { get; }
    }

    /// <summary>
    /// A SourceRef allows sharing a "reference" with others, with the main purpose of crossing a network boundary.
    /// Usually obtaining a SourceRef would be done via Actor messaging, in which one system asks a remote one,
    /// to share some data with it, and the remote one decides to do so in a back-pressured streaming fashion -- using a stream ref.
    /// 
    /// To create a <see cref="ISourceRef{TOut}"/> you have to materialize the <see cref="Source"/> that you want to
    /// obtain a reference to by attaching it to a <see cref="StreamRefs.SourceRef{T}"/>.
    /// 
    /// Stream refs can be seen as Reactive Streams over network boundaries.
    /// 
    /// For additional configuration see `reference.conf` as well as <see cref="StreamRefAttributes"/>.
    /// </summary>
    /// <seealso cref="ISinkRef{TIn}"/>
    public interface ISourceRef<TOut> : ISurrogated
    {
        Source<TOut, NotUsed> Source { get; }
    }

    public sealed class TargetRefNotInitializedYetException : IllegalStateException
    {
        public TargetRefNotInitializedYetException() : base(
            "Internal remote target actor ref not yet resolved, yet attempted to send messages to it. " +
            "This should not happen due to proper flow-control, please open a ticket on the issue tracker: https://github.com/akkadotnet/akka.net")
        {
        }
    }

    public sealed class StreamRefSubscriptionTimeoutException : IllegalStateException
    {
        public StreamRefSubscriptionTimeoutException(string message) : base(message)
        {
        }
    }

    public sealed class RemoteStreamRefActorTerminatedException : Exception
    {
        internal static readonly RemoteStreamRefActorTerminatedException Default =
            new RemoteStreamRefActorTerminatedException("Remote target receiver of data terminated. Local stream terminating, message loss (on remote side) may have happened.");

        public RemoteStreamRefActorTerminatedException(string message) : base(message)
        {
        }
    }

    public sealed class InvalidSequenceNumberException : IllegalStateException
    {
        public long ExpectedSeqNr { get; }
        public long GotSeqNr { get; }

        public InvalidSequenceNumberException(long expectedSeqNr, long gotSeqNr, string message) : base(
            $"{message} (expected: {expectedSeqNr}, got: {gotSeqNr}). In most cases this means that message loss on this connection has occurred and the stream will fail eagerly.")
        {
            ExpectedSeqNr = expectedSeqNr;
            GotSeqNr = gotSeqNr;
        }
    }

    /// <summary>
    /// Stream refs establish a connection between a local and remote actor, representing the origin and remote sides
    /// of a stream. Each such actor refers to the other side as its "partner". We make sure that no other actor than
    /// the initial partner can send demand/messages to the other side accidentally.
    /// 
    /// This exception is thrown when a message is recived from a non-partner actor,
    /// which could mean a bug or some actively malicient behavior from the other side.
    /// 
    /// This is not meant as a security feature, but rather as plain sanity-check.
    /// </summary>
    public sealed class InvalidPartnerActorException : IllegalStateException
    {
        public IActorRef ExpectedRef { get; }
        public IActorRef GotRef { get; }

        public InvalidPartnerActorException(IActorRef expectedRef, IActorRef gotRef, string message) : base(
            $"{message} (expected: {expectedRef}, got: {gotRef}). " +
            "This may happen due to 'double-materialization' on the other side of this stream ref. " +
            "Do note that stream refs are one-shot references and have to be paired up in 1:1 pairs. " +
            "Multi-cast such as broadcast etc can be implemented by sharing multiple new stream references. ")
        {
        }
    }
}
