using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Serialization
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
    public abstract class Serializer
    {
        /**
         * Completely unique value to identify this implementation of Serializer, used to optimize network traffic
         * Values from 0 to 16 is reserved for Akka internal usage
         */
        public abstract int Identifier { get; }
        /**
         * Returns whether this serializer needs a manifest in the fromBinary method
         */
        public abstract bool IncludeManifest { get; }
        /**
         * Serializes the given object into an Array of Byte
         */
        public abstract byte[] ToBinary(object obj);

        /**
         * Produces an object from an array of bytes, with an optional type;
         */
        public abstract object FromBinary(byte[] bytes, Type type);
    }

    public class JavaSerializer : Serializer
    {

        public override int Identifier
        {
            get { return 1; }
        }

        public override bool IncludeManifest
        {
            get { throw new NotSupportedException(); }
        }

        public override byte[] ToBinary(object obj)
        {
            throw new NotSupportedException();
        }

        public override object FromBinary(byte[] bytes, Type type)
        {
            throw new NotSupportedException();
        }
    }

    public class JsonSerializer : Serializer
    {
        public override bool IncludeManifest
        {
            get { return false; }
        }
        public override object FromBinary(byte[] bytes, Type type)
        {
            var json = Encoding.Default.GetString(bytes);
            return fastJSON.JSON.Instance.ToObject(json);
        }

        public override byte[] ToBinary(object obj)
        {
            var json = fastJSON.JSON.Instance.ToJSON(obj);
            var bytes = Encoding.Default.GetBytes(json);
            return bytes;
        }

        public override int Identifier
        {
            get { return -1; }
        }
    }    

    /**
     * This is a special Serializer that Serializes and deserializes nulls only
     */
    public class NullSerializer : Serializer
    {
        private readonly byte[] nullBytes = { };
        public override int Identifier
        {
            get { return 0; }
        }

        public override bool IncludeManifest
        {
            get { return false; }
        }

        public override byte[] ToBinary(object obj)
        {
            return nullBytes;
        }

        public override object FromBinary(byte[] bytes, Type type)
        {
            return null;
        }
    }

    /**
     * This is a special Serializer that Serializes and deserializes byte arrays only,
     * (just returns the byte array unchanged/uncopied)
     */
    public class ByteArraySerializer : Serializer
    {
        public override int Identifier
        {
            get { return 4; }
        }

        public override bool IncludeManifest
        {
            get { return false; }
        }

        public override byte[] ToBinary(object obj)
        {
            if (obj == null)
                return null;
            if (obj is byte[])
                return (byte[])obj;            
            throw new NotSupportedException();
        }

        public override object FromBinary(byte[] bytes, Type type)
        {
            return bytes;
        }
    }
}
