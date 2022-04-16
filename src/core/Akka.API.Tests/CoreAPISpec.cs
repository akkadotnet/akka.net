//-----------------------------------------------------------------------
// <copyright file="CoreAPISpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster;
using Akka.Cluster.Tools.Singleton;
using Akka.Persistence;
using Akka.Remote;
using Akka.Streams.Dsl;
using Xunit;
using Akka.Persistence.Query;
using static PublicApiGenerator.ApiGenerator;
using Akka.Cluster.Sharding;
using Akka.Cluster.Metrics;
using Akka.Persistence.Query.Sql;
using Akka.Persistence.Sql.Common.Journal;
using VerifyTests;
using VerifyXunit;

namespace Akka.API.Tests
{
    [UsesVerify]
    public class CoreAPISpec
    {
        static CoreAPISpec()
        {
            VerifyDiffPlex.Initialize();
        }

        [Fact]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task ApproveCore()
        {
            var publicApi = Filter(GeneratePublicApi(typeof(ActorSystem).Assembly));
            return Verifier.Verify(publicApi);
        }

        [Fact]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task ApproveRemote()
        {
            var publicApi = Filter(GeneratePublicApi(typeof(RemoteSettings).Assembly));
            return Verifier.Verify(publicApi);
        }

        [Fact]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task ApprovePersistence()
        {
            var publicApi = Filter(GeneratePublicApi(typeof(Persistent).Assembly));
            return Verifier.Verify(publicApi);
        }

        [Fact]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task ApprovePersistenceQuery()
        {
            var publicApi = Filter(GeneratePublicApi(typeof(PersistenceQuery).Assembly));
            return Verifier.Verify(publicApi);
        }

        [Fact]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task ApprovePersistenceSqlCommon()
        {
            var publicApi = Filter(GeneratePublicApi(typeof(SqlJournal).Assembly));
            return Verifier.Verify(publicApi);
        }

        [Fact]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task ApprovePersistenceSqlCommonQuery()
        {
            var publicApi = Filter(GeneratePublicApi(typeof(SqlReadJournal).Assembly));
            return Verifier.Verify(publicApi);
        }

        [Fact]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task ApproveCluster()
        {
            var publicApi = Filter(GeneratePublicApi(typeof(ClusterSettings).Assembly));
            return Verifier.Verify(publicApi);
        }

        [Fact]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task ApproveClusterTools()
        {
            var publicApi = Filter(GeneratePublicApi(typeof(ClusterSingletonManager).Assembly));
            return Verifier.Verify(publicApi);
        }

        [Fact]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task ApproveStreams()
        {
            var publicApi = Filter(GeneratePublicApi(typeof(Source).Assembly));
            return Verifier.Verify(publicApi);
        }

        [Fact]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task ApproveClusterSharding()
        {
            var publicApi = Filter(GeneratePublicApi(typeof(ClusterSharding).Assembly));
            return Verifier.Verify(publicApi);
        }

        [Fact]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task ApproveClusterMetrics()
        {
            var publicApi = Filter(GeneratePublicApi(typeof(ClusterMetrics).Assembly));
            return Verifier.Verify(publicApi);
        }

        [Fact]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task ApproveDistributedData()
        {
            var publicApi = Filter(GeneratePublicApi(typeof(DistributedData.DistributedData).Assembly));
            return Verifier.Verify(publicApi);
        }

        [Fact]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task ApproveCoordination()
        {
            var publicApi = Filter(GeneratePublicApi(typeof(Coordination.Lease).Assembly));
            return Verifier.Verify(publicApi);
        }

        [Fact]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task ApproveDiscovery()
        {
            var publicApi = Filter(GeneratePublicApi(typeof(Discovery.Lookup).Assembly));
            return Verifier.Verify(publicApi);
        }

        static string Filter(string text)
        {
            return string.Join(Environment.NewLine, text.Split(new[]
            {
                Environment.NewLine
            }, StringSplitOptions.RemoveEmptyEntries)
                .Where(l => !l.StartsWith("[assembly: ReleaseDateAttribute("))
                .Where(l => !string.IsNullOrWhiteSpace(l))
                );
        }
    }
}
