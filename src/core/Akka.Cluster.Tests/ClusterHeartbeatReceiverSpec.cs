//-----------------------------------------------------------------------
// <copyright file="ClusterHeartbeatReceiverSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;
using static Akka.Cluster.ClusterHeartbeatSender;

namespace Akka.Cluster.Tests
{
    public class ClusterHeartbeatReceiverSpec : AkkaSpec
    {
        public static Config Config = @"akka.actor.provider = cluster";

        public ClusterHeartbeatReceiverSpec(ITestOutputHelper output)
            : base(Config, output)
        {

        }

        [Fact]
        public void ClusterHeartbeatReceiver_should_respond_to_heartbeats_with_same_SeqNo_and_SendTime()
        {
            var heartbeater = Sys.ActorOf(ClusterHeartbeatReceiver.Props(() => Cluster.Get(Sys)));
            heartbeater.Tell(new Heartbeat(Cluster.Get(Sys).SelfAddress, 1, 2));
            ExpectMsg<HeartbeatRsp>(new HeartbeatRsp(Cluster.Get(Sys).SelfUniqueAddress, 1, 2));
        }
    }
}
