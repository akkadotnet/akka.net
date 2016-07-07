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
    public class StreamTcpException : Exception
    {
        public StreamTcpException(string message) : base(message)
        {
        }

        public StreamTcpException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected StreamTcpException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }

    public class BindFailedException : StreamTcpException
    {
        public static readonly BindFailedException Instance = new BindFailedException();

        private BindFailedException() : base("bind failed")
        {
        }

        protected BindFailedException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }

    public class ConnectionException : StreamTcpException
    {
        public ConnectionException(string message) : base(message)
        {
        }

        public ConnectionException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected ConnectionException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}