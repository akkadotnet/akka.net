//-----------------------------------------------------------------------
// <copyright file="OpenCircuitException.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.Serialization;
using Akka.Actor;

namespace Akka.Pattern
{
    /// <summary>
    /// Exception throws when CircuitBreaker is open
    /// </summary>
    public class OpenCircuitException : AkkaException
    {
        public OpenCircuitException( ) : base( "Circuit Breaker is open; calls are failing fast" ) { }

        public OpenCircuitException( string message )
            : base( message )
        {
        }

        public OpenCircuitException( string message, Exception innerException )
            : base( message, innerException )
        {
        }

        protected OpenCircuitException( SerializationInfo info, StreamingContext context )
            : base( info, context )
        {
        }
    }
}