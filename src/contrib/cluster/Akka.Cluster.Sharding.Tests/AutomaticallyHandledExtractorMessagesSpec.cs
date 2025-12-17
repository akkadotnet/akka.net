//-----------------------------------------------------------------------
// <copyright file="AutomaticallyHandledExtractorMessagesSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Sharding.Tests;

public class AutomaticallyHandledExtractorMessagesSpec
{
    public sealed record MyWrappedMessage(string EntityId, string Message);

    // custom IMessageExtractor
    public class MyMessageExtractor : IMessageExtractor
    {
        public string? EntityId(object message) => message switch
        {
            string s => s,
            MyWrappedMessage wrapped => wrapped.EntityId,
            _ => null
        };

        public object? EntityMessage(object message)
        {
            switch (message)
            {
                case MyWrappedMessage wrapped:
                    return wrapped.Message;
                default:
                    return message;
            }
        }

        public string? ShardId(object message) => message switch
        {
            string s => s,
            _ => null
        };

        public string ShardId(string entityId, object? messageHint = null)
        {
            return entityId;
        }
    }

    public static readonly TheoryData<(object shardingInput, object realMsg, string entityId, string shardId)>
        Messages = new()
        {
            // (new ShardRegion.StartEntity("foo"), new ShardRegion.StartEntity("foo"), "foo", "foo"),
            (new ShardingEnvelope("bar", "baz"), "baz", "bar", "bar"), ("bar", "bar", "bar", "bar"),
        };

    [Theory]
    [MemberData(nameof(Messages))]
    public void ShouldAutomaticallyHandleMessagesInCustomIMessageExtractor(
        (object shardingInput, object realMsg, string entityId, string shardId) data)
    {
        // arrange
        var extractor = new ExtractorAdapter(new MyMessageExtractor());

        // act
        var entityId = extractor.EntityId(data.shardingInput);
        var entityMessage = extractor.EntityMessage(data.shardingInput);
        var shardId = extractor.ShardId(entityId!, data.shardingInput);

        // assert
        entityId.Should().Be(data.entityId);
        entityMessage.Should().Be(data.realMsg);
        shardId.Should().Be(data.shardId);
    }

    [Fact]
    public void ShouldUnwrapMessageInsideShardingEnvelope()
    {
        // arrange
        var extractor = new ExtractorAdapter(new MyMessageExtractor());
        var myMessage = new MyWrappedMessage("entity1", "hello");
        var envelope = new ShardingEnvelope("entity1", myMessage);
        
        // act
        var entityId = extractor.EntityId(envelope);
        var entityMessage = extractor.EntityMessage(envelope);
        
        // assert
        entityId.Should().Be("entity1");
        entityMessage.Should().Be("hello");
    }
}