//-----------------------------------------------------------------------
// <copyright file="TestKitBase_AwaitConditions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Event;
using Akka.TestKit.Internal;

namespace Akka.TestKit
{
    /// <summary>
    /// TBD
    /// </summary>
    public abstract partial class TestKitBase
    {
        /// <summary>
        /// <para>Await until the given condition evaluates to <c>true</c> or until a timeout</para>
        /// <para>The timeout is taken from the innermost enclosing `within`
        /// block (if inside a `within` block) or the value specified in config value "akka.test.single-expect-default". 
        /// The value is <see cref="Dilated(TimeSpan)">dilated</see>, i.e. scaled by the factor 
        /// specified in config value "akka.test.timefactor"..</para>
        /// <para>A call to <paramref name="conditionIsFulfilled"/> is done immediately, then the threads sleep
        /// for about a tenth of the timeout value, before it checks the condition again. This is repeated until
        /// timeout or the condition evaluates to <c>true</c>. To specify another interval, use the overload
        /// <see cref="AwaitCondition(System.Func{bool},System.Nullable{System.TimeSpan},System.Nullable{System.TimeSpan},string)"/>
        /// </para>
        /// </summary>
        /// <param name="conditionIsFulfilled">The condition that must be fulfilled within the duration.</param>
        public void AwaitCondition(Func<bool> conditionIsFulfilled)
        {
            var maxDur = RemainingOrDefault;
            var interval = new TimeSpan(maxDur.Ticks / 10);
            var logger = _testState.TestKitSettings.LogTestKitCalls ? _testState.Log : null;
            InternalAwaitCondition(conditionIsFulfilled, maxDur, interval, (format, args) => _assertions.Fail(format, args), logger);
        }
        
        /// <summary>
        /// <para>Await until the given condition evaluates to <c>true</c> or until a timeout</para>
        /// <para>The timeout is taken from the innermost enclosing `within`
        /// block (if inside a `within` block) or the value specified in config value "akka.test.single-expect-default". 
        /// The value is <see cref="Dilated(TimeSpan)">dilated</see>, i.e. scaled by the factor 
        /// specified in config value "akka.test.timefactor"..</para>
        /// <para>A call to <paramref name="conditionIsFulfilled"/> is done immediately, then the threads sleep
        /// for about a tenth of the timeout value, before it checks the condition again. This is repeated until
        /// timeout or the condition evaluates to <c>true</c>. To specify another interval, use the overload
        /// <see cref="AwaitCondition(System.Func{bool},System.Nullable{System.TimeSpan},System.Nullable{System.TimeSpan},string)"/>
        /// </para>
        /// </summary>
        /// <param name="conditionIsFulfilled">The condition that must be fulfilled within the duration.</param>
        public async Task AwaitConditionAsync(Func<bool> conditionIsFulfilled)
        {
            var maxDur = RemainingOrDefault;
            var interval = new TimeSpan(maxDur.Ticks / 10);
            var logger = _testState.TestKitSettings.LogTestKitCalls ? _testState.Log : null;
            await InternalAwaitConditionAsync(conditionIsFulfilled, maxDur, interval, (format, args) => _assertions.Fail(format, args), logger);
        }

        /// <summary>
        /// <para>Await until the given condition evaluates to <c>true</c> or the timeout
        /// expires, whichever comes first.</para>
        /// <para>If no timeout is given, take it from the innermost enclosing `within`
        /// block (if inside a `within` block) or the value specified in config value "akka.test.single-expect-default". 
        /// The value is <see cref="Dilated(TimeSpan)">dilated</see>, i.e. scaled by the factor 
        /// specified in config value "akka.test.timefactor"..</para>
        /// <para>A call to <paramref name="conditionIsFulfilled"/> is done immediately, then the threads sleep
        /// for about a tenth of the timeout value, before it checks the condition again. This is repeated until
        /// timeout or the condition evaluates to <c>true</c>. To specify another interval, use the overload
        /// <see cref="AwaitCondition(System.Func{bool},System.Nullable{System.TimeSpan},System.Nullable{System.TimeSpan},string)"/>
        /// </para>
        /// </summary>
        /// <param name="conditionIsFulfilled">The condition that must be fulfilled within the duration.</param>
        /// <param name="max">The maximum duration. If undefined, uses the remaining time 
        /// (if inside a `within` block) or the value specified in config value "akka.test.single-expect-default". 
        /// The value is <see cref="Dilated(TimeSpan)">dilated</see>, i.e. scaled by the factor 
        /// specified in config value "akka.test.timefactor".</param>
        public void AwaitCondition(Func<bool> conditionIsFulfilled, TimeSpan? max)
        {
            var maxDur = RemainingOrDilated(max);
            var interval = new TimeSpan(maxDur.Ticks / 10);
            var logger = _testState.TestKitSettings.LogTestKitCalls ? _testState.Log : null;
            InternalAwaitCondition(conditionIsFulfilled, maxDur, interval, (format, args) => _assertions.Fail(format, args), logger);
        }
        
        /// <summary>
        /// <para>Await until the given condition evaluates to <c>true</c> or the timeout
        /// expires, whichever comes first.</para>
        /// <para>If no timeout is given, take it from the innermost enclosing `within`
        /// block (if inside a `within` block) or the value specified in config value "akka.test.single-expect-default". 
        /// The value is <see cref="Dilated(TimeSpan)">dilated</see>, i.e. scaled by the factor 
        /// specified in config value "akka.test.timefactor"..</para>
        /// <para>A call to <paramref name="conditionIsFulfilled"/> is done immediately, then the threads sleep
        /// for about a tenth of the timeout value, before it checks the condition again. This is repeated until
        /// timeout or the condition evaluates to <c>true</c>. To specify another interval, use the overload
        /// <see cref="AwaitCondition(System.Func{bool},System.Nullable{System.TimeSpan},System.Nullable{System.TimeSpan},string)"/>
        /// </para>
        /// </summary>
        /// <param name="conditionIsFulfilled">The condition that must be fulfilled within the duration.</param>
        /// <param name="max">The maximum duration. If undefined, uses the remaining time 
        /// (if inside a `within` block) or the value specified in config value "akka.test.single-expect-default". 
        /// The value is <see cref="Dilated(TimeSpan)">dilated</see>, i.e. scaled by the factor 
        /// specified in config value "akka.test.timefactor".</param>
        public async Task AwaitConditionAsync(Func<bool> conditionIsFulfilled, TimeSpan? max)
        {
            var maxDur = RemainingOrDilated(max);
            var interval = new TimeSpan(maxDur.Ticks / 10);
            var logger = _testState.TestKitSettings.LogTestKitCalls ? _testState.Log : null;
            await InternalAwaitConditionAsync(conditionIsFulfilled, maxDur, interval, (format, args) => _assertions.Fail(format, args), logger);
        }

        /// <summary>
        /// <para>Await until the given condition evaluates to <c>true</c> or the timeout
        /// expires, whichever comes first.</para>
        /// <para>If no timeout is given, take it from the innermost enclosing `within`
        /// block (if inside a `within` block) or the value specified in config value "akka.test.single-expect-default". 
        /// The value is <see cref="Dilated(TimeSpan)">dilated</see>, i.e. scaled by the factor 
        /// specified in config value "akka.test.timefactor"..</para>
        /// <para>A call to <paramref name="conditionIsFulfilled"/> is done immediately, then the threads sleep
        /// for about a tenth of the timeout value, before it checks the condition again. This is repeated until
        /// timeout or the condition evaluates to <c>true</c>. To specify another interval, use the overload
        /// <see cref="AwaitCondition(System.Func{bool},System.Nullable{System.TimeSpan},System.Nullable{System.TimeSpan},string)"/>
        /// </para>
        /// </summary>
        /// <param name="conditionIsFulfilled">The condition that must be fulfilled within the duration.</param>
        /// <param name="max">The maximum duration. If undefined, uses the remaining time 
        /// (if inside a `within` block) or the value specified in config value "akka.test.single-expect-default". 
        /// The value is <see cref="Dilated(TimeSpan)">dilated</see>, i.e. scaled by the factor 
        /// specified in config value "akka.test.timefactor".</param>
        /// <param name="message">The message used if the timeout expires.</param>
        public void AwaitCondition(Func<bool> conditionIsFulfilled, TimeSpan? max, string message)
        {
            var maxDur = RemainingOrDilated(max);
            var interval = new TimeSpan(maxDur.Ticks / 10);
            var logger = _testState.TestKitSettings.LogTestKitCalls ? _testState.Log : null;
            InternalAwaitCondition(conditionIsFulfilled, maxDur, interval, (format, args) => AssertionsFail(format, args, message), logger);
        }
        
        /// <summary>
        /// <para>Await until the given condition evaluates to <c>true</c> or the timeout
        /// expires, whichever comes first.</para>
        /// <para>If no timeout is given, take it from the innermost enclosing `within`
        /// block (if inside a `within` block) or the value specified in config value "akka.test.single-expect-default". 
        /// The value is <see cref="Dilated(TimeSpan)">dilated</see>, i.e. scaled by the factor 
        /// specified in config value "akka.test.timefactor"..</para>
        /// <para>A call to <paramref name="conditionIsFulfilled"/> is done immediately, then the threads sleep
        /// for about a tenth of the timeout value, before it checks the condition again. This is repeated until
        /// timeout or the condition evaluates to <c>true</c>. To specify another interval, use the overload
        /// <see cref="AwaitCondition(System.Func{bool},System.Nullable{System.TimeSpan},System.Nullable{System.TimeSpan},string)"/>
        /// </para>
        /// </summary>
        /// <param name="conditionIsFulfilled">The condition that must be fulfilled within the duration.</param>
        /// <param name="max">The maximum duration. If undefined, uses the remaining time 
        /// (if inside a `within` block) or the value specified in config value "akka.test.single-expect-default". 
        /// The value is <see cref="Dilated(TimeSpan)">dilated</see>, i.e. scaled by the factor 
        /// specified in config value "akka.test.timefactor".</param>
        /// <param name="message">The message used if the timeout expires.</param>
        public async Task AwaitConditionAsync(Func<bool> conditionIsFulfilled, TimeSpan? max, string message)
        {
            var maxDur = RemainingOrDilated(max);
            var interval = new TimeSpan(maxDur.Ticks / 10);
            var logger = _testState.TestKitSettings.LogTestKitCalls ? _testState.Log : null;
            await InternalAwaitConditionAsync(conditionIsFulfilled, maxDur, interval, (format, args) => AssertionsFail(format, args, message), logger);
        }

        /// <summary>
        /// <para>Await until the given condition evaluates to <c>true</c> or the timeout
        /// expires, whichever comes first.</para>
        /// <para>If no timeout is given, take it from the innermost enclosing `within`
        /// block.</para>
        /// <para>Note that the timeout is <see cref="Dilated(TimeSpan)">dilated</see>, i.e. scaled by the factor 
        /// specified in config value "akka.test.timefactor".</para>
        /// <para>The parameter <paramref name="interval"/> specifies the time between calls to <paramref name="conditionIsFulfilled"/>
        /// Between calls the thread sleeps. If <paramref name="interval"/> is undefined the thread only sleeps 
        /// one time, using the <paramref name="max"/> as duration, and then rechecks the condition and ultimately 
        /// succeeds or fails.</para>
        /// <para>To make sure that tests run as fast as possible, make sure you do not leave this value as undefined,
        /// instead set it to a relatively small value.</para>
        /// </summary>
        /// <param name="conditionIsFulfilled">The condition that must be fulfilled within the duration.</param>
        /// <param name="max">The maximum duration. If undefined, uses the remaining time 
        /// (if inside a `within` block) or the value specified in config value "akka.test.single-expect-default". 
        /// The value is <see cref="Dilated(TimeSpan)">dilated</see>, i.e. scaled by the factor 
        /// specified in config value "akka.test.timefactor".</param>
        /// <param name="interval">The time between calls to <paramref name="conditionIsFulfilled"/> to check
        /// if the condition is fulfilled. Between calls the thread sleeps. If undefined, negative or 
        /// <see cref="Timeout.InfiniteTimeSpan"/>the thread only sleeps one time, using the <paramref name="max"/>, 
        /// and then rechecks the condition and ultimately succeeds or fails.
        /// <para>To make sure that tests run as fast as possible, make sure you do not set this value as undefined,
        /// instead set it to a relatively small value.</para>
        /// </param>
        /// <param name="message">The message used if the timeout expires.</param>
        public void AwaitCondition(Func<bool> conditionIsFulfilled, TimeSpan? max, TimeSpan? interval, string message = null)
        {
            var maxDur = RemainingOrDilated(max);
            var logger = _testState.TestKitSettings.LogTestKitCalls ? _testState.Log : null;
            InternalAwaitCondition(conditionIsFulfilled, maxDur, interval, (format, args) => AssertionsFail(format, args, message), logger);
        }
        
        /// <summary>
        /// <para>Await until the given condition evaluates to <c>true</c> or the timeout
        /// expires, whichever comes first.</para>
        /// <para>If no timeout is given, take it from the innermost enclosing `within`
        /// block.</para>
        /// <para>Note that the timeout is <see cref="Dilated(TimeSpan)">dilated</see>, i.e. scaled by the factor 
        /// specified in config value "akka.test.timefactor".</para>
        /// <para>The parameter <paramref name="interval"/> specifies the time between calls to <paramref name="conditionIsFulfilled"/>
        /// Between calls the thread sleeps. If <paramref name="interval"/> is undefined the thread only sleeps 
        /// one time, using the <paramref name="max"/> as duration, and then rechecks the condition and ultimately 
        /// succeeds or fails.</para>
        /// <para>To make sure that tests run as fast as possible, make sure you do not leave this value as undefined,
        /// instead set it to a relatively small value.</para>
        /// </summary>
        /// <param name="conditionIsFulfilled">The condition that must be fulfilled within the duration.</param>
        /// <param name="max">The maximum duration. If undefined, uses the remaining time 
        /// (if inside a `within` block) or the value specified in config value "akka.test.single-expect-default". 
        /// The value is <see cref="Dilated(TimeSpan)">dilated</see>, i.e. scaled by the factor 
        /// specified in config value "akka.test.timefactor".</param>
        /// <param name="interval">The time between calls to <paramref name="conditionIsFulfilled"/> to check
        /// if the condition is fulfilled. Between calls the thread sleeps. If undefined, negative or 
        /// <see cref="Timeout.InfiniteTimeSpan"/>the thread only sleeps one time, using the <paramref name="max"/>, 
        /// and then rechecks the condition and ultimately succeeds or fails.
        /// <para>To make sure that tests run as fast as possible, make sure you do not set this value as undefined,
        /// instead set it to a relatively small value.</para>
        /// </param>
        /// <param name="message">The message used if the timeout expires.</param>
        public async Task AwaitConditionAsync(Func<bool> conditionIsFulfilled, TimeSpan? max, TimeSpan? interval, string message = null)
        {
            var maxDur = RemainingOrDilated(max);
            var logger = _testState.TestKitSettings.LogTestKitCalls ? _testState.Log : null;
            await InternalAwaitConditionAsync(conditionIsFulfilled, maxDur, interval, (format, args) => AssertionsFail(format, args, message), logger);
        }

        private void AssertionsFail(string format, object[] args, string message = null)
        {
            _assertions.Fail(format + (message ?? ""), args);
        }

        /// <summary>
        /// <para>Await until the given condition evaluates to <c>true</c> or the timeout
        /// expires, whichever comes first. Returns <c>true</c> if the condition was fulfilled.</para>        
        /// <para>The parameter <paramref name="interval"/> specifies the time between calls to <paramref name="conditionIsFulfilled"/>
        /// Between calls the thread sleeps. If <paramref name="interval"/> is not specified or <c>null</c> 100 ms is used.</para>
        /// </summary>
        /// <param name="conditionIsFulfilled">The condition that must be fulfilled within the duration.</param>
        /// <param name="max">The maximum duration.</param>
        /// <param name="interval">Optional. The time between calls to <paramref name="conditionIsFulfilled"/> to check
        /// if the condition is fulfilled. Between calls the thread sleeps. If undefined, 100 ms is used
        /// </param>
        /// <returns>TBD</returns>
        public bool AwaitConditionNoThrow(Func<bool> conditionIsFulfilled, TimeSpan max, TimeSpan? interval = null)
        {
            var intervalDur = interval.GetValueOrDefault(TimeSpan.FromMilliseconds(100));
            return InternalAwaitCondition(conditionIsFulfilled, max, intervalDur, (f, a) => { });
        }
        
        /// <summary>
        /// <para>Await until the given condition evaluates to <c>true</c> or the timeout
        /// expires, whichever comes first. Returns <c>true</c> if the condition was fulfilled.</para>        
        /// <para>The parameter <paramref name="interval"/> specifies the time between calls to <paramref name="conditionIsFulfilled"/>
        /// Between calls the thread sleeps. If <paramref name="interval"/> is not specified or <c>null</c> 100 ms is used.</para>
        /// </summary>
        /// <param name="conditionIsFulfilled">The condition that must be fulfilled within the duration.</param>
        /// <param name="max">The maximum duration.</param>
        /// <param name="interval">Optional. The time between calls to <paramref name="conditionIsFulfilled"/> to check
        /// if the condition is fulfilled. Between calls the thread sleeps. If undefined, 100 ms is used
        /// </param>
        /// <returns>TBD</returns>
        public Task<bool> AwaitConditionNoThrowAsync(Func<bool> conditionIsFulfilled, TimeSpan max, TimeSpan? interval = null)
        {
            var intervalDur = interval.GetValueOrDefault(TimeSpan.FromMilliseconds(100));
            return InternalAwaitConditionAsync(conditionIsFulfilled, max, intervalDur, (f, a) => { });
        }

        /// <summary>
        /// <para>Await until the given condition evaluates to <c>true</c> or the timeout
        /// expires, whichever comes first.</para>
        /// <para>If no timeout is given, take it from the innermost enclosing `within`
        /// block.</para>
        /// <para>Note that the timeout is <see cref="Dilated(TimeSpan)">dilated</see>, i.e. scaled by the factor 
        /// specified in config value "akka.test.timefactor".</para>
        /// <para>The parameter <paramref name="interval"/> specifies the time between calls to <paramref name="conditionIsFulfilled"/>
        /// Between calls the thread sleeps. If <paramref name="interval"/> is undefined the thread only sleeps 
        /// one time, using the <paramref name="max"/> as duration, and then rechecks the condition and ultimately 
        /// succeeds or fails.</para>
        /// <para>To make sure that tests run as fast as possible, make sure you do not leave this value as undefined,
        /// instead set it to a relatively small value.</para>
        /// </summary>
        /// <param name="conditionIsFulfilled">The condition that must be fulfilled within the duration.</param>
        /// <param name="max">The maximum duration. The value is <see cref="Dilated(TimeSpan)">dilated</see>, i.e. 
        /// scaled by the factor specified in config value "akka.test.timefactor".</param>
        /// <param name="interval">The time between calls to <paramref name="conditionIsFulfilled"/> to check
        /// if the condition is fulfilled. Between calls the thread sleeps. If undefined the thread only sleeps 
        /// one time, using the <paramref name="max"/>, and then rechecks the condition and ultimately 
        /// succeeds or fails.
        /// <para>To make sure that tests run as fast as possible, make sure you do not set this value as undefined,
        /// instead set it to a relatively small value.</para>
        /// </param>
        /// <param name="fail">Action that is called when the timeout expired. 
        /// The parameters conforms to <see cref="string.Format(string,object[])"/></param>
        /// <returns>TBD</returns>
        protected static bool InternalAwaitCondition(Func<bool> conditionIsFulfilled, TimeSpan max, TimeSpan? interval, Action<string, object[]> fail)
        {
            return InternalAwaitCondition(conditionIsFulfilled, max, interval, fail, null);
        }
        
        /// <summary>
        /// Async version of <see cref="InternalAwaitCondition(System.Func{bool},System.TimeSpan,System.Nullable{System.TimeSpan},System.Action{string,object[]})"/>
        /// </summary>
        /// <param name="conditionIsFulfilled"></param>
        /// <param name="max"></param>
        /// <param name="interval"></param>
        /// <param name="fail"></param>
        /// <returns></returns>
        protected static Task<bool> InternalAwaitConditionAsync(Func<bool> conditionIsFulfilled, TimeSpan max, TimeSpan? interval, Action<string, object[]> fail)
        {
            return InternalAwaitConditionAsync(conditionIsFulfilled, max, interval, fail, null);
        }

        /// <summary>
        /// <para>Await until the given condition evaluates to <c>true</c> or the timeout
        /// expires, whichever comes first.</para>
        /// <para>If no timeout is given, take it from the innermost enclosing `within`
        /// block.</para>
        /// <para>Note that the timeout is <see cref="Dilated(TimeSpan)">dilated</see>, i.e. scaled by the factor 
        /// specified in config value "akka.test.timefactor".</para>
        /// <para>The parameter <paramref name="interval"/> specifies the time between calls to <paramref name="conditionIsFulfilled"/>
        /// Between calls the thread sleeps. If <paramref name="interval"/> is undefined the thread only sleeps 
        /// one time, using the <paramref name="max"/> as duration, and then rechecks the condition and ultimately 
        /// succeeds or fails.</para>
        /// <para>To make sure that tests run as fast as possible, make sure you do not leave this value as undefined,
        /// instead set it to a relatively small value.</para>
        /// </summary>
        /// <param name="conditionIsFulfilled">The condition that must be fulfilled within the duration.</param>
        /// <param name="max">The maximum duration. The value is <see cref="Dilated(TimeSpan)">dilated</see>, i.e. 
        /// scaled by the factor specified in config value "akka.test.timefactor".</param>
        /// <param name="interval">The time between calls to <paramref name="conditionIsFulfilled"/> to check
        /// if the condition is fulfilled. Between calls the thread sleeps. If undefined the thread only sleeps 
        /// one time, using the <paramref name="max"/>, and then rechecks the condition and ultimately 
        /// succeeds or fails.
        /// <para>To make sure that tests run as fast as possible, make sure you do not set this value as undefined,
        /// instead set it to a relatively small value.</para>
        /// </param>
        /// <param name="fail">Action that is called when the timeout expired. 
        /// The parameters conforms to <see cref="string.Format(string,object[])"/></param>
        /// <param name="logger">If a <see cref="ILoggingAdapter"/> is specified, debug messages will be logged using it. If <c>null</c> nothing will be logged</param>
        /// <returns>TBD</returns>
        protected static bool InternalAwaitCondition(Func<bool> conditionIsFulfilled, TimeSpan max, TimeSpan? interval, Action<string, object[]> fail, ILoggingAdapter logger)
        {
            max.EnsureIsPositiveFinite("max");
            var start = Now;
            var stop = start + max;
            ConditionalLog(logger, "Awaiting condition for {0}.{1}", max, interval.HasValue ? " Will sleep " + interval.Value + " between checks" : "");

            while (!conditionIsFulfilled())
            {
                var now = Now;

                if (now > stop)
                {
                    const string message = "Timeout {0} expired while waiting for condition.";
                    ConditionalLog(logger, message, max);
                    fail(message, new object[] { max });
                    return false;
                }
                var sleepDuration = (stop - now).Min(interval);
                Thread.Sleep(sleepDuration);
            }
            ConditionalLog(logger, "Condition fulfilled after {0}", Now-start);
            return true;
        }
        
        /// <summary>
        /// Async version of <see cref="InternalAwaitCondition(System.Func{bool},System.TimeSpan,System.Nullable{System.TimeSpan},System.Action{string,object[]})"/>
        /// </summary>
        protected static async Task<bool> InternalAwaitConditionAsync(Func<bool> conditionIsFulfilled, TimeSpan max, TimeSpan? interval, Action<string, object[]> fail, ILoggingAdapter logger)
        {
            max.EnsureIsPositiveFinite("max");
            var start = Now;
            var stop = start + max;
            ConditionalLog(logger, "Awaiting condition for {0}.{1}", max, interval.HasValue ? " Will sleep " + interval.Value + " between checks" : "");

            while (!conditionIsFulfilled())
            {
                var now = Now;

                if (now > stop)
                {
                    const string message = "Timeout {0} expired while waiting for condition.";
                    ConditionalLog(logger, message, max);
                    fail(message, new object[] { max });
                    return false;
                }
                var sleepDuration = (stop - now).Min(interval);
                await Task.Delay(sleepDuration);
            }
            ConditionalLog(logger, "Condition fulfilled after {0}", Now-start);
            return true;
        }

        private static void ConditionalLog(ILoggingAdapter logger, string format, params object[] args)
        {
            if (logger != null)
                logger.Debug(format, args);
        }
    }
}
