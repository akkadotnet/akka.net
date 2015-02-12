using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Akka.Actor;
using Akka.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Akka.Serialization
{
    /// <summary>
    ///     Class NewtonSoftJsonSerializer.
    /// </summary>
    public class NewtonSoftJsonSerializer : Serializer
    {
        private readonly JsonSerializerSettings _settings;     

        /// <summary>
        ///     Initializes a new instance of the <see cref="NewtonSoftJsonSerializer" /> class.
        /// </summary>
        /// <param name="system">The system.</param>
        public NewtonSoftJsonSerializer(ExtendedActorSystem system)
            : base(system)
        {
            _settings = new JsonSerializerSettings
            {
                PreserveReferencesHandling = PreserveReferencesHandling.Objects,
                Converters = new List<JsonConverter> {new SurrogateConverter(system)},
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Ignore,
                MissingMemberHandling = MissingMemberHandling.Ignore,
                ObjectCreationHandling = ObjectCreationHandling.Replace, //important: if reuse, the serializer will overwrite properties in default references, e.g. Props.DefaultDeploy or Props.noArgs
                ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
                TypeNameHandling = TypeNameHandling.All,
                ContractResolver = new AkkaContractResolver(),
            };
        }

        public class AkkaContractResolver : DefaultContractResolver
        {
            protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
            {
                var prop = base.CreateProperty(member, memberSerialization);

                if (!prop.Writable)
                {
                    var property = member as PropertyInfo;
                    if (property != null)
                    {
                        var hasPrivateSetter = property.GetSetMethod(true) != null;
                        prop.Writable = hasPrivateSetter;
                    }
                }

                return prop;
            }
        }

        /// <summary>
        ///     Gets the identifier.
        /// </summary>
        /// <value>The identifier.</value>
        /// Completely unique value to identify this implementation of Serializer, used to optimize network traffic
        /// Values from 0 to 16 is reserved for Akka internal usage
        public override int Identifier
        {
            get { return -3; }
        }

        /// <summary>
        ///     Gets a value indicating whether [include manifest].
        /// </summary>
        /// <value><c>true</c> if [include manifest]; otherwise, <c>false</c>.</value>
        /// Returns whether this serializer needs a manifest in the fromBinary method
        public override bool IncludeManifest
        {
            get { return false; }
        }

        /// <summary>
        ///     To the binary.
        /// </summary>
        /// <param name="obj">The object.</param>
        /// <returns>System.Byte[][].</returns>
        /// Serializes the given object into an Array of Byte
        public override byte[] ToBinary(object obj)
        {
            Serialization.CurrentSystem = system;
            string data = JsonConvert.SerializeObject(obj, Formatting.None, _settings);
            byte[] bytes = Encoding.Default.GetBytes(data);
            return bytes;
        }

        /// <summary>
        ///     Froms the binary.
        /// </summary>
        /// <param name="bytes">The bytes.</param>
        /// <param name="type">The type.</param>
        /// <returns>System.Object.</returns>
        /// Produces an object from an array of bytes, with an optional type;
        public override object FromBinary(byte[] bytes, Type type)
        {
            Serialization.CurrentSystem = system;
            string data = Encoding.Default.GetString(bytes);

            object res = JsonConvert.DeserializeObject(data, _settings);
            return TranslateSurrogate(res,system);
        }

        private static object TranslateSurrogate(object deserializedValue,ActorSystem system)
        {
            var surrogate = deserializedValue as ISurrogate;
            if (surrogate != null)
            {
                return surrogate.FromSurrogate(system);
            }
            return deserializedValue;
        }

        public class SurrogateConverter : JsonConverter
        {
            private readonly ActorSystem _system;
            public SurrogateConverter(ActorSystem system)
            {
                _system = system;
            }
            /// <summary>
            ///     Determines whether this instance can convert the specified object type.
            /// </summary>
            /// <param name="objectType">Type of the object.</param>
            /// <returns><c>true</c> if this instance can convert the specified object type; otherwise, <c>false</c>.</returns>
            public override bool CanConvert(Type objectType)
            {
                return (typeof(ISurrogated).IsAssignableFrom(objectType));
            }

            /// <summary>
            ///     Reads the JSON representation of the object.
            /// </summary>
            /// <param name="reader">The <see cref="T:Newtonsoft.Json.JsonReader" /> to read from.</param>
            /// <param name="objectType">Type of the object.</param>
            /// <param name="existingValue">The existing value of object being read.</param>
            /// <param name="serializer">The calling serializer.</param>
            /// <returns>The object value.</returns>
            public override object ReadJson(JsonReader reader, Type objectType, object existingValue,
                JsonSerializer serializer)
            {
                var surrogate = serializer.Deserialize<ISurrogate>(reader);
                return TranslateSurrogate(surrogate, _system);
            }

            /// <summary>
            ///     Writes the JSON representation of the object.
            /// </summary>
            /// <param name="writer">The <see cref="T:Newtonsoft.Json.JsonWriter" /> to write to.</param>
            /// <param name="value">The value.</param>
            /// <param name="serializer">The calling serializer.</param>
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                var surrogated = (ISurrogated)value;
                var surrogate = surrogated.ToSurrogate(_system);
                serializer.Serialize(writer, surrogate);
            }
        }
    }
}