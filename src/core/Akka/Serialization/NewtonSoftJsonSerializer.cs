using Akka.Actor;
using Akka.Actor.Internals;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Serialization
{
    /// <summary>
    ///     Class NewtonSoftJsonSerializer.
    /// </summary>
    public class NewtonSoftJsonSerializer : Serializer
    {
        /// <summary>
        ///     The json serializer settings
        /// </summary>
        private static readonly JsonSerializerSettings JsonSerializerSettings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.All
        };

        /// <summary>
        ///     Initializes a new instance of the <see cref="NewtonSoftJsonSerializer" /> class.
        /// </summary>
        /// <param name="system">The system.</param>
        public NewtonSoftJsonSerializer(ExtendedActorSystem system)
            : base(system)
        {
            //TODO: we should use an instanced serializer to be threadsafe for other ActorSystems
            JsonConvert.DefaultSettings = () => new JsonSerializerSettings
            {
                Converters = new List<JsonConverter> { new ActorRefConverter() }
            };
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
            string data = JsonConvert.SerializeObject(obj, Formatting.None, JsonSerializerSettings);
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

            return JsonConvert.DeserializeObject(data, JsonSerializerSettings);
        }

        /// <summary>
        ///     Class ActorRefConverter.
        /// </summary>
        public class ActorRefConverter : JsonConverter
        {
            /// <summary>
            ///     Determines whether this instance can convert the specified object type.
            /// </summary>
            /// <param name="objectType">Type of the object.</param>
            /// <returns><c>true</c> if this instance can convert the specified object type; otherwise, <c>false</c>.</returns>
            public override bool CanConvert(Type objectType)
            {
                return (typeof(ActorRef).IsAssignableFrom(objectType));
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
                var surrogate = serializer.Deserialize<ActorRefSurrogate>(reader);
                return ((ActorSystemImpl) Serialization.CurrentSystem).Provider.ResolveActorRef(surrogate.Path);
            }

            /// <summary>
            ///     Writes the JSON representation of the object.
            /// </summary>
            /// <param name="writer">The <see cref="T:Newtonsoft.Json.JsonWriter" /> to write to.</param>
            /// <param name="value">The value.</param>
            /// <param name="serializer">The calling serializer.</param>
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                var @ref = (ActorRef)value;
                var surrogate = new ActorRefSurrogate(Serialization.SerializedActorPath(@ref));
                serializer.Serialize(writer, surrogate);
            }
        }
    }
}
