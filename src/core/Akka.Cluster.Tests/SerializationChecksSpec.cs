//-----------------------------------------------------------------------
// <copyright file="SerializationChecksSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Cluster.Tests
{
    public class SerializationChecksSpec : SerializationChecksBase
    {
        public SerializationChecksSpec(ITestOutputHelper output) : base(output, false)
        {
        }
    }
    
    public class SerializationChecksLegacySpec : SerializationChecksBase
    {
        public SerializationChecksLegacySpec(ITestOutputHelper output) : base(output, true)
        {
        }
    }
    
    public abstract class SerializationChecksBase : ClusterSpecBase
    {
        protected SerializationChecksBase(ITestOutputHelper output, bool useLegacyHeartbeat) : base(output, useLegacyHeartbeat)
        {
        }
        
        [Fact]
        public void Settings_serializemessages_and_serializecreators_must_be_on_for_tests()
        {
            Sys.Settings.SerializeAllCreators.ShouldBeTrue();
            Sys.Settings.SerializeAllMessages.ShouldBeTrue();
        }
    }
}

