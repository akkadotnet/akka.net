//-----------------------------------------------------------------------
// <copyright file="ProviderSelectionSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using Akka.Actor;
using Akka.Configuration;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Actor
{
    /// <summary>
    /// <c>ActorSystemImpl.ConfigureProvider</c> only takes the trimmer-friendly
    /// <see cref="LocalActorRefProvider"/> branch when <see cref="ProviderSelection.GetProvider"/> maps the
    /// configured provider onto <see cref="ProviderSelection.Local"/>, so every spelling of the local provider
    /// type name has to land there - including the bare one that ships in <c>akka.conf</c>.
    /// </summary>
    public class ProviderSelectionSpec
    {
        [Theory(DisplayName = "Should_return_Local_When_GetProvider_is_given_a_local_provider_type_name")]
        [InlineData("local")] // the alias
        [InlineData("Akka.Actor.LocalActorRefProvider")] // the bare type name that ships in akka.conf
        [InlineData(ProviderSelection.LocalActorRefProvider)] // "Akka.Actor.LocalActorRefProvider, Akka"
        public void Should_return_Local_When_GetProvider_is_given_a_local_provider_type_name(string providerClass)
        {
            ProviderSelection.GetProvider(providerClass)
                .Should().BeSameAs(ProviderSelection.Local.Instance);
        }

        [Fact(DisplayName = "Should_report_the_assembly_qualified_local_provider_name_When_the_default_config_is_used")]
        public void Should_report_the_assembly_qualified_local_provider_name_When_the_default_config_is_used()
        {
            var settings = new Settings(null, ConfigurationFactory.Default());

            settings.ProviderSelectionType.Should().BeSameAs(ProviderSelection.Local.Instance);
            settings.ProviderClass.Should().Be(ProviderSelection.LocalActorRefProvider);
        }
    }
}
