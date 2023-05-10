// -----------------------------------------------------------------------
//  <copyright file="TestConsumer.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------
#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Akka.Actor;
using Akka.Configuration;
using Akka.Delivery;
using Akka.Event;
using Akka.Serialization;

namespace Akka.Tests.Delivery;

/// <summary>
/// INTERNAL API
/// </summary>
public sealed class TestConsumer : ReceiveActor, IWithTimers
{
    public static readonly TimeSpan DefaultConsumerDelay = TimeSpan.FromMilliseconds(10);

    public TimeSpan Delay { get; }

    public Func<SomeAsyncJob, bool> EndCondition { get; }

    public IActorRef EndReplyTo { get; }

    public IActorRef ConsumerController { get; }

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private ImmutableHashSet<(string, long)> _processed = ImmutableHashSet<(string, long)>.Empty;
    private readonly bool _supportRestarts = false;
    private int _messageCount = 0;

    public TestConsumer(TimeSpan delay, Func<SomeAsyncJob, bool> endCondition, IActorRef endReplyTo,
        IActorRef consumerController, bool supportRestarts = false)
    {
        Delay = delay;
        EndCondition = endCondition;
        EndReplyTo = endReplyTo;
        ConsumerController = consumerController;
        _supportRestarts = supportRestarts;
        
        Active();
    }

    private void Active()
    {
        // normally this is handled by the Akka.Typed adapter, but since we're using the untyped we need to handle it here
        Receive<ConsumerController.Delivery<Job>>(job =>
        {
            var delivery = new JobDelivery(job.Message, job.ConfirmTo, job.ProducerId, job.SeqNr);
            ReceiveJobDelivery(delivery);
        });
        
        Receive<JobDelivery>(ReceiveJobDelivery);

        Receive<SomeAsyncJob>(job =>
        {
            // when replacing producer the seqNr may start from 1 again
            var cleanProcessed =
                (job.SeqNr == 1 ? _processed.Where(tuple => tuple.Item1 != job.ProducerId) : _processed)
                .ToImmutableHashSet();

            var nextMsg = (job.ProducerId, job.SeqNr);

            if (cleanProcessed.Contains(nextMsg))
                throw new InvalidOperationException($"Received duplicate [{nextMsg}]");

            _log.Info("processed [{0}] [msg: {1}] from [{2}]", job.SeqNr, job.Msg.Payload, job.ProducerId);
            job.ConfirmTo.Tell(global::Akka.Delivery.ConsumerController.Confirmed.Instance);

            if (EndCondition(job) && (_messageCount > 0 || _supportRestarts))
            {
                _log.Debug("End at [{0}]", job.SeqNr);
                EndReplyTo.Tell(new Collected(_processed.Select(c => c.Item1).ToImmutableHashSet(), _messageCount + 1));
                Context.Stop(Self);
            }
            else if (!_supportRestarts && EndCondition(job))
            {
                // BugFix: TestConsumer was recreated by a message sent by the Sharding system, but the EndCondition was already met
                // and we don't want to send another Collected that is missing some of the figures. Ignore.
                
            }
            else
            {
                _processed = cleanProcessed.Add(nextMsg);
                _messageCount++;
            }
        });
    }

    private void ReceiveJobDelivery(JobDelivery delivery)
    {
        _log.Debug("SeqNr {0} was delivered to consumer.", delivery.SeqNr);
        if (Delay == TimeSpan.Zero)
            Self.Tell(new SomeAsyncJob(delivery.Msg, delivery.ConfirmTo, delivery.ProducerId, delivery.SeqNr));
        else
        {
            // schedule to simulate slower consumer
            Timers.StartSingleTimer(delivery, // have to use a unique-per-message key here, otherwise messages from multiple producers will cancel each other
                new SomeAsyncJob(delivery.Msg, delivery.ConfirmTo, delivery.ProducerId, delivery.SeqNr),
                TimeSpan.FromMilliseconds(10));
        }
    }

    protected override void PreStart()
    {
        ConsumerController.Tell(new ConsumerController.Start<Job>(Self));
    }

    public sealed class Job : IEquatable<Job>
    {
        public Job(string payload)
        {
            Payload = payload;
        }

        public string Payload { get; }

        public bool Equals(Job? other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Payload == other.Payload;
        }

        public override bool Equals(object? obj)
        {
            return ReferenceEquals(this, obj) || obj is Job other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Payload.GetHashCode();
        }
        
        public override string ToString()
        {
            return $"Job({Payload})";
        }
    }

    public interface ICommand
    {
    }

    public sealed record JobDelivery(Job Msg, IActorRef ConfirmTo, string ProducerId, long SeqNr) : ICommand;

    public sealed class SomeAsyncJob : ICommand
    {
        public SomeAsyncJob(Job msg, IActorRef confirmTo, string producerId, long seqNr)
        {
            Msg = msg;
            ConfirmTo = confirmTo;
            ProducerId = producerId;
            SeqNr = seqNr;
        }

        public Job Msg { get; }
        public IActorRef ConfirmTo { get; }
        public string ProducerId { get; }
        public long SeqNr { get; }
    }

    public sealed class Collected
    {
        public Collected(ImmutableHashSet<string> producerIds, int messageCount)
        {
            ProducerIds = producerIds;
            MessageCount = messageCount;
        }

        public ImmutableHashSet<string> ProducerIds { get; }
        public int MessageCount { get; }
    }

    public static ConsumerController.SequencedMessage<Job> SequencedMessage(string producerId, long seqNr, IActorRef producerController,
        bool ack = false)
    {
        return new ConsumerController.SequencedMessage<Job>(producerId, seqNr, new Job($"msg-{seqNr}"),
            seqNr == 1, ack, producerController);
    }

    private static Func<SomeAsyncJob, bool> ConsumerEndCondition(long seqNr) => msg => msg.SeqNr >= seqNr;

    public static Props PropsFor(TimeSpan delay, long seqNr, IActorRef endReplyTo, IActorRef consumerController, bool supportsRestarts = false) =>
        Props.Create(() => new TestConsumer(delay, ConsumerEndCondition(seqNr), endReplyTo, consumerController, supportsRestarts));

    public static Props PropsFor(TimeSpan delay, Func<SomeAsyncJob, bool> endCondition, IActorRef endReplyTo,
        IActorRef consumerController, bool supportsRestarts = false) =>
        Props.Create(() => new TestConsumer(delay, endCondition, endReplyTo, consumerController, supportsRestarts));

    public ITimerScheduler Timers { get; set; } = null!;
}

/// <summary>
/// For testing purposes
/// </summary>
public sealed class ZeroLengthSerializer : SerializerWithStringManifest
{
    public static readonly Config Config = ConfigurationFactory.ParseString(@"
        akka.actor {
            serializers {
                delivery-zero-length = ""Akka.Tests.Delivery.ZeroLengthSerializer, Akka.Tests""
            }
            serialization-bindings {
                ""Akka.Tests.Delivery.ZeroLengthSerializer+TestMsg, Akka.Tests"" = delivery-zero-length
            }
        }");
    
    public class TestMsg
    {
        private TestMsg()
        {
        }
        public static readonly TestMsg Instance = new();
    }

    public ZeroLengthSerializer(ExtendedActorSystem system) : base(system)
    {
    }

    public override byte[] ToBinary(object obj)
    {
        switch (obj)
        {
            case TestMsg _:
                return Array.Empty<byte>();
            default:
                throw new ArgumentException($"Can't serialize object of type [{obj.GetType()}]");
        }
    }

    public override object FromBinary(byte[] bytes, string manifest)
    {
        switch (manifest)
        {
            case "A":
                return TestMsg.Instance;
            default:
                throw new ArgumentException($"Unimplemented deserialization of message with manifest [{manifest}] in [{GetType()}]");
        }
       
    }

    public override string Manifest(object obj)
    {
        switch (obj)
        {
            case TestMsg _:
                return "A";
            default:
                throw new ArgumentException($"Can't serialize object of type [{obj.GetType()}]");
        }
    }
    
    public override int Identifier => 919191;
}

/// <summary>
/// INTERNAL API
/// </summary>
public sealed class TestSerializer : SerializerWithStringManifest
{
    public static readonly Config Config = ConfigurationFactory.ParseString(@"
        akka.actor {
            serializers {
                delivery-test = ""Akka.Tests.Delivery.TestSerializer, Akka.Tests""
            }
            serialization-bindings {
                ""Akka.Tests.Delivery.TestConsumer+Job, Akka.Tests"" = delivery-test
            }
        }");
    
    public TestSerializer(ExtendedActorSystem system) : base(system)
    {
    }

    public override byte[] ToBinary(object obj)
    {
        switch (obj)
        {
            case TestConsumer.Job job:
                return Encoding.UTF8.GetBytes(job.Payload);
            default:
                throw new ArgumentException($"Can't serialize object of type [{obj.GetType()}]");
        }
    }

    public override object FromBinary(byte[] bytes, string manifest)
    {
        return new TestConsumer.Job(Encoding.UTF8.GetString(bytes));
    }

    public override string Manifest(object o)
    {
        return string.Empty;
    }
    
    public override int Identifier => 787878;
}