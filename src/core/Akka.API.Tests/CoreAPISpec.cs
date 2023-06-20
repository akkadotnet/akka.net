//-----------------------------------------------------------------------
// <copyright file="CoreAPISpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster;
using Akka.Cluster.Tools.Singleton;
using Akka.Persistence;
using Akka.Remote;
using Xunit;
using Akka.Persistence.Query;
using static PublicApiGenerator.ApiGenerator;
using Akka.Cluster.Sharding;
using Akka.Cluster.Metrics;
using Akka.Persistence.Query.InMemory;
using Akka.Persistence.Query.Sql;
using Akka.Persistence.Sql.Common.Journal;
using Akka.Streams;
using VerifyXunit;

namespace Akka.API.Tests
{
    [UsesVerify]
    public class CoreAPISpec
    {
        static Task VerifyAssembly<T>()
        {
            return Verifier.Verify(GeneratePublicApi(typeof(T).Assembly));
        }

        [Fact]
        public Task ApproveCore()
        {
            return VerifyAssembly<ActorSystem>();
        }

        [Fact]
        public Task ApproveRemote()
        {
            return VerifyAssembly<RemoteSettings>();
        }

        [Fact]
        public Task ApprovePersistence()
        {
            return VerifyAssembly<Persistent>();
        }

        [Fact]
        public Task ApprovePersistenceQuery()
        {
            return VerifyAssembly<PersistenceQuery>();
        }

        [Fact]
        public Task ApprovePersistenceSqlCommon()
        {
            return VerifyAssembly<SqlJournal>();
        }

        [Fact]
        public Task ApprovePersistenceSqlCommonQuery()
        {
            return VerifyAssembly<SqlReadJournal>();
        }

        [Fact]
        public Task ApprovePersistenceInMemoryQuery()
        {
            return VerifyAssembly<InMemoryReadJournal>();
        }

        [Fact]
        public Task ApproveCluster()
        {
            return VerifyAssembly<ClusterSettings>();
        }

        [Fact]
        public Task ApproveClusterTools()
        {
            return VerifyAssembly<ClusterSingletonManager>();
        }

        [Fact]
        public Task ApproveStreams()
        {
            return VerifyAssembly<Shape>();
        }

        [Fact]
        public Task ApproveClusterSharding()
        {
            return VerifyAssembly<ClusterSharding>();
        }

        [Fact]
        public Task ApproveClusterMetrics()
        {
            return VerifyAssembly<ClusterMetrics>();
        }

        [Fact]
        public Task ApproveDistributedData()
        {
            return VerifyAssembly<DistributedData.DistributedData>();
        }

        [Fact]
        public Task ApproveCoordination()
        {
            return VerifyAssembly<Coordination.Lease>();
        }

        [Fact]
        public Task ApproveDiscovery()
        {
            return VerifyAssembly<Discovery.Lookup>();
        }
    }
}
