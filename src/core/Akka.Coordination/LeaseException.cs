//-----------------------------------------------------------------------
// <copyright file="LeaseException.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.Serialization;

namespace Akka.Coordination
{
    /// <summary>
    /// Lease exception. Lease implementation should use it to report issues when acquiring lease
    /// </summary>
    public class LeaseException : Exception
    {
        /// <summary>
        /// Creates a new <see cref="LeaseException"/> instance.
        /// </summary>
        /// <param name="message">Exception message</param>
        public LeaseException(string message) : base(message)
        {
        }

        /// <summary>
        /// Creates a new <see cref="LeaseException"/> instance.
        /// </summary>
        /// <param name="message">Exception message</param>
        /// <param name="innerEx">Inner exception</param>
        public LeaseException(string message, Exception innerEx)
            : base(message, innerEx)
        {
        }

        protected LeaseException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {

        }
    }
}
