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
}