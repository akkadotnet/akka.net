﻿//-----------------------------------------------------------------------
// <copyright file="ClusterSingletonProxySettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Cluster.Tools.Singleton
{
    /// <summary>
    /// TBD
    /// </summary>
    public sealed class ClusterSingletonProxySettings : INoSerializationVerificationNeeded
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <exception cref="ConfigurationException">TBD</exception>
        /// <returns>TBD</returns>
        public static ClusterSingletonProxySettings Create(ActorSystem system)
        {
            system.Settings.InjectTopLevelFallback(ClusterSingletonManager.DefaultConfig());
            var config = system.Settings.Config.GetConfig("akka.cluster.singleton-proxy");
            if (config == null)
                throw new ConfigurationException($"Cannot create {typeof(ClusterSingletonProxySettings)}: akka.cluster.singleton-proxy configuration node not found");

            return Create(config);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="config">TBD</param>
        /// <returns>TBD</returns>
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
        /// Creates new instance of the <see cref="ClusterSingletonProxySettings"/>.
        /// </summary>
        /// <param name="singletonName">The actor name of the singleton actor that is started by the <see cref="ClusterSingletonManager"/>.</param>
        /// <param name="role">The role of the cluster nodes where the singleton can be deployed. If None, then any node will do.</param>
        /// <param name="singletonIdentificationInterval">Interval at which the proxy will try to resolve the singleton instance.</param>
        /// <param name="bufferSize">
        /// If the location of the singleton is unknown the proxy will buffer this number of messages and deliver them
        /// when the singleton is identified.When the buffer is full old messages will be dropped when new messages 
        /// are sent via the proxy. Use 0 to disable buffering, i.e.messages will be dropped immediately if the location 
        /// of the singleton is unknown.
        /// </param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when either the specified <paramref name="singletonIdentificationInterval"/>
        /// or <paramref name="bufferSize"/> is less than or equal to zero.
        /// </exception>
        /// <exception cref="ArgumentNullException"></exception>
        public ClusterSingletonProxySettings(string singletonName, string role, TimeSpan singletonIdentificationInterval, int bufferSize)
        {
            if (string.IsNullOrEmpty(singletonName))
                throw new ArgumentNullException(nameof(singletonName));
            if (singletonIdentificationInterval == TimeSpan.Zero)
                throw new ArgumentException("singletonIdentificationInterval must be positive", nameof(singletonIdentificationInterval));
            if (bufferSize <= 0)
                throw new ArgumentException("bufferSize must be positive", nameof(bufferSize));

            SingletonName = singletonName;
            Role = role;
            SingletonIdentificationInterval = singletonIdentificationInterval;
            BufferSize = bufferSize;
        }

        /// <summary>
        /// Creates a new <see cref="ClusterSingletonProxySettings" /> setting with the specified <paramref name="singletonName"/>.
        /// <note>
        /// This method is immutable and returns a new instance of the setting.
        /// </note>
        /// </summary>
        /// <param name="singletonName">The name of the singleton actor used by the <see cref="ClusterSingletonManager"/>.</param>
        /// <returns>A new setting with the provided <paramref name="singletonName" />.</returns>
        public ClusterSingletonProxySettings WithSingletonName(string singletonName)
        {
            return Copy(singletonName: singletonName);
        }

        /// <summary>
        /// Creates a new <see cref="ClusterSingletonProxySettings" /> setting with the specified <paramref name="role"/>
        /// from the <c>akka.cluster.role</c> in the configuration used. The <paramref name="role"/> specifies the nodes
        /// on which the targeted singleton can run.
        /// <note>
        /// This method is immutable and returns a new instance of the setting.
        /// </note>
        /// </summary>
        /// <param name="role">The role of the singleton proxy.</param>
        /// <returns>A new setting with the provided <paramref name="role" />.</returns>
        public ClusterSingletonProxySettings WithRole(string role)
        {
            return new ClusterSingletonProxySettings(
                singletonName: SingletonName,
                role: role,
                singletonIdentificationInterval: SingletonIdentificationInterval,
                bufferSize: BufferSize);
        }

        /// <summary>
        /// Creates a new <see cref="ClusterSingletonProxySettings" /> setting with the specified <paramref name="singletonIdentificationInterval"/>.
        /// <note>
        /// This method is immutable and returns a new instance of the setting.
        /// </note>
        /// </summary>
        /// <param name="singletonIdentificationInterval">The identification level of the singleton proxy.</param>
        /// <returns>A new setting with the provided <paramref name="singletonIdentificationInterval" />.</returns>
        public ClusterSingletonProxySettings WithSingletonIdentificationInterval(string singletonIdentificationInterval)
        {
            return Copy(singletonIdentificationInterval: SingletonIdentificationInterval);
        }

        /// <summary>
        /// Creates a new <see cref="ClusterSingletonProxySettings" /> setting with the specified <paramref name="bufferSize"/>.
        /// <note>
        /// This method is immutable and returns a new instance of the setting.
        /// </note>
        /// </summary>
        /// <param name="bufferSize">The buffer size of the singleton proxy.</param>
        /// <returns>A new setting with the provided <paramref name="bufferSize" />.</returns>
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
