//-----------------------------------------------------------------------
// <copyright file="StashOverflowException.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.Serialization;

namespace Akka.Actor
{
    /// <summary>
    /// Is thrown when the size of the Stash exceeds the capacity of the stash
    /// </summary>
    public class StashOverflowException : AkkaException
    {
        public StashOverflowException(string message, Exception cause = null) : base(message, cause) { }

        protected StashOverflowException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
