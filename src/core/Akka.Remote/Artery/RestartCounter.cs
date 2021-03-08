using System;
using System.Collections.Generic;
using System.Text;
using Akka.Util;

namespace Akka.Remote.Artery
{
    internal class RestartCounter
    {
        public sealed class State
        {
            public int Count { get; }
            public Deadline Deadline { get; }

            public State(int count, Deadline deadline)
            {
                Count = count;
                Deadline = deadline;
            }

            public State Copy(int? count = null, Deadline deadline = null)
                => new State(count ?? Count, deadline ?? Deadline);
        }

        private readonly int _maxRestart;
        private readonly TimeSpan _restartTimeout;

        private readonly AtomicReference<State> _state; 

        public RestartCounter(int maxRestart, TimeSpan restartTimeout)
        {
            _maxRestart = maxRestart;
            _restartTimeout = restartTimeout;

            _state = new AtomicReference<State>(new State(0, Deadline.Now + restartTimeout));

        }

        public int Count() => _state.Value.Count;

        public bool Restart()
        {
            while (true)
            {
                var s = _state.Value;

                var newState = s.Deadline.HasTimeLeft switch
                {
                    true => s.Copy(count: s.Count + 1),
                    _ => new State(1, Deadline.Now + _restartTimeout)
                };

                if (_state.CompareAndSet(s, newState))
                {
                    return newState.Count <= _maxRestart;
                }
            }
        }
    }
}
