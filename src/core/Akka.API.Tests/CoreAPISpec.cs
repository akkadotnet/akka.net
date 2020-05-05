//-----------------------------------------------------------------------
// <copyright file="CoreAPISpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Akka.Actor;
using Akka.Cluster;
using Akka.Cluster.Tools.Singleton;
using Akka.Persistence;
using Akka.Remote;
using Akka.Streams.Dsl;
using ApiApprover;
using ApprovalTests;
using Mono.Cecil;
using Xunit;
using Akka.Persistence.Query;
using PublicApiGenerator;
using static PublicApiGenerator.ApiGenerator;
using Akka.Cluster.Sharding;
using Akka.Cluster.Metrics;
using Akka.Persistence.Query.Sql;
using Akka.Persistence.Sql.Common.Journal;

namespace Akka.API.Tests
{
    public class CoreAPISpec
    {
        [Fact]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void ApproveCore()
        {
            var publicApi = Filter(GeneratePublicApi(typeof(ActorSystem).Assembly));
            Approvals.Verify(publicApi);
        }

        [Fact]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void ApproveRemote()
        {
            var publicApi = Filter(GeneratePublicApi(typeof(RemoteSettings).Assembly));
            Approvals.Verify(publicApi);
        }

        [Fact]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void ApprovePersistence()
        {
            var publicApi = Filter(GeneratePublicApi(typeof(Persistent).Assembly));
            Approvals.Verify(publicApi);
        }

        [Fact]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void ApprovePersistenceQuery()
        {
            var publicApi = Filter(GeneratePublicApi(typeof(PersistenceQuery).Assembly));
            Approvals.Verify(publicApi);
        }

        [Fact]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void ApprovePersistenceSqlCommon()
        {
            var publicApi = Filter(GeneratePublicApi(typeof(SqlJournal).Assembly));
            Approvals.Verify(publicApi);
        }

        [Fact]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void ApprovePersistenceSqlCommonQuery()
        {
            var publicApi = Filter(GeneratePublicApi(typeof(SqlReadJournal).Assembly));
            Approvals.Verify(publicApi);
        }

        [Fact]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void ApproveCluster()
        {
            var publicApi = Filter(GeneratePublicApi(typeof(ClusterSettings).Assembly));
            Approvals.Verify(publicApi);
        }

        [Fact]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void ApproveClusterTools()
        {
            var publicApi = Filter(GeneratePublicApi(typeof(ClusterSingletonManager).Assembly));
            Approvals.Verify(publicApi);
        }

        [Fact]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void ApproveStreams()
        {
            var publicApi = Filter(GeneratePublicApi(typeof(Source).Assembly));
            Approvals.Verify(publicApi);
        }

        [Fact]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void ApproveClusterSharding()
        {
            var publicApi = Filter(GeneratePublicApi(typeof(ClusterSharding).Assembly));
            Approvals.Verify(publicApi);
        }

        [Fact]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void ApproveClusterMetrics()
        {
            var publicApi = Filter(GeneratePublicApi(typeof(ClusterMetrics).Assembly));
            Approvals.Verify(publicApi);
        }

        [Fact]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void ApproveDistributedData()
        {
            var publicApi = Filter(GeneratePublicApi(typeof(DistributedData.DistributedData).Assembly));
            Approvals.Verify(publicApi);
        }

        [Fact]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void ApproveCoordination()
        {
            var publicApi = Filter(GeneratePublicApi(typeof(Coordination.Lease).Assembly));
            Approvals.Verify(publicApi);
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
