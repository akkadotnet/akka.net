//-----------------------------------------------------------------------
// <copyright file="JavaSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;

namespace Akka.Serialization
{
    /// <summary>
    /// This is a special <see cref="Serializer"/> that serializes and deserializes Java objects only.
    /// </summary>
    public class JavaSerializer : Serializer
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="JavaSerializer" /> class.
        /// </summary>
        /// <param name="system">The actor system to associate with this serializer. </param>
        public JavaSerializer(ExtendedActorSystem system)
            : base(system)
        {
        }

        /// <summary>
        /// Completely unique value to identify this implementation of the <see cref="Serializer"/> used to optimize network traffic
        /// </summary>
        public override int Identifier
        {
            get { return 1; }
        }

        /// <summary>
        /// N/A
        /// </summary>
        /// <exception cref="NotSupportedException">
        /// This exception is thrown automatically since this is an unsupported serializer here only as a placeholder.
        /// </exception>
        public override bool IncludeManifest
        {
            get { throw new NotSupportedException("This serializer is unsupported here only as a placeholder. Thus this property is not implemented."); }
        }

        /// <summary>
        /// N/A
        /// </summary>
        /// <param name="obj">N/A</param>
        /// <exception cref="NotSupportedException">
        /// This exception is thrown automatically since this is an unsupported serializer here only as a placeholder.
        /// </exception>
        /// <returns>N/A</returns>
        public override byte[] ToBinary(object obj)
        {
            throw new NotSupportedException("This serializer is unsupported here only as a placeholder. Thus this function is not implemented.");
        }

        /// <summary>
        /// N/A
        /// </summary>
        /// <param name="bytes">N/A</param>
        /// <param name="type">N/A</param>
        /// <exception cref="NotSupportedException">
        /// This exception is thrown automatically since this is an unsupported serializer here only as a placeholder.
        /// </exception>
        /// <returns>N/A</returns>
        public override object FromBinary(byte[] bytes, Type type)
        {
            throw new NotSupportedException("This serializer is unsupported here only as a placeholder. Thus this function is not implemented.");
        }
    }
}
