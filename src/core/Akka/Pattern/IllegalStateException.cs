//-----------------------------------------------------------------------
// <copyright file="IllegalStateException.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
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
        /// <param name="message">The message that describes the error.</param>
        /// <param name="innerEx">The inner <see cref="Exception"/> that was thrown.</param>
        public IllegalStateException(string message, Exception innerEx) : base(message, innerEx)
        {
        }
    }
}
