using System;
using System.Collections.Generic;
using System.Threading;
using Akka.Actor;
using Akka.Event;
using Akka.TestKit.TestEvent;

namespace Akka.TestKit.Internal
{
    /// <summary>
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    public class InternalEventFilterApplier : EventFilterApplier
    {
        private readonly IReadOnlyList<EventFilterBase> _filters;
        private readonly TestKitBase _testkit;

        public InternalEventFilterApplier(TestKitBase testkit, IReadOnlyList<EventFilterBase> filters)
        {
            _filters = filters;
            _testkit = testkit;
        }


        public void ExpectOne(Action action)
        {
            InternalExpect(action, 1);
        }

        public void ExpectOne(TimeSpan timeout, Action action)
        {
            InternalExpect(action, 1, timeout);
        }

        public void Expect(int expectedCount, Action action)
        {
            InternalExpect(action, expectedCount, null);
        }

        public void Expect(int expectedCount, TimeSpan timeout, Action action)
        {
            InternalExpect(action, expectedCount, timeout);
        }

        private void InternalExpect(Action action, int expectedCount, TimeSpan? timeout = null)
        {
            Intercept<object>(() => { action(); return null; }, _testkit.Sys, timeout, expectedCount);
        }


        public T ExpectOne<T>(Func<T> func)
        {
            return Intercept(func, _testkit.Sys, null, 1);
        }

        public T ExpectOne<T>(TimeSpan timeout, Func<T> func)
        {
            return Intercept(func, _testkit.Sys, timeout, 1);
        }

        public T Expect<T>(int expectedCount, Func<T> func)
        {
            return Intercept(func, _testkit.Sys, null, expectedCount);
        }

        public T Expect<T>(int expectedCount, TimeSpan timeout, Func<T> func)
        {
            return Intercept(func, _testkit.Sys, timeout, expectedCount);
        }



        public T Mute<T>(Func<T> func)
        {
            return Intercept(func, _testkit.Sys, null, null);
        }

        public void Mute(Action action)
        {
            Intercept<object>(() => { action(); return null; }, _testkit.Sys, null, null);
        }

        public UnmutableFilter Mute()
        {
            _testkit.Sys.EventStream.Publish(new Mute(_filters));
            return new InternalUnmutableFilter(_filters, _testkit.Sys);
        }


        public EventFilterFactory And
        {
            get
            {
                return new EventFilterFactory(_testkit,_filters);
            }
        }

        protected T Intercept<T>(Func<T> func, ActorSystem system, TimeSpan? timeout, int? expectedOccurrences, MatchedEventHandler matchedEventHandler = null)
        {
            var timeoutValue = timeout.HasValue ? _testkit.Dilated(timeout.Value) : TestKitExtension.For(system).TestEventFilterLeeway;
            matchedEventHandler = matchedEventHandler ?? new MatchedEventHandler();
            system.EventStream.Publish(new Mute(_filters));
            try
            {
                foreach(var filter in _filters)
                {
                    filter.EventMatched += matchedEventHandler.HandleEvent;
                }
                var result = func();

                if(!AwaitDone(timeoutValue, expectedOccurrences, matchedEventHandler))
                {
                    var actualNumberOfEvents = matchedEventHandler.ReceivedCount;
                    string msg;
                    if(expectedOccurrences.HasValue)
                    {
                        var expectedNumberOfEvents = expectedOccurrences.Value;
                        if(actualNumberOfEvents < expectedNumberOfEvents)
                            msg = string.Format("Timeout ({0}) while waiting for messages. Only received {1}/{2} messages that matched filter [{3}]", timeoutValue, actualNumberOfEvents, expectedNumberOfEvents, string.Join(",", _filters));
                        else
                        {
                            var tooMany = actualNumberOfEvents - expectedNumberOfEvents;
                            msg = string.Format("Received {0} {1} too many. Expected {2} {3} but recieved {4} that matched filter [{5}]", tooMany, GetMessageString(tooMany), expectedNumberOfEvents, GetMessageString(expectedNumberOfEvents), actualNumberOfEvents, string.Join(",", _filters));
                        }
                    }
                    else
                        msg = string.Format("Timeout ({0}) while waiting for messages that matched filter [{1}]", timeoutValue, _filters);

                    var testKitAssertionsProvider = TestKitAssertionsExtension.For(system);
                    testKitAssertionsProvider.Assertions.Fail(msg);
                }
                return result;
            }
            finally
            {
                foreach(var filter in _filters)
                {
                    filter.EventMatched -= matchedEventHandler.HandleEvent;
                }
                system.EventStream.Publish(new Unmute(_filters));
            }
        }

        protected bool AwaitDone(TimeSpan timeout, int? expectedOccurrences, MatchedEventHandler matchedEventHandler)
        {
            if(expectedOccurrences.HasValue)
            {
                var expected = expectedOccurrences.GetValueOrDefault();
                _testkit.AwaitConditionNoThrow(() => matchedEventHandler.ReceivedCount >= expected, timeout);
                return matchedEventHandler.ReceivedCount == expected;
            }
            return true;
        }

        protected static string GetMessageString(int number)
        {
            return number == 1 ? "message" : "messages";
        }

        protected class MatchedEventHandler
        {
            private int _receivedCount;

            public int ReceivedCount { get { return _receivedCount; } }

            public virtual void HandleEvent(EventFilterBase eventFilter, LogEvent logEvent)
            {
                if(_receivedCount != int.MaxValue) Interlocked.Increment(ref _receivedCount);
            }
        }

        protected class InternalUnmutableFilter : UnmutableFilter
        {
            private IReadOnlyCollection<EventFilterBase> _filters;
            private readonly ActorSystem _system;

            public InternalUnmutableFilter(IReadOnlyCollection<EventFilterBase> filters, ActorSystem system)
            {
                _filters = filters;
                _system = system;
            }

            public void Unmute()
            {
                var filters = _filters;
                _filters = null;
                if(!_isDisposed && filters != null)
                {
                    _system.EventStream.Publish(new Unmute(filters));
                }
            }

            private bool _isDisposed; //Automatically initialized to false;

            //Destructor:
            //~InternalUnmutableFilter() 
            //{
            //    // Finalizer calls Dispose(false)
            //    Dispose(false);
            //}

            /// <summary>Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.</summary>
            public void Dispose()
            {
                Dispose(true);
                //Take this object off the finalization queue and prevent finalization code for this object
                //from executing a second time.
                GC.SuppressFinalize(this);
            }


            /// <summary>Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.</summary>
            /// <param name="disposing">if set to <c>true</c> the method has been called directly or indirectly by a 
            /// user's code. Managed and unmanaged resources will be disposed.<br />
            /// if set to <c>false</c> the method has been called by the runtime from inside the finalizer and only 
            /// unmanaged resources can be disposed.</param>
            protected virtual void Dispose(bool disposing)
            {
                // If disposing equals false, the method has been called by the
                // runtime from inside the finalizer and you should not reference
                // other objects. Only unmanaged resources can be disposed.

                try
                {
                    //Make sure Dispose does not get called more than once, by checking the disposed field
                    if(!_isDisposed)
                    {
                        if(disposing)
                        {
                            Unmute();
                        }
                        //Clean up unmanaged resources
                    }
                    _isDisposed = true;
                }
                finally
                {
                    // base.dispose(disposing);
                }
            }
        }
    }
}