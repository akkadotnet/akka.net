// -----------------------------------------------------------------------
//  <copyright file="ClusterOptionsSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System.Collections.Generic;
using System.IO;
using System.Text;
using Akka.Actor;
using Akka.Cluster.Hosting.SBR;
using Akka.Cluster.SBR;
using Akka.Configuration;
using Akka.Hosting;
using Akka.Remote.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Akka.Cluster.Hosting.Tests;

public class ClusterOptionsSpec
{
    [Fact(DisplayName = "Empty ClusterOptions should contain default HOCON values")]
    public void EmptyClusterOptionsTest()
    {
        var builder = new AkkaConfigurationBuilder(new ServiceCollection(), "")
            .AddHocon(ConfigurationFactory.FromResource("Akka.Cluster.Configuration.Cluster.conf", typeof(Cluster).Assembly), HoconAddMode.Append)
            .WithActorRefProvider(ProviderSelection.Cluster.Instance)
            .BuildClusterHocon(new ClusterOptions());
        
        Assert.True(builder.Configuration.HasValue);

        var settings = new ClusterSettings(builder.Configuration.Value, "");

        Assert.Empty(settings.Roles);
        Assert.Equal(0, settings.AppVersion.CompareTo(Util.AppVersion.Create("assembly-version")));
        Assert.Empty(settings.MinNrOfMembersOfRole);
        Assert.Empty(settings.SeedNodes);
        Assert.Equal(1, settings.MinNrOfMembers);
        Assert.True(settings.LogInfo);
        Assert.False(settings.LogInfoVerbose);
        Assert.Equal(typeof(SplitBrainResolverProvider), settings.DowningProviderType);
        Assert.Equal(1.Seconds(), settings.HeartbeatInterval);
        Assert.Equal(1.Seconds(), settings.HeartbeatExpectedResponseAfter);
    }
    
    [Fact(DisplayName = "ClusterOptions should generate proper HOCON values")]
    public void ClusterOptionsTest()
    {
        var builder = new AkkaConfigurationBuilder(new ServiceCollection(), "")
            .AddHocon(ConfigurationFactory.FromResource("Akka.Cluster.Configuration.Cluster.conf", typeof(Cluster).Assembly), HoconAddMode.Append)
            .BuildClusterHocon(new ClusterOptions
            {
                Roles = new []{ "front-end", "back-end"},
                MinimumNumberOfMembersPerRole = new Dictionary<string, int>
                {
                    ["back-end"] = 5
                },
                AppVersion = "1.0.0",
                MinimumNumberOfMembers = 99,
                SeedNodes = new [] { "akka.tcp://system@somewhere.com:9999" },
                LogInfo = false,
                LogInfoVerbose = true,
                SplitBrainResolver = new KeepMajorityOption
                {
                    Role = "back-end"
                },
                FailureDetector = new PhiAccrualFailureDetectorOptions
                {
                    HeartbeatInterval = 1.1.Seconds(),
                    AcceptableHeartbeatPause = 1.1.Seconds(),
                    Threshold = 1.1,
                    MaxSampleSize = 1,
                    MinStandardDeviation = 1.1.Seconds(),
                    UnreachableNodesReaperInterval = 1.1.Seconds(),
                    ExpectedResponseAfter = 1.1.Seconds()
                }
            });
        
        Assert.True(builder.Configuration.HasValue);
        var settings = new ClusterSettings(builder.Configuration.Value, "");

        new[] { "front-end", "back-end" }.CollectionEquals(settings.Roles);
        
        Assert.Single(settings.MinNrOfMembersOfRole);
        Assert.True(settings.MinNrOfMembersOfRole.ContainsKey("back-end"));
        Assert.Equal(5, settings.MinNrOfMembersOfRole["back-end"]);
        
        Assert.Equal(0, settings.AppVersion.CompareTo(Util.AppVersion.Create("1.0.0")));
        Assert.Equal(new[] { Address.Parse("akka.tcp://system@somewhere.com:9999") }, settings.SeedNodes);
        Assert.Equal(99, settings.MinNrOfMembers);
        Assert.True(settings.LogInfo); // This is not intuitive, but LogInfo is defined as LogInfoVerbose || LogInfo in ClusterSettings
        Assert.True(settings.LogInfoVerbose);
        Assert.Equal(typeof(SplitBrainResolverProvider), settings.DowningProviderType);

        var sbrConfig = builder.Configuration.Value.GetConfig("akka.cluster.split-brain-resolver");
        Assert.Equal(SplitBrainResolverSettings.KeepMajorityName, sbrConfig.GetString("active-strategy"));
        Assert.Equal("back-end", sbrConfig.GetString($"{SplitBrainResolverSettings.KeepMajorityName}.role"));

        var detectorConfig = builder.Configuration.Value.GetConfig("akka.cluster.failure-detector");
        Assert.Equal(1.1.Seconds(), detectorConfig.GetTimeSpan("heartbeat-interval"));
        Assert.Equal(1.1.Seconds(), detectorConfig.GetTimeSpan("acceptable-heartbeat-pause"));
        Assert.Equal(1.1, detectorConfig.GetDouble("threshold"));
        Assert.Equal(1, detectorConfig.GetInt("max-sample-size"));
        Assert.Equal(1.1.Seconds(), detectorConfig.GetTimeSpan("min-std-deviation"));
        Assert.Equal(1.1.Seconds(), detectorConfig.GetTimeSpan("unreachable-nodes-reaper-interval"));
        Assert.Equal(1.1.Seconds(), detectorConfig.GetTimeSpan("expected-response-after"));
    }

    [Fact(DisplayName = "ClusterOptions should be bindable using Microsoft.Extensions.Configuration")]
    public void ClusterOptionsConfigurationTest()
    {
        const string json = @"
{
  ""Logging"": {
    ""LogLevel"": {
      ""Default"": ""Information"",
      ""Microsoft.AspNetCore"": ""Warning""
    }
  },
  ""ConnectionStrings"": {
    ""sqlServerLocal"": ""Server=localhost,1533;Database=Akka;User Id=sa;Password=l0lTh1sIsOpenSource;"",
  },
  ""Akka"": {
    ""ClusterOptions"": {
      ""Roles"": [ ""front-end"", ""back-end"" ],
      ""MinimumNumberOfMembersPerRole"" : {
        ""back-end"" : 5
      },
      ""AppVersion"": ""1.0.0"",
      ""MinimumNumberOfMembers"": 99,
      ""SeedNodes"": [ ""akka.tcp://system@somewhere.com:9999"" ],
      ""LogInfo"": false,
      ""LogInfoVerbose"": true
    },
    ""KeepMajorityOption"": {
      ""Role"" : ""back-end""
    }
  }
}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var jsonConfig = new ConfigurationBuilder().AddJsonStream(stream).Build();
        
        var clusterOptions = jsonConfig.GetSection("Akka:ClusterOptions").Get<ClusterOptions>()!;
        clusterOptions.SplitBrainResolver = jsonConfig.GetSection("Akka:KeepMajorityOption").Get<KeepMajorityOption>();
        
        var builder = new AkkaConfigurationBuilder(new ServiceCollection(), "")
            .AddHocon(ConfigurationFactory.FromResource("Akka.Cluster.Configuration.Cluster.conf", typeof(Cluster).Assembly), HoconAddMode.Append)
            .BuildClusterHocon(clusterOptions);
        
        Assert.True(builder.Configuration.HasValue);
        var settings = new ClusterSettings(builder.Configuration.Value, "");

        new[] { "front-end", "back-end" }.CollectionEquals(settings.Roles);
        
        Assert.Single(settings.MinNrOfMembersOfRole);
        Assert.True(settings.MinNrOfMembersOfRole.ContainsKey("back-end"));
        Assert.Equal(5, settings.MinNrOfMembersOfRole["back-end"]);
        
        Assert.Equal(0, settings.AppVersion.CompareTo(Util.AppVersion.Create("1.0.0")));
        Assert.Equal(new[] { Address.Parse("akka.tcp://system@somewhere.com:9999") }, settings.SeedNodes);
        Assert.Equal(99, settings.MinNrOfMembers);
        Assert.True(settings.LogInfo); // This is not intuitive, but LogInfo is defined as LogInfoVerbose || LogInfo in ClusterSettings
        Assert.True(settings.LogInfoVerbose);
        Assert.Equal(typeof(SplitBrainResolverProvider), settings.DowningProviderType);

        var sbrConfig = builder.Configuration.Value.GetConfig("akka.cluster.split-brain-resolver");
        Assert.Equal(SplitBrainResolverSettings.KeepMajorityName, sbrConfig.GetString("active-strategy"));
        Assert.Equal("back-end", sbrConfig.GetString($"{SplitBrainResolverSettings.KeepMajorityName}.role"));
    }
}