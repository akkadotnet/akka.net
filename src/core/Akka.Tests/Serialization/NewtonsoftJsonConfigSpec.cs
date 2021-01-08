//-----------------------------------------------------------------------
// <copyright file="NewtonsoftJsonConfigSpec.cs" company="Akka.NET Project">
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
    public class NewtonsoftJsonConfigSpec
    {
        [Fact]
        public void Json_serializer_should_have_correct_defaults()
        {
            using (var system = ActorSystem.Create(nameof(NewtonsoftJsonConfigSpec)))
            {
                var serializer = (NewtonSoftJsonSerializer)system.Serialization.FindSerializerForType(typeof(object));
                Assert.Equal(TypeNameHandling.All, serializer.Settings.TypeNameHandling);
                Assert.Equal(PreserveReferencesHandling.Objects, serializer.Settings.PreserveReferencesHandling);
                Assert.Equal(2, serializer.Settings.Converters.Count);
                Assert.Contains(serializer.Settings.Converters, x => x is DiscriminatedUnionConverter);
                Assert.Contains(serializer.Settings.Converters, x => x is NewtonSoftJsonSerializer.SurrogateConverter);
            }
        }

        [Fact]
        public void Json_serializer_should_allow_to_setup_custom_flags()
        {
            var config = ConfigurationFactory.ParseString(@"
                akka.actor {
                    serialization-settings.json {
                        preserve-object-references = false
                        encode-type-names = false
                    }
                }
            ");
            using (var system = ActorSystem.Create(nameof(NewtonsoftJsonConfigSpec), config))
            {
                var serializer = (NewtonSoftJsonSerializer)system.Serialization.FindSerializerForType(typeof(object));
                Assert.Equal(TypeNameHandling.None, serializer.Settings.TypeNameHandling);
                Assert.Equal(PreserveReferencesHandling.None, serializer.Settings.PreserveReferencesHandling);
                Assert.Equal(2, serializer.Settings.Converters.Count);
                Assert.Contains(serializer.Settings.Converters, x => x is DiscriminatedUnionConverter);
                Assert.Contains(serializer.Settings.Converters, x => x is NewtonSoftJsonSerializer.SurrogateConverter);
            }
        }

        [Fact]
        public void Json_serializer_should_allow_to_setup_custom_converters()
        {
            var config = ConfigurationFactory.ParseString(@"
                akka.actor {
                    serialization-settings.json {
                        converters = [   
                            ""Akka.Tests.Serialization.DummyConverter, Akka.Tests""
                            ""Akka.Tests.Serialization.DummyConverter2, Akka.Tests""
                        ]
                    }
                }
            ");
            using (var system = ActorSystem.Create(nameof(NewtonsoftJsonConfigSpec), config))
            {
                var serializer = (NewtonSoftJsonSerializer)system.Serialization.FindSerializerForType(typeof(object));
                Assert.Equal(TypeNameHandling.All, serializer.Settings.TypeNameHandling);
                Assert.Equal(PreserveReferencesHandling.Objects, serializer.Settings.PreserveReferencesHandling);
                Assert.Equal(4, serializer.Settings.Converters.Count);
                Assert.Contains(serializer.Settings.Converters, x => x is DiscriminatedUnionConverter);
                Assert.Contains(serializer.Settings.Converters, x => x is NewtonSoftJsonSerializer.SurrogateConverter);
                Assert.Contains(serializer.Settings.Converters, x => x is DummyConverter);
                Assert.Contains(serializer.Settings.Converters, x => x is DummyConverter2);
            }
        }
    }

    class DummyConverter : JsonConverter
    {
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        public override bool CanConvert(Type objectType)
        {
            throw new NotImplementedException();
        }
    }

    class DummyConverter2 : JsonConverter
    {
        public DummyConverter2(ExtendedActorSystem system)
        {
            if (system == null) 
                throw new ArgumentNullException(nameof(system));
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        public override bool CanConvert(Type objectType)
        {
            throw new NotImplementedException();
        }
    }
}
