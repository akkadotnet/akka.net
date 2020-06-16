using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Tests
{
    public class ProviderSelectionSpec
    {
        public ActorSystemSetup Setup { get; } = ActorSystemSetup.Create();
        public Config LocalConfig { get; } = ConfigurationFactory.Load();

        public Settings SettingsWith(string key)
        {
            var c = ConfigurationFactory.ParseString($"akka.actor.provider = \"{key}\"")
                .WithFallback(LocalConfig);
            return new Settings(null, c, Setup);
        }

        [Fact]
        public void ProviderSelectionMustCreateLocalProviderSelection()
        {
            var ps = ProviderSelection.Local.Instance;
            ps.Fqn.Should()
                .Be(ProviderSelection.LocalActorRefProvider);
            ps.HasCluster.Should().BeFalse();
            SettingsWith("local").ProviderClass.Should().Be(ps.Fqn);
        }

        [Fact]
        public void ProviderSelectionMustCreateRemoteProviderSelection()
        {
            var ps = ProviderSelection.Remote.Instance;
            ps.Fqn.Should()
                .Be(ProviderSelection.RemoteActorRefProvider);
            ps.HasCluster.Should().BeFalse();
            SettingsWith("remote").ProviderClass.Should().Be(ps.Fqn);
        }

        [Fact]
        public void ProviderSelectionMustCreateClusterProviderSelection()
        {
            var ps = ProviderSelection.Cluster.Instance;
            ps.Fqn.Should()
                .Be(ProviderSelection.ClusterActorRefProvider);
            ps.HasCluster.Should().BeTrue();
            SettingsWith("cluster").ProviderClass.Should().Be(ps.Fqn);
        }

        [Fact]
        public void ProviderSelectionMustCreateCustomProviderSelection()
        {
            var other = ProviderSelection.ClusterActorRefProvider;
                var ps = new ProviderSelection.Custom(other, "cluster");
            ps.Fqn.Should()
                .Be(other);
            ps.HasCluster.Should().BeFalse();
            SettingsWith(other).ProviderClass.Should().Be(ps.Fqn);
        }
    }
}
