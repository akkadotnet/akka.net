//-----------------------------------------------------------------------
// <copyright file="StashOverflowException.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.Serialization;

namespace Akka.Actor
{
    /// <summary>
    /// This exception is thrown when the size of the Stash exceeds the capacity of the stash.
    /// </summary>
    public class StashOverflowException : AkkaException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="StashOverflowException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="cause">The exception that is the cause of the current exception.</param>
        public StashOverflowException(string message, Exception cause = null) : base(message, cause) { }

#if SERIALIZATION
        /// <summary>
        /// Initializes a new instance of the <see cref="StashOverflowException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo" /> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext" /> that contains contextual information about the source or destination.</param>
        protected StashOverflowException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#endif
    }
}
