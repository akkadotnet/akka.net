//-----------------------------------------------------------------------
// <copyright file="RemoteRouterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Routing;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests
{

    public class RemoteConsistentHashingRouterSpec : AkkaSpec
    {

        public RemoteConsistentHashingRouterSpec()
            : base(@"akka.actor.provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""")
        {
            
        }

        [Fact]
        public void ConsistentHashingGroup_must_use_same_hash_ring_indepenent_of_self_address()
        {
            // simulating running router on two different nodes (a1, a2) with target routees on 3 other nodes (s1, s2, s3) 
            var a1 = new Address("akka.tcp", "Sys", "client1", 2552);
            var a2 = new Address("akka.tcp", "Sys", "client2", 2552);
            var s1 = new ActorSelectionRoutee(Sys.ActorSelection("akka.tcp://Sys@server1:2552/user/a/b"));
            var s2 = new ActorSelectionRoutee(Sys.ActorSelection("akka.tcp://Sys@server2:2552/user/a/b"));
            var s3 = new ActorSelectionRoutee(Sys.ActorSelection("akka.tcp://Sys@server3:2552/user/a/b"));
            var nodes1 = new List<ConsistentRoutee>(new [] { new ConsistentRoutee(s1, a1), new ConsistentRoutee(s2, a1), new ConsistentRoutee(s3, a1)});
            var nodes2 =
                new List<ConsistentRoutee>(new[]
                {new ConsistentRoutee(s1, a2), new ConsistentRoutee(s2, a2), new ConsistentRoutee(s3, a2)});
            var consistentHash1 = ConsistentHash.Create(nodes1, 10);
            var consistentHash2 = ConsistentHash.Create(nodes2, 10);
            var keys = new List<string>(new [] { "A", "B", "C", "D", "E", "F", "G"});
            var result1 = keys.Select(k => consistentHash1.NodeFor(k).Routee);
            var result2 = keys.Select(k => consistentHash2.NodeFor(k).Routee);
            Assert.Equal(result2,result1);
        }
    }
}

