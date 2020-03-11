//-----------------------------------------------------------------------
// <copyright file="Serializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Annotations;
using Akka.Util;
using Akka.Configuration;

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

        private readonly FastLazy<int> _value;

        /// <summary>
        /// Initializes a new instance of the <see cref="Serializer" /> class.
        /// </summary>
        /// <param name="system">The actor system to associate with this serializer. </param>
        protected Serializer(ExtendedActorSystem system)
        {
            this.system = system;
            _value = new FastLazy<int>(() => SerializerIdentifierHelper.GetSerializerIdentifierFromConfig(GetType(), system));
        }

        /// <summary>
        /// Completely unique value to identify this implementation of Serializer, used to optimize network traffic
        /// Values from 0 to 16 is reserved for Akka internal usage
        /// </summary>
        public virtual int Identifier => _value.Value;

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
        /// <returns>TBD</returns>
        public byte[] ToBinaryWithAddress(Address address, object obj)
        {
            return Serialization.WithTransport(system, address, () => ToBinary(obj));
        }

        /// <summary>
        /// Deserializes a byte array into an object of type <paramref name="type"/>.
        /// </summary>
        /// <param name="bytes">The array containing the serialized object</param>
        /// <param name="type">The type of object contained in the array</param>
        /// <returns>The object contained in the array</returns>
        public abstract object FromBinary(byte[] bytes, Type type);

        /// <summary>
        /// Deserializes a byte array into an object.
        /// </summary>
        /// <param name="bytes">The array containing the serialized object</param>
        /// <returns>The object contained in the array</returns>
        public T FromBinary<T>(byte[] bytes) => (T)FromBinary(bytes, typeof(T));
    }

    /// <summary>
    /// TBD
    /// </summary>
    public abstract class SerializerWithStringManifest : Serializer
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SerializerWithStringManifest"/> class.
        /// </summary>
        /// <param name="system">The actor system to associate with this serializer.</param>
        protected SerializerWithStringManifest(ExtendedActorSystem system) : base(system)
        {
        }

        /// <summary>
        /// Returns whether this serializer needs a manifest in the fromBinary method
        /// </summary>
        public sealed override bool IncludeManifest => true;

        /// <summary>
        /// Deserializes a byte array into an object of type <paramref name="type" />.
        ///
        /// It's recommended to throw <see cref="SerializationException"/> in <see cref="FromBinary(byte[], Type)"/>
        /// if the manifest is unknown.This makes it possible to introduce new message
        /// types and send them to nodes that don't know about them. This is typically
        /// needed when performing rolling upgrades, i.e.running a cluster with mixed
        /// versions for while. <see cref="SerializationException"/> is treated as a transient
        /// problem in the TCP based remoting layer.The problem will be logged
        /// and message is dropped.Other exceptions will tear down the TCP connection
        /// because it can be an indication of corrupt bytes from the underlying transport.
        /// </summary>
        /// <param name="bytes">The array containing the serialized object</param>
        /// <param name="type">The type of object contained in the array</param>
        /// <returns>The object contained in the array</returns>
        public sealed override object FromBinary(byte[] bytes, Type type)
        {
            var manifest = type.TypeQualifiedName();
            return FromBinary(bytes, manifest);
        }

        /// <summary>
        /// Deserializes a byte array into an object using an optional <paramref name="manifest"/> (type hint).
        ///
        /// It's recommended to throw <see cref="SerializationException"/> in <see cref="FromBinary(byte[], string)"/>
        /// if the manifest is unknown.This makes it possible to introduce new message
        /// types and send them to nodes that don't know about them. This is typically
        /// needed when performing rolling upgrades, i.e.running a cluster with mixed
        /// versions for while. <see cref="SerializationException"/> is treated as a transient
        /// problem in the TCP based remoting layer.The problem will be logged
        /// and message is dropped.Other exceptions will tear down the TCP connection
        /// because it can be an indication of corrupt bytes from the underlying transport.
        /// </summary>
        /// <param name="bytes">The array containing the serialized object</param>
        /// <param name="manifest">The type hint used to deserialize the object contained in the array.</param>
        /// <returns>The object contained in the array</returns>
        public abstract object FromBinary(byte[] bytes, string manifest);

        /// <summary>
        /// Returns the manifest (type hint) that will be provided in the <see cref="FromBinary(byte[],System.Type)"/> method.
        ///
        /// <note>
        /// This method returns <see cref="String.Empty"/> if a manifest is not needed.
        /// </note>
        /// </summary>
        /// <param name="o">The object for which the manifest is needed.</param>
        /// <returns>The manifest needed for the deserialization of the specified <paramref name="o"/>.</returns>
        public abstract string Manifest(object o);
    }

    /// <summary>
    /// INTERNAL API.
    /// </summary>
    [InternalApi]
    public static class SerializerIdentifierHelper
    {
        internal const string SerializationIdentifiers = "akka.actor.serialization-identifiers";

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="type">TBD</param>
        /// <param name="system">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown if the system couldn't find the given serializer <paramref name="type"/> id in the configuration.
        /// </exception>
        /// <returns>TBD</returns>
        public static int GetSerializerIdentifierFromConfig(Type type, ExtendedActorSystem system)
        {
            var config = system.Settings.Config.GetConfig(SerializationIdentifiers);
            /*
            if (config.IsNullOrEmpty())
                throw new ConfigurationException($"Cannot retrieve serialization identifier informations: {SerializationIdentifiers} configuration node not found");
            */
            var identifiers = config.AsEnumerable()
                .ToDictionary(pair => Type.GetType(pair.Key, true), pair => pair.Value.GetInt());

            if (!identifiers.TryGetValue(type, out int value))
                throw new ArgumentException($"Couldn't find serializer id for [{type}] under [{SerializationIdentifiers}] HOCON path", nameof(type));

            return value;
        }
    }
}

