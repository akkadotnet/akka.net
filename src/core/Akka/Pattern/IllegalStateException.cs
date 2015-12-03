//-----------------------------------------------------------------------
// <copyright file="IllegalStateException.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Runtime.Serialization;
using Akka.Actor;

namespace Akka.Pattern
{
    /// <summary>
    /// This exception is thrown when a method has been invoked at an illegal or inappropriate time.
    /// </summary>
    public class IllegalStateException : AkkaException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="IllegalStateException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public IllegalStateException(string message) : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="IllegalStateException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo" /> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext" /> that contains contextual information about the source or destination.</param>
        protected IllegalStateException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
