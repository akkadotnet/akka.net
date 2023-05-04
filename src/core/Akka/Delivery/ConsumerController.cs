// -----------------------------------------------------------------------
//  <copyright file="ConsumerController.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------
#nullable enable
using System;
using Akka.Actor;
using Akka.Annotations;
using Akka.Configuration;
using Akka.Delivery.Internal;
using Akka.Event;
using Akka.Util;

namespace Akka.Delivery;

public static class ConsumerController
{
    internal static void AssertLocalConsumer(IActorRef consumer)
    {
        if (consumer is IActorRefScope { IsLocal: false })
            throw new ArgumentException(
                $"Consumer [{consumer}] must be local");
    }
    
    public static Props Create<T>(IActorRefFactory actorRefFactory, Option<IActorRef> producerControllerReference, Settings? settings = null)
    {
        Props p;
        switch (actorRefFactory)
        {
            case IActorContext context:
                p = ConsumerControllerProps<T>(context, producerControllerReference, settings);
                break;
            case ActorSystem system:
                p = ConsumerControllerProps<T>(system, producerControllerReference, settings);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(actorRefFactory), $"Unrecognized IActorRefFactory: {actorRefFactory} - this is probably a bug.");
        }

        return p;
    }
    
    internal static Props CreateWithFuzzing<T>(IActorRefFactory actorRefFactory, Option<IActorRef> producerControllerReference, Func<object, double> fuzzing, Settings? settings = null)
    {
        Props p;
        switch (actorRefFactory)
        {
            case IActorContext context:
                p = ConsumerControllerProps<T>(context, producerControllerReference, settings, fuzzing);
                break;
            case ActorSystem system:
                p = ConsumerControllerProps<T>(system, producerControllerReference, settings, fuzzing);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(actorRefFactory), $"Unrecognized IActorRefFactory: {actorRefFactory} - this is probably a bug.");
        }

        return p;
    }

    private static Props ConsumerControllerProps<T>(IActorContext context, Option<IActorRef> producerControllerReference, Settings? settings = null, Func<object, double>? fuzzing = null)
    {
        return ConsumerControllerProps<T>(context.System, producerControllerReference, settings, fuzzing);
    }

    private static Props ConsumerControllerProps<T>(ActorSystem system, Option<IActorRef> producerControllerReference, Settings? settings = null,  Func<object, double>? fuzzing = null)
    {
        var realSettings = settings ?? ConsumerController.Settings.Create(system);
        // need to set the stash size equal to the flow control window
        return Props.Create(() => new ConsumerController<T>(producerControllerReference, realSettings, fuzzing))
            .WithStashCapacity(realSettings.FlowControlWindow);
    }
    
    /// <summary>
    ///     Commands that are specific to the consumer side of the reliable delivery pattern.
    /// </summary>
    /// <typeparam name="T">The type of messages the consumer manages.</typeparam>
    public interface IConsumerCommand<T>
    {
    }

    /// <summary>
    ///     Used to signal to the ConsumerController that we're ready to start message production.
    /// </summary>
    public sealed class Start<T> : IConsumerCommand<T>
    {
        public Start(IActorRef deliverTo)
        {
            DeliverTo = deliverTo;
        }

        public IActorRef DeliverTo { get; }
    }
    
    /// <summary>
    /// Instructs the <see cref="ConsumerController{T}"/> to register itself with the <see cref="ProducerController{T}"/>.
    /// </summary>
    public sealed class RegisterToProducerController<T> : IConsumerCommand<T>
    {
        public RegisterToProducerController(IActorRef producerController)
        {
            ProducerController = producerController;
        }

        public IActorRef ProducerController { get; }
    }

    /// <summary>
    ///     A sequenced message that is delivered to the consumer via the ProducerController.
    /// </summary>
    [InternalApi]
    public sealed class SequencedMessage<T> : IConsumerCommand<T>, IDeliverySerializable, IDeadLetterSuppression
    {
        internal SequencedMessage(string producerId, long seqNr, MessageOrChunk<T> messageOrChunk, bool first, bool ack,
            IActorRef producerController)
            : this(producerId, seqNr, messageOrChunk, first, ack)
        {
            ProducerController = producerController;
        }

        public SequencedMessage(string producerId, long seqNr, MessageOrChunk<T> messageOrChunk, bool first, bool ack)
        {
            SeqNr = seqNr;
            Message = messageOrChunk;
            First = first;
            Ack = ack;
            ProducerId = producerId;
        }

        public long SeqNr { get; }

        public string ProducerId { get; }
        public MessageOrChunk<T> Message { get; }

        public bool First { get; }

        public bool Ack { get; }

        internal bool IsFirstChunk => Message.Chunk is { FirstChunk: true } || Message.IsMessage;

        internal bool IsLastChunk => Message.Chunk is { LastChunk: true } || Message.IsMessage;

        /// <summary>
        /// TESTING ONLY
        /// </summary>
        internal IActorRef ProducerController { get; } = ActorRefs.Nobody;

        internal static SequencedMessage<T> FromChunkedMessage(string producerId, long seqNr,
            ChunkedMessage chunkedMessage, bool first, bool ack, IActorRef producerController)
        {
            return new SequencedMessage<T>(producerId, seqNr, chunkedMessage, first, ack, producerController);
        }
        
        /// <summary>
        /// INTERNAL API
        /// </summary>
        internal SequencedMessage<T> AsFirst()
        {
            return new(ProducerId, SeqNr, Message, true, Ack, ProducerController);
        }

        private bool Equals(SequencedMessage<T> other)
        {
            return SeqNr == other.SeqNr && ProducerId == other.ProducerId && Message.Equals(other.Message) && First == other.First && Ack == other.Ack;
        }

        public override bool Equals(object? obj)
        {
            return ReferenceEquals(this, obj) || obj is SequencedMessage<T> other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = SeqNr.GetHashCode();
                hashCode = (hashCode * 397) ^ ProducerId.GetHashCode();
                hashCode = (hashCode * 397) ^ Message.GetHashCode();
                hashCode = (hashCode * 397) ^ First.GetHashCode();
                hashCode = (hashCode * 397) ^ Ack.GetHashCode();
                return hashCode;
            }
        }
    }

    /// <summary>
    ///     Sent from the consumer controller to the consumer.
    /// </summary>
    public sealed class Delivery<T> : IConsumerCommand<T>, IDeliverySerializable, IDeadLetterSuppression
    {
        public Delivery(T message, IActorRef confirmTo, string producerId, long seqNr)
        {
            SeqNr = seqNr;
            Message = message;
            ConfirmTo = confirmTo;
            ProducerId = producerId;
        }

        public long SeqNr { get; }

        public string ProducerId { get; }
        public T Message { get; }
        
        public IActorRef ConfirmTo { get; }

        public override string ToString()
        {
            return $"Delivery({Message}, {ConfirmTo}, {ProducerId}, {SeqNr})";
        }
    }

    /// <summary>
    /// Deliver all buffered messages to consumer then shutdown.
    /// </summary>
    public sealed class DeliverThenStop<T> : IConsumerCommand<T>
    {
        private DeliverThenStop()
        {
        }
        public static readonly DeliverThenStop<T> Instance = new();
    }

    /// <summary>
    ///     Acknowledgement of a message that was received by the consumer, sent to the ConsumerController.
    /// </summary>
    public sealed class Confirmed
    {
        public static readonly Confirmed Instance = new();

        private Confirmed()
        {
        }
    }

    /// <summary>
    /// ConsumerController settings.
    /// </summary>
    public sealed record Settings
    {
        public static Settings Create(ActorSystem actorSystem)
        {
            return Create(actorSystem.Settings.Config.GetConfig("akka.reliable-delivery.consumer-controller")!);
        }
        
        public static Settings Create(Config config)
        {
            return new Settings(config.GetInt("flow-control-window"), config.GetTimeSpan("resend-interval-min"),
                config.GetTimeSpan("resend-interval-max"), config.GetBoolean("only-flow-control"));
        }

        private Settings(int flowControlWindow, TimeSpan resendIntervalMin, TimeSpan resendIntervalMax,
            bool onlyFlowControl)
        {
            FlowControlWindow = flowControlWindow;
            ResendIntervalMin = resendIntervalMin;
            ResendIntervalMax = resendIntervalMax;
            OnlyFlowControl = onlyFlowControl;
        }

        public int FlowControlWindow { get; init; }

        public TimeSpan ResendIntervalMin { get; init; }

        public TimeSpan ResendIntervalMax { get; init; }

        public bool OnlyFlowControl { get; init; }

        public override string ToString()
        {
            return $"ConsumerController.Settings({FlowControlWindow}, {ResendIntervalMin}, {ResendIntervalMax}, {OnlyFlowControl})";
        }
    }
}