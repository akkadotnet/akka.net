// -----------------------------------------------------------------------
//  <copyright file="DistributedPubSubPublishWithAckSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Configuration;
using Akka.TestKit;
using FluentAssertions.Extensions;
using Xunit;

namespace Akka.Cluster.Tools.Tests.PublishSubscribe;

[Collection(nameof(DistributedPubSubMediatorSpec))]
public class DistributedPubSubPublishWithAckSpec : AkkaSpec
{
    public DistributedPubSubPublishWithAckSpec(ITestOutputHelper output) : base(GetConfig(), output)
    {
    }

    private static Config GetConfig()
    {
        return ConfigurationFactory.ParseString(
            """
            akka.actor.provider = cluster
            akka.cluster.pub-sub.buffered-messages.max-per-topic = 2
            akka.cluster.pub-sub.buffered-messages.timeout-check-interval = 100ms
            """);
    }

    [Fact(DisplayName = "PublishWithAck message should be buffered")]
    public async Task PublishWithAckBufferTest()
    {
        var mediator = DistributedPubSub.Get(Sys).Mediator;
        mediator.Tell(new PublishWithAck("topic", "msg-1", 10.Seconds()));
        mediator.Tell(new PublishWithAck("topic", "msg-2", 10.Seconds()));

        // Should not send NACKs
        await ExpectNoMsgAsync(200.Milliseconds());
        
        // Should not re-send messages if topic does not match
        mediator.Tell(new Subscribe("topic2", TestActor));
        var subAck = await ExpectMsgAsync<SubscribeAck>();
        subAck.Subscribe.Topic.ShouldBe("topic2");
        await ExpectNoMsgAsync(200.Milliseconds());
        
        // Should not re-send messages if topic match
        mediator.Tell(new Subscribe("topic", TestActor));
        subAck = await ExpectMsgAsync<SubscribeAck>();
        subAck.Subscribe.Topic.ShouldBe("topic");

        await ExpectMsgAllOfMatchingPredicatesAsync([ 
            PredicateInfo.Create<string>(msg => msg is "msg-1"), 
            PredicateInfo.Create<PublishSucceeded>(msg => msg.Message is { Message: "msg-1", Topic: "topic" }), 
            PredicateInfo.Create<string>(msg => msg is "msg-2"), 
            PredicateInfo.Create<PublishSucceeded>(msg => msg.Message is { Message: "msg-2", Topic: "topic" }) ]).ToListAsync();

        // Should not send extra messages
        await ExpectNoMsgAsync(200.Milliseconds());
    }
    
    [Fact(DisplayName = "PublishWithAck message buffer should NACK buffer overflowed messages")]
    public async Task PublishWithAckBufferOverflowTest()
    {
        var mediator = DistributedPubSub.Get(Sys).Mediator;
        mediator.Tell(new PublishWithAck("topic", "msg-1", 10.Seconds()));
        mediator.Tell(new PublishWithAck("topic", "msg-2", 10.Seconds()));
        
        // Should dequeue and NACK msg-1, the oldest message in the buffer
        mediator.Tell(new PublishWithAck("topic", "msg-3", 10.Seconds()));
        var failed = await ExpectMsgAsync<PublishFailed>();
        failed.Message.Topic.ShouldBe("topic");
        failed.Message.Message.ShouldBe("msg-1");
        
        // Should dequeue and NACK msg-2, the oldest message in the buffer
        mediator.Tell(new PublishWithAck("topic", "msg-4", 10.Seconds()));
        failed = await ExpectMsgAsync<PublishFailed>();
        failed.Message.Topic.ShouldBe("topic");
        failed.Message.Message.ShouldBe("msg-2");
        
        // Should not re-send messages if topic match.
        // msg-1 and msg-2 should never be re-sent
        mediator.Tell(new Subscribe("topic", TestActor));
        var subAck = await ExpectMsgAsync<SubscribeAck>();
        subAck.Subscribe.Topic.ShouldBe("topic");

        await ExpectMsgAllOfMatchingPredicatesAsync([ 
            PredicateInfo.Create<string>(msg => msg is "msg-3"), 
            PredicateInfo.Create<PublishSucceeded>(msg => msg.Message is { Message: "msg-3", Topic: "topic" }), 
            PredicateInfo.Create<string>(msg => msg is "msg-4"), 
            PredicateInfo.Create<PublishSucceeded>(msg => msg.Message is { Message: "msg-4", Topic: "topic" }) ]).ToListAsync();

        // Should not send extra messages
        await ExpectNoMsgAsync(200.Milliseconds());
    }
    
    [Fact(DisplayName = "PublishWithAck message should fail when timed-out")]
    public async Task PublishWithAckMessageTimeoutTest()
    {
        var mediator = DistributedPubSub.Get(Sys).Mediator;
        mediator.Tell(new PublishWithAck("topic", "msg-1", 300.Milliseconds()));
        mediator.Tell(new PublishWithAck("topic", "msg-2", 10.Milliseconds()));
        
        // msg-2 should time out first
        var failed = await ExpectMsgAsync<PublishFailed>();
        failed.Message.Topic.ShouldBe("topic");
        failed.Message.Message.ShouldBe("msg-2");

        // msg-1 should time out second
        failed = await ExpectMsgAsync<PublishFailed>();
        failed.Message.Topic.ShouldBe("topic");
        failed.Message.Message.ShouldBe("msg-1");
        
        // Buffer is empty, should not re-send anything
        mediator.Tell(new Subscribe("topic", TestActor));
        var subAck = await ExpectMsgAsync<SubscribeAck>();
        subAck.Subscribe.Topic.ShouldBe("topic");

        // Should not send extra messages
        await ExpectNoMsgAsync(200.Milliseconds());
    }
}