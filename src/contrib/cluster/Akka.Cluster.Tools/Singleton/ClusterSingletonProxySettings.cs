//-----------------------------------------------------------------------
// <copyright file="ClusterSingletonProxySettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Cluster.Tools.Singleton
{
    public sealed class ClusterSingletonProxySettings
    {
        public static ClusterSingletonProxySettings Create(ActorSystem system)
        {
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

        public readonly string SingletonName;
        public readonly string Role;
        public readonly TimeSpan SingletonIdentificationInterval;
        public readonly int BufferSize;

        /// <summary>
        /// Creates new instance of the <see cref="ClusterSingletonProxySettings"/>.
        /// </summary>
        /// <param name="singletonName">The actor name of the singleton actor that is started by the <see cref="ClusterSingletonManager"/>.</param>
        /// <param name="role">The role of the cluster nodes where the singleton can be deployed. If None, then any node will do.</param>
        /// <param name="singletonIdentificationInterval">Interval at which the proxy will try to resolve the singleton instance.</param>
        /// <param name="bufferSize">
        /// If the location of the singleton is unknown the proxy will buffer this number of messages and deliver them
        /// when the singleton is identified.When the buffer is full old messages will be droppedwhen new messages 
        /// are sent viea the proxy.Use 0 to disable buffering, i.e.messages will be dropped immediately if the location 
        /// of the singleton is unknown.
        /// </param>
        public ClusterSingletonProxySettings(string singletonName, string role, TimeSpan singletonIdentificationInterval, int bufferSize)
        {
            if (string.IsNullOrEmpty(singletonName))
                throw new ArgumentNullException("singletonName");
            if (singletonIdentificationInterval == TimeSpan.Zero)
                throw new ArgumentException("ClusterSingletonProxySettings.SingletonIdentificationInterval must be positive", "singletonIdentificationInterval");
            if (bufferSize <= 0)
                throw new ArgumentException("ClusterSingletonProxySettings.BufferSize must be positive", "bufferSize");

            SingletonName = singletonName;
            Role = role;
            SingletonIdentificationInterval = singletonIdentificationInterval;
            BufferSize = bufferSize;
        }

        public ClusterSingletonProxySettings WithSingletonName(string singletonName)
        {
            return Copy(singletonName: singletonName);
        }

        public ClusterSingletonProxySettings WithRole(string role)
        {
            return new ClusterSingletonProxySettings(
                singletonName: SingletonName,
                role: role,
                singletonIdentificationInterval: SingletonIdentificationInterval,
                bufferSize: BufferSize);
        }

        public ClusterSingletonProxySettings WithSingletonIdentificationInterval(string singletonIdentificationInterval)
        {
            return Copy(singletonIdentificationInterval: SingletonIdentificationInterval);
        }

        public ClusterSingletonProxySettings WithBufferSize(int bufferSize)
        {
            return Copy(bufferSize: bufferSize);
        }

        public ClusterSingletonProxySettings Copy(string singletonName = null, string role = null,
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