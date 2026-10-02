using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.DistributedData;
using Akka.Hosting;
using Akka.Remote.Hosting;
using Xunit;

namespace Akka.Cluster.Hosting.Tests;

public class ClusterShardingDistributedDataSpecs: Akka.Hosting.TestKit.TestKit
{
    private const string ReplicatorName = "dDataReplicator";
    
    public ClusterShardingDistributedDataSpecs(ITestOutputHelper output): base(output: output)
    {
    }
    
    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        builder
            .WithRemoting()
            .WithClustering()
            // Join the cluster during host startup (matching the other cluster specs) rather than in
            // the test body, so cluster formation completes before the test body runs.
            .WithActors(async (system, _) =>
            {
                var cluster = Cluster.Get(system);
                await cluster.JoinAsync(cluster.SelfAddress);
            })
            .WithDistributedData(opt =>
            {
                opt.Name = ReplicatorName;
            });
    }

    [Fact(DisplayName = "WithDistributedData should start DistributedData extension automatically")]
    public async Task WithDistributedDataStartsAutomaticallyTest()
    {
        var cluster = Cluster.Get(Sys);
        await AwaitAssertAsync(() =>
                Assert.Equal(1, cluster.State.Members.Count(m => m.Status == MemberStatus.Up)),
            interval: TimeSpan.FromMilliseconds(200),
            duration: TimeSpan.FromSeconds(10));
        
        var settings = ReplicatorSettings.Create(Sys);
        var coordinatorName = settings.RestartReplicatorOnFailure ? $"{ReplicatorName}Supervisor" : ReplicatorName;
        
        var actorSelection = Sys.ActorSelection(new RootActorPath(cluster.SelfAddress) / "user" / coordinatorName);

        // Use a fresh TestProbe rather than TestActor: on Windows, TestActor can be a stale dead
        // reference due to the startup race window between EnsureTestActorAliveAsync and the test body.
        var probe = CreateTestProbe();
        await AwaitAssertAsync(async () =>
        {
            actorSelection.Tell(new Identify("coordinator"), probe.Ref);
            var identity = await probe.ExpectMsgAsync<ActorIdentity>(TimeSpan.FromSeconds(1));
            Assert.NotNull(identity.Subject);
        }, duration: TimeSpan.FromSeconds(10));
    }
}