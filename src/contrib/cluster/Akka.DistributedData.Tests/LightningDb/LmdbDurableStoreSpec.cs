// //-----------------------------------------------------------------------
// // <copyright file="LmdbSpec.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System.IO;
using Akka.Actor;
using Akka.Configuration;
using Akka.DistributedData.Durable;
using Akka.DistributedData.LightningDB;
using Xunit;
using Xunit.Abstractions;

namespace Akka.DistributedData.Tests.LightningDb
{
    public class LmdbDurableStoreSpec
    {
        private const string DDataDir = "thisdir";
        private readonly ITestOutputHelper _output;
        
        private static readonly Config BaseConfig = ConfigurationFactory.ParseString($@"
            akka.actor {{
                provider=""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
            }}
            akka.remote.dot-netty.tcp.port = 0
            akka.cluster.distributed-data.durable.lmdb {{
                dir = {DDataDir}
                map-size = 100 MiB
                write-behind-interval = off
            }}").WithFallback(DistributedData.DefaultConfig())
            .WithFallback(TestKit.Xunit2.TestKit.DefaultConfig);

        public LmdbDurableStoreSpec(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void Lmdb_should_not_throw_when_opening_existing_directory()
        {
            if(Directory.Exists(DDataDir))
            {
                var di = new DirectoryInfo(DDataDir);
                di.Delete(true);
            }
            Directory.CreateDirectory(DDataDir);

            var testKit = new TestKit.Xunit2.TestKit(BaseConfig, nameof(LmdbDurableStoreSpec), _output);
            var probe = testKit.CreateTestProbe();

            var config = testKit.Sys.Settings.Config.GetConfig("akka.cluster.distributed-data.durable");
            var lmdb = testKit.Sys.ActorOf(LmdbDurableStore.Props(config));
            lmdb.Tell(LoadAll.Instance, probe.Ref);

            probe.ExpectMsg<LoadAllCompleted>();
        }
    }
}