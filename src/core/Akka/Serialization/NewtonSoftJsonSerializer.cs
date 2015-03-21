using System;
using System.Collections.Generic;
using System.ComponentModel.Design.Serialization;
using System.Reflection;
using System.Text;
using Akka.Actor;
using Akka.Dispatch.SysMsg;
using Akka.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System.Globalization;

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
            var j = deserializedValue as JObject;
            if (j != null)
            {
                if (j["$"] != null)
                {
                    var value = j["$"].Value<string>();
                    return GetValue(value);
                }
            }
            var surrogate = deserializedValue as ISurrogate;
            if (surrogate != null)
            {
                return surrogate.FromSurrogate(system);
            }
            return deserializedValue;
        }

        private static object GetValue(string V)
        {
            var t = V.Substring(0, 1);
            var v = V.Substring(1);
            if (t == "I")
                return int.Parse(v, NumberFormatInfo.InvariantInfo);
            if (t == "F")
                return float.Parse(v, NumberFormatInfo.InvariantInfo);
            if (t == "M")
                return decimal.Parse(v, NumberFormatInfo.InvariantInfo);

            throw new NotSupportedException();
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
                if (objectType == typeof (int) || objectType == typeof (float) || objectType == typeof (decimal))
                    return true;

                if (typeof (ISurrogated).IsAssignableFrom(objectType))
                    return true;

                if (objectType == typeof (object))
                    return true;

                return false;
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
                return DeserializeFromReader(reader, serializer);
            }



            private object DeserializeFromReader(JsonReader reader, JsonSerializer serializer)
            {
                var surrogate = serializer.Deserialize(reader);
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
                if (value is int || value is decimal || value is float)
                {
                    writer.WriteStartObject();
                    writer.WritePropertyName("$");
                    writer.WriteValue(GetString(value));
                    writer.WriteEndObject();
                }
                else
                {
                    var value1 = value as ISurrogated;
                    if (value1 != null)
                    {
                        var surrogated = value1;
                        var surrogate = surrogated.ToSurrogate(_system);
                        serializer.Serialize(writer, surrogate);
                    }
                    else
                    {
                        serializer.Serialize(writer, value);
                    }
                }
            }

            private object GetString(object value)
            {
                if (value is int)
                    return "I" + ((int) value).ToString(NumberFormatInfo.InvariantInfo);
                if (value is float)
                    return "F" + ((float)value).ToString(NumberFormatInfo.InvariantInfo);
                if (value is decimal)
                    return "M" + ((decimal)value).ToString(NumberFormatInfo.InvariantInfo);
                throw new NotSupportedException();
            }
        }
    }
}