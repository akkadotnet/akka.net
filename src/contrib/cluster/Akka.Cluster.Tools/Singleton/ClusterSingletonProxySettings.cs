//-----------------------------------------------------------------------
// <copyright file="ClusterSingletonProxySettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Cluster.Singleton
{
    public sealed class ClusterSingletonProxySettings
    {
        public static ClusterSingletonProxySettings Create(ActorSystem system)
        {
            system.Settings.InjectTopLevelFallback(ClusterSingletonManager.DefaultConfig());
            var config = system.Settings.Config.GetConfig("akka.cluster.singleton-proxy");
            if (config == null)
                throw new ConfigurationException(string.Format("Cannot create {0}: akka.cluster.singleton-proxy configuration node not found", typeof(ClusterSingletonProxySettings)));

            return Create(config);
        }

        public static ClusterSingletonProxySettings Create(Config config)
        {
            var role = config.GetString("role");
            if (role == string.Empty) role = null;

            return new ClusterSingletonProxySettings(
                singletonName: config.GetString("singleton-name"),
                role: role,
                singletonIdentificationInterval: config.GetTimeSpan("singleton-identification-interval"),
                bufferSize: config.GetInt("buffer-size"));
        }

        /// <summary>
        /// Creates new instance of the <see cref="ClusterSingletonProxySettings"/>.
        /// </summary>
        /// <param name="singletonName">The actor name of the singleton actor that is started by the <see cref="ClusterSingletonManager"/>.</param>
        /// <param name="role">The role of the cluster nodes where the singleton can be deployed.</param>
        /// <param name="singletonIdentificationInterval">Interval at which the proxy will try to resolve the singleton instance.</param>
        /// <param name="bufferSize">If the location of the singleton is unknown the proxy will buffer this number of messages and deliver them when the singleton is identified.</param>
        public ClusterSingletonProxySettings(string singletonName, string role, TimeSpan singletonIdentificationInterval, int bufferSize)
        {
            if (string.IsNullOrEmpty(singletonName))
                throw new ArgumentNullException(nameof(singletonName));
            if (singletonIdentificationInterval == TimeSpan.Zero)
                throw new ArgumentException("ClusterSingletonProxySettings.SingletonIdentificationInterval must be positive", nameof(singletonIdentificationInterval));
            if (bufferSize <= 0)
                throw new ArgumentException("ClusterSingletonProxySettings.BufferSize must be positive", nameof(bufferSize));

            SingletonName = singletonName;
            Role = role;
            SingletonIdentificationInterval = singletonIdentificationInterval;
            BufferSize = bufferSize;
        }

        /// <summary>
        /// The actor name of the singleton actor that is started by the <see cref="ClusterSingletonManager"/>.
        /// </summary>
        public string SingletonName { get; }

        /// <summary>
        /// The role of the cluster nodes where the singleton can be deployed.
        /// </summary>
        public string Role { get; }

        /// <summary>
        /// Interval at which the proxy will try to resolve the singleton instance.
        /// </summary>
        public TimeSpan SingletonIdentificationInterval { get; }

        /// <summary>
        /// If the location of the singleton is unknown the proxy will buffer this number of messages and deliver them when the singleton is identified.
        /// </summary>
        public int BufferSize { get; }

        /// <summary>
        /// Create a singleton proxy with specified singleton name.
        /// </summary>
        public ClusterSingletonProxySettings WithSingletonName(string singletonName)
        {
            return Copy(singletonName: singletonName);
        }

        /// <summary>
        /// Create a singleton proxy with specified singleton role
        /// </summary>
        public ClusterSingletonProxySettings WithRole(string role)
        {
            return new ClusterSingletonProxySettings(
                singletonName: SingletonName,
                role: role,
                singletonIdentificationInterval: SingletonIdentificationInterval,
                bufferSize: BufferSize);
        }

        /// <summary>
        /// Create a singleton proxy with specified singleton identification interval.
        /// </summary>
        public ClusterSingletonProxySettings WithSingletonIdentificationInterval(string singletonIdentificationInterval)
        {
            return Copy(singletonIdentificationInterval: SingletonIdentificationInterval);
        }

        /// <summary>
        /// Create a singleton proxy with specified singleton buffer size.
        /// </summary>
        public ClusterSingletonProxySettings WithBufferSize(int bufferSize)
        {
            return Copy(bufferSize: bufferSize);
        }

        private ClusterSingletonProxySettings Copy(string singletonName = null, string role = null,
            TimeSpan? singletonIdentificationInterval = null, int? bufferSize = null)
        {
            return new ClusterSingletonProxySettings(
                singletonName: singletonName ?? SingletonName,
                role: role ?? Role,
                singletonIdentificationInterval: singletonIdentificationInterval ?? SingletonIdentificationInterval,
                bufferSize: bufferSize ?? BufferSize);
        }
    }
}