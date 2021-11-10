// //-----------------------------------------------------------------------
// // <copyright file="BugFix5279Spec.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Serialization;
using Akka.TestKit;
using Akka.Util;
using Xunit;
using FluentAssertions;

namespace Akka.Remote.Tests.Serialization
{
    public class BugFix5279Spec: AkkaSpec
    {
        [Theory]
        [InlineData(1, "I")]
        [InlineData(1L, "L")]
        [InlineData("1", "S")]
        public void PrimitiveSerializer_without_useNeutralPrimitives_should_return_custom_manifest(object data, string manifest)
        {
            var config = ConfigurationFactory.ParseString("use-legacy-behavior = off");
            var serializer = new PrimitiveSerializers((ExtendedActorSystem)Sys, config);
            serializer.Manifest(data).Should().Be(manifest);
        }
        
        [Theory]
        [InlineData(1)]
        [InlineData(1L)]
        [InlineData("1")]
        public void PrimitiveSerializer_without_useNeutralPrimitives_should_return_type_manifest(object data)
        {
            var config = ConfigurationFactory.ParseString("use-legacy-behavior = on");
            var serializer = new PrimitiveSerializers((ExtendedActorSystem)Sys, config);
            serializer.Manifest(data).Should().Be(data.GetType().TypeQualifiedName());
        }
        
    }
}