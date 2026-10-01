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
    /// INTERNAL API. The serializer rows a first-party module's reference.conf names, so <see cref="Serialization"/>
    /// can resolve those rows without <see cref="Type.GetType(string)"/>. Same shape as
    /// <see cref="SerializationSetup.CreateSerializers"/> - a module's table is, in effect, a built-in
    /// <see cref="SerializationSetup"/>.
    /// </summary>
    internal abstract class ModuleSerializers
    {
        public abstract ImmutableHashSet<SerializerDetails> Create(ExtendedActorSystem system);
    }

    /// <summary>
    /// INTERNAL API. A module's serializers, built and indexed by stripped full name for strict matching.
    /// </summary>
    internal sealed class LoadedModule
    {
        private readonly Dictionary<string, (SerializerDetails Details, string? Assembly, bool IsAkka)> _serializers = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (Type Type, string? Assembly, bool IsAkka)> _boundTypes = new(StringComparer.Ordinal);

        private LoadedModule(ImmutableHashSet<SerializerDetails> details)
        {
            foreach (var entry in details)
            {
                var serializerType = entry.Serializer.GetType();
                _serializers[KeyOf(serializerType)] = (entry, serializerType.Assembly.GetName().Name, IsAkka(serializerType));
                foreach (var type in entry.UseFor)
                    _boundTypes[KeyOf(type)] = (type, type.Assembly.GetName().Name, IsAkka(type));
            }
        }

        /// <summary>Builds <paramref name="module"/>'s serializers against <paramref name="system"/>; null when that fails with a version-skew error.</summary>
        internal static LoadedModule? TryCreate(ModuleSerializers module, ExtendedActorSystem system)
        {
            try
            {
                return new LoadedModule(module.Create(system));
            }
            catch (Exception e) when (ModuleSerializerTable.IsVersionSkew(e))
            {
                // the module, or something its serializers reference, is missing from this build: it counts as absent
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
    /// INTERNAL API. Resolves a <see cref="Serialization"/> instance's rows against a module's serializers, calling
    /// <see cref="ModuleSerializers.Create"/> at most once per module and caching the result for this instance's
    /// lifetime. <see cref="ModuleSerializerTable"/> caches the cheap part - the loaded <see cref="ModuleSerializers"/>
    /// object itself - once per process; building its serializers needs the <see cref="ExtendedActorSystem"/>, which
    /// only this, a per-instance cache, has.
    /// </summary>
    internal sealed class ModuleResolver
    {
        private readonly ExtendedActorSystem _system;
        private readonly ModuleSerializerTable _modules;
        private readonly Dictionary<string, LoadedModule?> _built = new(StringComparer.OrdinalIgnoreCase);

        // modules whose serializer rows this config contains; see FindBoundType for how a binding row uses them
        private readonly List<LoadedModule> _loaded = new();

        internal ModuleResolver(ExtendedActorSystem system, ModuleSerializerTable modules)
        {
            _system = system;
            _modules = modules;
        }

        /// <summary>The module <paramref name="typeName"/>'s assembly half names, if any row of it resolves there.</summary>
        internal SerializerDetails? FindSerializer(string typeName)
        {
            if (!Akka.Util.TypeExtensions.TrySplitTypeName(typeName, out var name, out var assembly) || assembly is null)
                return null;

            var module = ModuleFor(assembly);
            if (module is null)
                return null;

            var details = module.FindSerializer(name, assembly);
            if (details is not null && !_loaded.Contains(module))
                _loaded.Add(module);
            return details;
        }

        /// <summary>
        /// Resolves a binding row's type from the module tables, without reflection. Null when no module lists it.
        /// </summary>
        /// <remarks>
        /// Two lookups, in order:
        /// <list type="number">
        /// <item>The module the row's assembly half names. <c>"Akka.Remote.RemoteWatcher+Heartbeat, Akka.Remote"</c>
        /// is answered by Akka.Remote's table, building it if no serializer row has yet.</item>
        /// <item>Every module whose serializer rows this config resolved (<see cref="_loaded"/>). This covers
        /// bound types that live outside their module: Remote.conf binds <c>"System.String"</c> and
        /// <c>"Akka.Actor.Identify, Akka"</c> to its own serializers, and no module owns CoreLib or Akka.dll, so
        /// lookup 1 can't answer for them.</item>
        /// </list>
        /// Lookup 2 only asks modules this config uses. With no Remote serializer rows, a <c>"System.String"</c>
        /// binding gets no answer here and still throws with dynamic type loading off, as it did before. The binding
        /// loop runs after the serializer loop, so lookup 2's modules are complete by then. Two modules that list
        /// the same name list the same <see cref="Type"/>, so the order they are asked in doesn't matter.
        /// </remarks>
        internal Type? FindBoundType(string name, string? assembly)
        {
            if (assembly is not null && ModuleFor(assembly)?.FindBoundType(name, assembly) is { } owned)
                return owned;

            foreach (var module in _loaded)
            {
                if (module.FindBoundType(name, assembly) is { } type)
                    return type;
            }

            return null;
        }

        private LoadedModule? ModuleFor(string assembly)
        {
            if (_built.TryGetValue(assembly, out var cached))
                return cached;

            var built = _modules.ForAssembly(assembly) is { } raw ? LoadedModule.TryCreate(raw, _system) : null;
            _built[assembly] = built;
            return built;
        }
    }

    /// <summary>
    /// INTERNAL API. Finds a module's table by assembly simple name, loading it lazily and at most once per process.
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

        internal ModuleSerializerTable(IDictionary<string, Func<ModuleSerializers?>> modules)
            => _modules = new Dictionary<string, Func<ModuleSerializers?>>(modules, StringComparer.OrdinalIgnoreCase);

        /// <summary>The module shipped as <paramref name="assembly"/>; null when it is not a known module or fails to load.</summary>
        internal ModuleSerializers? ForAssembly(string assembly)
        {
            if (!_modules.TryGetValue(assembly, out var load))
                return null;

            // a race can load a module twice; the copies are equivalent and only one is kept
            return _loaded.GetOrAdd(assembly, _ => TryLoad(load));
        }

        private static ModuleSerializers? TryLoad(Func<ModuleSerializers?> load)
        {
            try
            {
                return load();
            }
            catch (Exception e) when (IsVersionSkew(e))
            {
                // the module's table is missing from this build: the module counts as absent
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
