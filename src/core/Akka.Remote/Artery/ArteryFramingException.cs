//-----------------------------------------------------------------------
// <copyright file="ArteryFramingException.cs" company="Akka.NET Project">
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
    /// Thrown when Artery TCP framing encounters input that cannot be a valid frame stream:
    /// an invalid connection preamble (wrong <c>AKKA</c> magic bytes or an unrecognized
    /// <see cref="ArteryStreamId"/> byte), or a frame whose declared length exceeds the
    /// configured maximum frame length. See
    /// <c>openspec/changes/artery-tcp-remoting/design.md</c> ("Envelope wire layout" /
    /// Decision 3) for the framing this guards.
    /// </summary>
    internal sealed class ArteryFramingException : AkkaException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ArteryFramingException"/> class.
        /// </summary>
        public ArteryFramingException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ArteryFramingException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="cause">The exception that is the cause of the current exception.</param>
        public ArteryFramingException(string message, Exception? cause = null) : base(message, cause)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ArteryFramingException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext"/> that contains contextual information about the source or destination.</param>
        public ArteryFramingException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
