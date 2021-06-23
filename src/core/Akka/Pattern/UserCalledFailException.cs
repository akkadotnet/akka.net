//-----------------------------------------------------------------------
// <copyright file="UserCalledFailException.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;
using Akka.Actor;

namespace Akka.Pattern
{
    public class UserCalledFailException : AkkaException
    {
        public UserCalledFailException() : base($"User code caused [{nameof(CircuitBreaker)}] to fail because it calls the [{nameof(CircuitBreaker.Fail)}()] method.")
        { }

        /// <summary>
        /// Initializes a new instance of the <see cref="UserCalledFailException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo" /> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext" /> that contains contextual information about the source or destination.</param>
        protected UserCalledFailException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }
}
