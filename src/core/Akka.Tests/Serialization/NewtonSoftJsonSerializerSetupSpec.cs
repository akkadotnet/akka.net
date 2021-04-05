//-----------------------------------------------------------------------
// <copyright file="NewtonSoftJsonSerializerSetupSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Serialization;
using Akka.TestKit;
using Akka.TestKit.Configs;
using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Tests.Serialization
{
    public class NewtonSoftJsonSerializerSetupSpec : AkkaSpec
    {
        internal class DummyContractResolver : DefaultContractResolver
        { }

        public static NewtonSoftJsonSerializerSetup SerializationSettings = NewtonSoftJsonSerializerSetup.Create(
            settings =>
            {
                settings.ReferenceLoopHandling = ReferenceLoopHandling.Error;
                settings.MissingMemberHandling = MissingMemberHandling.Error;
                settings.NullValueHandling = NullValueHandling.Include;
                settings.Converters = new List<JsonConverter> { new DummyConverter() };
                settings.ObjectCreationHandling = ObjectCreationHandling.Auto;
                settings.ContractResolver = new DummyContractResolver();
            });

        public static readonly BootstrapSetup Bootstrap = BootstrapSetup.Create().WithConfig(TestConfigs.DefaultConfig);

        public static readonly ActorSystemSetup ActorSystemSettings = ActorSystemSetup.Create(SerializationSettings, Bootstrap);

        public NewtonSoftJsonSerializerSetupSpec(ITestOutputHelper output) 
            : base(ActorSystem.Create("SerializationSettingsSpec", ActorSystemSettings), output) { }


        [Fact]
        public void Setup_should_be_used_inside_Json_serializer()
        {
            var serializer = (NewtonSoftJsonSerializer) Sys.Serialization.FindSerializerForType(typeof(object));
            var settings = serializer.Settings;
            settings.ReferenceLoopHandling.Should().Be(ReferenceLoopHandling.Error);
            settings.MissingMemberHandling.Should().Be(MissingMemberHandling.Error);
            settings.NullValueHandling.Should().Be(NullValueHandling.Include);
            settings.Converters.Any(c => c is DummyConverter).Should().Be(true);
        }

        [Fact]
        public void Setup_should_not_change_mandatory_settings()
        {
            var serializer = (NewtonSoftJsonSerializer) Sys.Serialization.FindSerializerForType(typeof(object));
            var settings = serializer.Settings;
            settings.ContractResolver.Should().BeOfType<NewtonSoftJsonSerializer.AkkaContractResolver>();
            settings.ObjectCreationHandling.Should().Be(ObjectCreationHandling.Replace);
            settings.Converters.Any(c => c is NewtonSoftJsonSerializer.SurrogateConverter).Should().Be(true);
            settings.Converters.Any(c => c is DiscriminatedUnionConverter).Should().Be(true);
        }
    }
}
