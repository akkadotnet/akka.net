//-----------------------------------------------------------------------
// <copyright file="NewtonSoftJsonSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Akka.Actor;
using Akka.Configuration;
using Akka.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Akka.Serialization
{
    /// <inheritdoc />
    /// <summary>
    /// This is a special <see cref="P:Akka.Serialization.CompatJsonSerializer.Serializer" /> that serializes and deserializes javascript objects only.
    /// These objects need to be in the JavaScript Object Notation (JSON) format.
    /// </summary>
    public class CompatJsonSerializer : Serializer
    {
        private static readonly string CoreAssemblyName = typeof(object).GetTypeInfo().Assembly.GetName().Name;

        private readonly JsonSerializer _serializer;

        /// <summary>
        /// TBD
        /// </summary>
        public JsonSerializerSettings Settings { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public object Serializer => _serializer;

        /// <summary>
        /// Initializes a new instance of the <see cref="NewtonSoftJsonSerializer" /> class.
        /// </summary>
        /// <param name="system">The actor system to associate with this serializer. </param>
        public CompatJsonSerializer(ExtendedActorSystem system)
            : this(system, NewtonSoftJsonSerializerSettings.Default)
        {
        }

        public CompatJsonSerializer(ExtendedActorSystem system, Config config)
            : this(system, NewtonSoftJsonSerializerSettings.Create(config))
        {
        }


        public CompatJsonSerializer(ExtendedActorSystem system, NewtonSoftJsonSerializerSettings settings)
            : base(system)
        {
            var converters = settings.Converters
                .Select(type => CreateConverter(type, system))
                .ToList();

            converters.Add(new CompatSurrogateConverter(this));
            converters.Add(new DiscriminatedUnionConverter());

            Settings = new JsonSerializerSettings
            {
                PreserveReferencesHandling = settings.PreserveObjectReferences
                    ? PreserveReferencesHandling.Objects
                    : PreserveReferencesHandling.None,
                Converters = converters,
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Ignore,
                MissingMemberHandling = MissingMemberHandling.Ignore,
                ObjectCreationHandling = ObjectCreationHandling.Replace, //important: if reuse, the serializer will overwrite properties in default references, e.g. Props.DefaultDeploy or Props.noArgs
                ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
                TypeNameHandling = settings.EncodeTypeNames
                    ? TypeNameHandling.All
                    : TypeNameHandling.None,
                ContractResolver = new AkkaContractResolver()
            };

            _serializer = JsonSerializer.Create(Settings);
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

        /// <inheritdoc />
        /// <summary>
        /// Returns whether this serializer needs a manifest in the fromBinary method
        /// </summary>
        public override bool IncludeManifest => false;

        /// <inheritdoc />
        /// <summary>
        /// Serializes the given object into a byte array
        /// </summary>
        /// <param name="obj">The object to serialize </param>
        /// <returns>A byte array containing the serialized object</returns>
        public override byte[] ToBinary(object obj)
        {
            string data = JsonConvert.SerializeObject(obj, Formatting.None, Settings);
#if CORECLR
            data = data.Replace("System.Private.CoreLib", "%CORE%");
#else
            data = data.Replace("mscorlib", "%CORE%");
#endif
            byte[] bytes = Encoding.UTF8.GetBytes(data);
            return bytes;
        }

        /// <inheritdoc />
        /// <summary>
        /// Deserializes a byte array into an object of type <paramref name="type" />.
        /// </summary>
        /// <param name="bytes">The array containing the serialized object</param>
        /// <param name="type">The type of object contained in the array</param>
        /// <returns>The object contained in the array</returns>
        public override object FromBinary(byte[] bytes, Type type)
        {
            string data = Encoding.UTF8.GetString(bytes);
            data = data.Replace("%CORE%", CoreAssemblyName);
            object res = JsonConvert.DeserializeObject(data, Settings);
            return TranslateSurrogate(res, this, type);
        }

        private static object TranslateSurrogate(object deserializedValue, CompatJsonSerializer parent, Type type)
        {
            var j = deserializedValue as JObject;
            if (j != null)
            {
                //The JObject represents a special akka.net wrapper for primitives (int,float,decimal) to preserve correct type when deserializing
                if (j["$"] != null)
                {
                    var value = j["$"].Value<string>();
                    return GetValue(value);
                }

                //The JObject is not of our concern, let Json.NET deserialize it.
                return j.ToObject(type, parent._serializer);
            }
            var surrogate = deserializedValue as ISurrogate;

            //The deserialized object is a surrogate, unwrap it
            if (surrogate != null)
            {
                return surrogate.FromSurrogate(parent.system);
            }
            return deserializedValue;
        }

        private static object GetValue(string value)
        {
            var t = value.Substring(0, 1);
            var v = value.Substring(1);
            if (t == "I")
                return int.Parse(v, NumberFormatInfo.InvariantInfo);
            if (t == "F")
                return float.Parse(v, NumberFormatInfo.InvariantInfo);
            if (t == "M")
                return decimal.Parse(v, NumberFormatInfo.InvariantInfo);

            throw new NotSupportedException();
        }

        /// <inheritdoc />
        /// <summary>
        /// TBD
        /// </summary>
        internal class CompatSurrogateConverter : JsonConverter
        {
            private readonly CompatJsonSerializer _parent;
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="parent">TBD</param>
            public CompatSurrogateConverter(CompatJsonSerializer parent)
            {
                _parent = parent;
            }
            /// <inheritdoc />
            /// <summary>
            ///     Determines whether this instance can convert the specified object type.
            /// </summary>
            /// <param name="objectType">Type of the object.</param>
            /// <returns><c>true</c> if this instance can convert the specified object type; otherwise, <c>false</c>.</returns>
            public override bool CanConvert(Type objectType)
            {
                if (objectType == typeof(int) || objectType == typeof(float) || objectType == typeof(decimal))
                    return true;

                if (typeof(ISurrogated).IsAssignableFrom(objectType))
                    return true;

                if (objectType == typeof(object))
                    return true;

                return false;
            }

            /// <inheritdoc />
            /// <summary>
            /// Reads the JSON representation of the object.
            /// </summary>
            /// <param name="reader">The <see cref="T:Newtonsoft.Json.JsonReader" /> to read from.</param>
            /// <param name="objectType">Type of the object.</param>
            /// <param name="existingValue">The existing value of object being read.</param>
            /// <param name="serializer">The calling serializer.</param>
            /// <returns>The object value.</returns>
            public override object ReadJson(JsonReader reader, Type objectType, object existingValue,
                JsonSerializer serializer)
            {
                return DeserializeFromReader(reader, serializer, objectType);
            }

            private object DeserializeFromReader(JsonReader reader, JsonSerializer serializer, Type objectType)
            {
                var surrogate = serializer.Deserialize(reader);
                return TranslateSurrogate(surrogate, _parent, objectType);
            }

            /// <inheritdoc />
            /// <summary>
            /// Writes the JSON representation of the object.
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
                        var surrogate = surrogated.ToSurrogate(_parent.system);
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
                    return "I" + ((int)value).ToString(NumberFormatInfo.InvariantInfo);
                if (value is float)
                    return "F" + ((float)value).ToString(NumberFormatInfo.InvariantInfo);
                if (value is decimal)
                    return "M" + ((decimal)value).ToString(NumberFormatInfo.InvariantInfo);
                throw new NotSupportedException();
            }
        }
    }
}
