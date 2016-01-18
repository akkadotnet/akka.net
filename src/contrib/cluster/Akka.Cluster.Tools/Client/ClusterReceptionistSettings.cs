//-----------------------------------------------------------------------
// <copyright file="ClusterReceptionistSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Cluster.Tools.Client
{
    [Serializable]
    public sealed class ClusterReceptionistSettings
    {
        /// <summary>
        /// Create settings from the default configuration "akka.cluster.client.receptionist".
        /// </summary>
        public static ClusterReceptionistSettings Create(ActorSystem system)
        {
            var config = system.Settings.Config.GetConfig("akka.cluster.client.receptionist");
            if (config == null)
                throw new ArgumentException(string.Format("Actor system [{0}] doesn't have `akka.cluster.client.receptionist` config set up", system.Name));

            return Create(config);
        }

        /// <summary>
        /// Create settings from a configuration with the same layout as the default configuration "akka.cluster.client.receptionist".
        /// </summary>
        public static ClusterReceptionistSettings Create(Config config)
        {
            return new ClusterReceptionistSettings(config.GetString("role"), config.GetInt("number-of-contacts"), config.GetTimeSpan("response-tunnel-receive-timeout"));
        }

        /// <summary>
        /// Start the receptionist on members tagged with this role. All members are used if undefined.
        /// </summary>
        public readonly string Role;

        /// <summary>
        /// The receptionist will send this number of contact points to the client.
        /// </summary>
        public readonly int NumberOfContacts;

        /// <summary>
        /// The actor that tunnel response messages to the client will be stopped after this time of inactivity.
        /// </summary>
        public readonly TimeSpan ResponseTunnelReceiveTimeout;

        public ClusterReceptionistSettings(string role, int numberOfContacts, TimeSpan responseTunnelReceiveTimeout)
        {
            Role = !string.IsNullOrEmpty(role) ? role : null;
            NumberOfContacts = numberOfContacts;
            ResponseTunnelReceiveTimeout = responseTunnelReceiveTimeout;
        }

        public ClusterReceptionistSettings WithRole(string role)
        {
            return Copy(role: role);
        }

        public ClusterReceptionistSettings WithoutRole()
        {
            return Copy(role: null);
        }

        public ClusterReceptionistSettings WithNumberOfContacts(int numberOfContacts)
        {
            return Copy(numberOfContacts: numberOfContacts);
        }

        public ClusterReceptionistSettings WithResponseTunnelReceiveTimeout(TimeSpan responseTunnelReceiveTimeout)
        {
            return Copy(responseTunnelReceiveTimeout: responseTunnelReceiveTimeout);
        }

        private ClusterReceptionistSettings Copy(string role = null, int? numberOfContacts = null,
            TimeSpan? responseTunnelReceiveTimeout = null)
        {
            return new ClusterReceptionistSettings(role ?? Role, numberOfContacts ?? NumberOfContacts, responseTunnelReceiveTimeout ?? ResponseTunnelReceiveTimeout);
        }
    }
}