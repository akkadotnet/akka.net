using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Akka.Pattern
{

    public interface State
    {
        void Enter();
    }

    public class Closed : AtomicInteger, State
    {

        public void Enter()
        {
        }
    }

    public class HalfOpen : AtomicBoolean, State
    {
        public void Enter()
        {
        }
    }

    public class Open : AtomicLong, State
    {
        public void Enter()
        {
        }
    }

    public class CircuitBreaker
    {
        private TimeSpan _resetTimeout;
        private TimeSpan _callTimeout;
        private int _maxFailures;
        private State _state;

        public CircuitBreaker(int maxFailures, TimeSpan callTimeout, TimeSpan resetTimeout)
        {
            _maxFailures = maxFailures;
            _callTimeout = callTimeout;
            _resetTimeout = resetTimeout;
        }

        public bool SwapState(State oldState,State newState)
        {
            return Interlocked.CompareExchange(ref _state, newState, oldState) == newState;
        }

        public void Transition(State fromState,State toState)
        {
            if (SwapState(fromState,toState))
            {
                toState.Enter();
            }
            else
            {
                throw new IllegalStateException("Illegal transition attempted from: " + fromState + " to " + toState);
            }
        }
    }
}
