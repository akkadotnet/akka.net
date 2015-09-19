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