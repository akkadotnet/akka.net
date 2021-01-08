//-----------------------------------------------------------------------
// <copyright file="StreamRefsProtocol.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Reactive.Streams;

namespace Akka.Streams.Implementation.StreamRef
{
    internal interface IStreamRefsProtocol { }

    /// <summary>
    /// Sequenced <see cref="ISubscriber{T}.OnNext"/> equivalent.
    /// The receiving end of these messages MUST fail the stream if it observes gaps in the sequence,
    /// as these messages will not be re-delivered.
    /// 
    /// Sequence numbers start from `0`.
    /// </summary>
    internal sealed class SequencedOnNext : IStreamRefsProtocol, IDeadLetterSuppression
    {
        public long SeqNr { get; }
        public object Payload { get; }

        public SequencedOnNext(long seqNr, object payload)
        {
            SeqNr = seqNr;
            Payload = payload ?? throw ReactiveStreamsCompliance.ElementMustNotBeNullException;
        }
    }

    /// <summary>
    /// Initial message sent to remote side to establish partnership between origin and remote stream refs.
    /// </summary>
    internal sealed class OnSubscribeHandshake : IStreamRefsProtocol, IDeadLetterSuppression
    {
        public OnSubscribeHandshake(IActorRef targetRef)
        {
            TargetRef = targetRef;
        }

        public IActorRef TargetRef { get; }
    }

    /// <summary>
    /// Sent to a the receiver side of a stream ref, once the sending side of the SinkRef gets signalled a Failure.
    /// </summary>
    internal sealed class RemoteStreamFailure : IStreamRefsProtocol
    {
        public RemoteStreamFailure(string message)
        {
            Message = message;
        }

        public string Message { get; }
    }

    /// <summary>
    /// Sent to a the receiver side of a stream ref, once the sending side of the SinkRef gets signalled a completion.
    /// </summary>
    internal sealed class RemoteStreamCompleted : IStreamRefsProtocol
    {
        public RemoteStreamCompleted(long seqNr)
        {
            SeqNr = seqNr;
        }

        public long SeqNr { get; }
    }

    /// <summary>
    /// INTERNAL API: Cumulative demand, equivalent to sequence numbering all events in a stream.
    /// 
    /// This message may be re-delivered.
    /// </summary>
    internal sealed class CumulativeDemand : IStreamRefsProtocol, IDeadLetterSuppression
    {
        public CumulativeDemand(long seqNr)
        {
            if (seqNr <= 0) throw ReactiveStreamsCompliance.NumberOfElementsInRequestMustBePositiveException;
            SeqNr = seqNr;
        }

        public long SeqNr { get; }
    }
}
