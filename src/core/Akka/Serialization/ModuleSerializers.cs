//-----------------------------------------------------------------------
// <copyright file="ModuleSerializers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using Akka.Actor;

namespace Akka.Serialization
{
    /// <summary>
    /// INTERNAL API. The serializers, bindings and ids of a first-party module, so <see cref="Serialization"/> can
    /// register them without <see cref="Type.GetType(string)"/> and without any HOCON row. <see cref="Create"/> has the same shape as
    /// <see cref="SerializationSetup.CreateSerializers"/>; unlike a <see cref="SerializationSetup"/>, every entry here
    /// is registered as a default as soon as the module loads - HOCON can still override an alias or a binding, and
    /// a <see cref="SerializationSetup"/> wins over both.
    /// </summary>
    internal abstract class ModuleSerializers
    {
        public abstract ImmutableHashSet<SerializerDetails> Create(ExtendedActorSystem system);

        /// <summary>
        /// The legacy row each of this module's V2 rows supersedes, as V2 alias to legacy alias - for example
        /// <c>["reliable-delivery-v2"] = "reliable-delivery"</c>. Both rows must come from <see cref="Create"/>, and the
        /// V2 row ships read-only (empty <see cref="SerializerDetails.UseFor"/>). When the Serialization V2 switch
        /// (<c>akka.actor.serialization-v2</c> or <see cref="SerializationV2Setup"/>) is on, the V2 row takes over the
        /// legacy row's default bindings; when it is off, nothing moves. Both ids stay registered either way.
        /// </summary>
        public virtual ImmutableDictionary<string, string> Supersedes => ImmutableDictionary<string, string>.Empty;
    }

    /// <summary>
    /// INTERNAL API. A module's serializers, built and indexed by stripped full name for strict matching.
    /// </summary>
    internal sealed class LoadedModule
    {
        private readonly Dictionary<string, (SerializerDetails Details, string? Assembly, bool IsAkka)> _serializers = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (Type Type, string? Assembly, bool IsAkka)> _boundTypes = new(StringComparer.Ordinal);

        // V2 row -> the legacy row it supersedes, and the reverse, both by alias
        private readonly Dictionary<string, SerializerDetails> _supersededBy = new(StringComparer.Ordinal);
        private readonly Dictionary<string, SerializerDetails> _supersedes = new(StringComparer.Ordinal);

        /// <summary>Every row <see cref="ModuleSerializers.Create"/> returned, for registering the module's defaults.</summary>
        internal ImmutableHashSet<SerializerDetails> Details { get; }

        /// <summary>Each V2 row with the legacy row it supersedes, as <see cref="ModuleSerializers.Supersedes"/> declared them.</summary>
        internal List<(SerializerDetails V2, SerializerDetails Legacy)> Takeovers { get; } = new();

        private LoadedModule(ImmutableHashSet<SerializerDetails> details, ImmutableDictionary<string, string> supersedes, string moduleName)
        {
            Details = details;
            foreach (var entry in details)
            {
                var serializerType = entry.Serializer.GetType();
                _serializers[KeyOf(serializerType)] = (entry, serializerType.Assembly.GetName().Name, IsAkka(serializerType));
                foreach (var type in entry.UseFor)
                    _boundTypes[KeyOf(type)] = (type, type.Assembly.GetName().Name, IsAkka(type));
            }

            if (supersedes.IsEmpty)
                return;

            var byAlias = new Dictionary<string, SerializerDetails>(StringComparer.Ordinal);
            foreach (var entry in details)
                byAlias[entry.Alias] = entry;

            // a broken declaration is a first-party bug: fail every system that loads the module, so no test run misses it
            foreach (var kv in supersedes)
            {
                if (!byAlias.TryGetValue(kv.Key, out var v2) || !byAlias.TryGetValue(kv.Value, out var legacy))
                    throw new InvalidOperationException(
                        $"Module [{moduleName}] declares that [{kv.Key}] supersedes [{kv.Value}], but its table has no row for one of them.");
                // no chains, no self-reference, and one V2 row per legacy row
                if (supersedes.ContainsKey(kv.Value) || _supersededBy.ContainsKey(kv.Value))
                    throw new InvalidOperationException(
                        $"Module [{moduleName}] declares that [{kv.Key}] supersedes [{kv.Value}], but [{kv.Value}] is a V2 row itself or is already superseded.");
                if (!v2.UseFor.IsEmpty)
                    throw new InvalidOperationException(
                        $"Module [{moduleName}] declares that [{kv.Key}] supersedes [{kv.Value}], but [{kv.Key}] binds types of its own; a V2 row ships read-only.");

                _supersedes[kv.Key] = legacy;
                _supersededBy[kv.Value] = v2;
                Takeovers.Add((v2, legacy));
            }
        }

        /// <summary>
        /// The types <paramref name="row"/> binds as a module default. With <paramref name="serializationV2"/> off, that is
        /// its own <see cref="SerializerDetails.UseFor"/>. With it on, a superseded legacy row binds nothing, and the V2
        /// row that supersedes it binds the legacy row's types.
        /// </summary>
        internal ImmutableHashSet<Type> DefaultBindings(SerializerDetails row, bool serializationV2)
        {
            if (!serializationV2)
                return row.UseFor;
            if (_supersededBy.ContainsKey(row.Alias))
                return ImmutableHashSet<Type>.Empty;
            return _supersedes.TryGetValue(row.Alias, out var legacy) ? row.UseFor.Union(legacy.UseFor) : row.UseFor;
        }

        /// <summary>
        /// Builds <paramref name="module"/>'s serializers against <paramref name="system"/>; null when that fails
        /// with a version-skew error, in which case <paramref name="skewError"/> is that exception - the caller
        /// decides whether to surface it.
        /// </summary>
        internal static LoadedModule? TryCreate(ModuleSerializers module, ExtendedActorSystem system, out Exception? skewError)
        {
            try
            {
                skewError = null;
                return new LoadedModule(module.Create(system), module.Supersedes, module.GetType().FullName ?? module.GetType().Name);
            }
            catch (Exception e) when (ModuleSerializerTable.IsVersionSkew(e))
            {
                // the module, or something its serializers reference, is missing from this build: it counts as absent
                skewError = e;
                return null;
            }
        }

        internal SerializerDetails? FindSerializer(string name, string? assembly)
            => _serializers.TryGetValue(name, out var s) && Accepts(s.Assembly, s.IsAkka, assembly) ? s.Details : null;

        internal Type? FindBoundType(string name, string? assembly)
            => _boundTypes.TryGetValue(name, out var t) && Accepts(t.Assembly, t.IsAkka, assembly) ? t.Type : null;

        private static string KeyOf(Type type) => Akka.Util.TypeExtensions.StripAssemblyIdentity(type.FullName ?? string.Empty);

        private static bool IsAkka(Type type) => type.Assembly == typeof(Serialization).Assembly;

        /// <summary>
        /// What <see cref="Type.GetType(string)"/> called from Akka.dll accepts: a bare name finds Akka.dll and framework
        /// types only; a framework type also matches any framework assembly name; anything else needs its own assembly.
        /// </summary>
        private static bool Accepts(string? actual, bool isAkka, string? assembly)
        {
            if (assembly is null)
                return isAkka || Serialization.FrameworkAssemblyNames.Contains(actual!);

            return Serialization.FrameworkAssemblyNames.Contains(assembly)
                ? Serialization.FrameworkAssemblyNames.Contains(actual!)
                : string.Equals(assembly, actual, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// INTERNAL API. Finds a module's table by assembly simple name, loading it lazily and at most once per table.
    /// </summary>
    internal sealed class ModuleSerializerTable
    {
        /// <summary>The process-wide table behind the public <see cref="Serialization"/> constructor.</summary>
        internal static readonly ModuleSerializerTable Default = new(new Dictionary<string, Func<ModuleSerializers?>>
        {
            // one entry per module, each passing its own literal to Load so the trimmer can see the type
            ["Akka.Remote"] = () => Load("Akka.Remote.Serialization.RemoteSerializers, Akka.Remote"),
            ["Akka.Streams"] = () => Load("Akka.Streams.Serialization.StreamsSerializers, Akka.Streams"),
            ["Akka.Cluster"] = () => Load("Akka.Cluster.Serialization.ClusterSerializers, Akka.Cluster"),
            ["Akka.Cluster.Tools"] = () => Load("Akka.Cluster.Tools.ToolsSerializers, Akka.Cluster.Tools"),
            ["Akka.Cluster.Sharding"] = () => Load("Akka.Cluster.Sharding.Serialization.ShardingSerializers, Akka.Cluster.Sharding"),
            ["Akka.DistributedData"] = () => Load("Akka.DistributedData.Serialization.DistributedDataSerializers, Akka.DistributedData"),
            ["Akka.Cluster.Metrics"] = () => Load("Akka.Cluster.Metrics.Serialization.MetricsSerializers, Akka.Cluster.Metrics"),
            ["Akka.Persistence"] = () => Load("Akka.Persistence.Serialization.PersistenceSerializers, Akka.Persistence"),
        });

        private readonly Dictionary<string, Func<ModuleSerializers?>> _modules;
        private readonly ConcurrentDictionary<string, ModuleSerializers?> _loaded = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, Exception> _loadErrors = new(StringComparer.OrdinalIgnoreCase);

        internal ModuleSerializerTable(IDictionary<string, Func<ModuleSerializers?>> modules)
            => _modules = new Dictionary<string, Func<ModuleSerializers?>>(modules, StringComparer.OrdinalIgnoreCase);

        /// <summary>The assembly names of every module this table knows about, loaded or not.</summary>
        internal IEnumerable<string> AssemblyNames => _modules.Keys;

        /// <summary>The module shipped as <paramref name="assembly"/>; null when it is not a known module or fails to load.</summary>
        internal ModuleSerializers? ForAssembly(string assembly)
        {
            if (!_modules.TryGetValue(assembly, out var load))
                return null;

            // a race can load a module twice; the copies are equivalent and only one is kept
            return _loaded.GetOrAdd(assembly, _ => TryLoad(assembly, load));
        }

        /// <summary>
        /// The version-skew error that stopped <paramref name="assembly"/>'s table from loading, or null when it loaded
        /// or simply is not deployed - an absent module is not an error, a broken one is.
        /// </summary>
        internal Exception? LoadError(string assembly) => _loadErrors.TryGetValue(assembly, out var error) ? error : null;

        private ModuleSerializers? TryLoad(string assembly, Func<ModuleSerializers?> load)
        {
            try
            {
                return load();
            }
            catch (Exception e) when (IsVersionSkew(e))
            {
                // the module's table is missing from this build: the module counts as absent, and the caller
                // can ask LoadError why
                _loadErrors[assembly] = e;
                return null;
            }
        }

        internal static bool IsVersionSkew(Exception e)
        {
            while (e is TargetInvocationException or TypeInitializationException && e.InnerException is { } inner)
                e = inner;
            return e is TypeLoadException or MissingMemberException or FileNotFoundException or FileLoadException;
        }

        /// <summary>Loads a module's table. Pass a literal: the annotation lets the trimmer keep that type and its constructor.</summary>
        internal static ModuleSerializers? Load(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] string typeName)
        {
            var type = Type.GetType(typeName);
            return type is null ? null : (ModuleSerializers?)Activator.CreateInstance(type);
        }
    }
}
