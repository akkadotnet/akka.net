//-----------------------------------------------------------------------
// <copyright file="LmdbDurableStoreSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.IO;
using Akka.Actor;
using Akka.Configuration;
using Akka.DistributedData.Durable;
using Akka.DistributedData.LightningDB;
using Akka.Event;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.DistributedData.Tests.LightningDb
{
    [Collection("LmdbDurableStoreSpec")]
    public class LmdbDurableStoreSpec : Akka.TestKit.Xunit2.TestKit
    {
        private const string DDataDir = "thisdir";
        private static readonly Config BaseConfig = ConfigurationFactory.ParseString(@"
                akka.actor.provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
                akka.remote.dot-netty.tcp.port = 0")
            .WithFallback(DistributedData.DefaultConfig())
            .WithFallback(TestKit.Xunit2.TestKit.DefaultConfig);

        private static readonly Config LmdbDefaultConfig = ConfigurationFactory.ParseString($@"
            lmdb {{
                dir = {DDataDir}
                map-size = 100 MiB
                write-behind-interval = off
            }}");

        public LmdbDurableStoreSpec(ITestOutputHelper output): base(BaseConfig, "LmdbDurableStoreSpec", output)
        {
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
            var lmdb = ActorOf(LmdbDurableStore.Props(LmdbDefaultConfig));
            lmdb.Tell(LoadAll.Instance);
            ExpectMsg<LoadAllCompleted>();
        }

        [Fact]
        public void Lmdb_creates_directory_when_handling_first_message()
        {
            if (Directory.Exists(DDataDir))
            {
                var di = new DirectoryInfo(DDataDir);
                di.Delete(true);
            }

            Directory.Exists(DDataDir).Should().BeFalse();
            IActorRef lmdb = ActorOf(LmdbDurableStore.Props(LmdbDefaultConfig));
            lmdb.Tell(LoadAll.Instance);          
            AwaitCondition(() => HasMessages);
            ExpectMsg<LoadAllCompleted>();
            Directory.Exists(DDataDir).Should().BeTrue();
        }

        [Fact]
        public void Lmdb_logs_meaningful_error_on_invalid_path()
        {
            // Try to create a directory with a path exceeding allowed length 
            string invalidName = new string('A', 247);           
            Directory.Exists(invalidName).Should().BeFalse();
            IActorRef lmdb = ActorOf(LmdbDurableStore.Props(ConfigurationFactory.ParseString($@"
            lmdb {{
                dir = {invalidName}
                map-size = 100 MiB
                write-behind-interval = off
            }}")));

            //Expect meaningful error log
            EventFilter.Custom(logEvent => logEvent is Error 
            && !logEvent.Message.ToString().Contains("Error while creating actor instance of type Akka.DistributedData.LightningDB.LmdbDurableStore")
            && logEvent.Cause is LoadFailedException 
            && logEvent.Cause.InnerException != null
            && logEvent.Cause.InnerException is DirectoryNotFoundException).ExpectOne(() =>
            {
                lmdb.Tell(LoadAll.Instance);
            });

            Directory.Exists(invalidName).Should().BeFalse();
        }
    }
}
