//-----------------------------------------------------------------------
// <copyright file="SplitBrainResolver.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Cluster
{
    public sealed class SplitBrainResolver : IDowningProvider
    {
        private readonly ClusterSettings _clusterSettings;
        private readonly Config _splitBrainResolverConfig;

        public SplitBrainResolver(ActorSystem system)
        {
            _clusterSettings = Cluster.Get(system).Settings;
            _splitBrainResolverConfig = system.Settings.Config.GetConfig("akka.cluster.split-brain-resolver");
        }

        public TimeSpan DownRemovalMargin => _clusterSettings.DownRemovalMargin;
        public Props DowningActorProps => SplitBrainStrategy.Props(_splitBrainResolverConfig);
    }

    internal abstract class SplitBrainStrategy : ActorBase
    {
        public static Actor.Props Props(Config config)
        {
            var activeStrategy = config.GetString("active-strategy");
            switch (activeStrategy)
            {
                case "static-quorum": return Actor.Props.Create(() => new StaticQuorum(StaticQuorum.Settings.Create(config.GetConfig("static-quorum")))).WithDeploy(Deploy.Local);
                case "keep-majority": return Actor.Props.Create(() => new KeepMajority(KeepMajority.Settings.Create(config.GetConfig("keep-majority")))).WithDeploy(Deploy.Local);
                case "keep-oldest": return Actor.Props.Create(() => new KeepOldest(KeepOldest.Settings.Create(config.GetConfig("keep-oldest")))).WithDeploy(Deploy.Local);
                case "keep-referee": return Actor.Props.Create(() => new KeepReferee(KeepReferee.Settings.Create(config.GetConfig("keep-referee")))).WithDeploy(Deploy.Local);
                default: throw new ConfigurationException($"Unrecognized value [{activeStrategy}] of `akka.cluster.split-brain-resolver.active-strategy`. Supported options are: static-quorum | keep-majority | keep-oldest | keep-referee.");
            }
        }

        protected override bool Receive(object message)
        {
            throw new NotImplementedException();
        }
    }

    internal sealed class StaticQuorum : SplitBrainStrategy
    {
        #region internal classes

        public sealed class Settings
        {
            public static Settings Create(Config config)
            {
                if (config == null) throw new ArgumentNullException(nameof(config));

                return new Settings(
                    quorumSize: config.GetInt("quorum-size"),
                    role: config.GetString("role", string.Empty));
            }

            public Settings(int quorumSize, string role)
            {
                QuorumSize = quorumSize;
                Role = role;
            }

            /// <summary>
            /// Minimum number of nodes that the cluster must have.
            /// </summary>
            public int QuorumSize { get; }

            /// <summary>
            /// If the 'role' is defined the decision is based only on members with that 'role'.
            /// </summary>
            public string Role { get; }
        }

        #endregion

        public StaticQuorum(Settings settings)
        {
            throw new NotImplementedException();
        }
    }

    internal sealed class KeepMajority : SplitBrainStrategy
    {
        #region internal classes

        public sealed class Settings
        {
            public static Settings Create(Config config)
            {
                if (config == null) throw new ArgumentNullException(nameof(config));

                return new Settings(
                    role: config.GetString("role", string.Empty));
            }

            public Settings(string role)
            {
                Role = role;
            }

            /// <summary>
            /// If the 'role' is defined the decision is based only on members with that 'role'.
            /// </summary>
            public string Role { get; }
        }

        #endregion

        public KeepMajority(Settings settings)
        {
            throw new NotImplementedException();
        }
    }

    internal sealed class KeepOldest : SplitBrainStrategy
    {
        #region internal classes

        public sealed class Settings
        {
            public static Settings Create(Config config)
            {
                if (config == null) throw new ArgumentNullException(nameof(config));

                return new Settings(
                    downIfAlone: config.GetBoolean("down-if-alone", true),
                    role: config.GetString("role", string.Empty));
            }

            public Settings(bool downIfAlone, string role)
            {
                DownIfAlone = downIfAlone;
                Role = role;
            }

            /// <summary>
            /// Enable downing of the oldest node when it is partitioned from all other nodes.
            /// </summary>
            public bool DownIfAlone { get; }

            /// <summary>
            /// If the 'role' is defined the decision is based only on members with that 'role',
            /// i.e. using the oldest member (singleton) within the nodes with that role.
            /// </summary>
            public string Role { get; }
        }

        #endregion

        public KeepOldest(Settings settings)
        {
            throw new NotImplementedException();
        }
    }

    internal sealed class KeepReferee : SplitBrainStrategy
    {
        #region internal classes

        public sealed class Settings
        {
            public static Settings Create(Config config)
            {
                if (config == null) throw new ArgumentNullException(nameof(config));

                return new Settings(
                    refereeAddress: Address.Parse(config.GetString("address")), 
                    downAllIfLessThanNodes: config.GetInt("down-all-if-less-than-nodes", 1));
            }

            public Settings(Address refereeAddress, int downAllIfLessThanNodes)
            {
                ReferreeAddress = refereeAddress;
                DownAllIfLessThanNodes = downAllIfLessThanNodes;
            }

            public Address ReferreeAddress { get; }
            public int DownAllIfLessThanNodes { get; }
        }

        #endregion

        public KeepReferee(Settings settings)
        {
            throw new NotImplementedException();
        }
    }
}