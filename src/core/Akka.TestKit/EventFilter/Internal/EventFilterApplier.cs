//-----------------------------------------------------------------------
// <copyright file="EventFilterApplier.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.TestKit.TestEvent;

namespace Akka.TestKit.Internal
{
    /// <summary>
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    public class InternalEventFilterApplier : IEventFilterApplier
    {
        private readonly IReadOnlyList<EventFilterBase> _filters;
        private readonly TestKitBase _testkit;
        private readonly ActorSystem _actorSystem;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="testkit">TBD</param>
        /// <param name="system">TBD</param>
        /// <param name="filters">TBD</param>
        public InternalEventFilterApplier(TestKitBase testkit, ActorSystem system, IReadOnlyList<EventFilterBase> filters)
        {
            _filters = filters;
            _testkit = testkit;
            _actorSystem = system;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="action">TBD</param>
        public void ExpectOne(Action action)
        {
            InternalExpect(action, _actorSystem, 1);
        }

        public Task ExpectOneAsync(Func<Task> actionAsync)
        {
            return InternalExpectAsync(actionAsync, _actorSystem, 1);
        }

        /// <summary>
        /// Async version of <see cref="ExpectOne(System.Action)"/>
        /// </summary>
        /// <param name="action"></param>
        public async Task ExpectOneAsync(Action action)
        {
            await InternalExpectAsync(action, _actorSystem, 1);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="timeout">TBD</param>
        /// <param name="action">TBD</param>
        public void ExpectOne(TimeSpan timeout, Action action)
        {
            InternalExpect(action, _actorSystem, 1, timeout);
        }
        
        /// <summary>
        /// Async version of <see cref="ExpectOne(System.TimeSpan,System.Action) "/>
        /// </summary>
        /// <returns></returns>
        public async Task ExpectOneAsync(TimeSpan timeout, Action action)
        {
            await InternalExpectAsync(action, _actorSystem, 1, timeout);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="expectedCount">TBD</param>
        /// <param name="action">TBD</param>
        public void Expect(int expectedCount, Action action)
        {
            InternalExpect(action, _actorSystem, expectedCount, null);
        }

        /// <summary>
        /// Async version of Expect
        /// </summary>
        public Task ExpectAsync(int expectedCount, Func<Task> actionAsync)
        {
            return InternalExpectAsync(actionAsync, _actorSystem, expectedCount, null);
        }

        /// <summary>
        /// Async version of <see cref="Expect(int,System.Action)"/>
        /// </summary>
        public async Task ExpectAsync(int expectedCount, Action action)
        {
            await InternalExpectAsync(action, _actorSystem, expectedCount, null);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="expectedCount">TBD</param>
        /// <param name="timeout">TBD</param>
        /// <param name="action">TBD</param>
        public void Expect(int expectedCount, TimeSpan timeout, Action action)
        {
            InternalExpect(action, _actorSystem, expectedCount, timeout);
        }
        
        /// <summary>
        /// Async version of <see cref="Expect(int,System.TimeSpan,System.Action)"/>
        /// </summary>
        public async Task ExpectAsync(int expectedCount, TimeSpan timeout, Action action)
        {
            await InternalExpectAsync(action, _actorSystem, expectedCount, timeout);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="func">TBD</param>
        /// <returns>TBD</returns>
        public T ExpectOne<T>(Func<T> func)
        {
            return Intercept(func, _actorSystem, null, 1);
        }
        
        /// <summary>
        /// Async version of ExpectOne
        /// </summary>
        public async Task<T> ExpectOneAsync<T>(Func<T> func)
        {
            return await InterceptAsync(func, _actorSystem, null, 1);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="timeout">TBD</param>
        /// <param name="func">TBD</param>
        /// <returns>TBD</returns>
        public T ExpectOne<T>(TimeSpan timeout, Func<T> func)
        {
            return Intercept(func, _actorSystem, timeout, 1);
        }
        
        /// <summary>
        /// Async version of ExpectOne
        /// </summary>
        public async Task<T> ExpectOneAsync<T>(TimeSpan timeout, Func<T> func)
        {
            return await InterceptAsync(func, _actorSystem, timeout, 1);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="expectedCount">TBD</param>
        /// <param name="func">TBD</param>
        /// <returns>TBD</returns>
        public T Expect<T>(int expectedCount, Func<T> func)
        {
            return Intercept(func, _actorSystem, null, expectedCount);
        }

        /// <summary>
        /// Async version of Expect
        /// </summary>
        public async Task<T> ExpectAsync<T>(int expectedCount, Func<T> func)
        {
            return await InterceptAsync(func, _actorSystem, null, expectedCount);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="timeout">TBD</param>
        /// <param name="expectedCount">TBD</param>
        /// <param name="func">TBD</param>
        /// <returns>TBD</returns>
        public T Expect<T>(int expectedCount, TimeSpan timeout, Func<T> func)
        {
            return Intercept(func, _actorSystem, timeout, expectedCount);
        }
        
        /// <summary>
        /// Async version of Expect
        /// </summary>
        public async Task<T> ExpectAsync<T>(int expectedCount, TimeSpan timeout, Func<T> func)
        {
            return await InterceptAsync(func, _actorSystem, timeout, expectedCount);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="func">TBD</param>
        /// <returns>TBD</returns>
        public T Mute<T>(Func<T> func)
        {
            return Intercept(func, _actorSystem, null, null);
        }
        
        /// <summary>
        /// Async version of Mute
        /// </summary>
        public async Task<T> MuteAsync<T>(Func<T> func)
        {
            return await InterceptAsync(func, _actorSystem, null, null);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="action">TBD</param>
        public void Mute(Action action)
        {
            Intercept<object>(() => { action(); return null; }, _actorSystem, null, null);
        }
        
        /// <summary>
        /// Async version of Mute
        /// </summary>
        public async Task MuteAsync(Action action)
        {
            await InterceptAsync<object>(() => { action(); return null; }, _actorSystem, null, null);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public IUnmutableFilter Mute()
        {
            _actorSystem.EventStream.Publish(new Mute(_filters));
            return new InternalUnmutableFilter(_filters, _actorSystem);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public EventFilterFactory And
        {
            get
            {
                return new EventFilterFactory(_testkit, _actorSystem, _filters);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="func">TBD</param>
        /// <param name="system">TBD</param>
        /// <param name="timeout">TBD</param>
        /// <param name="expectedOccurrences">TBD</param>
        /// <param name="matchedEventHandler">TBD</param>
        /// <returns>TBD</returns>
        protected T Intercept<T>(Func<T> func, ActorSystem system, TimeSpan? timeout, int? expectedOccurrences, MatchedEventHandler matchedEventHandler = null)
        {
            var leeway = system.HasExtension<TestKitSettings>()
                ? TestKitExtension.For(system).TestEventFilterLeeway
                : _testkit.TestKitSettings.TestEventFilterLeeway;

            var timeoutValue = timeout.HasValue ? _testkit.Dilated(timeout.Value) : leeway;
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
                            msg = string.Format("Timeout ({0}) while waiting for messages. Only received {1}/{2} messages that matched filter [{3}]", timeoutValue, actualNumberOfEvents, expectedNumberOfEvents, string.Join(",", _filters).Replace("{", "{{").Replace("}", "}}"));
                        else
                        {
                            var tooMany = actualNumberOfEvents - expectedNumberOfEvents;
                            msg = string.Format("Received {0} {1} too many. Expected {2} {3} but received {4} that matched filter [{5}]", tooMany, GetMessageString(tooMany), expectedNumberOfEvents, GetMessageString(expectedNumberOfEvents), actualNumberOfEvents, string.Join(",", _filters).Replace("{", "{{").Replace("}", "}}"));
                        }
                    }
                    else
                        msg = string.Format("Timeout ({0}) while waiting for messages that matched filter [{1}]", timeoutValue, string.Join(",", _filters).Replace("{", "{{").Replace("}", "}}"));

                    var assertionsProvider = system.HasExtension<TestKitAssertionsProvider>()
                        ? TestKitAssertionsExtension.For(system)
                        : TestKitAssertionsExtension.For(_testkit.Sys);
                    assertionsProvider.Assertions.Fail(msg);
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

        /// <summary>
        /// Async version of <see cref="Intercept{T}"/>
        /// </summary>
        protected Task<T> InterceptAsync<T>(Func<T> func, ActorSystem system, TimeSpan? timeout, int? expectedOccurrences, MatchedEventHandler matchedEventHandler = null)
        {
            return InterceptAsync(() => Task.FromResult(func()), system, timeout, expectedOccurrences, matchedEventHandler);
        }
        
        /// <summary>
        /// Async version of <see cref="Intercept{T}"/>
        /// </summary>
        protected async Task<T> InterceptAsync<T>(Func<Task<T>> func, ActorSystem system, TimeSpan? timeout, int? expectedOccurrences, MatchedEventHandler matchedEventHandler = null)
        {
            var leeway = system.HasExtension<TestKitSettings>()
                ? TestKitExtension.For(system).TestEventFilterLeeway
                : _testkit.TestKitSettings.TestEventFilterLeeway;

            var timeoutValue = timeout.HasValue ? _testkit.Dilated(timeout.Value) : leeway;
            matchedEventHandler = matchedEventHandler ?? new MatchedEventHandler();
            system.EventStream.Publish(new Mute(_filters));
            try
            {
                foreach(var filter in _filters)
                {
                    filter.EventMatched += matchedEventHandler.HandleEvent;
                }
                var result = await func();

                if(!await AwaitDoneAsync(timeoutValue, expectedOccurrences, matchedEventHandler))
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
                            msg = string.Format("Received {0} {1} too many. Expected {2} {3} but received {4} that matched filter [{5}]", tooMany, GetMessageString(tooMany), expectedNumberOfEvents, GetMessageString(expectedNumberOfEvents), actualNumberOfEvents, string.Join(",", _filters));
                        }
                    }
                    else
                        msg = string.Format("Timeout ({0}) while waiting for messages that matched filter [{1}]", timeoutValue, _filters);

                    var assertionsProvider = system.HasExtension<TestKitAssertionsProvider>()
                        ? TestKitAssertionsExtension.For(system)
                        : TestKitAssertionsExtension.For(_testkit.Sys);
                    assertionsProvider.Assertions.Fail(msg);
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="timeout">TBD</param>
        /// <param name="expectedOccurrences">TBD</param>
        /// <param name="matchedEventHandler">TBD</param>
        /// <returns>TBD</returns>
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
        
        /// <summary>
        /// Async version of <see cref="AwaitDone"/>
        /// </summary>
        protected async Task<bool> AwaitDoneAsync(TimeSpan timeout, int? expectedOccurrences, MatchedEventHandler matchedEventHandler)
        {
            if(expectedOccurrences.HasValue)
            {
                var expected = expectedOccurrences.GetValueOrDefault();
                await _testkit.AwaitConditionNoThrowAsync(() => matchedEventHandler.ReceivedCount >= expected, timeout);
                return matchedEventHandler.ReceivedCount == expected;
            }
            return true;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="number">TBD</param>
        /// <returns>TBD</returns>
        protected static string GetMessageString(int number)
        {
            return number == 1 ? "message" : "messages";
        }

        private void InternalExpect(Action action, ActorSystem actorSystem, int expectedCount, TimeSpan? timeout = null)
        {
            Intercept<object>(() => { action(); return null; }, actorSystem, timeout, expectedCount);
        }
        
        /// <summary>
        /// Async version of <see cref="InternalExpect"/>
        /// </summary>
        private async Task InternalExpectAsync(Func<Task> actionAsync, ActorSystem actorSystem, int expectedCount, TimeSpan? timeout = null)
        {
            await InterceptAsync<object>(() => { actionAsync(); return Task.FromResult<object>(null); }, actorSystem, timeout, expectedCount);
        }
        
        /// <summary>
        /// Async version of <see cref="InternalExpect"/>
        /// </summary>
        private async Task InternalExpectAsync(Action action, ActorSystem actorSystem, int expectedCount, TimeSpan? timeout = null)
        {
            await InterceptAsync<object>(() => { action(); return Task.FromResult<object>(null); }, actorSystem, timeout, expectedCount);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected class MatchedEventHandler
        {
            private int _receivedCount;

            /// <summary>
            /// TBD
            /// </summary>
            public int ReceivedCount { get { return _receivedCount; } }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="eventFilter">TBD</param>
            /// <param name="logEvent">TBD</param>
            public virtual void HandleEvent(EventFilterBase eventFilter, LogEvent logEvent)
            {
                if(_receivedCount != int.MaxValue) Interlocked.Increment(ref _receivedCount);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected class InternalUnmutableFilter : IUnmutableFilter
        {
            private IReadOnlyCollection<EventFilterBase> _filters;
            private readonly ActorSystem _system;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="filters">TBD</param>
            /// <param name="system">TBD</param>
            public InternalUnmutableFilter(IReadOnlyCollection<EventFilterBase> filters, ActorSystem system)
            {
                _filters = filters;
                _system = system;
            }

            /// <summary>
            /// TBD
            /// </summary>
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

            /// <inheritdoc/>
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
