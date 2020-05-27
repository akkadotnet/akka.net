using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Akka.Actor;
using Akka.Actor.Setup;

namespace Akka.Serialization
{
    /// <summary>
    /// Setup for the <see cref="ActorSystem"/> serialization subsystem.
    ///
    /// Constructor is INTERNAL API. Use the factory method <see cref="Create"/>.
    /// </summary>
    public sealed class SerializationSetup : Setup
    {
        internal SerializationSetup(Func<ExtendedActorSystem, ImmutableHashSet<SerializerDetails>> createSerializers)
        {
            CreateSerializers = createSerializers;
        }

        public Func<ExtendedActorSystem, ImmutableHashSet<SerializerDetails>> CreateSerializers { get; }

        /// <summary>
        /// create pairs of serializer and the set of classes it should be used for
        /// </summary>
        public static SerializationSetup Create(
            Func<ExtendedActorSystem, ImmutableHashSet<SerializerDetails>> createSerializers)
        {
            return new SerializationSetup(createSerializers);
        }
    }

    /// <summary>
    /// Constructor is internal API.
    ///
    /// Use the <see cref="SerializerDetails.Create"/> factory method instead
    /// </summary>
    public sealed class SerializerDetails
    {
        internal SerializerDetails(string @alias, Serializer serializer, ImmutableHashSet<Type> useFor)
        {
            Alias = alias;
            Serializer = serializer;
            UseFor = useFor;
        }

        /// <summary>
        /// The name of the serializer - must be unique.
        /// </summary>
        /// <example>
        /// i.e. "json" for the global JSON.NET serializer
        /// "IDomainSerializer" for a custom serializer that serializes "IDomain" messages, etc.
        /// </example>
        public string Alias { get; }

        /// <summary>
        /// The serializer that belongs to this <see cref="Alias"/>.
        /// </summary>
        public Serializer Serializer { get; }

        /// <summary>
        /// The types of messages that this 
        /// </summary>
        public ImmutableHashSet<Type> UseFor { get; }

        /// <summary>
        /// Factory method for creating programmatic setups for Serializers.
        /// </summary>
        /// <param name="alias">Register the serializer under this alias (this allows it to be used by bindings in the config)</param>
        /// <param name="serializer">The serializer implementation.</param>
        /// <param name="useFor">A set of types (classes, base classes, or interfaces) that will be bound to this serializer.
        /// This is the programmatic equivalent of the `akka.actor.serialization.serialization-bindings` HOCON section.</param>
        public static SerializerDetails Create(string alias, Serializer serializer, ImmutableHashSet<Type> useFor)
        {
            return new SerializerDetails(alias, serializer, useFor);
        }
    }

}
