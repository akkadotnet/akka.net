//-----------------------------------------------------------------------
// <copyright file="AutomaticallyHandledExtractorMessagesSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Sharding.Tests;

public class AutomaticallyHandledExtractorMessagesSpec
{
    // custom IMessageExtractor
    public class MyMessageExtractor : IMessageExtractor
    {
        public string? EntityId(object message) => message switch
        {
            string s => s,
            _ => null
        };

        public object? EntityMessage(object message) => message;

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
    
#pragma warning disable CS0618 // Type or member is obsolete
    private ExtractEntityId ExtractEntityId = message =>
    {
        if (message is string s)
            return (s, s);
        return Option<(string, object)>.None;
    };

    private ExtractShardId ExtractShardId = message =>
    {
        if (message is string s)
            return s;
        return null!;
    };
#pragma warning restore CS0618 // Type or member is obsolete

    public static readonly TheoryData<(object shardingInput, object realMsg, string entityId, string shardId)> Messages = new()
    {
        // (new ShardRegion.StartEntity("foo"), new ShardRegion.StartEntity("foo"), "foo", "foo"),
        (new ShardingEnvelope("bar", "baz"), "baz", "bar", "bar"),
        ("bar", "bar", "bar", "bar"),
    };
    
    [Theory]
    [MemberData(nameof(Messages))]
    public void ShouldAutomaticallyHandleMessagesInCustomIMessageExtractor((object shardingInput, object realMsg, string entityId, string shardId) data)
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
    
    // NOTE: so the old delegates are hopeless and will simply not work - you HAVE to handle the messages yourself there
    // need to repeat of the previous test but using the deprecated delegate methods and the adapter
    // [Theory]
    // [MemberData(nameof(Messages))]
    // public void ShouldAutomaticallyHandleMessagesInCustomIMessageExtractorUsingDelegates((object shardingInput, object realMsg, string entityId, string shardId) data)
    // {
    //     // arrange
    //     var extractor = new ExtractorAdapter(new DeprecatedHandlerExtractorAdapter(ExtractEntityId, ExtractShardId));
    //     
    //     // act
    //     var entityId = extractor.EntityId(data.shardingInput);
    //     var entityMessage = extractor.EntityMessage(data.shardingInput);
    //     var shardId = extractor.ShardId(entityId!, data.shardingInput);
    //     
    //     // assert
    //     entityId.Should().Be(data.entityId);
    //     entityMessage.Should().Be(data.realMsg);
    //     shardId.Should().Be(data.shardId);
    // }
}
