// -----------------------------------------------------------------------
//  <copyright file="UncleanIndirectlyConnectedSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System.Collections.Immutable;
using Akka.Actor;
using Akka.Util;
using FluentAssertions;
using Newtonsoft.Json;
using Xunit;
using static FluentAssertions.FluentActions;

namespace Akka.Cluster.Tests.SBR;

public class BugFix7141
{
    private class ReachabilityData
    {
        public Reachability.Record[] Records { get; set; }
        public VersionData[] Versions { get; set; }
        public Address[] SeenBy { get; set; }
    }
    
    private class VersionData
    {
        public UniqueAddress Address { get; set; }
        public long Version { get; set; }
    }
    
    private const string RecordData = """
{
  "Records": [
    {
      "Observer":
      {
        "Address":
        {
          "Host": "localhost",
          "Port": 6005,
          "System": "Sys",
          "Protocol": "akka.tcp"
        },
        "Uid": 5
      },
      "Subject":
      {
        "Address":
        {
          "Host": "localhost",
          "Port": 6003,
          "System": "Sys",
          "Protocol": "akka.tcp"
        },
        "Uid": 3
      },
      "Status": 1,
      "Version": 1
    },
    {
      "Observer":
      {
        "Address":
        {
          "Host": "localhost",
          "Port": 6006,
          "System": "Sys",
          "Protocol": "akka.tcp"
        },
        "Uid": 6
      },
      "Subject":
      {
        "Address":
        {
          "Host": "localhost",
          "Port": 6003,
          "System": "Sys",
          "Protocol": "akka.tcp"
        },
        "Uid": 3
      },
      "Status": 1,
      "Version": 1
    },
    {
      "Observer":
      {
        "Address":
        {
          "Host": "localhost",
          "Port": 6006,
          "System": "Sys",
          "Protocol": "akka.tcp"
        },
        "Uid": 6
      },
      "Subject":
      {
        "Address":
        {
          "Host": "localhost",
          "Port": 6005,
          "System": "Sys",
          "Protocol": "akka.tcp"
        },
        "Uid": 5
      },
      "Status": 1,
      "Version": 3
    },
    {
      "Observer":
      {
        "Address":
        {
          "Host": "localhost",
          "Port": 6006,
          "System": "Sys",
          "Protocol": "akka.tcp"
        },
        "Uid": 6
      },
      "Subject":
      {
        "Address":
        {
          "Host": "localhost",
          "Port": 6004,
          "System": "Sys",
          "Protocol": "akka.tcp"
        },
        "Uid": 4
      },
      "Status": 1,
      "Version": 2
    },
    {
      "Observer":
      {
        "Address":
        {
          "Host": "localhost",
          "Port": 6002,
          "System": "Sys",
          "Protocol": "akka.tcp"
        },
        "Uid": 2
      },
      "Subject":
      {
        "Address":
        {
          "Host": "localhost",
          "Port": 6003,
          "System": "Sys",
          "Protocol": "akka.tcp"
        },
        "Uid": 3
      },
      "Status": 1,
      "Version": 1
    },
    {
      "Observer":
      {
        "Address":
        {
          "Host": "localhost",
          "Port": 6002,
          "System": "Sys",
          "Protocol": "akka.tcp"
        },
        "Uid": 2
      },
      "Subject":
      {
        "Address":
        {
          "Host": "localhost",
          "Port": 6005,
          "System": "Sys",
          "Protocol": "akka.tcp"
        },
        "Uid": 5
      },
      "Status": 1,
      "Version": 3
    },
    {
      "Observer":
      {
        "Address":
        {
          "Host": "localhost",
          "Port": 6002,
          "System": "Sys",
          "Protocol": "akka.tcp"
        },
        "Uid": 2
      },
      "Subject":
      {
        "Address":
        {
          "Host": "localhost",
          "Port": 6004,
          "System": "Sys",
          "Protocol": "akka.tcp"
        },
        "Uid": 4
      },
      "Status": 1,
      "Version": 2
    },
    {
      "Observer":
      {
        "Address":
        {
          "Host": "localhost",
          "Port": 6001,
          "System": "Sys",
          "Protocol": "akka.tcp"
        },
        "Uid": 1
      },
      "Subject":
      {
        "Address":
        {
          "Host": "localhost",
          "Port": 6003,
          "System": "Sys",
          "Protocol": "akka.tcp"
        },
        "Uid": 3
      },
      "Status": 1,
      "Version": 1
    },
    {
      "Observer":
      {
        "Address":
        {
          "Host": "localhost",
          "Port": 6001,
          "System": "Sys",
          "Protocol": "akka.tcp"
        },
        "Uid": 1
      },
      "Subject":
      {
        "Address":
        {
          "Host": "localhost",
          "Port": 6005,
          "System": "Sys",
          "Protocol": "akka.tcp"
        },
        "Uid": 5
      },
      "Status": 1,
      "Version": 3
    },
    {
      "Observer":
      {
        "Address":
        {
          "Host": "localhost",
          "Port": 6001,
          "System": "Sys",
          "Protocol": "akka.tcp"
        },
        "Uid": 1
      },
      "Subject":
      {
        "Address":
        {
          "Host": "localhost",
          "Port": 6004,
          "System": "Sys",
          "Protocol": "akka.tcp"
        },
        "Uid": 4
      },
      "Status": 1,
      "Version": 2
    }
  ],
  "Versions": [
    {
      "Address":
      {
        "Address":
        {
          "Host": "localhost",
          "Port": 6005,
          "System": "Sys",
          "Protocol": "akka.tcp"
        },
        "Uid": 5
      },
      "Version": 1
    },
    {
      "Address":
      {
        "Address":
        {
          "Host": "localhost",
          "Port": 6006,
          "System": "Sys",
          "Protocol": "akka.tcp"
        },
        "Uid": 6
      },
      "Version": 3
    },
    {
      "Address":
      {
        "Address":
        {
          "Host": "localhost",
          "Port": 6002,
          "System": "Sys",
          "Protocol": "akka.tcp"
        },
        "Uid": 2
      },
      "Version": 3
    },
    {
      "Address":
      {
        "Address":
        {
          "Host": "localhost",
          "Port": 6001,
          "System": "Sys",
          "Protocol": "akka.tcp"
        },
        "Uid": 1
      },
      "Version": 3
    }
  ],
  "SeenBy": [
    {
      "Host": "localhost",
      "Port": 6002,
      "System": "Sys",
      "Protocol": "akka.tcp"
    },
    {
      "Host": "localhost",
      "Port": 6006,
      "System": "Sys",
      "Protocol": "akka.tcp"
    },
    {
      "Host": "localhost",
      "Port": 6001,
      "System": "Sys",
      "Protocol": "akka.tcp"
    }
  ]
}
""";

    [Fact]
    public void ShouldWork()
    {
        // arrange

        var data = JsonConvert.DeserializeObject<ReachabilityData>(
            RecordData, 
            new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All
            });
        
        // create split brain resolver
        var resolver = new Akka.Cluster.SBR.KeepMajority(string.Empty);

        // create unique addresses for members
        var address1 = new UniqueAddress(new Address("akka.tcp", "Sys", "localhost", 6001), 1);
        var address2 = new UniqueAddress(new Address("akka.tcp", "Sys", "localhost", 6002), 2);
        var address3 = new UniqueAddress(new Address("akka.tcp", "Sys", "localhost", 6003), 3);
        var address4 = new UniqueAddress(new Address("akka.tcp", "Sys", "localhost", 6004), 4);
        var address5 = new UniqueAddress(new Address("akka.tcp", "Sys", "localhost", 6005), 5);
        var address6 = new UniqueAddress(new Address("akka.tcp", "Sys", "localhost", 6006), 6);

        // create members
        var member1 = new Member(address1, 5, MemberStatus.Up, ImmutableHashSet.Create("role1"), AppVersion.Zero);
        var member2 = new Member(address2, 4, MemberStatus.Up, ImmutableHashSet.Create("role1"), AppVersion.Zero);
        var member3 = new Member(address3, 3, MemberStatus.Up, ImmutableHashSet.Create("role1"), AppVersion.Zero);
        var member4 = new Member(address4, 2, MemberStatus.Up, ImmutableHashSet.Create("role1"), AppVersion.Zero);
        var member5 = new Member(address5, 1, MemberStatus.Up, ImmutableHashSet.Create("role1"), AppVersion.Zero);
        var member6 = new Member(address6, 0, MemberStatus.Up, ImmutableHashSet.Create("role1"), AppVersion.Zero);

        // all other nodes up
        resolver.Add(Up(address1));
        resolver.Add(Up(address2));
        resolver.Add(Up(address3));
        resolver.Add(Up(address4));
        resolver.Add(Up(address5));
        resolver.Add(Up(address6));
        
        // set reachability
        resolver.AddReachable(member1);
        resolver.AddUnreachable(member2);
        resolver.AddUnreachable(member3);
        resolver.AddUnreachable(member4);
        resolver.AddReachable(member5);
        resolver.AddReachable(member6);

        var reachability = new Reachability(
            data.Records.ToImmutableList(), 
            data.Versions.ToImmutableDictionary(d => d.Address, d => d.Version));
        
        resolver.SetSeenBy(data.SeenBy.ToImmutableHashSet());
        resolver.SetReachability(reachability);
        
        var expectedDown = new[] { address2, address3, address4 }.ToImmutableHashSet();
        ImmutableHashSet<UniqueAddress> downedNodes = null;
        Invoking(() => downedNodes = resolver.NodesToDown()).Should().NotThrow();
        downedNodes.Should().BeEquivalentTo(expectedDown);
    }

    private Member Up(UniqueAddress address)
        => Member.Create(address, 0, MemberStatus.Up, ImmutableHashSet.Create("role1"), AppVersion.Zero);
}