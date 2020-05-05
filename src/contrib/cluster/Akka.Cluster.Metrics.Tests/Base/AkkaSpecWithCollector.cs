//-----------------------------------------------------------------------
// <copyright file="AkkaSpecWithCollector.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Cluster.Metrics.Collectors;
using Akka.TestKit;
using Xunit.Abstractions;

namespace Akka.Cluster.Metrics.Tests.Base
{
    /// <summary>
    /// Base class for specs that use <see cref="IMetricsCollector"/> property
    /// </summary>
    public abstract class AkkaSpecWithCollector : AkkaSpec
    {
        /// <summary>
        /// Collector used in specs
        /// </summary>
        protected IMetricsCollector Collector { get; }

        protected AkkaSpecWithCollector(string config, ITestOutputHelper output = null)
            : base(config, output)
        {
            Collector = new DefaultCollector((Sys as ExtendedActorSystem).Provider.RootPath.Address);
        }
    }
}
