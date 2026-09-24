//-----------------------------------------------------------------------
// <copyright file="DnsProviderConfigSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.IO;
using Akka.Tests.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.IO
{
    /// <summary>An <see cref="IDnsProvider"/> outside Akka.dll, only reachable by name.</summary>
    public sealed class CustomDnsProvider : IDnsProvider
    {
        public DnsBase Cache { get; } = new SimpleDnsCache();

        public Type ActorClass => typeof(InetAddressDnsResolver);

        public Type ManagerClass => typeof(SimpleDnsManager);
    }

    /// <summary>Forwards a <see cref="Dns.Resolved"/> to a <see cref="TaskCompletionSource{T}"/>.</summary>
    public sealed class ResolveProbe : ReceiveActor
    {
        public ResolveProbe(TaskCompletionSource<Dns.Resolved> tcs)
        {
            Receive<Dns.Resolved>(resolved => tcs.TrySetResult(resolved));
        }
    }

    /// <summary>Covers the <c>akka.io.dns.&lt;resolver&gt;.provider-object</c> built-in table and its reflection fallback.</summary>
    [Collection(DynamicTypeLoadingCollection.Name)]
    public class DnsProviderConfigSpec
    {
        private const string CustomProviderTypeName = "Akka.Tests.IO.CustomDnsProvider, Akka.Tests";

        private static async Task<Dns.Resolved> ResolveLocalhostAsync(ActorSystem system)
        {
            var tcs = new TaskCompletionSource<Dns.Resolved>(TaskCreationOptions.RunContinuationsAsynchronously);
            var probe = system.ActorOf(Props.Create(() => new ResolveProbe(tcs)));
            Dns.ResolveName("localhost", system, probe);
            return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }

        [Fact(DisplayName = "DnsExt should resolve DNS end to end under the default config when dynamic type loading is off")]
        public async Task Should_resolve_dns_end_to_end_When_using_the_default_config_and_dynamic_type_loading_is_disabled()
        {
            await AkkaFeaturesSpec.WithDynamicTypeLoading(false, async () =>
            {
                var system = ActorSystem.Create("default-dns-off");
                try
                {
                    var ext = Dns.Instance.Apply(system);
                    ext.Provider.Should().BeOfType<InetAddressDnsProvider>();

                    var resolved = await ResolveLocalhostAsync(system);
                    resolved.IsSuccess.Should().BeTrue(because: resolved.Exception?.Message);
                    resolved.Name.Should().Be("localhost");
                }
                finally
                {
                    await system.Terminate();
                }
            });
        }

        [Theory(DisplayName = "DnsExt should resolve every spelling of the built-in provider name when dynamic type loading is off")]
        [InlineData("Akka.IO.InetAddressDnsProvider")]
        [InlineData("Akka.IO.InetAddressDnsProvider, Akka")]
        [InlineData("Akka.IO.InetAddressDnsProvider,Akka")]
        [InlineData("Akka.IO.InetAddressDnsProvider, AKKA")]
        [InlineData("Akka.IO.InetAddressDnsProvider, Akka, Version=99.0.0.0, Culture=neutral, PublicKeyToken=null")]
        public async Task Should_resolve_every_spelling_of_the_built_in_provider_When_dynamic_type_loading_is_disabled(string providerObjectName)
        {
            var config = ConfigurationFactory.ParseString($@"akka.io.dns.inet-address.provider-object = ""{providerObjectName}""");

            await AkkaFeaturesSpec.WithDynamicTypeLoading(false, async () =>
            {
                var system = ActorSystem.Create("built-in-provider-off", config);
                try
                {
                    Dns.Instance.Apply(system).Provider.Should().BeOfType<InetAddressDnsProvider>();
                }
                finally
                {
                    await system.Terminate();
                }
            });
        }

        [Fact(DisplayName = "DnsExt should load a custom provider when dynamic type loading is on")]
        public async Task Should_load_a_custom_provider_When_dynamic_type_loading_is_enabled()
        {
            var config = ConfigurationFactory.ParseString($@"akka.io.dns.inet-address.provider-object = ""{CustomProviderTypeName}""");

            await AkkaFeaturesSpec.WithDynamicTypeLoading(true, async () =>
            {
                var system = ActorSystem.Create("custom-dns-provider-on", config);
                try
                {
                    var ext = Dns.Instance.Apply(system);
                    ext.Provider.Should().BeOfType<CustomDnsProvider>();

                    // exercises the reflection path in both DnsExt and SimpleDnsManager end to end
                    var resolved = await ResolveLocalhostAsync(system);
                    resolved.IsSuccess.Should().BeTrue(because: resolved.Exception?.Message);
                    resolved.Name.Should().Be("localhost");
                }
                finally
                {
                    await system.Terminate();
                }
            });
        }

        [Fact(DisplayName = "DnsExt should reject a provider-object that is not built in when dynamic type loading is off")]
        public async Task Should_throw_ConfigurationException_When_the_provider_is_not_built_in_and_dynamic_type_loading_is_disabled()
        {
            var config = ConfigurationFactory.ParseString($@"akka.io.dns.inet-address.provider-object = ""{CustomProviderTypeName}""");

            await AkkaFeaturesSpec.WithDynamicTypeLoading(false, async () =>
            {
                var system = ActorSystem.Create("custom-dns-provider-off", config);
                try
                {
                    var exception = Assert.Throws<ConfigurationException>(() => Dns.Instance.Apply(system));

                    exception.Message.Should().Contain("akka.io.dns.inet-address.provider-object");
                    exception.Message.Should().Contain(CustomProviderTypeName);
                    exception.Message.Should().Contain(AkkaFeaturesSpec.SwitchName);
                }
                finally
                {
                    await system.Terminate();
                }
            });
        }
    }
}
