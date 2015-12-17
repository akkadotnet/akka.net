//-----------------------------------------------------------------------
// <copyright file="Serializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;

namespace Akka.Serialization
{
    /// <summary>
    /// A Serializer represents a bimap between an object and an array of bytes representing that object.
    ///
    /// Serializers are loaded using reflection during <see cref="ActorSystem"/>
    /// start-up, where two constructors are tried in order:
    ///
    /// <ul>
    /// <li>taking exactly one argument of type <see cref="ExtendedActorSystem"/>;
    /// this should be the preferred one because all reflective loading of classes
    /// during deserialization should use ExtendedActorSystem.dynamicAccess (see
    /// [[akka.actor.DynamicAccess]]), and</li>
    /// <li>without arguments, which is only an option if the serializer does not
    /// load classes using reflection.</li>
    /// </ul>
    ///
    /// <b>Be sure to always use the PropertyManager for loading classes!</b> This is necessary to
    /// avoid strange match errors and inequalities which arise from different class loaders loading
    /// the same class.
    /// </summary>
    public abstract class Serializer
    {
        /// <summary>
        /// The actor system to associate with this serializer.
        /// </summary>
        protected readonly ExtendedActorSystem system;

        /// <summary>
        ///     Initializes a new instance of the <see cref="Serializer" /> class.
        /// </summary>
        /// <param name="system">The actor system to associate with this serializer. </param>
        public Serializer(ExtendedActorSystem system)
        {
            this.system = system;
        }

        /// <summary>
        /// Completely unique value to identify this implementation of Serializer, used to optimize network traffic
        /// Values from 0 to 16 is reserved for Akka internal usage
        /// </summary>
        public abstract int Identifier { get; }

        /// <summary>
        /// Returns whether this serializer needs a manifest in the fromBinary method
        /// </summary>
        public abstract bool IncludeManifest { get; }

        /// <summary>
        /// Serializes the given object into a byte array
        /// </summary>
        /// <param name="obj">The object to serialize </param>
        /// <returns>A byte array containing the serialized object</returns>
        public abstract byte[] ToBinary(object obj);

        /// <summary>
        /// Serializes the given object into a byte array and uses the given address to decorate serialized ActorRef's
        /// </summary>
        /// <param name="address">The address to use when serializing local ActorRef´s</param>
        /// <param name="obj">The object to serialize</param>
        /// <returns></returns>
        public byte[] ToBinaryWithAddress(Address address, object obj)
        {
            return Serialization.SerializeWithTransport(system, address, () => ToBinary(obj));
        }

        /// <summary>
        /// Deserializes a byte array into an object of type <paramref name="type"/>.
        /// </summary>
        /// <param name="bytes">The array containing the serialized object</param>
        /// <param name="type">The type of object contained in the array</param>
        /// <returns>The object contained in the array</returns>
        public abstract object FromBinary(byte[] bytes, Type type);
    }

    public abstract class SerializerWithStringManifest : Serializer
    {
        protected SerializerWithStringManifest(ExtendedActorSystem system) : base(system)
        {
        }

        public sealed override bool IncludeManifest { get { return true; } }

        public sealed override object FromBinary(byte[] bytes, Type type)
        {
            var manifest = type == null ? string.Empty : type.FullName;
            return FromBinary(bytes, manifest);
        }

        /// <summary>
        /// Produces an object from an array of bytes, with an optional type-hint.
        /// </summary>
        public abstract object FromBinary(byte[] binary, string manifest);

        /// <summary>
        /// Return the manifest (type hint) that will be provided in the fromBinary method.
        /// Return <see cref="string.Empty"/> if not needed.
        /// </summary>
        public abstract string Manifest(object o);
    }

    /// <summary>
    /// INTERNAL API.
    /// </summary>
    public static class SerializerIdentifierHelper
    {
        public const string SerializationIdentifiers = "akka.actor.serialization-identifiers";

        public static int GetSerializerIdentifierFromConfig(Type type, ExtendedActorSystem system)
        {
            var config = system.Settings.Config.GetConfig(SerializationIdentifiers);
            var identifiers = config.AsEnumerable()
                .ToDictionary(pair => Type.GetType(pair.Key, true), pair => pair.Value.GetInt());

            int value;
            if (identifiers.TryGetValue(type, out value))
            {
                return value;
            }
            else
            {
                throw new ArgumentException(string.Format("Couldn't find serializer id for [{0}] under [{1}] HOCON path", type, SerializationIdentifiers));
            }
        }
    }
}

