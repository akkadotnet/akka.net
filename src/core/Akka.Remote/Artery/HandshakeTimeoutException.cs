//-----------------------------------------------------------------------
// <copyright file="HandshakeTimeoutException.cs" company="Akka.NET Project">
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
    /// Thrown by <see cref="OutboundHandshakeStage"/> when a handshake does not complete within
    /// the configured <c>handshake-timeout</c> (default 20s — see
    /// <c>openspec/changes/artery-tcp-remoting/design.md</c>,
    /// "Handshake + association/UID (gate G2)"). Fails the outbound stream; the surrounding
    /// association is expected to retry.
    /// </summary>
    internal sealed class HandshakeTimeoutException : AkkaException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="HandshakeTimeoutException"/> class.
        /// </summary>
        public HandshakeTimeoutException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="HandshakeTimeoutException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="cause">The exception that is the cause of the current exception.</param>
        public HandshakeTimeoutException(string message, Exception? cause = null) : base(message, cause)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="HandshakeTimeoutException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext"/> that contains contextual information about the source or destination.</param>
        public HandshakeTimeoutException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
