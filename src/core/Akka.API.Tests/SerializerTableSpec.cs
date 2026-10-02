//-----------------------------------------------------------------------
// <copyright file="SerializerTableSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Serialization;
using VerifyXunit;
using Xunit;

namespace Akka.API.Tests
{
    /// <summary>
    /// The serializer tables of the first-party modules are the only source of their built-in serializers: each
    /// module registers its aliases, serializer ids and type bindings from code, with no HOCON row behind them.
    /// Those three things are the wire contract with older nodes, so they are approved here like the public API is.
    /// This is the one project that references Remote, Cluster, Cluster.Tools, Cluster.Sharding, DistributedData,
    /// Cluster.Metrics, Persistence, Streams and core, so it is the one place that sees every table at once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>verify/SerializerTableSpec.ApproveModuleSerializerTables.DotNet.verified.txt</c> lists every module's table: one
    /// line per alias (with its serializer type and id) and one line per bound type, all sorted ordinally. The first
    /// approved version is exactly what 1.6.0-beta1 shipped in the modules' HOCON. A change to the file is a change
    /// to what a module registers, so it shows up as a diff in review - a V2 serializer, or a binding moved at a
    /// cut-over, are reviewed the same way. Approve an intentional change the way the repo approves API changes:
    /// compare the <c>.received.txt</c> file Verify writes next to it with the <c>.verified.txt</c> file, and copy
    /// it over once the difference is what you meant.
    /// </para>
    /// <para>
    /// The cross-module checks run over the live tables and core's own rows, so a collision between two modules
    /// fails here, where all of them are visible.
    /// </para>
    /// </remarks>
    public class SerializerTableSpec
    {
        private const string SwitchName = "Akka.DynamicTypeLoading";

        private sealed record TableRow(string Module, string Alias, Type Serializer, int Id, IReadOnlyCollection<Type> BoundTypes);

        private static string TypeName(Type type) => $"{type.FullName}, {type.Assembly.GetName().Name}";

        /// <summary>Builds every table in <see cref="ModuleSerializerTable.Default"/> against a real actor system.</summary>
        private static async Task<List<TableRow>> BuildModuleRowsAsync()
        {
            var system = ActorSystem.Create("serializer-table-spec");
            try
            {
                var rows = new List<TableRow>();
                foreach (var assembly in ModuleSerializerTable.Default.AssemblyNames)
                {
                    var module = ModuleSerializerTable.Default.ForAssembly(assembly);
                    Assert.True(module is not null, $"Module [{assembly}] did not load: this project references every first-party module");

                    // a serializer that read its id from HOCON would throw here, since the system has none of the module rows
                    foreach (var details in module.Create((ExtendedActorSystem)system))
                        rows.Add(new TableRow(assembly, details.Alias, details.Serializer.GetType(), details.Serializer.Identifier,
                            details.UseFor.ToList()));
                }

                return rows;
            }
            finally
            {
                await system.Terminate();
            }
        }

        /// <summary>
        /// Core's own two serializers, from the rows akka.conf still ships. They are not in a module table, but their
        /// aliases, ids and bindings must not collide with a module's either.
        /// </summary>
        private static List<TableRow> CoreRows()
        {
            var config = ConfigurationFactory.Default();
            var serializers = config.GetConfig("akka.actor.serializers").AsEnumerable()
                .ToDictionary(kv => kv.Key, kv => Type.GetType(kv.Value.GetString(), throwOnError: true));
            var bindings = config.GetConfig("akka.actor.serialization-bindings").AsEnumerable()
                .Select(kv => (Type: Type.GetType(kv.Key, throwOnError: true), Alias: kv.Value.GetString())).ToList();

            return serializers.Select(s => new TableRow("Akka", s.Key, s.Value,
                // core's serializers declare their id in code, so the instance needs no constructor to answer
                ((Serializer)RuntimeHelpers.GetUninitializedObject(s.Value)).Identifier,
                bindings.Where(b => b.Alias == s.Key).Select(b => b.Type).ToList())).ToList();
        }

        /// <summary>One line per alias, one line per bound type under it; modules, aliases and types sorted ordinally.</summary>
        private static string Render(IEnumerable<TableRow> rows)
        {
            var text = new StringBuilder();
            foreach (var module in rows.GroupBy(r => r.Module).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                text.Append("module ").Append(module.Key).Append('\n');
                foreach (var row in module.OrderBy(r => r.Alias, StringComparer.Ordinal))
                {
                    text.Append("  ").Append(row.Alias).Append(": ").Append(TypeName(row.Serializer))
                        .Append(" (id ").Append(row.Id).Append(")\n");
                    foreach (var name in row.BoundTypes.Select(TypeName).OrderBy(n => n, StringComparer.Ordinal))
                        text.Append("    ").Append(name).Append('\n');
                }
            }

            return text.ToString();
        }

        [Fact(DisplayName = "Should_match_the_approved_snapshot_When_listing_every_module_serializer_table")]
        public async Task ApproveModuleSerializerTables()
        {
            await Verifier.Verify(Render(await BuildModuleRowsAsync()));
        }

        [Fact(DisplayName = "Should_have_unique_aliases_When_combining_every_module_and_core")]
        public async Task Should_have_unique_aliases_When_combining_every_module_and_core()
        {
            var rows = (await BuildModuleRowsAsync()).Concat(CoreRows());

            var duplicates = rows.GroupBy(r => r.Alias).Where(g => g.Count() > 1)
                .Select(g => $"{g.Key} ({string.Join(", ", g.Select(r => r.Module))})").ToList();

            Assert.True(duplicates.Count == 0, $"Aliases used by more than one table: {string.Join("; ", duplicates)}");
        }

        [Fact(DisplayName = "Should_have_unique_serializer_ids_When_combining_every_module_and_core")]
        public async Task Should_have_unique_serializer_ids_When_combining_every_module_and_core()
        {
            var rows = (await BuildModuleRowsAsync()).Concat(CoreRows());

            var duplicates = rows.GroupBy(r => r.Id).Where(g => g.Count() > 1)
                .Select(g => $"{g.Key} ({string.Join(", ", g.Select(r => r.Serializer.FullName))})").ToList();

            Assert.True(duplicates.Count == 0, $"Serializer ids used by more than one serializer: {string.Join("; ", duplicates)}");
        }

        [Fact(DisplayName = "Should_register_each_serializer_type_under_one_alias_When_combining_every_module_and_core")]
        public async Task Should_register_each_serializer_type_under_one_alias_When_combining_every_module_and_core()
        {
            var rows = (await BuildModuleRowsAsync()).Concat(CoreRows()).ToList();

            // every serializer already has an id (it is read from the instance), so what can go wrong is one type
            // listed under two aliases
            var shared = rows.GroupBy(r => r.Serializer).Where(g => g.Count() > 1)
                .Select(g => $"{g.Key.FullName} ({string.Join(", ", g.Select(r => r.Alias))})").ToList();

            Assert.True(shared.Count == 0, $"Serializer types registered under more than one alias: {string.Join("; ", shared)}");
        }

        [Fact(DisplayName = "Should_map_every_bound_type_to_exactly_one_alias_When_combining_every_module_and_core")]
        public async Task Should_map_every_bound_type_to_exactly_one_alias_When_combining_every_module_and_core()
        {
            var rows = (await BuildModuleRowsAsync()).Concat(CoreRows()).ToList();

            var multiplyBound = rows.SelectMany(r => r.BoundTypes.Select(t => (Type: t, r.Module, r.Alias)))
                .GroupBy(b => b.Type).Where(g => g.Count() > 1)
                .Select(g => $"{g.Key.FullName} ({string.Join(", ", g.Select(b => $"{b.Module}:{b.Alias}"))})").ToList();

            Assert.True(multiplyBound.Count == 0, $"Types bound more than once: {string.Join("; ", multiplyBound)}");
        }

        /// <summary>
        /// A plain system's config carries none of the modules' rows - only core's own - yet every module assembly this
        /// project references is deployed, so each table registers from code. Every id gives the table's serializer
        /// type and every bound type finds it, with dynamic type loading on and off.
        /// </summary>
        [Theory(DisplayName = "Should_resolve_every_table_entry_When_a_plain_system_has_every_module_deployed")]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Should_resolve_every_table_entry_When_a_plain_system_has_every_module_deployed(bool dynamicTypeLoading)
        {
            var rows = await BuildModuleRowsAsync();

            var system = ActorSystem.Create("serializer-table-plain-system");
            try
            {
                var configured = system.Settings.Config.GetConfig("akka.actor.serializers").AsEnumerable().Select(kv => kv.Key).ToList();
                Assert.DoesNotContain(configured, alias => rows.Any(r => r.Alias == alias));

                var hadSwitch = AppContext.TryGetSwitch(SwitchName, out var previous);
                AppContext.SetSwitch(SwitchName, dynamicTypeLoading);
                try
                {
                    var serialization = new Akka.Serialization.Serialization((ExtendedActorSystem)system);

                    foreach (var row in rows)
                    {
                        Assert.IsType(row.Serializer, serialization.GetSerializerById(row.Id));

                        foreach (var type in row.BoundTypes)
                            Assert.True(row.Serializer == serialization.FindSerializerForType(type).GetType(),
                                $"{type.FullName} ({row.Module}) resolved to {serialization.FindSerializerForType(type).GetType().FullName}, " +
                                $"not {row.Serializer.FullName}");
                    }
                }
                finally
                {
                    AppContext.SetSwitch(SwitchName, !hadSwitch || previous);
                }
            }
            finally
            {
                await system.Terminate();
            }
        }
    }
}
