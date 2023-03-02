//-----------------------------------------------------------------------
// <copyright file="ClientTimeoutException.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.Serialization;

namespace Akka.Cluster.Sharding.External
{
    public class ClientTimeoutException : Exception
    {
        public ClientTimeoutException(string message) : base(message)
        {
        }

        public ClientTimeoutException(string message, Exception innerEx)
            : base(message, innerEx)
        {
        }

        protected ClientTimeoutException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
