//-----------------------------------------------------------------------
// <copyright file="Serializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Annotations;
using Akka.Util;
using Akka.Configuration;
using Akka.Util.Reflection;

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
        internal static string GetErrorForSerializerId(int id) => SerializerErrorCode.GetErrorForSerializerId(id);
        
        /// <summary>
        /// The actor system to associate with this serializer.
        /// </summary>
        protected readonly ExtendedActorSystem system;

        internal ExtendedActorSystem ExtendedSystem => system;

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
        /// Returns the manifest that should be stored with <paramref name="obj"/>.
        /// </summary>
        public virtual string Manifest(object obj)
        {
            return IncludeManifest ? obj.GetType().TypeQualifiedName() : string.Empty;
        }

        /// <summary>
        /// Serializes the given object into a byte array and uses the given address to decorate serialized ActorRef's
        /// </summary>
        /// <param name="address">The address to use when serializing local ActorRef´s</param>
        /// <param name="obj">The object to serialize</param>
        /// <returns>A byte array containing the serialized object with decorated ActorRef's</returns>
        public byte[] ToBinaryWithAddress(Address address, object obj)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            return Serialization.WithTransport(system, address, () => ToBinary(obj));
#pragma warning restore CS0618 // Type or member is obsolete
        }

        /// <summary>
        /// Deserializes a byte array into an object of type <paramref name="type"/>.
        /// </summary>
        /// <param name="bytes">The array containing the serialized object</param>
        /// <param name="type">The type of object contained in the array</param>
        /// <returns>The object contained in the array</returns>
        public abstract object FromBinary(byte[] bytes, Type type);

        /// <summary>
        /// Deserializes a byte array into an object using a string manifest.
        /// </summary>
        public virtual object FromBinary(byte[] bytes, string manifest)
        {
            if (string.IsNullOrEmpty(manifest))
                return FromBinary(bytes, (Type)null);

            Type type;
            try
            {
                type = TypeCache.GetType(manifest);
            }
            catch (Exception ex)
            {
                throw new SerializationException($"Cannot find manifest class [{manifest}] for serializer with id [{Identifier}].", ex);
            }

            return FromBinary(bytes, type);
        }

        /// <summary>
        /// Deserializes a byte array into an object.
        /// </summary>
        /// <param name="bytes">The array containing the serialized object</param>
        /// <returns>The object contained in the array</returns>
        public T FromBinary<T>(byte[] bytes) => (T)FromBinary(bytes, typeof(T));
    }

    /// <summary>
    /// A specialized serializer that uses string manifests for type hinting during deserialization.
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
        public abstract override object FromBinary(byte[] bytes, string manifest);

        /// <summary>
        /// Returns the manifest (type hint) that will be provided in the <see cref="FromBinary(byte[],System.Type)"/> method.
        ///
        /// <note>
        /// This method returns <see cref="String.Empty"/> if a manifest is not needed.
        /// </note>
        /// </summary>
        /// <param name="o">The object for which the manifest is needed.</param>
        /// <returns>The manifest needed for the deserialization of the specified <paramref name="o"/>.</returns>
        public abstract override string Manifest(object o);
    }

    /// <summary>
    /// INTERNAL API.
    /// </summary>
    [InternalApi]
    public static class SerializerIdentifierHelper
    {
        internal const string SerializationIdentifiers = "akka.actor.serialization-identifiers";

        /// <summary>
        /// Gets the serializer identifier for the specified type from the configuration.
        /// </summary>
        /// <param name="type">The type of the serializer to get the identifier for.</param>
        /// <param name="system">The actor system containing the configuration.</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown if the system couldn't find the given serializer <paramref name="type"/> id in the configuration.
        /// </exception>
        /// <returns>The serializer identifier for the specified type.</returns>
        /// <remarks>
        /// <para>
        /// The keys under <c>akka.actor.serialization-identifiers</c> are matched against the name of the
        /// <paramref name="type"/> that is already in hand, rather than resolved back into a <see cref="Type"/>.
        /// Each key is run through
        /// <see cref="Akka.Util.TypeExtensions.StripAssemblyIdentity(string)"/> and then split at the comma that
        /// separates the type name from the assembly name: the type name is compared case-sensitively, the
        /// assembly name case-insensitively, which is how <see cref="Type.GetType(string)"/> compared them.
        /// </para>
        /// <para>
        /// Assembly-qualified keys are matched across the whole block first, and only then bare
        /// <see cref="Type.FullName"/> keys, so a bare key cannot shadow an exact assembly-qualified key
        /// further down the block. A bare key matches a type of that name in <em>any</em> assembly.
        /// </para>
        /// <para>
        /// Matching by name also fixes a latent bug. The lookup used to call
        /// <c>Type.GetType(key, throwOnError: true)</c> for <em>every</em> key in the block, so one key naming a
        /// type this application cannot load - a serializer from a package that is configured but not
        /// referenced, say - made every identifier lookup throw, including the lookups for serializers whose
        /// own key was perfectly good.
        /// </para>
        /// </remarks>
        public static int GetSerializerIdentifierFromConfig(Type type, ExtendedActorSystem system)
        {
            var config = system.Settings.Config.GetConfig(SerializationIdentifiers);
            /*
            if (config.IsNullOrEmpty())
                throw new ConfigurationException($"Cannot retrieve serialization identifier informations: {SerializationIdentifiers} configuration node not found");
            */

            // TypeQualifiedName() is the cached "Namespace.Type, Assembly" spelling with the assembly identity
            // already stripped, including inside a generic type's arguments. Split it the same way the keys are
            // split so the two halves are guaranteed to line up. Nested types spell with '+' on both sides.
            var qualifiedName = type.TypeQualifiedName();
            var separator = IndexOfAssemblySeparator(qualifiedName);
            var fullName = separator < 0 ? qualifiedName : qualifiedName.Substring(0, separator).TrimEnd();
            var assemblyName = separator < 0 ? string.Empty : qualifiedName.Substring(separator + 1).Trim();

            // Pass 1: assembly-qualified keys, over the whole block, so one of them always beats a bare key.
            foreach (var pair in config.AsEnumerable())
            {
                var key = Akka.Util.TypeExtensions.StripAssemblyIdentity(pair.Key);
                var keySeparator = IndexOfAssemblySeparator(key);
                if (keySeparator < 0)
                    continue;

                if (string.Equals(key.Substring(0, keySeparator).TrimEnd(), fullName, StringComparison.Ordinal) &&
                    string.Equals(key.Substring(keySeparator + 1).Trim(), assemblyName, StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Value.GetInt();
                }
            }

            // Pass 2: bare keys.
            foreach (var pair in config.AsEnumerable())
            {
                var key = Akka.Util.TypeExtensions.StripAssemblyIdentity(pair.Key);
                if (IndexOfAssemblySeparator(key) < 0 && string.Equals(key, fullName, StringComparison.Ordinal))
                    return pair.Value.GetInt();
            }

            throw new ArgumentException($"Couldn't find serializer id for [{type}] under [{SerializationIdentifiers}] HOCON path", nameof(type));
        }

        /// <summary>
        /// The index of the comma that separates the type name from the assembly name, which is the first comma
        /// at bracket depth zero - the commas inside a generic type's argument list do not count.
        /// </summary>
        /// <returns>The index, or <c>-1</c> when <paramref name="typeName"/> carries no assembly name.</returns>
        private static int IndexOfAssemblySeparator(string typeName)
        {
            var depth = 0;
            for (var i = 0; i < typeName.Length; i++)
            {
                switch (typeName[i])
                {
                    case '[':
                        depth++;
                        break;
                    case ']':
                        depth--;
                        break;
                    case ',' when depth == 0:
                        return i;
                }
            }

            return -1;
        }
    }
}
