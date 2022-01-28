// //-----------------------------------------------------------------------
// // <copyright file="Seed.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Cluster.Cpu.Benchmark
{
    public class BenchmarkNode
    {
        private const string Address = "127.0.0.1";
        private const int BasePort = 15225;
        
        public static async Task<int> EntryPoint(string[] args)
        {
            var node = new BenchmarkNode(int.Parse(args[1]));
            node.Start();
            
            // wait forever until we get killed
            await Task.Delay(TimeSpan.FromDays(1));

            return 0;
        }

        private readonly Config _config;
        private ActorSystem _actorSystem;

        public BenchmarkNode(int nodeOffset)
        {
            _config = ConfigurationFactory.ParseString($@"
akka {{
    log-dead-letters = off
    log-dead-letters-during-shutdown = off

    actor.provider = cluster

    remote {{
        # log-remote-lifecycle-events = DEBUG
        dot-netty.tcp {{
            transport-class = ""Akka.Remote.Transport.DotNetty.TcpTransport, Akka.Remote""
            applied-adapters = []
            transport-protocol = tcp
            hostname = ""0.0.0.0""
            public-hostname = {Address}
            port = {BasePort + nodeOffset}
        }}
    }}

    cluster {{
        seed-nodes = [""{new Address("akka.tcp", nameof(BenchmarkNode), Address, BasePort)}""]
        roles = [benchmark-node]
    }}
}}");
        }

        public void Start()
        {
            _actorSystem = ActorSystem.Create(nameof(BenchmarkNode), _config);
        }

        public async Task StopAsync()
            => await CoordinatedShutdown.Get(_actorSystem).Run(CoordinatedShutdown.ClrExitReason.Instance);
    }
}