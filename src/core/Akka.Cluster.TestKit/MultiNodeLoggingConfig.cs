//-----------------------------------------------------------------------
// <copyright file="MultiNodeLoggingConfig.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Remote.TestKit;

namespace Akka.Cluster.Tests.MultiNode
{
    /// <summary>
    /// Static <see cref="Config"/> provider that allows toggleable logging
    /// for <see cref="MultiNodeSpec"/> instances within the Akka.Cluster.Tests assembly
    /// </summary>
    public static class MultiNodeLoggingConfig
    {
// ReSharper disable once InconsistentNaming
        private static readonly Config _loggingConfig =
            ConfigurationFactory.ParseString(@"
                akka.loggers = []");

        /// <summary>
        /// Used to specify which loggers to enable for the <see cref="MultiNodeClusterSpec"/> instances
        /// </summary>
        public static Config LoggingConfig
        {
            get { return _loggingConfig; }
        }
    }
}

