//-----------------------------------------------------------------------
// <copyright file="CoreAPISpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Akka.Actor;
using Akka.Cluster;
using Akka.Cluster.Tools.Singleton;
using Akka.Persistence;
using Akka.Remote;
using Akka.Streams.Dsl;
using ApprovalTests;
using Xunit;
using Akka.Persistence.Query;
using static PublicApiGenerator.ApiGenerator;
using Akka.Cluster.Sharding;
using Akka.Cluster.Metrics;
using Akka.Persistence.Query.Sql;
using Akka.Persistence.Sql.Common.Journal;
using ApprovalTests.Core;
using ApprovalTests.Reporters;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Akka.API.Tests
{
#if(DEBUG)
    [UseReporter(typeof(DiffPlexReporter), typeof(DiffReporter), typeof(AllFailingTestsClipboardReporter))]
#else
    [UseReporter(typeof(DiffPlexReporter))]
#endif
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
        
        [Fact]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void ApproveDiscovery()
        {
            var publicApi = Filter(GeneratePublicApi(typeof(Discovery.Lookup).Assembly));
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

    internal class ApiNotApprovedException : XunitException
    {
        public ApiNotApprovedException(string message) : base($"Failed API approval. Diff:\n{message}")
        { }

        public override string StackTrace { get; } = string.Empty;
    }

    internal class DiffPlexReporter : IApprovalFailureReporter
    {
        public void Report(string approved, string received)
        {
            var approvedText = File.ReadAllText(approved);
            var receivedText = File.ReadAllText(received);

            var diffBuilder = new SideBySideDiffBuilder(new Differ());
            var diff = diffBuilder.BuildDiffModel(approvedText, receivedText);

            var sb = new StringBuilder()
                .AppendLine($"<<<<<<<<< {Path.GetFileName(approved)}")
                .AppendDiff(diff.OldText)
                .AppendLine("=========")
                .AppendDiff(diff.NewText)
                .Append($">>>>>>>>> {Path.GetFileName(received)}");

            //_out.WriteLine(sb.ToString());
            throw new ApiNotApprovedException(sb.ToString());
        }
    }

    internal static class Extensions
    {
        public static StringBuilder AppendDiff(this StringBuilder output, DiffPaneModel diff)
        {
            foreach (var line in diff.Lines)
            {
                switch (line.Type)
                {
                    case ChangeType.Deleted:
                        output.AppendLine($"[{line.Position:0000}] - {line.Text}");
                        break;
                    case ChangeType.Inserted:
                        output.AppendLine($"[{line.Position:0000}] + {line.Text}");
                        break;
                    case ChangeType.Modified:
                        output.AppendLine($"[{line.Position:0000}] ? {line.Text}");
                        break;
                }
            }

            return output;
        }

    }
}
