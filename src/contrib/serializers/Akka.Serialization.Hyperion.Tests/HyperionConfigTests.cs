//-----------------------------------------------------------------------
// <copyright file="HyperionConfigTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
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
                    }
                }
            ");
            using (var system = ActorSystem.Create(nameof(HyperionConfigTests), config))
            {
                var serializer = (HyperionSerializer)system.Serialization.FindSerializerForType(typeof(object));
                Assert.False(serializer.Settings.VersionTolerance);
                Assert.False(serializer.Settings.PreserveObjectReferences);
                Assert.Equal("NoKnownTypes", serializer.Settings.KnownTypesProvider.Name);
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
}
