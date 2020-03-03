//-----------------------------------------------------------------------
// <copyright file="StageException.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

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
    }
}
