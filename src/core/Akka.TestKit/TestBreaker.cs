//-----------------------------------------------------------------------
// <copyright file="TestBreaker.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Akka.Pattern;

namespace Akka.TestKit
{
    public class TestBreaker
    {
        public CountdownEvent HalfOpenLatch { get; private set; }
        public CountdownEvent OpenLatch { get; private set; }
        public CountdownEvent ClosedLatch { get; private set; }
        public CircuitBreaker Instance { get; private set; }

        public TestBreaker( CircuitBreaker instance )
        {
            HalfOpenLatch = new CountdownEvent( 1 );
            OpenLatch = new CountdownEvent( 1 );
            ClosedLatch = new CountdownEvent( 1 );
            Instance = instance;
            Instance.OnClose( ( ) => { if ( !ClosedLatch.IsSet ) ClosedLatch.Signal( ); } )
                    .OnHalfOpen( ( ) => { if ( !HalfOpenLatch.IsSet ) HalfOpenLatch.Signal( ); } )
                    .OnOpen( ( ) => { if ( !OpenLatch.IsSet ) OpenLatch.Signal( ); } );
        }


    }
}