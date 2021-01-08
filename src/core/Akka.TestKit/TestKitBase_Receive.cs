//-----------------------------------------------------------------------
// <copyright file="TestKitBase_Receive.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Akka.TestKit.Internal;

namespace Akka.TestKit
{
    /// <summary>
    /// TBD
    /// </summary>
    public abstract partial class TestKitBase
    {
        /// <summary>
        /// Receives messages until <paramref name="isMessage"/> returns <c>true</c>.
        /// Use it to ignore certain messages while waiting for a specific message.
        /// </summary>
        /// <param name="isMessage">The is message.</param>
        /// <param name="max">The maximum.</param>
        /// <param name="hint">The hint.</param>
        /// <returns>Returns the message that <paramref name="isMessage"/> matched</returns>
        public object FishForMessage(Predicate<object> isMessage, TimeSpan? max = null, string hint = "")
        {
            return FishForMessage<object>(isMessage, max, hint);
        }

        /// <summary>
        /// Receives messages until <paramref name="isMessage"/> returns <c>true</c>.
        /// Use it to ignore certain messages while waiting for a specific message.
        /// </summary>
        /// <typeparam name="T">The type of the expected message. Messages of other types are ignored.</typeparam>
        /// <param name="isMessage">The is message.</param>
        /// <param name="max">The maximum.</param>
        /// <param name="hint">The hint.</param>
        /// <returns>Returns the message that <paramref name="isMessage"/> matched</returns>
        public T FishForMessage<T>(Predicate<T> isMessage, TimeSpan? max = null, string hint = "")
        {
            var maxValue = RemainingOrDilated(max);
            var end = Now + maxValue;
            while (true)
            {
                var left = end - Now;
                var msg = ReceiveOne(left);
                _assertions.AssertTrue(msg != null, "Timeout ({0}) during fishForMessage{1}", maxValue, string.IsNullOrEmpty(hint) ? "" : ", hint: " + hint);
                if (msg is T && isMessage((T)msg))
                {
                    return (T)msg;
                }
            }
        }


        /// <summary>
        /// Receive one message from the internal queue of the TestActor.
        /// This method blocks the specified duration or until a message
        /// is received. If no message was received, <c>null</c> is returned.
        /// <remarks>This method does NOT automatically scale its Duration parameter using <see cref="Dilated(TimeSpan)" />!</remarks>
        /// </summary>
        /// <param name="max">The maximum duration to wait. 
        /// If <c>null</c> the config value "akka.test.single-expect-default" is used as timeout.
        /// If set to a negative value or <see cref="Timeout.InfiniteTimeSpan"/>, blocks forever.
        /// <remarks>This method does NOT automatically scale its Duration parameter using <see cref="Dilated(TimeSpan)" />!</remarks></param>
        /// <returns>The message if one was received; <c>null</c> otherwise</returns>
        public object ReceiveOne(TimeSpan? max = null)
        {
            MessageEnvelope envelope;
            if (TryReceiveOne(out envelope, max, CancellationToken.None))
                return envelope.Message;
            return null;
        }

        /// <summary>
        /// Receive one message from the internal queue of the TestActor.
        /// This method blocks until cancelled. 
        /// </summary>
        /// <param name="cancellationToken">A token used to cancel the operation</param>
        /// <returns>The message if one was received; <c>null</c> otherwise</returns>
        public object ReceiveOne(CancellationToken cancellationToken)
        {
            MessageEnvelope envelope;
            if (TryReceiveOne(out envelope, Timeout.InfiniteTimeSpan, cancellationToken))
                return envelope.Message;
            return null;
        }


        /// <summary>
        /// Receive one message from the internal queue of the TestActor within 
        /// the specified duration. The method blocks the specified duration.
        /// <remarks><b>Note!</b> that the returned <paramref name="envelope"/> 
        /// is a <see cref="MessageEnvelope"/> containing the sender and the message.</remarks>
        /// <remarks>This method does NOT automatically scale its Duration parameter using <see cref="Dilated(TimeSpan)" />!</remarks>
        /// </summary>
        /// <param name="envelope">The received envelope.</param>
        /// <param name="max">Optional: The maximum duration to wait. 
        ///     If <c>null</c> the config value "akka.test.single-expect-default" is used as timeout.
        ///     If set to a negative value or <see cref="Timeout.InfiniteTimeSpan"/>, blocks forever.
        ///     <remarks>This method does NOT automatically scale its Duration parameter using <see cref="Dilated(TimeSpan)" />!</remarks></param>
        /// <returns><c>True</c> if a message was received within the specified duration; <c>false</c> otherwise.</returns>
        public bool TryReceiveOne(out MessageEnvelope envelope, TimeSpan? max = null)
        {
            return TryReceiveOne(out envelope, max, CancellationToken.None);
        }

        /// <summary>
        /// Receive one message from the internal queue of the TestActor within 
        /// the specified duration.
        /// <para><c>True</c> is returned if a message existed, and the message 
        /// is returned in <paramref name="envelope" />. The method blocks the 
        /// specified duration, and can be cancelled using the 
        /// <paramref name="cancellationToken" />.
        /// </para> 
        /// <remarks>This method does NOT automatically scale its duration parameter using <see cref="Dilated(TimeSpan)" />!</remarks>
        /// </summary>
        /// <param name="envelope">The received envelope.</param>
        /// <param name="max">The maximum duration to wait. 
        ///     If <c>null</c> the config value "akka.test.single-expect-default" is used as timeout.
        ///     If set to <see cref="Timeout.InfiniteTimeSpan"/>, blocks forever (or until cancelled).
        ///     <remarks>This method does NOT automatically scale its Duration parameter using <see cref="Dilated(TimeSpan)" />!</remarks>
        /// </param>
        /// <param name="cancellationToken">A token used to cancel the operation.</param>
        /// <returns><c>True</c> if a message was received within the specified duration; <c>false</c> otherwise.</returns>
        public bool TryReceiveOne(out MessageEnvelope envelope, TimeSpan? max, CancellationToken cancellationToken)
        {
            return InternalTryReceiveOne(out envelope, max, cancellationToken, true);
        }

        private bool InternalTryReceiveOne(out MessageEnvelope envelope, TimeSpan? max, CancellationToken cancellationToken, bool shouldLog)
        {
            bool didTake;
            var maxDuration = GetTimeoutOrDefault(max);
            var start = Now;
            if (maxDuration.IsZero())
            {
                ConditionalLog(shouldLog, "Trying to receive message from TestActor queue. Will not wait.");
                didTake = _testState.Queue.TryTake(out envelope);
            }
            else if (maxDuration.IsPositiveFinite())
            {
                ConditionalLog(shouldLog, "Trying to receive message from TestActor queue within {0}", maxDuration);
                didTake = _testState.Queue.TryTake(out envelope, (int)maxDuration.TotalMilliseconds, cancellationToken);
            }
            else if (maxDuration == Timeout.InfiniteTimeSpan)
            {
                ConditionalLog(shouldLog, "Trying to receive message from TestActor queue. Will wait indefinitely.");
                didTake = _testState.Queue.TryTake(out envelope, -1, cancellationToken);
            }
            else
            {
                ConditionalLog(shouldLog, "Trying to receive message from TestActor queue with negative timeout.");
                //Negative
                envelope = null;
                didTake = false;
            }

            _testState.LastWasNoMsg = false;
            if (didTake)
            {
                ConditionalLog(shouldLog, "Received message after {0}.", Now - start);
                _testState.LastMessage = envelope;
                return true;
            }
            ConditionalLog(shouldLog, "Received no message after {0}.{1}", Now - start, cancellationToken.IsCancellationRequested ? " Was canceled" : "");
            envelope = NullMessageEnvelope.Instance;
            _testState.LastMessage = envelope;
            return false;
        }


        /// <summary>
        /// Receive a series of messages until the function returns null or the overall
        /// maximum duration is elapsed or expected messages count is reached.
        /// Returns the sequence of messages.
        /// 
        /// Note that it is not an error to hit the `max` duration in this case.
        /// The max duration is scaled by <see cref="Dilated(TimeSpan)"/>
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="max">TBD</param>
        /// <param name="filter">TBD</param>
        /// <param name="msgs">TBD</param>
        /// <returns>TBD</returns>
        public IReadOnlyList<T> ReceiveWhile<T>(TimeSpan? max, Func<object, T> filter, int msgs = int.MaxValue) where T : class
        {
            return ReceiveWhile(filter, max, Timeout.InfiniteTimeSpan, msgs);
        }

        /// <summary>
        /// Receive a series of messages until the function returns null or the idle 
        /// timeout is met or the overall maximum duration is elapsed or 
        /// expected messages count is reached.
        /// Returns the sequence of messages.
        /// 
        /// Note that it is not an error to hit the `max` duration in this case.
        /// The max duration is scaled by <see cref="Dilated(TimeSpan)"/>
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="max">TBD</param>
        /// <param name="idle">TBD</param>
        /// <param name="filter">TBD</param>
        /// <param name="msgs">TBD</param>
        /// <returns>TBD</returns>
        public IReadOnlyList<T> ReceiveWhile<T>(TimeSpan? max, TimeSpan? idle, Func<object, T> filter, int msgs = int.MaxValue) where T : class
        {
            return ReceiveWhile(filter, max, idle, msgs);
        }

        /// <summary>
        /// Receive a series of messages until the function returns null or the idle 
        /// timeout is met (disabled by default) or the overall
        /// maximum duration is elapsed or expected messages count is reached.
        /// Returns the sequence of messages.
        /// 
        /// Note that it is not an error to hit the `max` duration in this case.
        /// The max duration is scaled by <see cref="Dilated(TimeSpan)"/>
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="filter">TBD</param>
        /// <param name="max">TBD</param>
        /// <param name="idle">TBD</param>
        /// <param name="msgs">TBD</param>
        /// <returns>TBD</returns>
        public IReadOnlyList<T> ReceiveWhile<T>(Func<object, T> filter, TimeSpan? max = null, TimeSpan? idle = null, int msgs = int.MaxValue) where T : class
        {
            var maxValue = RemainingOrDilated(max);
            var start = Now;
            var stop = start + maxValue;
            ConditionalLog("Trying to receive {0}messages of type {1} while filter returns non-nulls during {2}", msgs == int.MaxValue ? "" : msgs + " ", typeof(T), maxValue);
            var count = 0;
            var acc = new List<T>();
            var idleValue = idle.GetValueOrDefault(Timeout.InfiniteTimeSpan);
            MessageEnvelope msg = NullMessageEnvelope.Instance;
            while (count < msgs)
            {
                MessageEnvelope envelope;
                if (!TryReceiveOne(out envelope, (stop - Now).Min(idleValue)))
                {
                    _testState.LastMessage = msg;
                    break;
                }
                var message = envelope.Message;
                var result = filter(message);
                if (result == null)
                {
                    _testState.Queue.AddFirst(envelope);  //Put the message back in the queue
                    _testState.LastMessage = msg;
                    break;
                }
                msg = envelope;
                acc.Add(result);
                count++;
            }
            ConditionalLog("Received {0} messages with filter during {1}", count, Now - start);

            _testState.LastWasNoMsg = true;
            return acc;
        }


        /// <summary>
        /// Receive a series of messages.
        /// It will continue to receive messages until the <paramref name="shouldIgnore"/> predicate returns <c>false</c> or the idle 
        /// timeout is met (disabled by default) or the overall
        /// maximum duration is elapsed or expected messages count is reached.
        /// If a message that isn't of type <typeparamref name="T"/> the parameter <paramref name="shouldIgnoreOtherMessageTypes"/> 
        /// declares if the message should be ignored or not.
        /// <para>Returns the sequence of messages.</para>
        /// 
        /// Note that it is not an error to hit the `max` duration in this case.
        /// The max duration is scaled by <see cref="Dilated(TimeSpan)"/>
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="shouldIgnore">TBD</param>
        /// <param name="max">TBD</param>
        /// <param name="idle">TBD</param>
        /// <param name="msgs">TBD</param>
        /// <param name="shouldIgnoreOtherMessageTypes">TBD</param>
        /// <returns>TBD</returns>
        public IReadOnlyList<T> ReceiveWhile<T>(Predicate<T> shouldIgnore, TimeSpan? max = null, TimeSpan? idle = null, int msgs = int.MaxValue, bool shouldIgnoreOtherMessageTypes = true) where T : class
        {
            var start = Now;
            var maxValue = RemainingOrDilated(max);
            var stop = start + maxValue;
            ConditionalLog("Trying to receive {0}messages of type {1} while predicate returns true during {2}. Messages of other types will {3}", msgs == int.MaxValue ? "" : msgs + " ", typeof(T), maxValue, shouldIgnoreOtherMessageTypes ? "be ignored" : "cause this to stop");

            var count = 0;
            var acc = new List<T>();
            var idleValue = idle.GetValueOrDefault(Timeout.InfiniteTimeSpan);
            MessageEnvelope msg = NullMessageEnvelope.Instance;
            while (count < msgs)
            {
                MessageEnvelope envelope;
                if (!TryReceiveOne(out envelope, (stop - Now).Min(idleValue)))
                {
                    _testState.LastMessage = msg;
                    break;
                }
                var message = envelope.Message;
                var typedMessage = message as T;
                var shouldStop = false;
                if (typedMessage != null)
                {
                    if (shouldIgnore(typedMessage))
                    {
                        acc.Add(typedMessage);
                        count++;
                    }
                    else
                    {
                        shouldStop = true;
                    }
                }
                else
                {
                    shouldStop = !shouldIgnoreOtherMessageTypes;
                }
                if (shouldStop)
                {
                    _testState.Queue.AddFirst(envelope);  //Put the message back in the queue
                    _testState.LastMessage = msg;
                    break;
                }
                msg = envelope;
            }
            ConditionalLog("Received {0} messages with filter during {1}", count, Now - start);

            _testState.LastWasNoMsg = true;
            return acc;
        }

        /// <summary>
        /// Receive the specified number of messages using <see cref="RemainingOrDefault"/> as timeout.
        /// </summary>
        /// <param name="numberOfMessages">The number of messages.</param>
        /// <returns>The received messages</returns>
        public IReadOnlyCollection<object> ReceiveN(int numberOfMessages)
        {
            var result = InternalReceiveN(numberOfMessages, RemainingOrDefault, true).ToList();
            return result;
        }

        /// <summary>
        /// Receive the specified number of messages in a row before the given deadline.
        /// The deadline is scaled by "akka.test.timefactor" using <see cref="Dilated"/>.
        /// </summary>
        /// <param name="numberOfMessages">The number of messages.</param>
        /// <param name="max">The timeout scaled by "akka.test.timefactor" using <see cref="Dilated"/>.</param>
        /// <returns>The received messages</returns>
        public IReadOnlyCollection<object> ReceiveN(int numberOfMessages, TimeSpan max)
        {
            max.EnsureIsPositiveFinite("max");
            var dilated = Dilated(max);
            var result = InternalReceiveN(numberOfMessages, dilated, true).ToList();
            return result;
        }

        private IEnumerable<object> InternalReceiveN(int numberOfMessages, TimeSpan max, bool shouldLog)
        {
            var start = Now;
            var stop = max + start;
            ConditionalLog(shouldLog, "Trying to receive {0} messages during {1}.", numberOfMessages, max);
            for (int i = 0; i < numberOfMessages; i++)
            {
                var timeout = stop - Now;
                var o = ReceiveOne(timeout);
                var condition = o != null;
                if (!condition)
                {
                    var elapsed = Now - start;
                    const string failMessage = "Timeout ({0}) while expecting {1} messages. Only got {2} after {3}.";
                    ConditionalLog(shouldLog, failMessage, max, numberOfMessages, i, elapsed);
                    _assertions.AssertTrue(false, failMessage, max, numberOfMessages, i, elapsed);
                }
                yield return o;
            }
            ConditionalLog(shouldLog, "Received {0} messages during {1}.", numberOfMessages, Now - start);
        }
    }
}
