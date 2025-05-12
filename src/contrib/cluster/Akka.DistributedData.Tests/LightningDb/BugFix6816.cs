//-----------------------------------------------------------------------
// <copyright file="BugFix6816.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using Akka.Actor;
using Akka.Configuration;
using Akka.DistributedData.Durable;
using Akka.DistributedData.LightningDB;
using Akka.Event;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.DistributedData.Tests.LightningDb;

[Collection("DistributedDataSpec")]
public class BugFix6816: Akka.TestKit.Xunit2.TestKit
{
    private const string DDataDir = "thatdir";
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

    public BugFix6816(ITestOutputHelper output): base(BaseConfig, "LmdbDurableStoreSpec", output)
    {
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
        var lmdb = ActorOf(LmdbDurableStore.Props(LmdbDefaultConfig));
        lmdb.Tell(LoadAll.Instance);          
        AwaitCondition(() => HasMessages);
        ExpectMsg<LoadAllCompleted>();
        Directory.Exists(DDataDir).Should().BeTrue();
    }

    [Fact]
    public void Lmdb_logs_meaningful_error_for_invalid_dir_path()
    {
        var invalidName = Environment.OSVersion.Platform is PlatformID.Win32NT 
            ? "\"invalid?directory\"" : "/dev/null/illegal";
        
        Directory.Exists(invalidName).Should().BeFalse();

        var probe = CreateTestProbe();
        Sys.EventStream.Subscribe(probe, typeof(Error));
        
        var lmdb = ActorOf(LmdbDurableStore.Props(ConfigurationFactory.ParseString($@"
        lmdb {{
            dir = {invalidName}
            map-size = 100 MiB
            write-behind-interval = off
        }}")));

        //Expect meaningful error log
        var err =probe.ExpectMsg<Error>();
        err.Message.ToString().Should()
            .NotContain("Error while creating actor instance of type Akka.DistributedData.LightningDB.LmdbDurableStore");
        err.Cause.Should().BeOfType<ActorInitializationException>();
        err.Cause.InnerException.Should().NotBeNull();
        (err.Cause.InnerException is IOException or DirectoryNotFoundException or ArgumentException).Should().BeTrue();
        
        Directory.Exists(invalidName).Should().BeFalse();
    }
    
}
