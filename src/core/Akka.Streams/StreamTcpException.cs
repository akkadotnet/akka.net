//-----------------------------------------------------------------------
// <copyright file="StreamTcpException.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.Serialization;

namespace Akka.Streams
{
    /// <summary>
    /// TBD
    /// </summary>
    public class StreamTcpException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="StreamTcpException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public StreamTcpException(string message) : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="StreamTcpException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public StreamTcpException(string message, Exception innerException) : base(message, innerException)
        {
        }

#if SERIALIZATION
        /// <summary>
        /// Initializes a new instance of the <see cref="StreamTcpException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo" /> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext" /> that contains contextual information about the source or destination.</param>
        protected StreamTcpException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
#endif
    }

    /// <summary>
    /// TBD
    /// </summary>
    public class BindFailedException : StreamTcpException
    {
        /// <summary>
        /// The single instance of this exception
        /// </summary>
        public static readonly BindFailedException Instance = new BindFailedException();

        private BindFailedException() : base("bind failed")
        {
        }

#if SERIALIZATION
        /// <summary>
        /// Initializes a new instance of the <see cref="BindFailedException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo" /> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext" /> that contains contextual information about the source or destination.</param>
        protected BindFailedException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
#endif
    }

    /// <summary>
    /// TBD
    /// </summary>
    public class ConnectionException : StreamTcpException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ConnectionException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public ConnectionException(string message) : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ConnectionException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public ConnectionException(string message, Exception innerException) : base(message, innerException)
        {
        }

#if SERIALIZATION
        /// <summary>
        /// Initializes a new instance of the <see cref="ConnectionException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo" /> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext" /> that contains contextual information about the source or destination.</param>
        protected ConnectionException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
#endif
    }
}
