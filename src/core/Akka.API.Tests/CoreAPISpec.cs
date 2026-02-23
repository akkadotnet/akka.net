//-----------------------------------------------------------------------
// <copyright file="CoreAPISpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
using PublicApiGenerator;
using Akka.Cluster.Sharding;
using Akka.Cluster.Metrics;
using Akka.Persistence.Query.InMemory;
using Akka.Streams;
using Akka.TestKit;
using VerifyXunit;

namespace Akka.API.Tests
{
    public class CoreAPISpec
    {
        // Exclude compiler-generated state machine attributes from API surface.
        // These contain auto-generated type names (e.g., <MethodName>d__123) that change
        // whenever code structure changes, causing unnecessary API approval churn.
        private static readonly ApiGeneratorOptions ApiOptions = new ApiGeneratorOptions
        {
            ExcludeAttributes = new[]
            {
                "System.Runtime.CompilerServices.AsyncIteratorStateMachineAttribute",
                "System.Runtime.CompilerServices.AsyncStateMachineAttribute",
                "System.Runtime.CompilerServices.IteratorStateMachineAttribute"
            }
        };

        static Task VerifyAssembly<T>()
        {
            return Verifier.Verify(typeof(T).Assembly.GeneratePublicApi(ApiOptions));
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
        
        [Fact]
        public Task ApproveTestKit()
        {
            return VerifyAssembly<TestKitBase>();
        }
        
        
        [Fact]
        public Task ApproveTestKitXunit2()
        {
            return VerifyAssembly<TestKit.Xunit2.TestKit>();
        }
    }
}
