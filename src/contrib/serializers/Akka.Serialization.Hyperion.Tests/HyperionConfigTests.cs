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
#elif NET5_0
                Assert.Equal("gii", @override("ghi"));
                Assert.Equal("hij", @override("hij"));
#else
                throw new Exception("Test can not be completed because no proper compiler directive is set for this test build");
#endif
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
