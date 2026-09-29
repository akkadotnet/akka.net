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
        /// Asserts a module's table names exactly the types its config rows name, in both directions: every row
        /// resolves to a type the table has, and the table has no type without a row.
        /// </summary>
        public static void AssertTableMatchesConfig(Config moduleConfig, IEnumerable<Type> serializerTypes, IEnumerable<Type> boundTypes)
        {
            var configuredSerializerTypes = SerializerRows(moduleConfig).Select(r => Type.GetType(r.TypeName, throwOnError: true));
            var configuredBoundTypes = BindingRows(moduleConfig).Select(r => Type.GetType(r.TypeName, throwOnError: true));

            serializerTypes.Should().BeEquivalentTo(configuredSerializerTypes);
            boundTypes.Should().BeEquivalentTo(configuredBoundTypes);
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
