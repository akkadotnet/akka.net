//-----------------------------------------------------------------------
// <copyright file="ProviderSelectionSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using Akka.Actor;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Actor
{
    /// <summary>
    /// <c>ActorSystemImpl.ConfigureProvider</c> only takes the trimmer-friendly
    /// <see cref="LocalActorRefProvider"/> branch when <see cref="ProviderSelection.GetProvider"/> maps the
    /// configured provider onto <see cref="ProviderSelection.Local"/>, so both spellings of the local provider
    /// type name have to land there - including the bare one that ships in <c>akka.conf</c>.
    /// </summary>
    public class ProviderSelectionSpec
    {
        [Fact(DisplayName = "Should_return_Local_When_GetProvider_is_given_the_bare_local_provider_type_name")]
        public void Should_return_Local_When_GetProvider_is_given_the_bare_local_provider_type_name()
        {
            ProviderSelection.GetProvider("Akka.Actor.LocalActorRefProvider")
                .Should().BeSameAs(ProviderSelection.Local.Instance);
        }

        [Fact(DisplayName = "Should_return_Local_When_GetProvider_is_given_the_assembly_qualified_local_provider_type_name")]
        public void Should_return_Local_When_GetProvider_is_given_the_assembly_qualified_local_provider_type_name()
        {
            ProviderSelection.GetProvider("Akka.Actor.LocalActorRefProvider, Akka")
                .Should().BeSameAs(ProviderSelection.Local.Instance);

            // the constant and the literal above must agree
            ProviderSelection.GetProvider(ProviderSelection.LocalActorRefProvider)
                .Should().BeSameAs(ProviderSelection.Local.Instance);
        }

        [Fact(DisplayName = "Should_report_the_assembly_qualified_local_provider_name_When_the_default_config_is_used")]
        public void Should_report_the_assembly_qualified_local_provider_name_When_the_default_config_is_used()
        {
            var settings = new Settings(null, Akka.Configuration.ConfigurationFactory.Default());

            settings.ProviderSelectionType.Should().BeSameAs(ProviderSelection.Local.Instance);
            settings.ProviderClass.Should().Be(ProviderSelection.LocalActorRefProvider);
        }
    }
}
