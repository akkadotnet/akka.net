//-----------------------------------------------------------------------
// <copyright file="BuiltInSerializerIdentifierSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Serialization;
using Xunit;

namespace Akka.API.Tests
{
    /// <summary>
    /// Every built-in serializer declares its wire id in code, and every module registers its serializers and
    /// bindings from code. This is the one project that references all of Remote, Cluster, Cluster.Tools,
    /// Cluster.Sharding, DistributedData, Cluster.Metrics, Persistence, Streams and core, so it's the one place
    /// that can check them against each other in a single test.
    ///
    /// The modules no longer ship the rows these checks compare against, so they run against
    /// <see cref="FrozenSerializerRows"/>: a copy of what each module's HOCON carried at 1.6.0-beta1. A mismatch
    /// means a built-in serializer's id, alias or binding changed, which breaks the wire format with older nodes.
    /// </summary>
    public class BuiltInSerializerIdentifierSpec
    {
        private const string SwitchName = "Akka.DynamicTypeLoading";

        /// <summary>Every module's frozen rows, plus the rows core's own akka.conf still ships.</summary>
        private static IReadOnlyList<(string Module, Config Rows)> AllRows { get; } =
            FrozenSerializerRows.All.Append(("Akka", ConfigurationFactory.Default())).ToList();

        private static IEnumerable<(string Module, string Alias, string TypeName)> SerializerRows() =>
            AllRows.SelectMany(m => m.Rows.GetConfig("akka.actor.serializers").AsEnumerable()
                .Select(kv => (m.Module, kv.Key, kv.Value.GetString())));

        private static IEnumerable<(string Module, string TypeName, string Alias)> BindingRows() =>
            AllRows.SelectMany(m => m.Rows.GetConfig("akka.actor.serialization-bindings").AsEnumerable()
                .Select(kv => (m.Module, kv.Key, kv.Value.GetString())));

        private static IEnumerable<(string Module, string TypeName, int Id)> IdentifierRows() =>
            AllRows.SelectMany(m => m.Rows.GetConfig("akka.actor.serialization-identifiers").AsEnumerable()
                .Select(kv => (m.Module, kv.Key, kv.Value.GetInt())));

        /// <summary>
        /// Each serializer is created with <see cref="RuntimeHelpers.GetUninitializedObject"/>, which skips the
        /// constructor entirely, leaving the instance's actor-system field null. A built-in serializer's <c>Identifier</c>
        /// is either a hardcoded constant or a <c>GetType()</c> guard around one - never a HOCON read - so this is safe
        /// for all of them. It also proves a serializer that still resolved its id lazily from HOCON would touch the
        /// null actor-system field on first access and throw, instead of quietly returning the right answer.
        /// </summary>
        [Fact(DisplayName = "Should_match_the_frozen_serialization_identifiers_When_reading_every_builtin_serializer_Identifier")]
        public void Should_match_the_frozen_serialization_identifiers_When_reading_every_builtin_serializer_Identifier()
        {
            foreach (var (module, typeName, expectedId) in IdentifierRows())
            {
                var type = Type.GetType(typeName, throwOnError: true);

                var serializer = (Serializer)RuntimeHelpers.GetUninitializedObject(type);
                Assert.True(expectedId == serializer.Identifier,
                    $"{type.FullName} ({module}) declared Identifier {serializer.Identifier}, but its 1.6.0-beta1 row says {expectedId}");
            }
        }

        [Fact(DisplayName = "Should_have_unique_aliases_When_combining_every_module_and_core")]
        public void Should_have_unique_aliases_When_combining_every_module_and_core()
        {
            var duplicates = SerializerRows().GroupBy(r => r.Alias).Where(g => g.Count() > 1)
                .Select(g => $"{g.Key} ({string.Join(", ", g.Select(r => r.Module))})").ToList();

            Assert.True(duplicates.Count == 0, $"Aliases used by more than one module: {string.Join("; ", duplicates)}");
        }

        [Fact(DisplayName = "Should_have_unique_serializer_ids_When_combining_every_module_and_core")]
        public void Should_have_unique_serializer_ids_When_combining_every_module_and_core()
        {
            var duplicates = IdentifierRows().GroupBy(r => r.Id).Where(g => g.Count() > 1)
                .Select(g => $"{g.Key} ({string.Join(", ", g.Select(r => r.TypeName))})").ToList();

            Assert.True(duplicates.Count == 0, $"Serializer ids used by more than one serializer: {string.Join("; ", duplicates)}");
        }

        [Fact(DisplayName = "Should_map_every_bound_type_to_exactly_one_alias_When_combining_every_module_and_core")]
        public void Should_map_every_bound_type_to_exactly_one_alias_When_combining_every_module_and_core()
        {
            var aliases = SerializerRows().Select(r => r.Alias).ToHashSet();

            var bindings = BindingRows().Select(r => (r.Module, Type: Type.GetType(r.TypeName, throwOnError: true), r.Alias)).ToList();

            foreach (var (module, type, alias) in bindings)
                Assert.True(aliases.Contains(alias), $"{type.FullName} ({module}) is bound to [{alias}], which no module or core registers");

            var multiplyBound = bindings.GroupBy(b => b.Type).Where(g => g.Count() > 1)
                .Select(g => $"{g.Key.FullName} ({string.Join(", ", g.Select(b => $"{b.Module}:{b.Alias}"))})").ToList();

            Assert.True(multiplyBound.Count == 0, $"Types bound more than once: {string.Join("; ", multiplyBound)}");
        }

        [Fact(DisplayName = "Should_have_an_id_row_for_every_serializer_When_combining_every_module_and_core")]
        public void Should_have_an_id_row_for_every_serializer_When_combining_every_module_and_core()
        {
            // every alias names a serializer type that has an id row, and every id row names a registered serializer type
            var serializerTypes = SerializerRows().Select(r => Type.GetType(r.TypeName, throwOnError: true)).ToHashSet();
            var idTypes = IdentifierRows().Select(r => Type.GetType(r.TypeName, throwOnError: true)).ToHashSet();

            Assert.True(serializerTypes.SetEquals(idTypes),
                $"Serializers without an id row: [{string.Join(", ", serializerTypes.Except(idTypes).Select(t => t.FullName))}]; " +
                $"id rows without a serializer: [{string.Join(", ", idTypes.Except(serializerTypes).Select(t => t.FullName))}]");
        }

        /// <summary>
        /// The plain system's config carries none of the modules' rows - only core's own - yet every module
        /// assembly this project references is deployed, so each module's table registers from code.
        /// </summary>
        [Theory(DisplayName = "Should_resolve_every_frozen_row_When_a_plain_system_has_every_module_deployed")]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Should_resolve_every_frozen_row_When_a_plain_system_has_every_module_deployed(bool dynamicTypeLoading)
        {
            var system = ActorSystem.Create("api-plain-system");
            try
            {
                var rows = system.Settings.Config.GetConfig("akka.actor.serializers").AsEnumerable().Select(kv => kv.Key).ToList();
                Assert.DoesNotContain(rows, alias => FrozenSerializerRows.All.Any(m =>
                    m.Rows.GetConfig("akka.actor.serializers").AsEnumerable().Any(r => r.Key == alias)));

                var hadSwitch = AppContext.TryGetSwitch(SwitchName, out var previous);
                AppContext.SetSwitch(SwitchName, dynamicTypeLoading);
                try
                {
                    var serialization = new Akka.Serialization.Serialization((ExtendedActorSystem)system);

                    foreach (var (module, frozen) in FrozenSerializerRows.All)
                    {
                        var typeByAlias = frozen.GetConfig("akka.actor.serializers").AsEnumerable()
                            .ToDictionary(kv => kv.Key, kv => Type.GetType(kv.Value.GetString(), throwOnError: true));

                        foreach (var row in frozen.GetConfig("akka.actor.serialization-identifiers").AsEnumerable())
                            Assert.IsType(Type.GetType(row.Key, throwOnError: true), serialization.GetSerializerById(row.Value.GetInt()));

                        foreach (var row in frozen.GetConfig("akka.actor.serialization-bindings").AsEnumerable())
                        {
                            var boundType = Type.GetType(row.Key, throwOnError: true);
                            Assert.True(typeByAlias[row.Value.GetString()] == serialization.FindSerializerForType(boundType).GetType(),
                                $"{boundType.FullName} ({module}) resolved to {serialization.FindSerializerForType(boundType).GetType().FullName}");
                        }
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
