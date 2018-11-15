//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Cluster.Tools.Client;
using Akka.Configuration;

namespace Samples.Cluster.ClusterClient.Client
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            Config config = @"
                akka.actor.provider = remote
                akka.remote.dot-netty.tcp.hostname = localhost
                akka.remote.dot-netty.tcp.port = 0 #random port
            ";

            var actorSystem = ActorSystem.Create("ClusterClient", config
                .WithFallback(ClusterClientReceptionist.DefaultConfig()));
            var receptionistAddress = Address.Parse("akka.tcp://clusterNodes@localhost:13310");
            var pinger = actorSystem.ActorOf(Props.Create(() => new PingerActor(receptionistAddress)), "pinger");

            actorSystem.WhenTerminated.Wait();
        }
    }
}
