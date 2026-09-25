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
    /// INTERNAL API. A serializer type a module's reference.conf names, and a factory that calls the constructor
    /// reflection would pick: <c>(system, config)</c> for a non-empty settings block, <c>(system)</c> otherwise.
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
    /// INTERNAL API. Finds a module's <see cref="ModuleSerializers"/> by assembly simple name, loading it lazily and
    /// at most once per table.
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
            catch (Exception e) when ((e is TargetInvocationException or TypeInitializationException ? e.InnerException : e)
                is TypeLoadException or MissingMemberException or FileNotFoundException or FileLoadException)
            {
                // version skew: the module's table, or something it references, is missing, so the module counts as absent
                return null;
            }
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
