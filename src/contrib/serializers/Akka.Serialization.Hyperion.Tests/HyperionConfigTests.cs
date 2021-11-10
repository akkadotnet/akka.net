//-----------------------------------------------------------------------
// <copyright file="HyperionConfigTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using FluentAssertions;
using Hyperion;
using Xunit;

namespace Akka.Serialization.Hyperion.Tests
{
    public class HyperionConfigTests
    {
        [Fact]
        public void Hyperion_serializer_should_have_correct_defaults()
        {
            var config = ConfigurationFactory.ParseString(@"
                akka.actor {
                    serializers.hyperion = ""Akka.Serialization.HyperionSerializer, Akka.Serialization.Hyperion""
                    serialization-bindings {
                        ""System.Object"" = hyperion
                    }
                }
            ");
            using (var system = ActorSystem.Create(nameof(HyperionConfigTests), config))
            {
                var serializer = (HyperionSerializer)system.Serialization.FindSerializerForType(typeof(object));
                Assert.True(serializer.Settings.VersionTolerance);
                Assert.True(serializer.Settings.PreserveObjectReferences);
                Assert.Equal("NoKnownTypes", serializer.Settings.KnownTypesProvider.Name);
                Assert.True(serializer.Settings.DisallowUnsafeType);
            }
        }

        [Fact]
        public void Hyperion_serializer_should_allow_to_setup_custom_flags()
        {
            var config = ConfigurationFactory.ParseString(@"
                akka.actor {
                    serializers.hyperion = ""Akka.Serialization.HyperionSerializer, Akka.Serialization.Hyperion""
                    serialization-bindings {
                        ""System.Object"" = hyperion
                    }
                    serialization-settings.hyperion {
                        preserve-object-references = false
                        version-tolerance = false
                        disallow-unsafe-type = false
                    }
                }
            ");
            using (var system = ActorSystem.Create(nameof(HyperionConfigTests), config))
            {
                var serializer = (HyperionSerializer)system.Serialization.FindSerializerForType(typeof(object));
                Assert.False(serializer.Settings.VersionTolerance);
                Assert.False(serializer.Settings.PreserveObjectReferences);
                Assert.Equal("NoKnownTypes", serializer.Settings.KnownTypesProvider.Name);
                Assert.False(serializer.Settings.DisallowUnsafeType);
            }
        }

        [Fact]
        public void Hyperion_serializer_should_allow_to_setup_custom_types_provider_with_default_constructor()
        {
            var config = ConfigurationFactory.ParseString(@"
                akka.actor {
                    serializers.hyperion = ""Akka.Serialization.HyperionSerializer, Akka.Serialization.Hyperion""
                    serialization-bindings {
                        ""System.Object"" = hyperion
                    }
                    serialization-settings.hyperion {
                        known-types-provider = ""Akka.Serialization.Hyperion.Tests.DummyTypesProviderWithDefaultCtor, Akka.Serialization.Hyperion.Tests""
                    }
                }
            ");
            using (var system = ActorSystem.Create(nameof(HyperionConfigTests), config))
            {
                var serializer = (HyperionSerializer)system.Serialization.FindSerializerForType(typeof(object));
                Assert.True(serializer.Settings.VersionTolerance);
                Assert.True(serializer.Settings.PreserveObjectReferences);
                Assert.Equal(typeof(DummyTypesProviderWithDefaultCtor), serializer.Settings.KnownTypesProvider);
                Assert.True(serializer.Settings.DisallowUnsafeType);
            }
        }

        [Fact]
        public void Hyperion_serializer_should_allow_to_setup_custom_types_provider_with_non_default_constructor()
        {
            var config = ConfigurationFactory.ParseString(@"
                akka.actor {
                    serializers.hyperion = ""Akka.Serialization.HyperionSerializer, Akka.Serialization.Hyperion""
                    serialization-bindings {
                        ""System.Object"" = hyperion
                    }
                    serialization-settings.hyperion {
                        known-types-provider = ""Akka.Serialization.Hyperion.Tests.DummyTypesProvider, Akka.Serialization.Hyperion.Tests""
                    }
                }
            ");
            using (var system = ActorSystem.Create(nameof(HyperionConfigTests), config))
            {
                var serializer = (HyperionSerializer)system.Serialization.FindSerializerForType(typeof(object));
                Assert.True(serializer.Settings.VersionTolerance);
                Assert.True(serializer.Settings.PreserveObjectReferences);
                Assert.Equal(typeof(DummyTypesProvider), serializer.Settings.KnownTypesProvider);
                Assert.True(serializer.Settings.DisallowUnsafeType);
            }
        }

        [Fact]
        public void Hyperion_serializer_should_read_cross_platform_package_name_override_settings()
        {
            var config = ConfigurationFactory.ParseString(@"
                akka.actor {
                    serializers.hyperion = ""Akka.Serialization.HyperionSerializer, Akka.Serialization.Hyperion""
                    serialization-bindings {
                        ""System.Object"" = hyperion
                    }
                    serialization-settings.hyperion {
                        cross-platform-package-name-overrides = {
                            netfx = [
                            {
                                fingerprint = ""a"",
                                rename-from = ""b"",
                                rename-to = ""c""
                            }]
                            netcore = [
                            {
                                fingerprint = ""d"",
                                rename-from = ""e"",
                                rename-to = ""f""
                            }]
                            net = [
                            {
                                fingerprint = ""g"",
                                rename-from = ""h"",
                                rename-to = ""i""
                            }]
                        }
                    }
                }
            ");
            using (var system = ActorSystem.Create(nameof(HyperionConfigTests), config))
            {
                var serializer = (HyperionSerializer)system.Serialization.FindSerializerForType(typeof(object));
                var overrides = serializer.Settings.PackageNameOverrides.ToList();
                Assert.NotEmpty(overrides);
                var @override = overrides[0];

#if NET471
                Assert.Equal("acc", @override("abc"));
                Assert.Equal("bcd", @override("bcd"));
#elif NETCOREAPP3_1
                Assert.Equal("dff", @override("def"));
                Assert.Equal("efg", @override("efg"));
#elif NET6_0
                Assert.Equal("gii", @override("ghi"));
                Assert.Equal("hij", @override("hij"));
#else
                throw new Exception("Test can not be completed because no proper compiler directive is set for this test build");
#endif
            }
        }
        
        [Fact]
        public void Hyperion_serializer_should_allow_to_setup_surrogates()
        {
            var config = ConfigurationFactory.ParseString(@"
                akka.actor {
                    serializers.hyperion = ""Akka.Serialization.HyperionSerializer, Akka.Serialization.Hyperion""
                    serialization-bindings {
                        ""System.Object"" = hyperion
                    }
                    serialization-settings.hyperion {
                        surrogates = [
                            ""Akka.Serialization.Hyperion.Tests.FooHyperionSurrogate, Akka.Serialization.Hyperion.Tests""
                        ]
                    }
                }
            ");
            using (var system = ActorSystem.Create(nameof(HyperionConfigTests), config))
            {
                var serializer = (HyperionSerializer)system.Serialization.FindSerializerForType(typeof(object));
                FooHyperionSurrogate.Surrogated.Clear();
                
                var expected = new Foo("bar");
                var serialized = serializer.ToBinary(expected);
                var deserialized = serializer.FromBinary<Foo>(serialized);
                deserialized.Bar.Should().Be("bar.");
                FooHyperionSurrogate.Surrogated.Count.Should().Be(1);
                FooHyperionSurrogate.Surrogated[0].Should().BeEquivalentTo(expected);
            }
        }

    }

    class DummyTypesProvider : IKnownTypesProvider
    {
        public DummyTypesProvider(ExtendedActorSystem system)
        {
            if (system == null)
                throw new ArgumentNullException(nameof(system));
        }

        public IEnumerable<Type> GetKnownTypes() => Enumerable.Empty<Type>();
    }

    class DummyTypesProviderWithDefaultCtor : IKnownTypesProvider
    {
        public IEnumerable<Type> GetKnownTypes() => Enumerable.Empty<Type>();
    }
    
    public class Foo
    {
        public Foo(string bar)
        {
            Bar = bar;
        }

        public string Bar { get; }
    }
        
    public class FooSurrogate
    {
        public FooSurrogate(string bar)
        {
            Bar = bar;
        }

        public string Bar { get; }
    }
        
    public class FooHyperionSurrogate : Surrogate
    {
        public static readonly List<Foo> Surrogated = new List<Foo>();
        
        public FooHyperionSurrogate()
        {
            From = typeof(Foo);
            To = typeof(FooSurrogate);
            ToSurrogate = obj =>
            {
                var foo = (Foo)obj;
                Surrogated.Add(foo);
                return new FooSurrogate(foo.Bar + ".");
            };
            FromSurrogate = obj => new Foo(((FooSurrogate)obj).Bar);
        }
    }

}
