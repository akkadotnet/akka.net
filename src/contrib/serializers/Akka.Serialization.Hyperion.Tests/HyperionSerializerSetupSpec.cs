using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;
using FluentAssertions;
using Hyperion;

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
                .WithKnownTypeProvider<NoKnownTypes>();
            var settings =
                new HyperionSerializerSettings(
                    false, 
                    false, 
                    typeof(DummyTypesProvider), 
                    new Func<string, string>[] { s => $"{s}.." },
                    new Surrogate[0]);
            var appliedSettings = setup.ApplySettings(settings);

            appliedSettings.PreserveObjectReferences.Should().BeTrue(); // overriden
            appliedSettings.VersionTolerance.Should().BeFalse(); // default
            appliedSettings.KnownTypesProvider.Should().Be(typeof(NoKnownTypes)); // overriden
            appliedSettings.PackageNameOverrides.Count().Should().Be(1); // from settings
            appliedSettings.PackageNameOverrides.First()("a").Should().Be("a..");
            appliedSettings.Surrogates.ToList().Count.Should().Be(0); // from settings
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
    }
}
