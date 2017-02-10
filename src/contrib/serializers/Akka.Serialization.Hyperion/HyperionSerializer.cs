//-----------------------------------------------------------------------
// <copyright file="HyperionSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Util;
using Hyperion;

// ReSharper disable once CheckNamespace
namespace Akka.Serialization
{
    /// <summary>
    /// This is a special <see cref="Serializer"/> that serializes and deserializes plain old CLR objects (POCOs).
    /// </summary>
    public class HyperionSerializer : Serializer
    {
        private readonly Hyperion.Serializer _serializer;

        /// <summary>
        /// Initializes a new instance of the <see cref="HyperionSerializer"/> class.
        /// </summary>
        /// <param name="system">The actor system to associate with this serializer.</param>
        public HyperionSerializer(ExtendedActorSystem system) 
            : this(system, HyperionSerializerSettings.Default)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="HyperionSerializer"/> class.
        /// </summary>
        /// <param name="system">The actor system to associate with this serializer.</param>
        /// <param name="config">Configuration passed from related HOCON config path.</param>
        public HyperionSerializer(ExtendedActorSystem system, Config config) 
            : this(system, HyperionSerializerSettings.Create(config))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="HyperionSerializer"/> class.
        /// </summary>
        /// <param name="system">The actor system to associate with this serializer.</param>
        /// <param name="settings">Serializer settings.</param>
        public HyperionSerializer(ExtendedActorSystem system, HyperionSerializerSettings settings)
            : base(system)
        {
            var akkaSurrogate =
                Surrogate
                .Create<ISurrogated, ISurrogate>(
                from => from.ToSurrogate(system),
                to => to.FromSurrogate(system));

            var provider = CreateKnownTypesProvider(system, settings.KnownTypesProvider);

            _serializer =
                new Hyperion.Serializer(new SerializerOptions(
                    preserveObjectReferences: settings.PreserveObjectReferences,
                    versionTolerance: settings.VersionTolerance,
                    surrogates: new[] { akkaSurrogate },
                    knownTypes: provider.GetKnownTypes()));
        }

        /// <summary>
        /// Completely unique value to identify this implementation of Serializer, used to optimize network traffic
        /// </summary>
        public override int Identifier
        {
            get { return -5; }
        }

        /// <summary>
        /// Returns whether this serializer needs a manifest in the fromBinary method
        /// </summary>
        public override bool IncludeManifest
        {
            get { return false; }
        }

        /// <summary>
        /// Serializes the given object into a byte array
        /// </summary>
        /// <param name="obj">The object to serialize</param>
        /// <returns>A byte array containing the serialized object </returns>
        public override byte[] ToBinary(object obj)
        {
            using (var ms = new MemoryStream())
            {
                _serializer.Serialize(obj, ms);
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Deserializes a byte array into an object of type <paramref name="type" />.
        /// </summary>
        /// <param name="bytes">The array containing the serialized object</param>
        /// <param name="type">The type of object contained in the array</param>
        /// <returns>The object contained in the array</returns>
        public override object FromBinary(byte[] bytes, Type type)
        {
            using (var ms = new MemoryStream(bytes))
            {
                var res = _serializer.Deserialize<object>(ms);
                return res;
            }
        }

        private IKnownTypesProvider CreateKnownTypesProvider(ExtendedActorSystem system, Type type)
        {
            var ctors = type.GetConstructors();
            var ctor = ctors.FirstOrDefault(c =>
            {
                var parameters = c.GetParameters();
                return parameters.Length == 1 && (parameters[0].ParameterType == typeof(ActorSystem)
                    || parameters[0].ParameterType == typeof(ExtendedActorSystem));
            });

            return ctor == null
                ? (IKnownTypesProvider) Activator.CreateInstance(type)
                : (IKnownTypesProvider) ctor.Invoke(new object[] {system});
        }
    }

    public sealed class HyperionSerializerSettings
    {
        public static readonly HyperionSerializerSettings Default = new HyperionSerializerSettings(
            preserveObjectReferences: true,
            versionTolerance: true,
            knownTypesProvider: typeof(NoKnownTypes));

        public static HyperionSerializerSettings Create(Config config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config), "HyperionSerializerSettings require a config, default path: `akka.serializers.hyperion`");

            var type = Type.GetType(config.GetString("known-types-provider"), true);
            if (!typeof(IKnownTypesProvider).IsAssignableFrom(type)) 
                throw new ArgumentException($"Known types provider must implement an interface {typeof(IKnownTypesProvider).FullName}", nameof(config));

            return new HyperionSerializerSettings(
                preserveObjectReferences: config.GetBoolean("preserve-object-references", true),
                versionTolerance: config.GetBoolean("version-tolerance", true),
                knownTypesProvider: type);
        }

        public readonly bool PreserveObjectReferences;
        public readonly bool VersionTolerance;
        public readonly Type KnownTypesProvider;

        public HyperionSerializerSettings(bool preserveObjectReferences, bool versionTolerance, Type knownTypesProvider)
        {
            PreserveObjectReferences = preserveObjectReferences;
            VersionTolerance = versionTolerance;
            KnownTypesProvider = knownTypesProvider;
        }
    }
}
