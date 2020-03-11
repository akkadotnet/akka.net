//-----------------------------------------------------------------------
// <copyright file="HyperionSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
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
        /// <summary>
        /// Settings used for an underlying Hyperion serializer implementation.
        /// </summary>
        public readonly HyperionSerializerSettings Settings;

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
            this.Settings = settings;
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
                    knownTypes: provider.GetKnownTypes(),
                    ignoreISerializable:true));
        }

        /// <summary>
        /// Completely unique value to identify this implementation of Serializer, used to optimize network traffic
        /// </summary>
        public override int Identifier => -5;

        /// <summary>
        /// Returns whether this serializer needs a manifest in the fromBinary method
        /// </summary>
        public override bool IncludeManifest => false;

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
            try
            {
                using (var ms = new MemoryStream(bytes))
                {
                    var res = _serializer.Deserialize<object>(ms);
                    return res;
                }
            }
            catch (TypeLoadException e)
            {
                throw new SerializationException(e.Message, e);
            }
            catch(NotSupportedException e)
            {
                throw new SerializationException(e.Message, e);
            }
            catch (ArgumentException e)
            {
                throw new SerializationException(e.Message, e);
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

    /// <summary>
    /// A typed settings class for a <see cref="HyperionSerializer"/>.
    /// </summary>
    public sealed class HyperionSerializerSettings
    {
        /// <summary>
        /// Default settings used by <see cref="HyperionSerializer"/> when no config has been specified.
        /// </summary>
        public static readonly HyperionSerializerSettings Default = new HyperionSerializerSettings(
            preserveObjectReferences: true,
            versionTolerance: true,
            knownTypesProvider: typeof(NoKnownTypes));

        /// <summary>
        /// Creates a new instance of <see cref="HyperionSerializerSettings"/> using provided HOCON config.
        /// Config can contain several key-values, that are mapped to a class fields:
        /// <ul>
        /// <li>`preserve-object-references` (boolean) mapped to <see cref="PreserveObjectReferences"/></li>
        /// <li>`version-tolerance` (boolean) mapped to <see cref="VersionTolerance"/></li>
        /// <li>`known-types-provider` (fully qualified type name) mapped to <see cref="KnownTypesProvider"/></li>
        /// </ul>
        /// </summary>
        /// <exception cref="ArgumentNullException">Raised when <paramref name="config"/> was not provided.</exception>
        /// <exception cref="ArgumentException">Raised when `known-types-provider` type doesn't implement <see cref="IKnownTypesProvider"/> interface.</exception>
        /// <param name="config"></param>
        /// <returns></returns>
        public static HyperionSerializerSettings Create(Config config)
        {
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<HyperionSerializerSettings>("akka.serializers.hyperion");

            var typeName = config.GetString("known-types-provider", null);
            var type = !string.IsNullOrEmpty(typeName) ? Type.GetType(typeName, true) : null;

            return new HyperionSerializerSettings(
                preserveObjectReferences: config.GetBoolean("preserve-object-references", true),
                versionTolerance: config.GetBoolean("version-tolerance", true),
                knownTypesProvider: type);
        }

        /// <summary>
        /// When true, it tells <see cref="HyperionSerializer"/> to keep
        /// track of references in serialized/deserialized object graph.
        /// </summary>
        public readonly bool PreserveObjectReferences;

        /// <summary>
        /// When true, it tells <see cref="HyperionSerializer"/> to encode
        /// a list of currently serialized fields into type manifest.
        /// </summary>
        public readonly bool VersionTolerance;

        /// <summary>
        /// A type implementing <see cref="IKnownTypesProvider"/>, that will
        /// be used when <see cref="HyperionSerializer"/> is being constructed
        /// to provide a list of message types that are supposed to be known
        /// implicitly by all communicating parties. Implementing class must
        /// provide either a default constructor or a constructor taking
        /// <see cref="ExtendedActorSystem"/> as its only parameter.
        /// </summary>
        public readonly Type KnownTypesProvider;

        /// <summary>
        /// Creates a new instance of a <see cref="HyperionSerializerSettings"/>.
        /// </summary>
        /// <param name="preserveObjectReferences">Flag which determines if serializer should keep track of references in serialized object graph.</param>
        /// <param name="versionTolerance">Flag which determines if field data should be serialized as part of type manifest.</param>
        /// <param name="knownTypesProvider">Type implementing <see cref="IKnownTypesProvider"/> to be used to determine a list of types implicitly known by all cooperating serializer.</param>
        /// <exception cref="ArgumentException">Raised when `known-types-provider` type doesn't implement <see cref="IKnownTypesProvider"/> interface.</exception>
        public HyperionSerializerSettings(bool preserveObjectReferences, bool versionTolerance, Type knownTypesProvider)
        {
            knownTypesProvider = knownTypesProvider ?? typeof(NoKnownTypes);
            if (!typeof(IKnownTypesProvider).IsAssignableFrom(knownTypesProvider))
                throw new ArgumentException($"Known types provider must implement an interface {typeof(IKnownTypesProvider).FullName}");

            PreserveObjectReferences = preserveObjectReferences;
            VersionTolerance = versionTolerance;
            KnownTypesProvider = knownTypesProvider;
        }
    }
}
