//-----------------------------------------------------------------------
// <copyright file="ArteryEnvelopeException.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Runtime.Serialization;
using Akka.Actor;

namespace Akka.Remote.Artery
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// Thrown when <see cref="ArteryEnvelopeCodec"/> encounters an envelope that cannot be decoded
    /// (or, at encode time, input that cannot be encoded) per the wire layout in
    /// <c>openspec/changes/artery-tcp-remoting/design.md</c> ("Envelope wire layout" / Decision 4):
    /// an unsupported version, a reserved flag bit set, a tag offset out of bounds, a truncated
    /// literal, a declared payload offset outside the frame, or an oversized literal at encode.
    /// </summary>
    internal sealed class ArteryEnvelopeException : AkkaException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ArteryEnvelopeException"/> class.
        /// </summary>
        public ArteryEnvelopeException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ArteryEnvelopeException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="cause">The exception that is the cause of the current exception.</param>
        public ArteryEnvelopeException(string message, Exception? cause = null) : base(message, cause)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ArteryEnvelopeException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext"/> that contains contextual information about the source or destination.</param>
        public ArteryEnvelopeException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
