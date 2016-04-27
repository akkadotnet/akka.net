//-----------------------------------------------------------------------
// <copyright file="StreamLimitReachedException.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.Serialization;

namespace Akka.Streams
{
    public class StreamLimitReachedException : Exception
    {
        public StreamLimitReachedException(long max) : base($"Limit of {max} reached")
        {
        }

        protected StreamLimitReachedException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}