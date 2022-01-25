using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security.Claims;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;
using FluentAssertions;
using Hyperion;
using Hyperion.Internal;

namespace Akka.Serialization.Hyperion.Tests
{
    public class HyperionSerializerSetupSpec : AkkaSpec
    {
        private static Config Config
         => ConfigurationFactory.ParseString(@"
akka.actor {
    serializers {
        hyperion = ""Akka.Serialization.Hyperion, Akka.Serialization.Hyperion""
    }

    serialization-bindings {
      ""System.Object"" = hyperion
    }
}
");

        public HyperionSerializerSetupSpec(ITestOutputHelper output) : base (Config, output)
        { }

        [Fact]
        public void Setup_should_be_converted_to_settings_correctly()
        {
            var setup = HyperionSerializerSetup.Empty
                .WithPreserveObjectReference(true)
                .WithKnownTypeProvider<NoKnownTypes>()
                .WithDisallowUnsafeType(false);
            var settings =
                new HyperionSerializerSettings(
                    false, 
                    false, 
                    typeof(DummyTypesProvider), 
                    new Func<string, string>[] { s => $"{s}.." },
                    new Surrogate[0],
                    true);
            var appliedSettings = setup.ApplySettings(settings);

            appliedSettings.PreserveObjectReferences.Should().BeTrue(); // overriden
            appliedSettings.VersionTolerance.Should().BeFalse(); // default
            appliedSettings.KnownTypesProvider.Should().Be(typeof(NoKnownTypes)); // overriden
            appliedSettings.PackageNameOverrides.Count().Should().Be(1); // from settings
            appliedSettings.PackageNameOverrides.First()("a").Should().Be("a..");
            appliedSettings.Surrogates.ToList().Count.Should().Be(0); // from settings
            appliedSettings.DisallowUnsafeType.ShouldBe(false); // overriden
        }

        [Fact]
        public void Setup_package_override_should_work()
        {
            var setup = HyperionSerializerSetup.Empty
                .WithPackageNameOverrides(new Func<string, string>[]
                {
                    s => s.Contains("Hyperion.Override")
                        ? s.Replace(".Override", "")
                        : s
                });

            var settings = HyperionSerializerSettings.Default;
            var appliedSettings = setup.ApplySettings(settings);

            var adapter = appliedSettings.PackageNameOverrides.First();
            adapter("My.Hyperion.Override").Should().Be("My.Hyperion");
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
        
        [Fact]
        public void Setup_surrogate_should_work()
        {
            var surrogated = new List<Foo>();
            var setup = HyperionSerializerSetup.Empty
                .WithSurrogates(new [] { Surrogate.Create<Foo, FooSurrogate>(
                    foo =>
                    {
                        surrogated.Add(foo);
                        return new FooSurrogate(foo.Bar + ".");
                    }, 
                    surrogate => new Foo(surrogate.Bar))
                });
            var settings = setup.ApplySettings(HyperionSerializerSettings.Default);
            var serializer = new HyperionSerializer((ExtendedActorSystem)Sys, settings);

            var expected = new Foo("bar");
            var serialized = serializer.ToBinary(expected);
            var deserialized = serializer.FromBinary<Foo>(serialized);
            deserialized.Bar.Should().Be("bar.");
            surrogated.Count.Should().Be(1);
            surrogated[0].Should().BeEquivalentTo(expected);
        }

        [Theory]
        [MemberData(nameof(DangerousObjectFactory))]
        public void Setup_disallow_unsafe_type_should_work(object dangerousObject, Type type)
        {
            var serializer = new HyperionSerializer((ExtendedActorSystem)Sys, HyperionSerializerSettings.Default);
            var serialized = serializer.ToBinary(dangerousObject);
            serializer.Invoking(s => s.FromBinary(serialized, type)).Should().Throw<SerializationException>();
        }

        [Theory]
        [MemberData(nameof(TypeFilterObjectFactory))]
        public void Setup_TypeFilter_should_filter_types_properly(object sampleObject, bool shouldSucceed)
        {
            var setup = HyperionSerializerSetup.Empty
                .WithTypeFilter(TypeFilterBuilder.Create()
                    .Include<ClassA>()
                    .Include<ClassB>()
                    .Build());
            
            var settings = setup.ApplySettings(HyperionSerializerSettings.Default);
            var serializer = new HyperionSerializer((ExtendedActorSystem)Sys, settings);
            
            ((TypeFilter)serializer.Settings.TypeFilter).FilteredTypes.Count.Should().Be(2);
            var serialized = serializer.ToBinary(sampleObject);
            object deserialized = null;
            Action act = () => deserialized = serializer.FromBinary<object>(serialized);
            if (shouldSucceed)
            {
                act.Should().NotThrow();
                deserialized.GetType().Should().Be(sampleObject.GetType());
            }
            else
            {
                act.Should().Throw<SerializationException>()
                    .WithInnerException<UserEvilDeserializationException>();
            }
        }

        public static IEnumerable<object[]> DangerousObjectFactory()
        {
            var isWindow = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            
            yield return new object[]{ new FileInfo("C:\\Windows\\System32"), typeof(FileInfo) };
            yield return new object[]{ new ClaimsIdentity(), typeof(ClaimsIdentity)};
            if (isWindow)
            {
                yield return new object[]{ WindowsIdentity.GetAnonymous(), typeof(WindowsIdentity) };
                yield return new object[]{ new WindowsPrincipal(WindowsIdentity.GetAnonymous()), typeof(WindowsPrincipal)};
            }
#if NET471
            yield return new object[]{ new Process(), typeof(Process)};
#endif
            yield return new object[]{ new ClaimsIdentity(), typeof(ClaimsIdentity)};
        }

        public static IEnumerable<object[]> TypeFilterObjectFactory()
        {
            yield return new object[] { new ClassA(), true };
            yield return new object[] { new ClassB(), true };
            yield return new object[] { new ClassC(), false };
        }

        public class ClassA { }

        public class ClassB { }

        public class ClassC { }
    }
}
