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
using ApprovalTests.Reporters.Mac;
using ApprovalUtilities.Utilities;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using Xunit.Sdk;
using P4MergeReporter = ApprovalTests.Reporters.P4MergeReporter;
using P4MacMergeReporter = ApprovalTests.Reporters.Mac.P4MergeReporter;

namespace Akka.API.Tests
{
#if(DEBUG)
    [UseReporter(typeof(DiffPlexReporter), typeof(CustomDiffReporter), typeof(AllFailingTestsClipboardReporter))]
#else
    [UseReporter(typeof(DiffPlexReporter), typeof(CustomDiffReporter))]
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

    #region Suppress FrameworkAssertReporter hack
    // The built-in FrameworkAssertReporter that is being called inside the DiffReporter class
    // is buggy in a CI/CD environment because it is trying to be clever, could not distinguish
    // between XUnit and XUnit2, and will throw Null Reference Exception every time it ran.
    //
    // This is probably fixed in latest version of ApiApprover but we couldn't switch to that
    // version because the latest ApiGenerator returns a different API report format.
    //
    // FIX: This hack removes FrameworkAssertReporter from the possible reporter list and retains
    // all of the other reporters in DiffReporter

    internal class CustomDiffReporter : FirstWorkingReporter
    {
        public CustomDiffReporter() : base(
            CustomWindowsDiffReporter.Instance, 
            CustomMacDiffReporter.Instance)
        { }
    }

    internal class CustomMacDiffReporter : FirstWorkingReporter
    {
        public static readonly CustomMacDiffReporter Instance = new CustomMacDiffReporter();
        public CustomMacDiffReporter()
            : base(

                BeyondCompareMacReporter.INSTANCE,
                DiffMergeReporter.INSTANCE,
                KaleidoscopeDiffReporter.INSTANCE,
                P4MacMergeReporter.INSTANCE,
                KDiff3Reporter.INSTANCE,
                TkDiffReporter.INSTANCE,
                QuietReporter.INSTANCE)
        { }

        public override bool IsWorkingInThisEnvironment(string forFile) => OsUtils.IsUnixOs() && base.IsWorkingInThisEnvironment(forFile);
    }

    internal class CustomWindowsDiffReporter : FirstWorkingReporter
    {
        public static readonly CustomWindowsDiffReporter Instance = new CustomWindowsDiffReporter();
        public CustomWindowsDiffReporter()
            : base(
                CodeCompareReporter.INSTANCE,
                BeyondCompareReporter.INSTANCE,
                TortoiseDiffReporter.INSTANCE,
                AraxisMergeReporter.INSTANCE,
                P4MergeReporter.INSTANCE,
                WinMergeReporter.INSTANCE,
                KDiffReporter.INSTANCE,
                VisualStudioReporter.INSTANCE,
                QuietReporter.INSTANCE)
        { }

        public override bool IsWorkingInThisEnvironment(string forFile) => OsUtils.IsWindowsOs() && base.IsWorkingInThisEnvironment(forFile);
    }

    #endregion

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
