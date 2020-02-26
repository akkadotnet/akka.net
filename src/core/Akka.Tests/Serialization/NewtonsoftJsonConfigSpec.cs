//-----------------------------------------------------------------------
// <copyright file="NewtonsoftJsonConfigSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Collections.Generic;
using Akka.Actor;
using Hocon;
using FluentAssertions;
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

        public static IEnumerable<object[]> HoconGenerator()
        {
            yield return new object[]
            {
                @"
                    foo{
                      bar.biz = 12
                      baz = ""quoted""
                    }
                ", string.Empty, string.Empty
            };

            yield return new object[]
            {
                @"
                    foo{
                      bar.biz = 12
                      baz = ""quoted""
                    }
                ", @"
                    foo.bar.twink = """"""tripple
quoted
string""""""
                ", string.Empty
            };

            yield return new object[]
            {
                @"
                    foo{
                      bar.biz = 12
                      baz = ""quoted""
                    }
                ", @"
                    foo.bar.twink = """"""tripple
quoted
string""""""
                ", @"
                    ""weird[]"".""@stuff"" = I am weird
                "
            };
        }
        [Theory]
        [MemberData(nameof(HoconGenerator))]
        public void Json_serializer_should_serialize_hocon_config(string hocon, string fallback1, string fallback2)
        {
            using (var system = ActorSystem.Create(nameof(NewtonsoftJsonConfigSpec)))
            {
                var serializer = (NewtonSoftJsonSerializer)system.Serialization.FindSerializerForType(typeof(object));

                var hocon1 = HoconConfigurationFactory.ParseString(hocon);
                var fb1 = string.IsNullOrEmpty(fallback1) ? Config.Empty : HoconConfigurationFactory.ParseString(fallback1);
                var fb2 = string.IsNullOrEmpty(fallback2) ? Config.Empty : HoconConfigurationFactory.ParseString(fallback2);
                var final = hocon1.WithFallback(fb1).WithFallback(fb2);

                var serialized = serializer.ToBinary(final);
                var deserialized = serializer.FromBinary<Config>(serialized);
                final.DumpConfig().Should().Be(deserialized.DumpConfig());
            }
        }

        [Fact]
        public void Json_serializer_should_serialize_empty_Config()
        {
            using (var system = ActorSystem.Create(nameof(NewtonsoftJsonConfigSpec)))
            {
                var config = Config.Empty;

                var serializer = (NewtonSoftJsonSerializer)system.Serialization.FindSerializerForType(typeof(object));
                var serialized = serializer.ToBinary(config);
                var deserialized = serializer.FromBinary<Config>(serialized);

                deserialized.IsEmpty.Should().BeTrue();
            }
        }

        [Fact]
        public void Json_serializer_should_serialize_quoted_key_with_invalid_characters()
        {
            using (var system = ActorSystem.Create(nameof(NewtonsoftJsonConfigSpec)))
            {
                var hoconString = @"this.""should[]"".work = true";
                var config = HoconConfigurationFactory.ParseString(hoconString);

                var serializer = (NewtonSoftJsonSerializer)system.Serialization.FindSerializerForType(typeof(object));
                var serialized = serializer.ToBinary(config);
                var deserialized = serializer.FromBinary<Config>(serialized);

                Assert.True(config.GetBoolean("this.\"should[]\".work"));
                Assert.True(deserialized.GetBoolean("this.\"should[]\".work"));
                Assert.Equal(config, deserialized);
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
