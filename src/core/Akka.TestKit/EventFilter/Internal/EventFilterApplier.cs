//-----------------------------------------------------------------------
// <copyright file="EventFilterApplier.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.TestKit.TestEvent;
using Nito.AsyncEx.Synchronous;

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
        /// <param name="cancellationToken"></param>
        public void ExpectOne(Action action, CancellationToken cancellationToken = default)
        {
            ExpectOneAsync(async () => action(), cancellationToken)
                .WaitAndUnwrapException(cancellationToken);
        }

        /// <summary>
        /// Async version of <see cref="ExpectOne(System.Action, CancellationToken)"/>
        /// </summary>
        /// <param name="action"></param>
        /// <param name="cancellationToken"></param>
        public async Task ExpectOneAsync(Func<Task> action, CancellationToken cancellationToken = default)
        {
            await InternalExpectAsync(
                    actionAsync: action,
                    actorSystem: _actorSystem,
                    expectedCount: 1,
                    timeout: null,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="timeout">TBD</param>
        /// <param name="action">TBD</param>
        /// <param name="cancellationToken"></param>
        public void ExpectOne(
            TimeSpan timeout,
            Action action,
            CancellationToken cancellationToken = default)
        {
            ExpectOneAsync(timeout, async () => action(), cancellationToken)
                .WaitAndUnwrapException(cancellationToken);
        }
        
        /// <summary>
        /// Async version of <see cref="ExpectOne(System.TimeSpan,System.Action,CancellationToken) "/>
        /// </summary>
        /// <returns></returns>
        public async Task ExpectOneAsync(
            TimeSpan timeout,
            Func<Task> action,
            CancellationToken cancellationToken = default)
        {
            await InternalExpectAsync(
                    actionAsync: action,
                    actorSystem: _actorSystem,
                    expectedCount: 1,
                    timeout: timeout,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="expectedCount">TBD</param>
        /// <param name="action">TBD</param>
        /// <param name="cancellationToken"></param>
        public void Expect(
            int expectedCount,
            Action action,
            CancellationToken cancellationToken = default)
        {
            ExpectAsync(expectedCount, async () => action(), cancellationToken)
                .WaitAndUnwrapException(cancellationToken);
        }

        /// <summary>
        /// Async version of Expect
        /// </summary>
        public async Task ExpectAsync(
            int expectedCount,
            Func<Task> actionAsync,
            CancellationToken cancellationToken = default)
            => await InternalExpectAsync(
                    actionAsync: actionAsync,
                    actorSystem: _actorSystem,
                    expectedCount: expectedCount,
                    timeout: null,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

        /// <summary>
        /// Async version of Expect
        /// </summary>
        public async Task ExpectAsync(
            int expectedCount,
            Func<Task> actionAsync,
            TimeSpan? timeout,
            CancellationToken cancellationToken = default)
        {
            await InternalExpectAsync(
                    actionAsync: actionAsync,
                    actorSystem: _actorSystem,
                    expectedCount: expectedCount,
                    timeout: timeout,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="expectedCount">TBD</param>
        /// <param name="timeout">TBD</param>
        /// <param name="action">TBD</param>
        /// <param name="cancellationToken"></param>
        public void Expect(
            int expectedCount,
            TimeSpan timeout,
            Action action,
            CancellationToken cancellationToken = default)
        {
            ExpectAsync(expectedCount, timeout, async () => action(), cancellationToken)
                .WaitAndUnwrapException(cancellationToken); 
        }
        
        /// <summary>
        /// Async version of <see cref="Expect(int,System.TimeSpan,System.Action,CancellationToken)"/>
        /// </summary>
        public async Task ExpectAsync(
            int expectedCount,
            TimeSpan timeout,
            Func<Task> action,
            CancellationToken cancellationToken = default)
        {
            await InternalExpectAsync(
                    actionAsync: action,
                    actorSystem: _actorSystem,
                    expectedCount: expectedCount,
                    timeout: timeout,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="func">TBD</param>
        /// <param name="cancellationToken"></param>
        /// <returns>TBD</returns>
        public T ExpectOne<T>(Func<T> func, CancellationToken cancellationToken = default)
        {
            return ExpectOneAsync(async () => func(), cancellationToken)
                .WaitAndUnwrapException(cancellationToken);
        }
        
        /// <summary>
        /// Async version of ExpectOne
        /// </summary>
        public async Task<T> ExpectOneAsync<T>(
            Func<Task<T>> func,
            CancellationToken cancellationToken = default)
        {
            return await InterceptAsync(
                    func: func,
                    system: _actorSystem,
                    timeout: null,
                    expectedOccurrences: 1,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="timeout">TBD</param>
        /// <param name="func">TBD</param>
        /// <param name="cancellationToken"></param>
        /// <returns>TBD</returns>
        public T ExpectOne<T>(
            TimeSpan timeout,
            Func<T> func,
            CancellationToken cancellationToken = default)
        {
            return ExpectOneAsync(timeout, async () => func(), cancellationToken)
                .WaitAndUnwrapException();
        }
        
        /// <summary>
        /// Async version of ExpectOne
        /// </summary>
        public async Task<T> ExpectOneAsync<T>(
            TimeSpan timeout,
            Func<Task<T>> func,
            CancellationToken cancellationToken = default)
        {
            return await InterceptAsync(
                    func: func, 
                    system: _actorSystem,
                    timeout: timeout,
                    expectedOccurrences: 1,
                    matchedEventHandler: null,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="expectedCount">TBD</param>
        /// <param name="func">TBD</param>
        /// <param name="cancellationToken"></param>
        /// <returns>TBD</returns>
        public T Expect<T>(
            int expectedCount,
            Func<T> func,
            CancellationToken cancellationToken = default)
        {
            return ExpectAsync(expectedCount, async () => func(), cancellationToken)
                .WaitAndUnwrapException();
        }

        /// <summary>
        /// Async version of Expect
        /// </summary>
        public async Task<T> ExpectAsync<T>(
            int expectedCount, 
            Func<Task<T>> func,
            CancellationToken cancellationToken = default)
        {
            return await InterceptAsync(
                    func: func,
                    system: _actorSystem,
                    timeout: null,
                    expectedOccurrences: expectedCount,
                    matchedEventHandler: null,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="timeout">TBD</param>
        /// <param name="expectedCount">TBD</param>
        /// <param name="func">TBD</param>
        /// <param name="cancellationToken"></param>
        /// <returns>TBD</returns>
        public T Expect<T>(
            int expectedCount,
            TimeSpan timeout,
            Func<T> func,
            CancellationToken cancellationToken = default)
        {
            return ExpectAsync(expectedCount, timeout, async () => func(), cancellationToken)
                .WaitAndUnwrapException();
        }
        
        /// <summary>
        /// Async version of Expect
        /// Note: <paramref name="func"/> might not get awaited.
        /// </summary>
        public async Task<T> ExpectAsync<T>(
            int expectedCount,
            TimeSpan timeout,
            Func<Task<T>> func,
            CancellationToken cancellationToken = default)
        {
            return await InterceptAsync(
                    func: func,
                    system: _actorSystem,
                    timeout: timeout,
                    expectedOccurrences: expectedCount,
                    matchedEventHandler: null,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="func">TBD</param>
        /// <param name="cancellationToken"></param>
        /// <returns>TBD</returns>
        public T Mute<T>(Func<T> func, CancellationToken cancellationToken = default)
        {
            return MuteAsync(async () => func(), cancellationToken)
                .WaitAndUnwrapException();
        }
        
        /// <summary>
        /// Async version of Mute
        /// </summary>
        public async Task<T> MuteAsync<T>(Func<Task<T>> func, CancellationToken cancellationToken = default)
        {
            return await InterceptAsync(
                    func: func,
                    system: _actorSystem,
                    timeout: null,
                    expectedOccurrences: null,
                    matchedEventHandler: null,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="action">TBD</param>
        /// <param name="cancellationToken"></param>
        public void Mute(Action action, CancellationToken cancellationToken = default)
        {
            MuteAsync(async () => action(), cancellationToken)
                .WaitAndUnwrapException(cancellationToken);
        }
        
        /// <summary>
        /// Async version of Mute
        /// </summary>
        public async Task MuteAsync(Func<Task> action, CancellationToken cancellationToken = default)
        {
            await InterceptAsync<object>(
                    func: async () =>
                    {
                        await action();
                        return NotUsed.Instance;
                    }, 
                    system: _actorSystem, 
                    timeout: null,
                    expectedOccurrences: null,
                    matchedEventHandler: null,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
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
        /// <param name="cancellationToken"></param>
        /// <returns>TBD</returns>
        protected T Intercept<T>(
            Func<T> func,
            ActorSystem system,
            TimeSpan? timeout,
            int? expectedOccurrences, 
            MatchedEventHandler matchedEventHandler = null,
            CancellationToken cancellationToken = default)
        {
            return InterceptAsync(
                    func: async () => func(),
                    system: system,
                    timeout: timeout,
                    expectedOccurrences: expectedOccurrences,
                    matchedEventHandler: matchedEventHandler,
                    cancellationToken: cancellationToken)
                .WaitAndUnwrapException();
        }

        /// <summary>
        /// Async version of <see cref="Intercept{T}"/>
        /// </summary>
        protected async Task<T> InterceptAsync<T>(
            Func<Task<T>> func,
            ActorSystem system,
            TimeSpan? timeout,
            int? expectedOccurrences,
            MatchedEventHandler matchedEventHandler = null,
            CancellationToken cancellationToken = default)
        {
            var leeway = system.HasExtension<TestKitSettings>()
                ? TestKitExtension.For(system).TestEventFilterLeeway
                : _testkit.TestKitSettings.TestEventFilterLeeway;

            TimeSpan timeoutValue;
            if (timeout.HasValue)
            {
                timeoutValue = _testkit.Dilated(timeout.Value);
            }
            else
            {
                timeoutValue = _testkit.RemainingOrDilated(leeway);
                // If we expected 0 occurence and we're inside a Within block, timeout need to be slightly less else Within will throw
                if (_testkit.IsInsideWithin && expectedOccurrences == 0)
                {
                    timeoutValue -= TimeSpan.FromMilliseconds(200);
                    if(timeoutValue < TimeSpan.Zero)
                        timeoutValue = TimeSpan.Zero;
                }
            }
            
            matchedEventHandler ??= new MatchedEventHandler();
            system.EventStream.Publish(new Mute(_filters));
            try
            {
                foreach(var filter in _filters)
                {
                    filter.EventMatched += matchedEventHandler.HandleEvent;
                }
                var result = await func();

                if(!await AwaitDoneAsync(timeoutValue, expectedOccurrences, matchedEventHandler, cancellationToken))
                {
                    var actualNumberOfEvents = matchedEventHandler.ReceivedCount;
                    string msg;
                    if(expectedOccurrences.HasValue)
                    {
                        var expectedNumberOfEvents = expectedOccurrences.Value;
                        if(actualNumberOfEvents < expectedNumberOfEvents)
                            msg =
                                $"Timeout ({timeoutValue}) while waiting for messages. " +
                                $"Only received {actualNumberOfEvents}/{expectedNumberOfEvents} messages " +
                                $"that matched filter [{string.Join(",", _filters)}]";
                        else
                        {
                            var tooMany = actualNumberOfEvents - expectedNumberOfEvents;
                            msg =
                                $"Received {tooMany} {GetMessageString(tooMany)} too many. " +
                                $"Expected {expectedNumberOfEvents} {GetMessageString(expectedNumberOfEvents)} " +
                                $"but received {actualNumberOfEvents} that matched filter [{string.Join(",", _filters)}]";
                        }
                    }
                    else
                        msg = $"Timeout ({timeoutValue}) while waiting for messages that matched filter [{_filters}]";

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
        /// <param name="cancellationToken"></param>
        /// <returns>TBD</returns>
        protected bool AwaitDone(
            TimeSpan timeout,
            int? expectedOccurrences,
            MatchedEventHandler matchedEventHandler,
            CancellationToken cancellationToken = default)
        {
            return AwaitDoneAsync(timeout, expectedOccurrences, matchedEventHandler, cancellationToken)
                .WaitAndUnwrapException();
        }
        
        /// <summary>
        /// Async version of <see cref="AwaitDone"/>
        /// </summary>
        protected async Task<bool> AwaitDoneAsync(
            TimeSpan timeout,
            int? expectedOccurrences,
            MatchedEventHandler matchedEventHandler,
            CancellationToken cancellationToken = default)
        {
            if(expectedOccurrences.HasValue)
            {
                var expected = expectedOccurrences.GetValueOrDefault();
                if (expected > 0)
                {
                    await _testkit.AwaitConditionNoThrowAsync(async () => matchedEventHandler.ReceivedCount >= expected, timeout, cancellationToken: cancellationToken);
                    return matchedEventHandler.ReceivedCount == expected;
                }
                else
                {
                    // if expecting no events to arrive - assert that given condition will never match
                    var foundEvent = await _testkit.AwaitConditionNoThrowAsync(async () => matchedEventHandler.ReceivedCount > 0, timeout, cancellationToken: cancellationToken);
                    return foundEvent == false;
                }
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

        private async Task InternalExpectAsync(
            Func<Task> actionAsync,
            ActorSystem actorSystem, 
            int expectedCount, 
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            await InterceptAsync(
                    async () =>
                    {
                        await actionAsync();
                        return NotUsed.Instance;
                    }, actorSystem, timeout, expectedCount, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
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
