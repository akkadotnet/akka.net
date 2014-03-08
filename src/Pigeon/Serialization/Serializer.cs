using System;
using System.Collections.Generic;
using System.Text;
using Akka.Actor;
using Newtonsoft.Json;

namespace Akka.Serialization
{
    /**
     * A Serializer represents a bimap between an object and an array of bytes representing that object.
     *
     * Serializers are loaded using reflection during [[akka.actor.ActorSystem]]
     * start-up, where two constructors are tried in order:
     *
     * <ul>
     * <li>taking exactly one argument of type [[akka.actor.ExtendedActorSystem]];
     * this should be the preferred one because all reflective loading of classes
     * during deserialization should use ExtendedActorSystem.dynamicAccess (see
     * [[akka.actor.DynamicAccess]]), and</li>
     * <li>without arguments, which is only an option if the serializer does not
     * load classes using reflection.</li>
     * </ul>
     *
     * <b>Be sure to always use the PropertyManager for loading classes!</b> This is necessary to
     * avoid strange match errors and inequalities which arise from different class loaders loading
     * the same class.
     */

    /// <summary>
    ///     Class Serializer.
    /// </summary>
    public abstract class Serializer
    {
        /// <summary>
        ///     The system
        /// </summary>
        protected readonly ActorSystem system;

        /// <summary>
        ///     Initializes a new instance of the <see cref="Serializer" /> class.
        /// </summary>
        /// <param name="system">The system.</param>
        public Serializer(ActorSystem system)
        {
            this.system = system;
        }

        /**
         * Completely unique value to identify this implementation of Serializer, used to optimize network traffic
         * Values from 0 to 16 is reserved for Akka internal usage
         */

        /// <summary>
        ///     Gets the identifier.
        /// </summary>
        /// <value>The identifier.</value>
        public abstract int Identifier { get; }

        /**
         * Returns whether this serializer needs a manifest in the fromBinary method
         */

        /// <summary>
        ///     Gets a value indicating whether [include manifest].
        /// </summary>
        /// <value><c>true</c> if [include manifest]; otherwise, <c>false</c>.</value>
        public abstract bool IncludeManifest { get; }

        /**
         * Serializes the given object into an Array of Byte
         */

        /// <summary>
        ///     To the binary.
        /// </summary>
        /// <param name="obj">The object.</param>
        /// <returns>System.Byte[][].</returns>
        public abstract byte[] ToBinary(object obj);

        /**
         * Produces an object from an array of bytes, with an optional type;
         */

        /// <summary>
        ///     Froms the binary.
        /// </summary>
        /// <param name="bytes">The bytes.</param>
        /// <param name="type">The type.</param>
        /// <returns>System.Object.</returns>
        public abstract object FromBinary(byte[] bytes, Type type);
    }

    /// <summary>
    ///     Class JavaSerializer.
    /// </summary>
    public class JavaSerializer : Serializer
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="JavaSerializer" /> class.
        /// </summary>
        /// <param name="system">The system.</param>
        public JavaSerializer(ActorSystem system) : base(system)
        {
        }

        /// <summary>
        ///     Gets the identifier.
        /// </summary>
        /// <value>The identifier.</value>
        /// Completely unique value to identify this implementation of Serializer, used to optimize network traffic
        /// Values from 0 to 16 is reserved for Akka internal usage
        public override int Identifier
        {
            get { return 1; }
        }

        /// <summary>
        ///     Gets a value indicating whether [include manifest].
        /// </summary>
        /// <value><c>true</c> if [include manifest]; otherwise, <c>false</c>.</value>
        /// <exception cref="System.NotSupportedException"></exception>
        /// Returns whether this serializer needs a manifest in the fromBinary method
        public override bool IncludeManifest
        {
            get { throw new NotSupportedException(); }
        }

        /// <summary>
        ///     To the binary.
        /// </summary>
        /// <param name="obj">The object.</param>
        /// <returns>System.Byte[][].</returns>
        /// <exception cref="System.NotSupportedException"></exception>
        /// Serializes the given object into an Array of Byte
        public override byte[] ToBinary(object obj)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        ///     Froms the binary.
        /// </summary>
        /// <param name="bytes">The bytes.</param>
        /// <param name="type">The type.</param>
        /// <returns>System.Object.</returns>
        /// <exception cref="System.NotSupportedException"></exception>
        /// Produces an object from an array of bytes, with an optional type;
        public override object FromBinary(byte[] bytes, Type type)
        {
            throw new NotSupportedException();
        }
    }

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
        public NewtonSoftJsonSerializer(ActorSystem system)
            : base(system)
        {
            //TODO: we should use an instanced serializer to be threadsafe for other ActorSystems
            JsonConvert.DefaultSettings = () => new JsonSerializerSettings
            {
                Converters = new List<JsonConverter> {new ActorRefConverter()}
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
                return (typeof (ActorRef).IsAssignableFrom(objectType));
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
                return Serialization.CurrentSystem.Provider.ResolveActorRef(surrogate.Path);
            }

            /// <summary>
            ///     Writes the JSON representation of the object.
            /// </summary>
            /// <param name="writer">The <see cref="T:Newtonsoft.Json.JsonWriter" /> to write to.</param>
            /// <param name="value">The value.</param>
            /// <param name="serializer">The calling serializer.</param>
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                var @ref = (ActorRef) value;
                var surrogate = new ActorRefSurrogate(Serialization.SerializedActorPath(@ref));
                serializer.Serialize(writer, surrogate);
            }
        }
    }

    /**
     * This is a special Serializer that Serializes and deserializes nulls only
     */

    /// <summary>
    ///     Class NullSerializer.
    /// </summary>
    public class NullSerializer : Serializer
    {
        /// <summary>
        ///     The null bytes
        /// </summary>
        private readonly byte[] nullBytes = {};

        /// <summary>
        ///     Initializes a new instance of the <see cref="NullSerializer" /> class.
        /// </summary>
        /// <param name="system">The system.</param>
        public NullSerializer(ActorSystem system) : base(system)
        {
        }

        /// <summary>
        ///     Gets the identifier.
        /// </summary>
        /// <value>The identifier.</value>
        /// Completely unique value to identify this implementation of Serializer, used to optimize network traffic
        /// Values from 0 to 16 is reserved for Akka internal usage
        public override int Identifier
        {
            get { return 0; }
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
            return nullBytes;
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
            return null;
        }
    }

    /**
     * This is a special Serializer that Serializes and deserializes byte arrays only,
     * (just returns the byte array unchanged/uncopied)
     */

    /// <summary>
    ///     Class ByteArraySerializer.
    /// </summary>
    public class ByteArraySerializer : Serializer
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="ByteArraySerializer" /> class.
        /// </summary>
        /// <param name="system">The system.</param>
        public ByteArraySerializer(ActorSystem system) : base(system)
        {
        }

        /// <summary>
        ///     Gets the identifier.
        /// </summary>
        /// <value>The identifier.</value>
        /// Completely unique value to identify this implementation of Serializer, used to optimize network traffic
        /// Values from 0 to 16 is reserved for Akka internal usage
        public override int Identifier
        {
            get { return 4; }
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
        /// <exception cref="System.NotSupportedException"></exception>
        /// Serializes the given object into an Array of Byte
        public override byte[] ToBinary(object obj)
        {
            if (obj == null)
                return null;
            if (obj is byte[])
                return (byte[]) obj;
            throw new NotSupportedException();
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
            return bytes;
        }
    }

    //TODO: add as an extra project??
    //public class ProtoBufNetSerializer : Serializer
    //{

    //    public ProtoBufNetSerializer(ActorSystem system)
    //        : base(system)
    //    {
    //    }

    //    public override int Identifier
    //    {
    //        get { return -2; }
    //    }

    //    public override bool IncludeManifest
    //    {
    //        get { return true; }
    //    }

    //    public override byte[] ToBinary(object obj)
    //    {
    //        using (var ms = new MemoryStream())
    //        {
    //            ProtoBuf.Serializer.NonGeneric.Serialize(ms, obj);
    //            var bytes = ms.ToArray();
    //            return bytes;
    //        }

    //    }

    //    public override object FromBinary(byte[] bytes, Type type)
    //    {
    //        using (var ms = new MemoryStream())
    //        {
    //            ms.Write(bytes, 0, bytes.Length);
    //            ms.Position = 0;
    //            var res = ProtoBuf.Serializer.NonGeneric.Deserialize(type, ms);
    //            return res;
    //        }
    //    }
    //}
}