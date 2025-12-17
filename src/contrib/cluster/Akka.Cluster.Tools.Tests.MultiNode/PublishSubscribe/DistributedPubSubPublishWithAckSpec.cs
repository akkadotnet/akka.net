// -----------------------------------------------------------------------
//  <copyright file="DistributedPubSubPublishWithAckSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Configuration;
using Akka.Event;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using FluentAssertions;
using FluentAssertions.Extensions;

namespace Akka.Cluster.Tools.Tests.MultiNode.PublishSubscribe;

public class DistributedPubSubPublishWithAckSpecSpecConfig : MultiNodeConfig
{
    public readonly RoleName First;
    public readonly RoleName Second;

    public DistributedPubSubPublishWithAckSpecSpecConfig()
    {
        First = Role("first");
        Second = Role("second");

        CommonConfig = ConfigurationFactory.ParseString(
            """
            akka.loglevel = INFO
            akka.actor.provider = "Akka.Cluster.ClusterActorRefProvider, Akka.Cluster"
            akka.actor.serialize-messages = off
            akka.remote.log-remote-lifecycle-events = off
            akka.cluster.auto-down-unreachable-after = 0s
            akka.cluster.pub-sub.max-delta-elements = 500
            akka.cluster.pub-sub.buffered-messages.max-per-topic = 2
            akka.cluster.pub-sub.buffered-messages.timeout-check-interval = 200ms
            akka.testconductor.query-timeout = 1m # we were having timeouts shutting down nodes with 5s default
            """).WithFallback(DistributedPubSub.DefaultConfig());
    }
}

public class DistributedPubSubPublishWithAckSpec : MultiNodeClusterSpec
{
    #region setup
    private readonly RoleName _first;
    private readonly RoleName _second;

    public DistributedPubSubPublishWithAckSpec() : this(new DistributedPubSubPublishWithAckSpecSpecConfig())
    {
    }

    protected DistributedPubSubPublishWithAckSpec(DistributedPubSubPublishWithAckSpecSpecConfig config) : base(config, typeof(DistributedPubSubMediatorSpec))
    {
        _first = config.First;
        _second = config.Second;
    }

    public IActorRef Mediator => DistributedPubSub.Get(Sys).Mediator;

    private void Join(RoleName from, RoleName to)
    {
        RunOn(() =>
        {
            Cluster.Join(Node(to).Address);
            _ = DistributedPubSub.Get(Sys).Mediator;
        }, from);
        EnterBarrier(from.Name + "-joined");
    }
    
    #endregion

    [MultiNodeFact]
    public void DistributedPubSubPublishWithAckSpecs()
    {
        DistributedPubSubMediator_must_startup_2_nodes_cluster();
        PublishWithAck_must_buffer_message();
        Second_node_must_subscribe();
        PublishWithAck_must_deliver_buffered_messages();
    }

    public void DistributedPubSubMediator_must_startup_2_nodes_cluster()
    {
        Within(TimeSpan.FromSeconds(15), () =>
        {
            Join(_first, _first);
            Join(_second, _first);
            EnterBarrier("after-1");
        });
    }

    public void PublishWithAck_must_buffer_message()
    {
        Within(15.Seconds(), () =>
        {
            RunOn(() =>
            {
                Mediator.Tell(new PublishWithAck("content", "hi-1!", 20.Seconds()));
                Mediator.Tell(new PublishWithAck("content", "hi-2!", 20.Seconds()));
                ExpectNoMsg(200.Milliseconds());
            }, _first);
            
            RunOn(() =>
            {
                ExpectNoMsg(200.Milliseconds());
            }, _second);
            
            EnterBarrier("after-2");
        });
    }

    public void Second_node_must_subscribe()
    {
        RunOn(() =>
        {
            Mediator.Tell(new Subscribe("content", TestActor));
            // SubscribeAck must arrive first
            ExpectMsg<SubscribeAck>();
            Sys.Log.Info("Second node subscribed");
        }, _second);
        
        EnterBarrier("after-3");
    }

    public void PublishWithAck_must_deliver_buffered_messages()
    {
        Within(TimeSpan.FromSeconds(15), () =>
        {
            RunOn(() =>
            {
                ExpectMsg<PublishSucceeded>().Message.Message.Should().Be("hi-1!");
                ExpectMsg<PublishSucceeded>().Message.Message.Should().Be("hi-2!");
            }, _first);
            
            RunOn(() =>
            {
                ExpectMsg("hi-1!");
                ExpectMsg("hi-2!");
            }, _second);

            EnterBarrier("after-4");
        });
    }
}
