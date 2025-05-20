using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Configuration;
using Akka.TestKit;
using Akka.TestKit.Xunit2;
using Xunit;
using FluentAssertions;

namespace Akka.Cluster.Tools.Tests.PublishSubscribe
{
    public class DistributedPubSubMediatorUriEncodingSpec : AkkaSpec
    {
        public DistributedPubSubMediatorUriEncodingSpec() 
            : base(ConfigurationFactory.ParseString(@"
                akka.actor.provider = cluster
                akka.remote.dot-netty.tcp.port = 0
                akka.cluster.pub-sub.max-delta-elements = 50
                akka.cluster.pub-sub.log-buffer-size = 1000").WithFallback(TestKitBase.DefaultConfig))
        {
        }

        [Fact]
        public async Task DistributedPubSubMediator_Should_Handle_Topic_Names_With_Special_Characters()
        {
            // Arrange
            var pubSubMediator = DistributedPubSub.Get(Sys).Mediator;
            
            // Act & Assert - Topic with colon
            await TestTopicWithSpecialCharacter(pubSubMediator, "parent:child");
            
            // Act & Assert - Topic with forward slash
            await TestTopicWithSpecialCharacter(pubSubMediator, "parent/child");
            
            // Act & Assert - Topic with question mark
            await TestTopicWithSpecialCharacter(pubSubMediator, "parent?child");
            
            // Act & Assert - Topic with hash
            await TestTopicWithSpecialCharacter(pubSubMediator, "parent#child");
            
            // Act & Assert - Topic with percent sign
            await TestTopicWithSpecialCharacter(pubSubMediator, "parent%child");
            
            // Act & Assert - Topic with plus sign
            await TestTopicWithSpecialCharacter(pubSubMediator, "parent+child");
            
            // Act & Assert - Topic with at sign
            await TestTopicWithSpecialCharacter(pubSubMediator, "parent@child");
        }
        
        private async Task TestTopicWithSpecialCharacter(IActorRef pubSubMediator, string topic)
        {
            // Arrange
            var probe = CreateTestProbe();
            var message = new TestMessage($"Hello from {topic}");
            
            // Subscribe to the topic with special character
            await pubSubMediator.Ask<SubscribeAck>(
                new Subscribe(topic, probe.Ref));
            
            // Act - publish a message to the topic
            pubSubMediator.Tell(new Publish(topic, message));
            
            // Assert - the message should be received
            probe.ExpectMsg<TestMessage>(m => 
                m.Content == message.Content, 
                TimeSpan.FromSeconds(3), 
                $"Failed to receive message for topic: {topic}");
        }
        
        private class TestMessage
        {
            public TestMessage(string content)
            {
                Content = content;
            }
            
            public string Content { get; }
        }
    }
} 