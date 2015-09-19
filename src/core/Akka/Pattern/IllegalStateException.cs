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
    /// Signals that a method has been invoked at an illegal or
    /// inappropriate time.
    /// </summary>
    public class IllegalStateException : AkkaException
    {

        public IllegalStateException(string message) : base(message)
        {

        }

        protected IllegalStateException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}

