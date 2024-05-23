// -----------------------------------------------------------------------
//  <copyright file="OldChangedBufferSpecs.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------
#nullable enable
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Cluster.Tools.Tests.Singleton;

/// <summary>
/// Reproduction for https://github.com/akkadotnet/akka.net/issues/7196 - clearly, what we did
/// </summary>
public class OldChangedBufferSpecs : AkkaSpec
{
    public IActorRef CreateOldestChangedBuffer(string role, bool considerAppVersion)
    {
        return Sys.ActorOf(Props.Create(() => new OldestChangedBuffer(role, considerAppVersion)));
    }
    
    private readonly ActorSystem _otherNodeV1;
    private readonly ActorSystem _nonHostingNode;
    private ActorSystem? _otherNodeV2;
    
    public OldChangedBufferSpecs(ITestOutputHelper output) : base("""
                                          
                                                        akka.loglevel = INFO
                                                        akka.actor.provider = "cluster"
                                                        akka.cluster.roles = [singleton]
                                                        akka.cluster.auto-down-unreachable-after = 2s
                                                        akka.cluster.singleton.min-number-of-hand-over-retries = 5
                                                        akka.remote {
                                                          dot-netty.tcp {
                                                            hostname = "127.0.0.1"
                                                            port = 0
                                                          }
                                                        }
                                          """, output)
    {
        _otherNodeV1 = ActorSystem.Create(Sys.Name, Sys.Settings.Config);
        _nonHostingNode = ActorSystem.Create(Sys.Name, ConfigurationFactory.ParseString("akka.cluster.roles = [other]")
            .WithFallback(Sys.Settings.Config));
    }

    [Fact(DisplayName = "Singletons should not move to higher AppVersion nodes until after older incarnation is downed")]
    public async Task Bugfix7196Spec()
    {
        
    }
}