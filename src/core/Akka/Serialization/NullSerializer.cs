using Akka.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Serialization
{
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
        private readonly byte[] nullBytes = { };

        /// <summary>
        ///     Initializes a new Instance of the <see cref="NullSerializer" /> class.
        /// </summary>
        /// <param name="system">The system.</param>
        public NullSerializer(ExtendedActorSystem system)
            : base(system)
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
}
