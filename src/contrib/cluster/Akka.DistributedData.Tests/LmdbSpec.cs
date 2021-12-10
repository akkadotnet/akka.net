// //-----------------------------------------------------------------------
// // <copyright file="LmdbSpec.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System.IO;
using System.Threading;
using Akka.Actor;
using Akka.Configuration;
using Akka.DistributedData.Durable;
using Akka.DistributedData.LightningDB;
using Xunit;
using Xunit.Abstractions;

namespace Akka.DistributedData.Tests
{
    public class LmdbSpec: TestKit.Xunit2.TestKit
    {
        private static readonly Config BaseConfig = ConfigurationFactory.ParseString(@"
            akka.actor {
                provider=""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
            }
            akka.remote.dot-netty.tcp.port = 0
            akka.cluster.distributed-data.durable.lmdb {
                dir = thisdir
                map-size = 100 MiB
                write-behind-interval = off

            }").WithFallback(DistributedData.DefaultConfig());

        public LmdbSpec(ITestOutputHelper output) : base(BaseConfig, nameof(LmdbSpec), output: output)
        {
        }

        [Fact]
        public void Lmdb_opening_existing_directory_should_work()
        {
            var probe = CreateTestProbe();

            Directory.CreateDirectory("./thisdir");
            
            var config = Sys.Settings.Config.GetConfig("akka.cluster.distributed-data.durable");
            var lmdb = Sys.ActorOf(LmdbDurableStore.Props(config));
            lmdb.Tell(LoadAll.Instance, probe.Ref);

            probe.ExpectMsg<LoadAllCompleted>();
        }
    }
}