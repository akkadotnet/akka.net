//-----------------------------------------------------------------------
// <copyright file="StageException.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.Serialization;

namespace Akka.Streams
{
    /// <summary>
    /// TBD
    /// </summary>
    public class NoSuchElementException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NoSuchElementException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public NoSuchElementException(string message) : base(message)
        {

        }

        /// <summary>
        /// Initializes a new instance of the <see cref="NoSuchElementException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext"/> that contains contextual information about the source or destination.</param>
        protected NoSuchElementException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
