﻿using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote;
using Akka.TestKit;
using Xunit;

namespace Akka.Cluster.Tests
{
    /// <summary>
    /// This class will be used to verify if akka.actor.provider aliases work as designed.
    /// It's placed here instead of Akka.Tests to verify, that aliases are correctly resolved to actual classes.
    /// </summary>
    public class ActorRefProvidersConfigSpec
    {
        [Fact]
        public void ActorProviderConfig_should_resolve_local_alias()
        {
            ConfigureAndVerify("local", typeof(LocalActorRefProvider));
        }

        [Fact]
        public void ActorProviderConfig_should_resolve_remote_alias()
        {
            ConfigureAndVerify("remote", typeof(RemoteActorRefProvider));
        }

        [Fact]
        public void ActorProviderConfig_should_resolve_cluster_alias()
        {
            ConfigureAndVerify("cluster", typeof(ClusterActorRefProvider));
        }

        private void ConfigureAndVerify(string alias, Type actorProviderType)
        {
            var config = ConfigurationFactory.ParseString(@"akka.actor.provider = " + alias);
            using (var system = ActorSystem.Create(nameof(ActorRefProvidersConfigSpec), config))
            {
                var ext = (ExtendedActorSystem) system;
                ext.Provider.GetType().ShouldBe(actorProviderType);
            }
        }
    }
}