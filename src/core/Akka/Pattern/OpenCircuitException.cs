//-----------------------------------------------------------------------
// <copyright file="OpenCircuitException.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.Serialization;
using Akka.Actor;

namespace Akka.Pattern
{
    /// <summary>
    /// This exception is thrown when the CircuitBreaker is open.
    /// </summary>
    public class OpenCircuitException : AkkaException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="OpenCircuitException"/> class.
        /// </summary>
        public OpenCircuitException() : base("Circuit Breaker is open; calls are failing fast") { }

        /// <summary>
        /// Initializes a new instance of the <see cref="OpenCircuitException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public OpenCircuitException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="OpenCircuitException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="cause">The exception that is the cause of the current exception.</param>
        public OpenCircuitException(string message, Exception cause)
            : base(message, cause)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="OpenCircuitException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo" /> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext" /> that contains contextual information about the source or destination.</param>
        protected OpenCircuitException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
