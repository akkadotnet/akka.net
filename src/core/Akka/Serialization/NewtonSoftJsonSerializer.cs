//-----------------------------------------------------------------------
// <copyright file="NewtonSoftJsonSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Akka.Actor;
using Akka.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace Akka.Serialization
{
    /// <summary>
    /// A typed settings for a <see cref="NewtonSoftJsonSerializer"/> class.
    /// </summary>
    public sealed class NewtonSoftJsonSerializerSettings
    {
        /// <summary>
        /// A default instance of <see cref="NewtonSoftJsonSerializerSettings"/> used when no custom configuration has been provided.
        /// </summary>
        public static readonly NewtonSoftJsonSerializerSettings Default = new NewtonSoftJsonSerializerSettings(
            encodeTypeNames: true,
            preserveObjectReferences: true,
            converters: Enumerable.Empty<Type>());

        /// <summary>
        /// Creates a new instance of the <see cref="NewtonSoftJsonSerializerSettings"/> based on a provided <paramref name="config"/>.
        /// Config may define several key-values:
        /// <ul>
        /// <li>`encode-type-names` (boolean) mapped to <see cref="EncodeTypeNames"/></li>
        /// <li>`preserve-object-references` (boolean) mapped to <see cref="PreserveObjectReferences"/></li>
        /// <li>`converters` (type list) mapped to <see cref="Converters"/>. They must implement <see cref="JsonConverter"/> and define either default constructor or constructor taking <see cref="ExtendedActorSystem"/> as its only parameter.</li>
        /// </ul>
        /// </summary>
        /// <exception cref="ArgumentNullException">Raised when no <paramref name="config"/> was provided.</exception>
        /// <exception cref="ArgumentException">Raised when types defined in `converters` list didn't inherit <see cref="JsonConverter"/>.</exception>
        public static NewtonSoftJsonSerializerSettings Create(Config config)
        {
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<NewtonSoftJsonSerializerSettings>();

            return new NewtonSoftJsonSerializerSettings(
                encodeTypeNames: config.GetBoolean("encode-type-names", true),
                preserveObjectReferences: config.GetBoolean("preserve-object-references", true),
                converters: GetConverterTypes(config));
        }

        private static IEnumerable<Type> GetConverterTypes(Config config)
        {
            var converterNames = config.GetStringList("converters", new string[] { });

            if (converterNames != null)
                foreach (var converterName in converterNames)
                {
                    var type = Type.GetType(converterName, true);
                    if (!typeof(JsonConverter).IsAssignableFrom(type))
                        throw new ArgumentException($"Type {type} doesn't inherit from a {typeof(JsonConverter)}.");

                    yield return type;
                }
        }

        /// <summary>
        /// When true, serializer will encode a type names into serialized json $type field. This must be true 
        /// if <see cref="NewtonSoftJsonSerializer"/> is a default serializer in order to support polymorphic 
        /// deserialization.
        /// </summary>
        public bool EncodeTypeNames { get; }

        /// <summary>
        /// When true, serializer will track a reference dependencies in serialized object graph. This must be 
        /// true if <see cref="NewtonSoftJsonSerializer"/>.
        /// </summary>
        public bool PreserveObjectReferences { get; }

        /// <summary>
        /// A collection of an additional converter types to be applied to a <see cref="NewtonSoftJsonSerializer"/>.
        /// Converters must inherit from <see cref="JsonConverter"/> class and implement a default constructor.
        /// </summary>
        public IEnumerable<Type> Converters { get; }

        /// <summary>
        /// Creates a new instance of the <see cref="NewtonSoftJsonSerializerSettings"/>.
        /// </summary>
        /// <param name="encodeTypeNames">Determines if a special `$type` field should be emitted into serialized JSON. Must be true if corresponding serializer is used as default.</param>
        /// <param name="preserveObjectReferences">Determines if object references should be tracked within serialized object graph. Must be true if corresponding serialize is used as default.</param>
        /// <param name="converters">A list of types implementing a <see cref="JsonConverter"/> to support custom types serialization.</param>
        public NewtonSoftJsonSerializerSettings(bool encodeTypeNames, bool preserveObjectReferences, IEnumerable<Type> converters)
        {
            if (converters == null)
                throw new ArgumentNullException(nameof(converters), $"{nameof(NewtonSoftJsonSerializerSettings)} requires a sequence of converters.");

            EncodeTypeNames = encodeTypeNames;
            PreserveObjectReferences = preserveObjectReferences;
            Converters = converters;
        }
    }

    /// <summary>
    /// This is a special <see cref="Serializer"/> that serializes and deserializes javascript objects only.
    /// These objects need to be in the JavaScript Object Notation (JSON) format.
    /// </summary>
    public partial class NewtonSoftJsonSerializer : Serializer
    {
        private readonly ThreadLocal<JsonSerializer> _serializer;

        /// <summary>
        /// TBD
        /// </summary>
        public JsonSerializerSettings Settings { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public object Serializer => _serializer.Value;

        /// <summary>
        /// Initializes a new instance of the <see cref="NewtonSoftJsonSerializer" /> class.
        /// </summary>
        /// <param name="system">The actor system to associate with this serializer. </param>
        public NewtonSoftJsonSerializer(ExtendedActorSystem system)
            : this(system, NewtonSoftJsonSerializerSettings.Default)
        {
        }

        public NewtonSoftJsonSerializer(ExtendedActorSystem system, Config config)
            : this(system, NewtonSoftJsonSerializerSettings.Create(config))
        {
        }


        public NewtonSoftJsonSerializer(ExtendedActorSystem system, NewtonSoftJsonSerializerSettings settings)
            : base(system)
        {
            Settings = new JsonSerializerSettings
            {
                PreserveReferencesHandling = settings.PreserveObjectReferences
                    ? PreserveReferencesHandling.Objects
                    : PreserveReferencesHandling.None,
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Ignore,
                MissingMemberHandling = MissingMemberHandling.Ignore,
                ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
                TypeNameHandling = settings.EncodeTypeNames
                    ? TypeNameHandling.All
                    : TypeNameHandling.None,
            };

            if (system != null)
            {
                var settingsSetup = system.Settings.Setup.Get<NewtonSoftJsonSerializerSetup>()
                    .GetOrElse(NewtonSoftJsonSerializerSetup.Create(s => { }));

                settingsSetup.ApplySettings(Settings);
            }

            var converters = settings.Converters
                .Select(type => CreateConverter(type, system))
                .ToList();

            var primitiveNumberConverter = new PrimitiveNumberConverter();
            var surrogateConverter = new SurrogateConverter(system);
            converters.Add(new DelegatedObjectConverter(primitiveNumberConverter, surrogateConverter));
            converters.Add(primitiveNumberConverter);
            converters.Add(surrogateConverter);
            converters.Add(new SurrogatedConverter(system));
            converters.Add(new DiscriminatedUnionConverter());

            foreach (var converter in converters)
            {
                Settings.Converters.Add(converter);
            }

            Settings.ObjectCreationHandling = ObjectCreationHandling.Replace; //important: if reuse, the serializer will overwrite properties in default references, e.g. Props.DefaultDeploy or Props.noArgs
            Settings.ContractResolver = new AkkaContractResolver();

            _serializer = new ThreadLocal<JsonSerializer>(() =>
            {
                var serializer = JsonSerializer.CreateDefault(Settings);
                serializer.Formatting = Formatting.None;
                return serializer;
            });
        }

        private static JsonConverter CreateConverter(Type converterType, ExtendedActorSystem actorSystem)
        {
            var ctor = converterType.GetConstructors()
                .FirstOrDefault(c =>
                {
                    var parameters = c.GetParameters();
                    return parameters.Length == 1 && parameters[0].ParameterType == typeof(ExtendedActorSystem);
                });

            return ctor == null
                ? (JsonConverter)Activator.CreateInstance(converterType)
                : (JsonConverter)Activator.CreateInstance(converterType, actorSystem);
        }

        internal class AkkaContractResolver : DefaultContractResolver
        {
            protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
            {
                var prop = base.CreateProperty(member, memberSerialization);

                if (!prop.Writable && member is PropertyInfo property)
                {
                    var hasPrivateSetter = property.GetSetMethod(true) != null;
                    prop.Writable = hasPrivateSetter;
                }

                return prop;
            }
        }

        /// <summary>
        /// Returns whether this serializer needs a manifest in the fromBinary method
        /// </summary>
        public override bool IncludeManifest => false;

        /// <summary>
        /// Serializes the given object into a byte array
        /// </summary>
        /// <param name="obj">The object to serialize </param>
        /// <returns>A byte array containing the serialized object</returns>
        public override byte[] ToBinary(object obj)
        {
            var stringBuilder = new StringBuilder(256);
            var writer = new StringWriter(stringBuilder);
            using (var jsonWriter = new JsonTextWriter(writer))
            {
                jsonWriter.Formatting = _serializer.Value.Formatting;
                _serializer.Value.Serialize(jsonWriter, obj);
            }
            return Encoding.UTF8.GetBytes(stringBuilder.ToString());
        }

        /// <summary>
        /// Deserializes a byte array into an object of type <paramref name="type"/>.
        /// </summary>
        /// <param name="bytes">The array containing the serialized object</param>
        /// <param name="type">The type of object contained in the array</param>
        /// <returns>The object contained in the array</returns>
        public override object FromBinary(byte[] bytes, Type type)
        {
            var reader = new StringReader(Encoding.UTF8.GetString(bytes));
            using (var jsonReader = new JsonTextReader(reader))
            {
                return _serializer.Value.Deserialize(jsonReader, type);
            }
        }
    }
}
