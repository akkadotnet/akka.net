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
    /// The parity checks a module's serializer-table spec runs against <see cref="FrozenSerializerRows"/> - the
    /// rows the module's HOCON shipped at 1.6.0-beta1, before the table became the only source of them: the table
    /// matches the frozen rows in both directions, Akka.Hosting's AssemblyQualifiedName spelling still resolves,
    /// and building with the switch off logs no warning. Every member here takes only public Akka types - a module's
    /// internal <c>ModuleSerializers</c> table, and the internal API needed to force a reflection-only baseline for
    /// comparison, stay in that module's own spec, which already has the access to use them.
    /// </summary>
    public static class ModuleSerializerSpecs
    {
        private const string SwitchName = "Akka.DynamicTypeLoading";

        /// <summary>The alias/type-name rows of `akka.actor.serializers` in <paramref name="moduleConfig"/>.</summary>
        public static IEnumerable<(string Alias, string TypeName)> SerializerRows(Config moduleConfig) =>
            moduleConfig.GetConfig("akka.actor.serializers").AsEnumerable().Select(kv => (kv.Key, kv.Value.GetString()));

        /// <summary>The type-name/alias rows of `akka.actor.serialization-bindings` in <paramref name="moduleConfig"/>.</summary>
        public static IEnumerable<(string TypeName, string Alias)> BindingRows(Config moduleConfig) =>
            moduleConfig.GetConfig("akka.actor.serialization-bindings").AsEnumerable().Select(kv => (kv.Key, kv.Value.GetString()));

        /// <summary>The type-name/id rows of `akka.actor.serialization-identifiers` in <paramref name="moduleConfig"/>.</summary>
        public static IEnumerable<(string TypeName, int Id)> IdentifierRows(Config moduleConfig) =>
            moduleConfig.GetConfig("akka.actor.serialization-identifiers").AsEnumerable().Select(kv => (kv.Key, kv.Value.GetInt()));

        /// <summary>
        /// Asserts a module's table is a complete, alias-accurate mirror of the rows 1.6.0-beta1 shipped: every
        /// registration's alias names that registration's serializer type in `akka.actor.serializers` (and vice
        /// versa - no extra alias), every `akka.actor.serialization-bindings` row matches exactly one registration
        /// whose <c>UseFor</c> contains that row's type under that row's alias (and vice versa - no extra binding in
        /// the table), and every serializer's <c>Identifier</c> equals its `akka.actor.serialization-identifiers`
        /// row. The table also has to be consistent with itself: aliases are unique, and no type is bound twice.
        /// </summary>
        /// <param name="frozenRows">The module's rows from <see cref="FrozenSerializerRows"/>.</param>
        /// <param name="details">The module's built serializers, as its <c>ModuleSerializers.Create</c> returns them.</param>
        public static void AssertTableMatchesFrozenRows(Config frozenRows, IEnumerable<SerializerDetails> details)
        {
            var table = details.ToList();

            table.Select(r => r.Alias).Should().OnlyHaveUniqueItems("an alias names one serializer");
            table.SelectMany(r => r.UseFor).Should().OnlyHaveUniqueItems("a type is bound to one serializer");

            var configuredTypeByAlias = SerializerRows(frozenRows)
                .ToDictionary(r => r.Alias, r => Type.GetType(r.TypeName, throwOnError: true)!);

            // aliases match in both directions: every table alias is a frozen row for the same type, and every
            // frozen row has a table entry
            table.Select(r => (r.Alias, Type: r.Serializer.GetType())).Should().BeEquivalentTo(
                configuredTypeByAlias.Select(kv => (Alias: kv.Key, Type: kv.Value)));

            var configuredBindings = BindingRows(frozenRows)
                .Select(r => (Type: Type.GetType(r.TypeName, throwOnError: true)!, r.Alias));

            var tableBindings = table.SelectMany(r => r.UseFor.Select(t => (Type: t, r.Alias)));

            // bindings match in both directions: every table binding is a frozen row under the same alias, and
            // every frozen row is bound by exactly one registration under that alias
            tableBindings.Should().BeEquivalentTo(configuredBindings);

            // ids match in both directions: the serializer ids are the wire contract with older nodes
            table.Select(r => (Type: r.Serializer.GetType(), Id: r.Serializer.Identifier)).Should().BeEquivalentTo(
                IdentifierRows(frozenRows).Select(r => (Type: Type.GetType(r.TypeName, throwOnError: true)!, r.Id)));
        }

        /// <summary>
        /// Asserts a plain system - its config holds none of the module's rows, only the module's assembly is
        /// deployed - resolves what 1.6.0-beta1 resolved from the shipped rows: every serializer id gives the
        /// frozen type, and every bound type finds the serializer type its frozen alias names. Runs with dynamic
        /// type loading on and off.
        /// </summary>
        /// <param name="systemName">A name for the throwaway systems.</param>
        /// <param name="frozenRows">The module's rows from <see cref="FrozenSerializerRows"/>.</param>
        /// <param name="getSerializerById">
        /// <c>(serialization, id) =&gt; serialization.GetSerializerById(id)</c> - that method is internal to Akka, which
        /// the module's test project can reach and this shared project cannot.
        /// </param>
        public static async Task AssertPlainSystemResolvesFrozenRows(
            string systemName, Config frozenRows, Func<Serialization, int, Serializer> getSerializerById)
        {
            var typeByAlias = SerializerRows(frozenRows)
                .ToDictionary(r => r.Alias, r => Type.GetType(r.TypeName, throwOnError: true)!);

            foreach (var dynamicTypeLoading in new[] { true, false })
            {
                await WithSystem($"{systemName}-{dynamicTypeLoading}", Config.Empty, null, system =>
                {
                    SerializerRows(system.Settings.Config).Select(r => r.Alias)
                        .Should().NotContain(typeByAlias.Keys, "the module's rows are not in the config");

                    var serialization = BuildDefault(system, dynamicTypeLoading);

                    foreach (var (typeName, id) in IdentifierRows(frozenRows))
                        getSerializerById(serialization, id).Should().BeOfType(Type.GetType(typeName, throwOnError: true)!, $"id {id}");

                    foreach (var (typeName, alias) in BindingRows(frozenRows))
                    {
                        var type = Type.GetType(typeName, throwOnError: true)!;
                        serialization.FindSerializerForType(type).Should().BeOfType(typeByAlias[alias], $"{type.FullName}, dynamic type loading {dynamicTypeLoading}");
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
        /// may be null when a test deliberately wants a system without the module's own rows.
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
        /// Asserts every row in <paramref name="moduleConfig"/>, respelled as an AssemblyQualifiedName the way
        /// Akka.Hosting writes it, still resolves with the switch off, matching what <paramref name="reference"/>
        /// (a system already built from the module's own config) resolves for the same type. Pass
        /// <paramref name="extraTypes"/> for bound types the binding rows alone don't name, such as a closed
        /// generic sample of an open generic binding.
        /// </summary>
        public static async Task AssertHostingSpellingResolves(
            string systemName, Config moduleConfig, ActorSystem reference, IEnumerable<Type>? extraTypes = null)
        {
            string Aqn(string typeName) => Type.GetType(typeName, throwOnError: true)!.AssemblyQualifiedName!;
            var hosting = ConfigurationFactory.ParseString(string.Join("\n",
                SerializerRows(moduleConfig).Select(r => $@"akka.actor.serializers.{r.Alias} = ""{Aqn(r.TypeName)}""")
                    .Concat(BindingRows(moduleConfig).Select(r => $@"akka.actor.serialization-bindings {{ ""{Aqn(r.TypeName)}"" = {r.Alias} }}"))));

            await WithSystem(systemName, hosting, moduleConfig, system =>
            {
                var serialization = BuildDefault(system, dynamicTypeLoading: false);
                var types = BindingRows(moduleConfig).Select(r => Type.GetType(r.TypeName, throwOnError: true)!);
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
