//-----------------------------------------------------------------------
// <copyright file="AkkaFeaturesSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Threading.Tasks;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Util
{
    /// <summary>
    /// <see cref="AppContext"/> switches are process-wide, so anything that flips
    /// <c>Akka.DynamicTypeLoading</c> belongs in this collection and never runs beside another spec.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class DynamicTypeLoadingCollection
    {
        public const string Name = "Akka.DynamicTypeLoading";
    }

    [Collection(DynamicTypeLoadingCollection.Name)]
    public class AkkaFeaturesSpec
    {
        private const string SwitchName = "Akka.DynamicTypeLoading";

        /// <summary>
        /// Runs <paramref name="body"/> with the <c>Akka.DynamicTypeLoading</c> switch forced to
        /// <paramref name="enabled"/> and puts it back afterwards.
        /// </summary>
        /// <remarks>
        /// <see cref="AppContext"/> has no way to unset a switch, so an unset switch is restored as
        /// <c>true</c> - which is the value <see cref="AkkaFeatures.IsDynamicTypeLoadingSupported"/> reports
        /// for an unset switch anyway.
        /// </remarks>
        private static async Task WithDynamicTypeLoading(bool enabled, Func<Task> body)
        {
            var hadSwitch = AppContext.TryGetSwitch(SwitchName, out var previous);
            AppContext.SetSwitch(SwitchName, enabled);
            try
            {
                await body();
            }
            finally
            {
                AppContext.SetSwitch(SwitchName, !hadSwitch || previous);
            }
        }

        [Fact(DisplayName = "AkkaFeatures should re-read the Akka.DynamicTypeLoading switch on every call")]
        public async Task Should_reread_the_switch_When_it_changes()
        {
            // nothing in Akka.NET or in the test host sets the switch, so an unset switch reports the
            // shipping default. AppContext cannot unset a switch once set, so this has to be asserted
            // before the spec touches it.
            AppContext.TryGetSwitch(SwitchName, out var alreadySet);
            if (!alreadySet)
                AkkaFeatures.IsDynamicTypeLoadingSupported.Should().BeTrue();

            // the property is deliberately not cached, which is what lets these specs flip it at runtime
            await WithDynamicTypeLoading(false, () =>
            {
                AkkaFeatures.IsDynamicTypeLoadingSupported.Should().BeFalse();
                AppContext.SetSwitch(SwitchName, true);
                AkkaFeatures.IsDynamicTypeLoadingSupported.Should().BeTrue();
                return Task.CompletedTask;
            });
        }

        [Theory(DisplayName = "TypeExtensions.StripAssemblyIdentity should reduce an assembly-qualified name to Ns.T, Asm")]
        [InlineData("Akka.Event.SemanticLogMessageFormatter")]
        [InlineData("Akka.Event.SemanticLogMessageFormatter, Akka")]
        [InlineData("Akka.Event.SemanticLogMessageFormatter, Akka, Version=1.5.60.0, Culture=neutral, PublicKeyToken=null")]
        [InlineData("Akka.Event.SemanticLogMessageFormatter, Akka, Version=99.0.0.0, Culture=neutral, PublicKeyToken=null")]
        [InlineData("Akka.Event.SemanticLogMessageFormatter, Akka, Version=99.0.0.0, Culture=neutral, PublicKeyToken=null, ProcessorArchitecture=MSIL, Retargetable=Yes")]
        public void Should_strip_assembly_identity_From_a_qualified_type_name(string typeName)
        {
            // this is what lets a BuiltIn* table carry two keys and still match what Akka.Hosting writes
            Akka.Util.TypeExtensions.StripAssemblyIdentity(typeName).Should()
                .BeOneOf("Akka.Event.SemanticLogMessageFormatter", "Akka.Event.SemanticLogMessageFormatter, Akka");
        }
    }
}
