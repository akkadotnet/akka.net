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
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Serialization
{
    /// <summary>
    /// INTERNAL API. A serializer type a module's reference.conf names, and its factory. The factory must be a plain
    /// <c>new X(system, config)</c> for a non-empty settings block and <c>new X(system)</c> otherwise - the constructor
    /// reflection would pick. Return a serializer; null skips the alias (a safety net, not a feature).
    /// </summary>
    internal sealed record ModuleSerializer(Type Type, Func<ExtendedActorSystem, Config, Serializer> Create);

    /// <summary>
    /// INTERNAL API. The serializer and bound types a first-party module's reference.conf names, so
    /// <see cref="Serialization"/> can resolve those rows without <see cref="Type.GetType(string)"/>.
    /// </summary>
    internal abstract class ModuleSerializers
    {
        public abstract IReadOnlyList<ModuleSerializer> Serializers { get; }

        public abstract IReadOnlyList<Type> BoundTypes { get; }
    }

    /// <summary>
    /// INTERNAL API. A loaded <see cref="ModuleSerializers"/>, indexed by stripped full name for strict matching.
    /// </summary>
    internal sealed class LoadedModule
    {
        private readonly Dictionary<string, (ModuleSerializer Entry, string? Assembly, bool IsAkka)> _serializers = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (Type Type, string? Assembly, bool IsAkka)> _boundTypes = new(StringComparer.Ordinal);

        internal LoadedModule(ModuleSerializers module)
        {
            foreach (var entry in module.Serializers)
                _serializers[KeyOf(entry.Type)] = (entry, entry.Type.Assembly.GetName().Name, IsAkka(entry.Type));
            foreach (var type in module.BoundTypes)
                _boundTypes[KeyOf(type)] = (type, type.Assembly.GetName().Name, IsAkka(type));
        }

        internal ModuleSerializer? FindSerializer(string name, string? assembly)
            => _serializers.TryGetValue(name, out var s) && Accepts(s.Assembly, s.IsAkka, assembly) ? s.Entry : null;

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
            // one entry per module, each passing its own literal to Load so the trimmer can see the type, e.g.
            // ["Akka.Remote"] = () => Load("Akka.Remote.Serialization.RemoteSerializers, Akka.Remote")
        });

        private readonly Dictionary<string, Func<ModuleSerializers?>> _modules;
        private readonly ConcurrentDictionary<string, LoadedModule?> _loaded = new(StringComparer.OrdinalIgnoreCase);

        internal ModuleSerializerTable(IDictionary<string, Func<ModuleSerializers?>> modules)
            => _modules = new Dictionary<string, Func<ModuleSerializers?>>(modules, StringComparer.OrdinalIgnoreCase);

        /// <summary>The module shipped as <paramref name="assembly"/>; null when it is not a known module or fails to load.</summary>
        internal LoadedModule? ForAssembly(string assembly)
        {
            if (!_modules.TryGetValue(assembly, out var load))
                return null;

            // a race can load a module twice; the copies are equivalent and only one is kept
            return _loaded.GetOrAdd(assembly, _ => TryLoad(load));
        }

        private static LoadedModule? TryLoad(Func<ModuleSerializers?> load)
        {
            try
            {
                // reads both lists here, so a member missing from either also lands in the catch
                return load() is { } module ? new LoadedModule(module) : null;
            }
            catch (Exception e) when (IsVersionSkew(e))
            {
                // the module's table, or something it references, is missing from this build: the module counts as absent
                return null;
            }
        }

        private static bool IsVersionSkew(Exception e)
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
