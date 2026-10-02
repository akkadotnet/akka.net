//-----------------------------------------------------------------------
// <copyright file="ModuleSerializerSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using FluentAssertions;

namespace Akka.Serialization
{
    /// <summary>
    /// The behavioural checks a module's serializer-table spec runs against its own live table: a plain system
    /// resolves every alias, id and binding the table lists, a HOCON alias or binding override still wins, Akka.Hosting's
    /// AssemblyQualifiedName spelling still resolves, and building with the switch off logs no warning. What the
    /// tables contain is approved in Akka.API.Tests (<c>SerializerTableSpec</c>), so nothing here keeps a second
    /// copy of it. Every member takes only public Akka types - a module's internal <c>ModuleSerializers</c> table
    /// stays in that module's own spec, which already has the access to use it.
    /// </summary>
    public static class ModuleSerializerSpecs
    {
        private const string SwitchName = "Akka.DynamicTypeLoading";

        private static string Spell(Type type) => $"{type.FullName}, {type.Assembly.GetName().Name}";

        /// <summary>
        /// The `akka.actor.serializers` and `akka.actor.serialization-bindings` rows an application would have copied
        /// from a 1.5 reference.conf, written from <paramref name="table"/>.
        /// </summary>
        public static Config RowsOf(IEnumerable<SerializerDetails> table)
        {
            var details = table.ToList();
            var serializers = details.Select(d => $@"{d.Alias} = ""{Spell(d.Serializer.GetType())}""");
            var bindings = details.SelectMany(d => d.UseFor.Select(t => $@"""{Spell(t)}"" = {d.Alias}"));
            return ConfigurationFactory.ParseString(
                $"akka.actor.serializers {{\n{string.Join("\n", serializers)}\n}}\n" +
                $"akka.actor.serialization-bindings {{\n{string.Join("\n", bindings)}\n}}");
        }

        /// <summary>
        /// Asserts a plain system - its config holds none of the module's rows, only the module's assembly is
        /// deployed - resolves what <paramref name="table"/> lists: every serializer id gives the table's serializer
        /// type, and every bound type finds it. Runs with dynamic type loading on and off.
        /// </summary>
        /// <param name="systemName">A name for the throwaway systems.</param>
        /// <param name="table">The module's built serializers, as its <c>ModuleSerializers.Create</c> returns them.</param>
        /// <param name="getSerializerById">
        /// <c>(serialization, id) =&gt; serialization.GetSerializerById(id)</c> - that method is internal to Akka, which
        /// the module's test project can reach and this shared project cannot.
        /// </param>
        public static async Task AssertPlainSystemResolvesTable(
            string systemName, IEnumerable<SerializerDetails> table, Func<Serialization, int, Serializer> getSerializerById)
        {
            var details = table.ToList();

            foreach (var dynamicTypeLoading in new[] { true, false })
            {
                await WithSystem($"{systemName}-{dynamicTypeLoading}", Config.Empty, null, system =>
                {
                    system.Settings.Config.GetConfig("akka.actor.serializers").AsEnumerable().Select(kv => kv.Key)
                        .Should().NotContain(details.Select(d => d.Alias), "the module's rows are not in the config");

                    var serialization = BuildDefault(system, dynamicTypeLoading);

                    foreach (var entry in details)
                    {
                        var expected = entry.Serializer.GetType();
                        getSerializerById(serialization, entry.Serializer.Identifier).Should().BeOfType(expected, $"id of {entry.Alias}");

                        foreach (var type in entry.UseFor)
                            serialization.FindSerializerForType(type).Should()
                                .BeOfType(expected, $"{type.FullName}, dynamic type loading {dynamicTypeLoading}");
                    }
                });
            }
        }

        /// <summary>
        /// Asserts an application can still replace a built-in alias: with no module rows in the config at all,
        /// `akka.actor.serializers.<paramref name="alias"/>` pointed at the byte-array serializer takes over every
        /// type in <paramref name="boundTypes"/>, which the module's default bound to that alias.
        /// </summary>
        public static async Task AssertAliasOverrideWins(string systemName, string alias, params Type[] boundTypes)
        {
            var overrides = ConfigurationFactory.ParseString(
                $@"akka.actor.serializers.{alias} = ""Akka.Serialization.ByteArraySerializer, Akka""");

            await WithSystem(systemName, overrides, null, system =>
            {
                var serialization = BuildDefault(system, dynamicTypeLoading: false);
                foreach (var type in boundTypes)
                    serialization.FindSerializerForType(type).Should().BeOfType<ByteArraySerializer>(type.FullName);
            });
        }

        /// <summary>Builds a <see cref="Serialization"/> over the default module table, holding the switch at <paramref name="dynamicTypeLoading"/> for the call.</summary>
        public static Serialization BuildDefault(ActorSystem system, bool dynamicTypeLoading)
        {
            var hadSwitch = AppContext.TryGetSwitch(SwitchName, out var previous);
            AppContext.SetSwitch(SwitchName, dynamicTypeLoading);
            try
            {
                return new Serialization((ExtendedActorSystem)system);
            }
            finally
            {
                AppContext.SetSwitch(SwitchName, !hadSwitch || previous);
            }
        }

        /// <summary>
        /// Starts a throwaway system from <paramref name="config"/> (falling back to <paramref name="moduleConfig"/>,
        /// then the default config), runs <paramref name="body"/>, then terminates it. <paramref name="moduleConfig"/>
        /// may be null when a test deliberately wants a system without any module rows.
        /// </summary>
        public static async Task WithSystem(string name, Config config, Config? moduleConfig, Action<ActorSystem> body)
        {
            var full = (moduleConfig is null ? config : config.WithFallback(moduleConfig))
                .WithFallback(ConfigurationFactory.Default());
            var system = ActorSystem.Create(name, full);
            try
            {
                body(system);
            }
            finally
            {
                await system.Terminate();
            }
        }

        /// <summary>
        /// Asserts every alias and bound type of <paramref name="table"/>, respelled as an AssemblyQualifiedName the way
        /// Akka.Hosting writes it, still resolves with the switch off, matching what <paramref name="reference"/>
        /// (a system with no rows, so everything comes from the module's defaults) resolves for the same type. Pass
        /// <paramref name="extraTypes"/> for bound types the table alone doesn't name, such as a closed
        /// generic sample of an open generic binding.
        /// </summary>
        public static async Task AssertHostingSpellingResolves(
            string systemName, IEnumerable<SerializerDetails> table, ActorSystem reference, IEnumerable<Type>? extraTypes = null)
        {
            var details = table.ToList();
            var hosting = ConfigurationFactory.ParseString(string.Join("\n",
                details.Select(d => $@"akka.actor.serializers.{d.Alias} = ""{d.Serializer.GetType().AssemblyQualifiedName}""")
                    .Concat(details.SelectMany(d => d.UseFor.Select(t =>
                        $@"akka.actor.serialization-bindings {{ ""{t.AssemblyQualifiedName}"" = {d.Alias} }}")))));

            await WithSystem(systemName, hosting, null, system =>
            {
                var serialization = BuildDefault(system, dynamicTypeLoading: false);
                var types = details.SelectMany(d => d.UseFor);
                if (extraTypes is not null)
                    types = types.Concat(extraTypes);

                foreach (var type in types)
                    serialization.FindSerializerForType(type)
                        .Should().BeOfType(reference.Serialization.FindSerializerForType(type).GetType(), type.FullName);
            });
        }

        /// <summary>Builds a <see cref="Serialization"/> over the default module table from <paramref name="system"/>'s config, switch off, asserting it logs no warning.</summary>
        public static async Task<Serialization> AssertBuildsWithoutWarning(ActorSystem system, EventFilterFactory eventFilter)
        {
            Serialization? serialization = null;
            await eventFilter.Warning().ExpectAsync(0, () =>
            {
                serialization = BuildDefault(system, dynamicTypeLoading: false);
                return Task.CompletedTask;
            });
            return serialization!;
        }

        /// <summary>
        /// Asserts that <paramref name="create"/> - ordinarily <c>system =&gt; new XSerializers().Create(system)</c> -
        /// does not throw against a fresh system that never loaded the module's own reference.conf, only core's
        /// own akka.conf. A module's serializers must build even for a system that has no reason to know the
        /// module exists yet - building one must never depend on that module's own config being present.
        /// </summary>
        public static async Task AssertBuildsWithoutModuleConfig(string systemName, Action<ExtendedActorSystem> create)
        {
            var system = ActorSystem.Create(systemName);
            try
            {
                Action act = () => create((ExtendedActorSystem)system);
                act.Should().NotThrow();
            }
            finally
            {
                await system.Terminate();
            }
        }
    }
}
