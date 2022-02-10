//-----------------------------------------------------------------------
// <copyright file="HyperionSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Configuration;
using Akka.Serialization.Hyperion;
using Akka.Util;
using Hyperion;
using HySerializer = Hyperion.Serializer;

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

        private readonly HySerializer _serializer;

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
            Settings = settings;
            var surrogates = settings.Surrogates.ToList();
            surrogates.Add(Surrogate
                .Create<ISurrogated, ISurrogate>(
                    from => from.ToSurrogate(system),
                    to => to.FromSurrogate(system)));
            
            var provider = CreateKnownTypesProvider(system, settings.KnownTypesProvider);

            if (system != null)
            {
                var settingsSetup = system.Settings.Setup.Get<HyperionSerializerSetup>()
                    .GetOrElse(HyperionSerializerSetup.Empty);

                settingsSetup.ApplySettings(Settings);
            }

            _serializer =
                new HySerializer(new SerializerOptions(
                    versionTolerance: settings.VersionTolerance,
                    preserveObjectReferences: settings.PreserveObjectReferences,
                    surrogates: surrogates,
                    serializerFactories: null,
                    knownTypes: provider.GetKnownTypes(),
                    ignoreISerializable:true,
                    packageNameOverrides: settings.PackageNameOverrides,
                    disallowUnsafeTypes: settings.DisallowUnsafeType,
                    typeFilter: settings.TypeFilter));
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
            catch (Exception ex)
            {
                throw new SerializationException($"Failed to deserialize instance of type {type}. {ex.Message}", ex);
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
            knownTypesProvider: typeof(NoKnownTypes), 
            packageNameOverrides: new List<Func<string, string>>(),
            surrogates: new Surrogate[0],
            disallowUnsafeType: true,
            typeFilter: DisabledTypeFilter.Instance);

        /// <summary>
        /// Creates a new instance of <see cref="HyperionSerializerSettings"/> using provided HOCON config.
        /// Config can contain several key-values, that are mapped to a class fields:
        /// <ul>
        /// <li>`preserve-object-references` (boolean) mapped to <see cref="PreserveObjectReferences"/></li>
        /// <li>`version-tolerance` (boolean) mapped to <see cref="VersionTolerance"/></li>
        /// <li>`known-types-provider` (fully qualified type name) mapped to <see cref="KnownTypesProvider"/></li>
        /// </ul>
        /// </summary>
        /// <exception>ArgumentNullException: Raised when <paramref name="config"/> was not provided.</exception>
        /// <exception>ArgumentException: Raised when `known-types-provider` type doesn't implement <see cref="IKnownTypesProvider"/> interface.</exception>
        /// <param name="config"></param>
        /// <returns></returns>
        public static HyperionSerializerSettings Create(Config config)
        {
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<HyperionSerializerSettings>("akka.actor.serialization-settings.hyperion");

            var typeName = config.GetString("known-types-provider", null);
            var type = !string.IsNullOrWhiteSpace(typeName) ? Type.GetType(typeName, true) : null;

            var framework = RuntimeInformation.FrameworkDescription;
            string frameworkKey;
            if (framework.Contains(".NET Framework"))
                frameworkKey = "netfx";
            else if (framework.Contains(".NET Core"))
                frameworkKey = "netcore";
            else
                frameworkKey = "net";

            var packageNameOverrides = new List<Func<string, string>>();
            var overrideConfigs = config.GetValue($"cross-platform-package-name-overrides.{frameworkKey}");
            if (overrideConfigs != null)
            {
                var configs = overrideConfigs.GetArray().Select(value => value.GetObject());
                foreach (var obj in configs)
                {
                    var fingerprint = obj.GetKey("fingerprint").GetString();
                    var renameFrom = obj.GetKey("rename-from").GetString();
                    var renameTo = obj.GetKey("rename-to").GetString();
                    packageNameOverrides.Add(packageName => 
                        packageName.Contains(fingerprint) 
                            ? packageName.Replace(renameFrom, renameTo) 
                            : packageName);
                }
            }

            var surrogates = new List<Surrogate>();
            var surrogateList = config.GetStringList("surrogates");
            foreach (var surrogateClass in surrogateList)
            {
                var surrogateType = Type.GetType(surrogateClass);
                if (surrogateType == null)
                {
                    throw new ConfigurationException($"The surrogate type name did not resolve to an actual Type: '{surrogateClass}'");
                }

                surrogates.Add((Surrogate)Activator.CreateInstance(surrogateType));
            }

            var typeFilter = (ITypeFilter) DisabledTypeFilter.Instance;
            var allowedList = config.GetStringList("allowed-types");
            if (allowedList.Count > 0)
            {
                var filterBuilder = TypeFilterBuilder.Create();
                foreach (var allowedFqcn in allowedList)
                {
                    var allowedType = Type.GetType(allowedFqcn);
                    if (allowedType is null)
                        throw new ConfigurationException($"Could not load type [{allowedFqcn}] from allowed-type.");
                    filterBuilder.Include(allowedType);
                }

                typeFilter = filterBuilder.Build();
            }

            return new HyperionSerializerSettings(
                preserveObjectReferences: config.GetBoolean("preserve-object-references", true),
                versionTolerance: config.GetBoolean("version-tolerance", true),
                knownTypesProvider: type,
                packageNameOverrides: packageNameOverrides,
                surrogates: surrogates,
                disallowUnsafeType: config.GetBoolean("disallow-unsafe-type", true),
                typeFilter: typeFilter);
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
        /// A list of lambda functions, used to transform incoming deserialized
        /// package names before they are instantiated
        /// </summary>
        public readonly IEnumerable<Func<string, string>> PackageNameOverrides;

        /// <summary>
        /// A list of serialization surrogates for types that can not be serialized by
        /// Hyperion directly.
        /// </summary>
        public readonly IEnumerable<Surrogate> Surrogates;
        
        /// <summary>
        /// If set, will cause the Hyperion serializer to block potentially dangerous and unsafe types
        /// from being deserialized during run-time
        /// </summary>
        public readonly bool DisallowUnsafeType;

        /// <summary>
        /// If set, Hyperion serializer will use the to filter the types that are
        /// being deserialized during run-time
        /// </summary>
        public readonly ITypeFilter TypeFilter;

        /// <summary>
        /// Creates a new instance of a <see cref="HyperionSerializerSettings"/>.
        /// </summary>
        /// <param name="preserveObjectReferences">Flag which determines if serializer should keep track of references in serialized object graph.</param>
        /// <param name="versionTolerance">Flag which determines if field data should be serialized as part of type manifest.</param>
        /// <param name="knownTypesProvider">Type implementing <see cref="IKnownTypesProvider"/> to be used to determine a list of types implicitly known by all cooperating serializer.</param>
        /// <exception>Raised when `known-types-provider` type doesn't implement <see cref="IKnownTypesProvider"/> interface.</exception>
        [Obsolete]
        public HyperionSerializerSettings(bool preserveObjectReferences, bool versionTolerance, Type knownTypesProvider)
            : this(preserveObjectReferences, versionTolerance, knownTypesProvider, new List<Func<string, string>>(), new Surrogate[0], true, DisabledTypeFilter.Instance)
        { }

        /// <summary>
        /// Creates a new instance of a <see cref="HyperionSerializerSettings"/>.
        /// </summary>
        /// <param name="preserveObjectReferences">Flag which determines if serializer should keep track of references in serialized object graph.</param>
        /// <param name="versionTolerance">Flag which determines if field data should be serialized as part of type manifest.</param>
        /// <param name="knownTypesProvider">Type implementing <see cref="IKnownTypesProvider"/> to be used to determine a list of types implicitly known by all cooperating serializer.</param>
        /// <param name="packageNameOverrides">An array of package name overrides for cross platform compatibility</param>
        /// <exception>Raised when `known-types-provider` type doesn't implement <see cref="IKnownTypesProvider"/> interface.</exception>
        [Obsolete]
        public HyperionSerializerSettings(
            bool preserveObjectReferences, 
            bool versionTolerance, 
            Type knownTypesProvider, 
            IEnumerable<Func<string, string>> packageNameOverrides)
            : this(preserveObjectReferences, versionTolerance, knownTypesProvider, packageNameOverrides, new Surrogate[0], true, DisabledTypeFilter.Instance)
        { }

        /// <summary>
        /// Creates a new instance of a <see cref="HyperionSerializerSettings"/>.
        /// </summary>
        /// <param name="preserveObjectReferences">Flag which determines if serializer should keep track of references in serialized object graph.</param>
        /// <param name="versionTolerance">Flag which determines if field data should be serialized as part of type manifest.</param>
        /// <param name="knownTypesProvider">Type implementing <see cref="IKnownTypesProvider"/> to be used to determine a list of types implicitly known by all cooperating serializer.</param>
        /// <param name="packageNameOverrides">An array of package name overrides for cross platform compatibility</param>
        /// <param name="surrogates">A list of Surrogate instances that are used to de/serialize complex objects into a much simpler serialized objects.</param>
        /// <exception>ArgumentException: Raised when `known-types-provider` type doesn't implement <see cref="IKnownTypesProvider"/> interface.</exception>
        public HyperionSerializerSettings(
            bool preserveObjectReferences, 
            bool versionTolerance, 
            Type knownTypesProvider, 
            IEnumerable<Func<string, string>> packageNameOverrides,
            IEnumerable<Surrogate> surrogates)
            : this(preserveObjectReferences, versionTolerance, knownTypesProvider, packageNameOverrides, surrogates, true, DisabledTypeFilter.Instance)
        { }

        /// <summary>
        /// Creates a new instance of a <see cref="HyperionSerializerSettings"/>.
        /// </summary>
        /// <param name="preserveObjectReferences">Flag which determines if serializer should keep track of references in serialized object graph.</param>
        /// <param name="versionTolerance">Flag which determines if field data should be serialized as part of type manifest.</param>
        /// <param name="knownTypesProvider">Type implementing <see cref="IKnownTypesProvider"/> to be used to determine a list of types implicitly known by all cooperating serializer.</param>
        /// <param name="packageNameOverrides">An array of package name overrides for cross platform compatibility</param>
        /// <param name="surrogates">A list of Surrogate instances that are used to de/serialize complex objects into a much simpler serialized objects.</param>
        /// <param name="disallowUnsafeType">Block unsafe types from being deserialized.</param>
        /// <exception>ArgumentException: Raised when `known-types-provider` type doesn't implement <see cref="IKnownTypesProvider"/> interface.</exception>
        public HyperionSerializerSettings(
            bool preserveObjectReferences,
            bool versionTolerance,
            Type knownTypesProvider,
            IEnumerable<Func<string, string>> packageNameOverrides,
            IEnumerable<Surrogate> surrogates,
            bool disallowUnsafeType)
            : this(preserveObjectReferences, versionTolerance, knownTypesProvider, packageNameOverrides, surrogates, disallowUnsafeType, DisabledTypeFilter.Instance)
        { }

        /// <summary>
        /// Creates a new instance of a <see cref="HyperionSerializerSettings"/>.
        /// </summary>
        /// <param name="preserveObjectReferences">Flag which determines if serializer should keep track of references in serialized object graph.</param>
        /// <param name="versionTolerance">Flag which determines if field data should be serialized as part of type manifest.</param>
        /// <param name="knownTypesProvider">Type implementing <see cref="IKnownTypesProvider"/> to be used to determine a list of types implicitly known by all cooperating serializer.</param>
        /// <param name="packageNameOverrides">An array of package name overrides for cross platform compatibility</param>
        /// <param name="surrogates">A list of Surrogate instances that are used to de/serialize complex objects into a much simpler serialized objects.</param>
        /// <param name="disallowUnsafeType">Block unsafe types from being deserialized.</param>
        /// <param name="typeFilter">A ITypeFilter instance that will filter types from being deserialized.</param>
        /// <exception>ArgumentException: Raised when `known-types-provider` type doesn't implement <see cref="IKnownTypesProvider"/> interface.</exception>
        public HyperionSerializerSettings(
            bool preserveObjectReferences, 
            bool versionTolerance, 
            Type knownTypesProvider, 
            IEnumerable<Func<string, string>> packageNameOverrides,
            IEnumerable<Surrogate> surrogates, 
            bool disallowUnsafeType,
            ITypeFilter typeFilter)
        {
            knownTypesProvider = knownTypesProvider ?? typeof(NoKnownTypes);
            if (!typeof(IKnownTypesProvider).IsAssignableFrom(knownTypesProvider))
                throw new ArgumentException($"Known types provider must implement an interface {typeof(IKnownTypesProvider).FullName}");

            PreserveObjectReferences = preserveObjectReferences;
            VersionTolerance = versionTolerance;
            KnownTypesProvider = knownTypesProvider;
            PackageNameOverrides = packageNameOverrides;
            Surrogates = surrogates;
            DisallowUnsafeType = disallowUnsafeType;
            TypeFilter = typeFilter;
        }
    }
}
