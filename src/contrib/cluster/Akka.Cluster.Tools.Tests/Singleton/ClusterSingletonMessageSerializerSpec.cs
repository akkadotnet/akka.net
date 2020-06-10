//-----------------------------------------------------------------------
// <copyright file="ClusterSingletonMessageSerializerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.Serialization;
using Akka.TestKit;
using Xunit;

namespace Akka.Cluster.Tools.Tests.Singleton
{
    public class ClusterSingletonMessageSerializerSpec : AkkaSpec
    {
        public ClusterSingletonMessageSerializerSpec()
            : base(ConfigurationFactory.ParseString(@"akka.actor.provider = cluster
                                                      akka.remote.dot-netty.tcp.port = 0").WithFallback(ClusterSingletonManager.DefaultConfig()))
        {
        }

        [Fact]
        public void Can_serialize_HandOverDone()
        {
            var message = HandOverDone.Instance;
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_HandOverInProgress()
        {
            var message = HandOverInProgress.Instance;
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_HandOverToMe()
        {
            var message = HandOverToMe.Instance;
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_TakeOverFromMe()
        {
            var message = TakeOverFromMe.Instance;
            AssertEqual(message);
        }

        private T AssertAndReturn<T>(T message)
        {
            var serializer = (SerializerWithStringManifest)Sys.Serialization.FindSerializerFor(message);
            var serialized = serializer.ToBinary(message);
            return (T)serializer.FromBinary(serialized, serializer.Manifest(message));
        }

        private void AssertEqual<T>(T message)
        {
            var deserialized = AssertAndReturn(message);
            Assert.Equal(message, deserialized);
        }
    }
}
