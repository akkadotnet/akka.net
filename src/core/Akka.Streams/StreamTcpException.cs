//-----------------------------------------------------------------------
// <copyright file="StreamTcpException.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        public StreamTcpException(string message) : base(message)
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <param name="innerException">TBD</param>
        public StreamTcpException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="info">TBD</param>
        /// <param name="context">TBD</param>
        protected StreamTcpException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public class BindFailedException : StreamTcpException
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly BindFailedException Instance = new BindFailedException();

        private BindFailedException() : base("bind failed")
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="info">TBD</param>
        /// <param name="context">TBD</param>
        protected BindFailedException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public class ConnectionException : StreamTcpException
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        public ConnectionException(string message) : base(message)
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <param name="innerException">TBD</param>
        public ConnectionException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="info">TBD</param>
        /// <param name="context">TBD</param>
        protected ConnectionException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}