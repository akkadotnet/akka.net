//-----------------------------------------------------------------------
// <copyright file="CustomSerializerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Xunit;

namespace Akka.Tests.Serialization
{
    public class CustomSerializerSpec
    {
        /// <summary>
        /// Here we basically verify that a serializer decides where its Serializer Identifier is coming
        /// from. When using the default Serializer base class, it read from hocon config. But this should not be 
        /// a neccesity
        /// </summary>
        [Fact]
        public void Custom_serializer_must_be_owner_of_its_serializerId()
        {
            var config = ConfigurationFactory.ParseString(@"
                akka.actor {
                    serializers {
                        custom = ""Akka.Tests.Serialization.CustomSerializer, Akka.Tests""
                    }
                    serialization-bindings {
                        ""System.Object"" = custom
                    }
                }
            ");
            //The above config explictly does not configures the serialization-identifiers section
            using (var system = ActorSystem.Create(nameof(CustomSerializerSpec), config))
            {
                var serializer = (CustomSerializer)system.Serialization.FindSerializerForType(typeof(object));
                Assert.Equal(666, serializer.Identifier);
            }
        }
    }

    public class CustomSerializer : Serializer
    {
        public CustomSerializer(ExtendedActorSystem system) : base(system)
        {
        }

        /// <summary>
        /// This custom serializer overrides the Identifier implementation and returns a hard coded value
        /// </summary>
        public override int Identifier => 666;

        public override bool IncludeManifest => false;

        public override byte[] ToBinary(object obj)
        {
            throw new NotImplementedException();
        }

        public override object FromBinary(byte[] bytes, Type type)
        {
            throw new NotImplementedException();
        }
    }
}
