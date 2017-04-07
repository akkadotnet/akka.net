﻿//-----------------------------------------------------------------------
// <copyright file="DistributedData.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Configuration;

namespace Akka.DistributedData
{
    /// <summary>
    /// Akka extension for convenient configuration and use of the
    /// <see cref="Akka.DistributedData.Replicator"/>. Configuration settings are defined in the
    /// `akka.cluster.ddata` section, see `reference.conf`.
    /// </summary>
    public class DistributedData : IExtension
    {
        private readonly ReplicatorSettings _settings;
        private readonly ActorSystem _system;

        /// <summary>
        /// Returns true if this member is not tagged with the role configured for the replicas.
        /// </summary>
        public bool IsTerminated => Cluster.Cluster.Get(_system).IsTerminated || (!string.IsNullOrEmpty(_settings.Role) && !Cluster.Cluster.Get(_system).SelfRoles.Contains(_settings.Role));

        /// <summary>
        /// Actor reference of the <see cref="Akka.DistributedData.Replicator"/>.
        /// </summary>
        public IActorRef Replicator { get; }

        /// <summary>
        /// Checks if a durable store for this extension is configured and in use.
        /// </summary>
        public bool IsDurable => _settings.IsDurable;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
        public static DistributedData Get(ActorSystem system) =>
            system.WithExtension<DistributedData, DistributedDataProvider>();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        public DistributedData(ExtendedActorSystem system)
        {
            system.Settings.InjectTopLevelFallback(DefaultConfig());
            var config = system.Settings.Config.GetConfig("akka.cluster.distributed-data");
            _settings = ReplicatorSettings.Create(config);
            _system = system;
            if (IsTerminated)
            {
                system.Log.Warning("Replicator points to dead letters: Make sure the cluster node is not terminated and has the proper role!");
                Replicator = system.DeadLetters;
            }
            else
            {
                var name = config.GetString("name");
                Replicator = system.ActorOf(Akka.DistributedData.Replicator.Props(_settings), name);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public static Config DefaultConfig() => 
            ConfigurationFactory.FromResource<DistributedData>("Akka.DistributedData.reference.conf");
    }

    /// <summary>
    /// TBD
    /// </summary>
    public class DistributedDataProvider : ExtensionIdProvider<DistributedData>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
        public override DistributedData CreateExtension(ExtendedActorSystem system) => new DistributedData(system);
    }

    public static class DistributedDataExtensions
    {
        /// <summary>
        /// Returns th <see cref="DistributedData"/> extension configured for provided 
        /// <paramref name="system"/>. Configuration is supplied automatically from HOCON 
        /// config under the path: `akka.cluster.distributed-data`
        /// </summary>
        public static DistributedData DistributedData(this ActorSystem system)
        {
            return Akka.DistributedData.DistributedData.Get(system);
        }
    }
}