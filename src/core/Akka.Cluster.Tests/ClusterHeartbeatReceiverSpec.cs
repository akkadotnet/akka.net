﻿//-----------------------------------------------------------------------
// <copyright file="ClusterHeartbeatReceiverSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;
using static Akka.Cluster.ClusterHeartbeatSender;

namespace Akka.Cluster.Tests
{
    public class ClusterHeartbeatReceiverSpec : ClusterHeartbeatReceiverBase
    {
        public ClusterHeartbeatReceiverSpec(ITestOutputHelper output) : base(output, false)
        {
        }
    }
    
    public class ClusterHeartbeatReceiverLegacySpec : ClusterHeartbeatReceiverBase
    {
        public ClusterHeartbeatReceiverLegacySpec(ITestOutputHelper output) : base(output, true)
        {
        }
    }
    
    public abstract class ClusterHeartbeatReceiverBase : AkkaSpec
    {
        private static Config Config(bool useLegacyHeartbeat) => $@"
akka.loglevel=DEBUG
akka.cluster.debug.verbose-heartbeat-logging = on
akka.actor.provider = cluster
akka.cluster.use-legacy-heartbeat-message = {(useLegacyHeartbeat ? "true" : "false")}
";
        
        protected ClusterHeartbeatReceiverBase(ITestOutputHelper output, bool useLegacyHeartbeat)
            : base(Config(useLegacyHeartbeat), output)
        {

        }

        [Fact]
        public async Task ClusterHeartbeatReceiver_should_respond_to_heartbeats_with_same_SeqNo_and_SendTime()
        {
            var heartbeater = Sys.ActorOf(ClusterHeartbeatReceiver.Props(Cluster.Get(Sys)));
            heartbeater.Tell(new Heartbeat(Cluster.Get(Sys).SelfAddress, 1, 2));
            await ExpectMsgAsync(new HeartbeatRsp(Cluster.Get(Sys).SelfUniqueAddress, 1, 2));
        }
        
        [Fact]
        public async Task ClusterHeartbeatReceiver_should_write_correct_debug_messages_on_heartbeat()
        {
            var heartbeater = Sys.ActorOf(ClusterHeartbeatReceiver.Props(Cluster.Get(Sys)));

            await EventFilter.Debug(contains: "- Sequence number [2]")
                .ExpectOneAsync(() => { heartbeater.Tell(new Heartbeat(Cluster.Get(Sys).SelfAddress, 2, 3)); return Task.CompletedTask; });
        }
        
        [Fact]
        public async Task ClusterHeartbeatSender_should_write_correct_debug_messages_on_heartbeat_rsp()
        {
            var heartbeater = Sys.ActorOf(Props.Create(() => new ClusterHeartbeatSender(Cluster.Get(Sys))));
            heartbeater.Tell(new ClusterEvent.CurrentClusterState());
            
            await EventFilter.Debug(contains: "- Sequence number [2] - Creation time [00:00:03]")
                .ExpectOneAsync(() => { heartbeater.Tell(new HeartbeatRsp(Cluster.Get(Sys).SelfUniqueAddress, 2, 3000000000)); return Task.CompletedTask; });
        }
    }
}
