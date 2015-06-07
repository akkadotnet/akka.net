//-----------------------------------------------------------------------
// <copyright file="Serializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
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
        /// Deserializes a byte array into an object of type <paramref name="type"/>.
        /// </summary>
        /// <param name="bytes">The array containing the serialized object</param>
        /// <param name="type">The type of object contained in the array</param>
        /// <returns>The object contained in the array</returns>
        public abstract object FromBinary(byte[] bytes, Type type);
    }

    /**
     * A Serializer represents a bimap between an object and an array of bytes representing that object.
     *
     * For serialization of data that need to evolve over time the `SerializerWithStringManifest` is recommended instead
     * of [[Serializer]] because the manifest (type hint) is a `String` instead of a `Class`. That means
     * that the class can be moved/removed and the serializer can still deserialize old data by matching
     * on the `String`. This is especially useful for Akka Persistence.
     *
     * The manifest string can also encode a version number that can be used in [[#fromBinary]] to
     * deserialize in different ways to migrate old data to new domain objects.
     *
     * If the data was originally serialized with [[Serializer]] and in a later version of the
     * system you change to `SerializerWithStringManifest` the manifest string will be the full class name if
     * you used `includeManifest=true`, otherwise it will be the empty string.
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
     * <b>Be sure to always use the [[akka.actor.DynamicAccess]] for loading classes!</b> This is necessary to
     * avoid strange match errors and inequalities which arise from different class loaders loading
     * the same class.
     */
    public abstract class SerializerWithStringManifest : Serializer
    {
        public override bool IncludeManifest { get { return true; } }
        
        protected SerializerWithStringManifest(ExtendedActorSystem system) : base(system)
        {
        }

        public sealed override object FromBinary(byte[] bytes, Type type)
        {
            var manifestString = type == null ? string.Empty : type.Name;
            return FromBinary(bytes, manifestString);
        }

        protected abstract object FromBinary(byte[] bytes, string manifestString);

        public abstract string Manifest(object o);
    }

    public static class SerializerIdentifierHelper
    {
        /// <summary>
        /// Configuration path of the serialization identifiers in HOCON config files.
        /// </summary>
        public const string SerializationIdentifiers = "akka.actor.serialization-identifiers";

        public static int GetSerializerIdentifierFromConfig(Type serializerType, ActorSystem system)
        {
            return system.Settings.Config.GetInt(SerializationIdentifiers + "." + serializerType.Name);
        }

        public static int GetSerializerIdentifierFromConfig<TSerializer>(ActorSystem system)
            where TSerializer : Serializer
        {
            return GetSerializerIdentifierFromConfig(typeof(TSerializer), system);
        }
    }
}

