//-----------------------------------------------------------------------
// <copyright file="CoreAPISpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Akka.Cluster;
using Akka.Cluster.Tools.Singleton;
using Akka.Persistence;
using Akka.Remote;
using Akka.Streams.Dsl;
using ApiApprover;
using ApprovalTests;
using Mono.Cecil;
using Xunit;

namespace Akka.API.Tests
{
    public class CoreAPISpec
    {
        [Fact]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void ApproveCore()
        {
            var assemblyPath = Path.GetFullPath(typeof(PatternMatch).Assembly.Location);
            var asm = AssemblyDefinition.ReadAssembly(assemblyPath);
            var publicApi = Filter(PublicApiGenerator.CreatePublicApiForAssembly(asm));
            Approvals.Verify(publicApi);
        }

        [Fact]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void ApproveRemote()
        {
            var assemblyPath = Path.GetFullPath(typeof(RemoteSettings).Assembly.Location);
            var asm = AssemblyDefinition.ReadAssembly(assemblyPath);
            var publicApi = Filter(PublicApiGenerator.CreatePublicApiForAssembly(asm));
            Approvals.Verify(publicApi);
        }

        [Fact]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void ApprovePersistence()
        {
            var assemblyPath = Path.GetFullPath(typeof(Persistent).Assembly.Location);
            var asm = AssemblyDefinition.ReadAssembly(assemblyPath);
            var publicApi = Filter(PublicApiGenerator.CreatePublicApiForAssembly(asm));
            Approvals.Verify(publicApi);
        }

        [Fact]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void ApproveCluster()
        {
            var assemblyPath = Path.GetFullPath(typeof(ClusterSettings).Assembly.Location);
            var asm = AssemblyDefinition.ReadAssembly(assemblyPath);
            var publicApi = Filter(PublicApiGenerator.CreatePublicApiForAssembly(asm));
            Approvals.Verify(publicApi);
        }

        [Fact]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void ApproveClusterTools()
        {
            var assemblyPath = Path.GetFullPath(typeof(ClusterSingletonManager).Assembly.Location);
            var asm = AssemblyDefinition.ReadAssembly(assemblyPath);
            var publicApi = Filter(PublicApiGenerator.CreatePublicApiForAssembly(asm));
            Approvals.Verify(publicApi);
        }
        [Fact]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void ApproveStreams()
        {
            var assemblyPath = Path.GetFullPath(typeof(Source).Assembly.Location);
            var asm = AssemblyDefinition.ReadAssembly(assemblyPath);
            var publicApi = Filter(PublicApiGenerator.CreatePublicApiForAssembly(asm));
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
