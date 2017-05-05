﻿//-----------------------------------------------------------------------
// <copyright file="DistributedData.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
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

        #region async API

        /// <summary>
        /// Asynchronously returns list of locally known keys.
        /// </summary>
        /// <returns></returns>
        public async Task<ImmutableHashSet<string>> GetKeysAsync()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Asynchronously tries to get a replicated value of type <typeparamref name="T"/> stored 
        /// under a given <paramref name="key"/>, while trying to achieve provided read 
        /// <paramref name="consistency"/>.
        /// 
        /// If no <paramref name="consistency"/> will be provided, a <see cref="ReadLocal"/> will be used.
        /// </summary>
        /// <exception cref="KeyNotFoundException">Thrown if no value was stored at the moment of get request.</exception>
        /// <exception cref="DataDeletedException">Thrown if value under provided <paramref name="key"/> was permamently deleted. That key can't be used anymore.</exception>
        /// <exception cref="TimeoutException">Thrown if get request consistency was not achieved within possible time limit attached to a provided read <paramref name="consistency"/>.</exception>
        /// <typeparam name="T">Replicated data type to get.</typeparam>
        /// <param name="key">Key under which a replicated data is stored.</param>
        /// <param name="consistency">A read consistency requested for this write.</param>
        /// <returns>A task which may return a replicated data value or throw an exception.</returns>
        public async Task<T> GetAsync<T>(IKey<T> key, IReadConsistency consistency = null) where T : IReplicatedData<T>
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Asynchronously tries to update a replicated value stored under provided <paramref name="key"/> 
        /// with a <paramref name="replica"/> value within write <paramref name="consistency"/> boundaries. 
        /// In case of possible conflict a <see cref="IReplicatedData{T}.Merge(T)"/> operation will be performed.
        ///  
        /// If no <paramref name="consistency"/> will be provided, a <see cref="WriteLocal"/> will be used.
        /// Keep in mind that failure doesn't mean that write has failed, only that consistency limits were 
        /// not fulfilled. The value will be probably further updated as propagated using gossip protocol.
        /// </summary>
        /// <exception cref="DataDeletedException">Thrown if value under provided <paramref name="key"/> was permamently deleted. That key can't be used anymore.</exception>
        /// <exception cref="TimeoutException">Thrown if update request consistency was not achieved within possible time limit attached to a provided read <paramref name="consistency"/>.</exception>
        /// <typeparam name="T">Replicated data type to update.</typeparam>
        /// <param name="key">Key under which a replicated data is stored.</param>
        /// <param name="replica">Value used to perform an update.</param>
        /// <param name="consistency">A write consistency requested for this write.</param>
        /// <returns>A task which may complete successfully if update was confirmed within provided consistency or throw an exception.</returns>
        public async Task UpdateAsync<T>(IKey<T> key, T replica, IWriteConsistency consistency = null) where T : IReplicatedData<T>
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Asynchronously tries to delete a replicated value stored under provided <paramref name="key"/> within 
        /// specified <paramref name="consistency"/> boundaries. Once deleted, provided key can no longer be used.
        /// As deletion must be remembered, deleted keys will occupy a small portion of memory, producing a garbadge.
        /// 
        /// If no <paramref name="consistency"/> will be provided, a <see cref="WriteLocal"/> will be used.
        /// Keep in mind that failure doesn't mean that delete has failed, only that consistency limits were 
        /// not fulfilled. The deletion will be propagated using gossip protocol.
        /// </summary>
        /// <exception cref="TimeoutException">Thrown if update request consistency was not achieved within possible time limit attached to a provided read <paramref name="consistency"/>.</exception>
        /// <typeparam name="T">Replicated data type to update.</typeparam>
        /// <param name="key">Key under which a replicated data is stored.</param>
        /// <param name="consistency">A consistency level requested for this deletion.</param>
        /// <returns></returns>
        public async Task DeleteAsync<T>(IKey<T> key, IWriteConsistency consistency = null) where T : IReplicatedData<T>
        {
            throw new NotImplementedException();
        }

        #endregion
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