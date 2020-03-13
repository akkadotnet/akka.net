//-----------------------------------------------------------------------
// <copyright file="ClusterSpecBase.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;
using Akka.TestKit;

namespace Akka.Cluster.Tests
{
    /// <summary>
    /// Abstract base class for cluster specs - turns on required serialization properties
    /// </summary>
    public abstract class ClusterSpecBase : AkkaSpec
    {
        protected ClusterSpecBase(Config config) : base(config.WithFallback(BaseConfig))
        {
            
        }

        protected ClusterSpecBase()
            : base(BaseConfig)
        {

        }

        protected static readonly Config BaseConfig = ConfigurationFactory.ParseString(@"
                            akka.actor.serialize-messages = on
                            akka.actor.serialize-creators = on");
    }
}

