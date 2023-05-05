// -----------------------------------------------------------------------
//  <copyright file="ShardingConsumerControllerImpl.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Delivery;
using Akka.Event;
using Akka.Util.Extensions;

namespace Akka.Cluster.Sharding.Delivery.Internal;

/// <summary>
/// INTERNAL API
/// </summary>
/// <typeparam name="T">The types of messages handled by the ConsumerController</typeparam>
internal class ShardingConsumerController<T> : ReceiveActor, IWithStash
{
    public ShardingConsumerController(Func<IActorRef, Props> consumerProps,
        ShardingConsumerController.Settings settings)
    {
        ConsumerProps = consumerProps;
        Settings = settings;
        WaitForStart();
    }

    public Func<IActorRef, Props> ConsumerProps { get; }
    public ShardingConsumerController.Settings Settings { get; }

    private readonly ILoggingAdapter _log = Context.GetLogger();

    // gets started asynchronously in the PreStart method
    private IActorRef _consumer = ActorRefs.Nobody;

    /// <summary>
    /// Map of producerControllers to producerIds
    /// </summary>
    public ImmutableDictionary<IActorRef, string> ProducerControllers { get; private set; } =
        ImmutableDictionary<IActorRef, string>.Empty;

    /// <summary>
    /// Map of producerIds to consumerControllers
    /// </summary>
    public ImmutableDictionary<string, IActorRef> ConsumerControllers { get; private set; } =
        ImmutableDictionary<string, IActorRef>.Empty;

    private void WaitForStart()
    {
        Receive<ConsumerController.Start<T>>(start =>
        {
            ConsumerController.AssertLocalConsumer(start.DeliverTo);
            Context.Unwatch(_consumer);
            _consumer = start.DeliverTo;
            Context.Watch(start.DeliverTo);
            Become(Active);
            Stash.UnstashAll();
        });

        Receive<ConsumerController.IConsumerCommand<T>>(command => { Stash.Stash(); });

        Receive<Terminated>(t =>
        {
            _log.Debug("Consumer terminated before initialized.");
            Context.Stop(Self);
        });
    }

    private void Active()
    {
        Receive<ConsumerController.SequencedMessage<T>>(seqMsg =>
        {
            if (ConsumerControllers.TryGetValue(seqMsg.ProducerId, out var consumerController))
            {
                consumerController.Tell(seqMsg);
                ProducerControllers = UpdatedProducerControllers(seqMsg.ProducerController, seqMsg.ProducerId);
            }
            else
            {
                _log.Debug("Starting ConsumerController for producerId {0}", seqMsg.ProducerId);
                var cc = Context.ActorOf(
                    ConsumerController.Create<T>(Context, seqMsg.ProducerController.AsOption(),
                        Settings.ConsumerControllerSettings), Uri.EscapeUriString($"consumerController-{seqMsg.ProducerId}"));
                Context.Watch(cc);
                cc.Tell(new ConsumerController.Start<T>(_consumer));
                cc.Tell(seqMsg);
                ConsumerControllers = ConsumerControllers.Add(seqMsg.ProducerId, cc);
                ProducerControllers = UpdatedProducerControllers(seqMsg.ProducerController, seqMsg.ProducerId);
            }
        });

        Receive<Terminated>(t => t.ActorRef.Equals(_consumer), terminated =>
        {
            _log.Debug("Consumer terminated.");
            Context.Stop(Self);
        });

        Receive<Terminated>(t =>
        {
            if (ProducerControllers.TryGetValue(t.ActorRef, out var producer))
            {
                _log.Debug("ProducerController for producerId [{0}] terminated.", producer);
                ProducerControllers = ProducerControllers.Remove(t.ActorRef);
                if (ConsumerControllers.TryGetValue(producer, out var consumerController))
                {
                    consumerController.Tell(ConsumerController.DeliverThenStop<T>.Instance);
                }
            }
            else
            {
                var found = false;
                foreach (var kv in ConsumerControllers.Where(c =>
                             c.Value.Equals(t.ActorRef)))
                {
                    found = true;
                    _log.Debug("ConsumerController for producerId [{0}] terminated.", kv.Key);
                    ConsumerControllers = ConsumerControllers.Remove(kv.Key);
                }

                if (!found)
                {
                    _log.Debug("Unknown [{0}] terminated.", t.ActorRef);
                }
            }
        });
    }

    private ImmutableDictionary<IActorRef, string> UpdatedProducerControllers(IActorRef producerController,
        string producer)
    {
        if (ProducerControllers.ContainsKey(producerController))
            return ProducerControllers;
        Context.Watch(producerController);
        return ProducerControllers.Add(producerController, producer);
    }

    protected override void PreStart()
    {
        var self = Self;
        _consumer = Context.ActorOf(ConsumerProps(self), "consumer");
        Context.Watch(_consumer);
    }

    public IStash Stash { get; set; } = null!;
}