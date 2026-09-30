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
    /// The parity checks a module's serializer-table spec runs against its own reference.conf: the table's data
    /// matches the config in both directions, Akka.Hosting's AssemblyQualifiedName spelling still resolves, and
    /// building with the switch off logs no warning. Every member here takes only public Akka types - a module's
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

        /// <summary>
        /// Asserts a module's table is a complete, alias-accurate mirror of its config: every registration's alias
        /// names that registration's type in `akka.actor.serializers` (and vice versa - no extra alias), and every
        /// `akka.actor.serialization-bindings` row matches exactly one registration whose <c>Bindings</c> contains
        /// that row's type under that row's alias (and vice versa - no extra binding in the table).
        /// </summary>
        /// <param name="moduleConfig">The module's own reference.conf (or the combined config of its files).</param>
        /// <param name="registrations">Each registration's alias, serializer type, and the types it binds.</param>
        public static void AssertTableMatchesConfig(
            Config moduleConfig, IEnumerable<(string Alias, Type Type, IReadOnlyList<Type> Bindings)> registrations)
        {
            var table = registrations.ToList();

            var configuredTypeByAlias = SerializerRows(moduleConfig)
                .ToDictionary(r => r.Alias, r => Type.GetType(r.TypeName, throwOnError: true)!);

            // aliases match in both directions: every table alias is a config row for the same type, and every
            // config row has a table entry
            table.Select(r => (r.Alias, r.Type)).Should().BeEquivalentTo(
                configuredTypeByAlias.Select(kv => (Alias: kv.Key, Type: kv.Value)));

            var configuredBindings = BindingRows(moduleConfig)
                .Select(r => (Type: Type.GetType(r.TypeName, throwOnError: true)!, r.Alias));

            var tableBindings = table.SelectMany(r => r.Bindings.Select(t => (Type: t, r.Alias)));

            // bindings match in both directions: every table binding is a config row under the same alias, and
            // every config row is bound by exactly one registration under that alias
            tableBindings.Should().BeEquivalentTo(configuredBindings);
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
            var full = config.WithFallback(moduleConfig ?? Config.Empty).WithFallback(ConfigurationFactory.Default());
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
    }
}
