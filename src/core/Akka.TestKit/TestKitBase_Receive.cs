//-----------------------------------------------------------------------
// <copyright file="TestKitBase_Receive.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
            return FishForMessage(isMessage: isMessage, max: max, hint: hint, allMessages: null);
        }

        /// <summary>
        /// Receives messages until <paramref name="isMessage"/> returns <c>true</c>.
        /// Use it to ignore certain messages while waiting for a specific message.
        /// </summary>
        /// <typeparam name="T">The type of the expected message. Messages of other types are ignored.</typeparam>
        /// <param name="isMessage">The is message.</param>
        /// <param name="max">The maximum.</param>
        /// <param name="hint">The hint.</param>
        /// <param name="allMessages">If null then will be ignored. If not null then will be initially cleared, then filled with all the messages until <paramref name="isMessage"/> returns <c>true</c></param>
        /// <returns>Returns the message that <paramref name="isMessage"/> matched</returns>
        public T FishForMessage<T>(Predicate<T> isMessage, ArrayList allMessages, TimeSpan? max = null, string hint = "")
        {
            var maxValue = RemainingOrDilated(max);
            var end = Now + maxValue;
            allMessages?.Clear();
            while (true)
            {
                var left = end - Now;
                var msg = ReceiveOne(left);
                _assertions.AssertTrue(msg != null, "Timeout ({0}) during fishForMessage{1}", maxValue, string.IsNullOrEmpty(hint) ? "" : ", hint: " + hint);
                if (msg is T msg1 && isMessage(msg1))
                {
                    return msg1;
                }
                allMessages?.Add(msg);
            }
        }

        /// <summary>
        /// Receives messages until <paramref name="max"/>.
        ///
        /// Ignores all messages except for a message of type <typeparamref name="T"/>.
        /// Asserts that all messages are not of the of type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The type that the message is not supposed to be.</typeparam>
        /// <param name="max">Optional. The maximum wait duration. Defaults to <see cref="RemainingOrDefault"/> when unset.</param>
        public async Task FishUntilMessageAsync<T>(TimeSpan? max = null)
        {
            await Task.Run(() =>
            {
                ReceiveWhile<object>(max: max, shouldContinue: x =>
                {
                    _assertions.AssertFalse(x is T, "did not expect a message of type {0}", typeof(T));
                    return true; // please continue receiving, don't stop
                });
            });
        }

        /// <summary>
        /// Waits for a <paramref name="max"/> period of 'radio-silence' limited to a number of <paramref name="maxMessages"/>.
        /// Note: 'radio-silence' definition: period when no messages arrive at.
        /// </summary>
        /// <param name="max">A temporary period of 'radio-silence'.</param>
        /// <param name="maxMessages">The method asserts that <paramref name="maxMessages"/> is never reached.</param>
        /// If set to null then this method will loop for an infinite number of <paramref name="max"/> periods. 
        /// NOTE: If set to null and radio-silence is never reached then this method will never return.  
        /// <returns>Returns all the messages encountered before 'radio-silence' was reached.</returns>
        public async Task<ArrayList> WaitForRadioSilenceAsync(TimeSpan? max = null, uint? maxMessages = null)
        {
            return await Task.Run(() =>
            {
                var messages = new ArrayList();

                for (uint i = 0; ; i++)
                {
                    _assertions.AssertFalse(maxMessages.HasValue && i > maxMessages.Value, $"{nameof(maxMessages)} violated (current iteration: {i}).");

                    var message = ReceiveOne(max: max);

                    if (message == null)
                    {
                        return ArrayList.ReadOnly(messages);
                    }

                    messages.Add(message);
                }
            });
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
            var task = InternalTryReceiveOneAsync(max, cancellationToken, shouldLog).AsTask();
            task.Wait();
            var received = task.Result;
            envelope = received.envelope;
            return received.success;
        }

        private async ValueTask<(bool success, MessageEnvelope envelope)> InternalTryReceiveOneAsync(TimeSpan? max, CancellationToken cancellationToken, bool shouldLog)
        {
            (bool didTake, MessageEnvelope env) take;
            var maxDuration = GetTimeoutOrDefault(max);
            var start = Now;    
            if (maxDuration.IsZero())
            {
                ConditionalLog(shouldLog, "Trying to receive message from TestActor queue. Will not wait.");
                var didTake = _testState.Queue.TryTake(out var item, cancellationToken);
                take = (didTake, item);
            }
            else if (maxDuration.IsPositiveFinite())
            {
                ConditionalLog(shouldLog, "Trying to receive message from TestActor queue within {0}", maxDuration);
                take = await _testState.Queue.TryTakeAsync((int)maxDuration.TotalMilliseconds, cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (maxDuration == Timeout.InfiniteTimeSpan)
            {
                ConditionalLog(shouldLog, "Trying to receive message from TestActor queue. Will wait indefinitely.");
                take = await _testState.Queue.TryTakeAsync(-1, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                ConditionalLog(shouldLog, "Trying to receive message from TestActor queue with negative timeout.");
                //Negative
                take = (false, null);
            }

            _testState.LastWasNoMsg = false;
            if (take.env == null)
                take.env = NullMessageEnvelope.Instance;

            _testState.LastMessage = take.env;

            if (take.didTake)
            {
                ConditionalLog(shouldLog, "Received message after {0}.", Now - start);
                return take;
            }

            ConditionalLog(shouldLog, "Received no message after {0}.{1}", Now - start, cancellationToken.IsCancellationRequested ? " Was canceled" : "");
            return take;
        }

        #region Peek methods
        /// <summary>
        /// Peek one message from the head of the internal queue of the TestActor.
        /// This method blocks the specified duration or until a message
        /// is received. If no message was received, <c>null</c> is returned.
        /// <remarks>This method does NOT automatically scale its Duration parameter using <see cref="Dilated(TimeSpan)" />!</remarks>
        /// </summary>
        /// <param name="max">The maximum duration to wait. 
        /// If <c>null</c> the config value "akka.test.single-expect-default" is used as timeout.
        /// If set to a negative value or <see cref="Timeout.InfiniteTimeSpan"/>, blocks forever.
        /// <remarks>This method does NOT automatically scale its Duration parameter using <see cref="Dilated(TimeSpan)" />!</remarks></param>
        /// <returns>The message if one was received; <c>null</c> otherwise</returns>
        public object PeekOne(TimeSpan? max = null)
        {
            if (InternalTryPeekOne(out var envelope, max, CancellationToken.None, true))
                return envelope.Message;
            return null;
        }

        /// <summary>
        /// Peek one message from the head of the internal queue of the TestActor.
        /// This method blocks until cancelled. 
        /// </summary>
        /// <param name="cancellationToken">A token used to cancel the operation</param>
        /// <returns>The message if one was received; <c>null</c> otherwise</returns>
        public object PeekOne(CancellationToken cancellationToken)
        {
            if (InternalTryPeekOne(out var envelope, Timeout.InfiniteTimeSpan, cancellationToken, true))
                return envelope.Message;
            return null;
        }

        /// <summary>
        /// Peek one message from the head of the internal queue of the TestActor within 
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
        public bool TryPeekOne(out MessageEnvelope envelope, TimeSpan? max = null)
        {
            return InternalTryPeekOne(out envelope, max, CancellationToken.None, true);
        }

        /// <summary>
        /// Peek one message from the head of the internal queue of the TestActor within 
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
        public bool TryPeekOne(out MessageEnvelope envelope, TimeSpan? max, CancellationToken cancellationToken)
        {
            return InternalTryPeekOne(out envelope, max, cancellationToken, true);
        }

        private bool InternalTryPeekOne(out MessageEnvelope envelope, TimeSpan? max, CancellationToken cancellationToken, bool shouldLog)
        {
            var task = InternalTryPeekOneAsync(max, cancellationToken, shouldLog).AsTask();
            task.Wait();
            var received = task.Result;
            envelope = received.envelope;
            return received.success;
        }
        private async ValueTask<(bool success, MessageEnvelope envelope)> InternalTryPeekOneAsync(TimeSpan? max, CancellationToken cancellationToken, bool shouldLog)
        {
            (bool didPeek, MessageEnvelope env) peek;
            var maxDuration = GetTimeoutOrDefault(max);
            var start = Now;

            if (maxDuration.IsZero())
            {
                ConditionalLog(shouldLog, "Trying to peek message from TestActor queue. Will not wait.");
                var peeked = _testState.Queue.TryPeek(out var item);
                peek = (peeked, item);
            }
            else if (maxDuration.IsPositiveFinite())
            {
                ConditionalLog(shouldLog, "Trying to peek message from TestActor queue within {0}", maxDuration);
                peek = await _testState.Queue.TryPeekAsync((int)maxDuration.TotalMilliseconds, cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (maxDuration == Timeout.InfiniteTimeSpan)
            {
                ConditionalLog(shouldLog, "Trying to peek message from TestActor queue. Will wait indefinitely.");
                peek = await _testState.Queue.TryPeekAsync(-1, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                ConditionalLog(shouldLog, "Trying to peek message from TestActor queue with negative timeout.");
                //Negative
                peek = (false, null);
            }

            _testState.LastWasNoMsg = false;

            if (peek.env == null)
                peek.env = NullMessageEnvelope.Instance;

            _testState.LastMessage = peek.env;

            if (peek.didPeek)
            {
                ConditionalLog(shouldLog, "Peeked message after {0}.", Now - start);
                return peek;
            }

            ConditionalLog(shouldLog, "Peeked no message after {0}.{1}", Now - start, cancellationToken.IsCancellationRequested ? " Was canceled" : "");
            return peek;
        }
        #endregion

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
        public IReadOnlyList<T> ReceiveWhile<T>(TimeSpan? max, TimeSpan? idle, Func<object, T> filter, int msgs = int.MaxValue)
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
        public IReadOnlyList<T> ReceiveWhile<T>(Func<object, T> filter, TimeSpan? max = null, TimeSpan? idle = null, int msgs = int.MaxValue)
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
                // Peek the message on the front of the queue
                if (!TryPeekOne(out var envelope, (stop - Now).Min(idleValue)))
                {
                    _testState.LastMessage = msg;
                    break;
                }
                var message = envelope.Message;
                var result = filter(message);
                
                // If the message is accepted by the filter, remove it from the queue
                if (result != null)
                {
                    // This should happen immediately (zero timespan). Something is wrong if this fails.
                    if (!InternalTryReceiveOne(out var removed, TimeSpan.Zero, CancellationToken.None, true))
                        throw new InvalidOperationException("[RACY] Could not dequeue an item from test queue.");
                    
                    // The removed item should be equal to the one peeked previously
                    if(!ReferenceEquals(envelope, removed))
                        throw new InvalidOperationException("[RACY] Dequeued item does not match earlier peeked item");
                    
                    msg = envelope;
                }
                // If the message is rejected by the filter, stop the loop
                else
                {
                    _testState.LastMessage = msg;
                    break;
                }
                
                // Store the accepted message and continue.
                acc.Add(result);
                count++;
            }
            ConditionalLog("Received {0} messages with filter during {1}", count, Now - start);

            _testState.LastWasNoMsg = true;
            return acc;
        }


        /// <summary>
        /// Receive a series of messages.
        /// It will continue to receive messages until the <paramref name="shouldContinue"/> predicate returns <c>false</c> or the idle 
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
        /// <param name="shouldContinue">TBD</param>
        /// <param name="max">TBD</param>
        /// <param name="idle">TBD</param>
        /// <param name="msgs">TBD</param>
        /// <param name="shouldIgnoreOtherMessageTypes">TBD</param>
        /// <returns>TBD</returns>
        public IReadOnlyList<T> ReceiveWhile<T>(Predicate<T> shouldContinue, TimeSpan? max = null, TimeSpan? idle = null, int msgs = int.MaxValue, bool shouldIgnoreOtherMessageTypes = true) where T : class
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
                if (!TryPeekOne(out var envelope, (stop - Now).Min(idleValue)))
                {
                    _testState.LastMessage = msg;
                    break;
                }
                var message = envelope.Message;
                var typedMessage = message as T;
                var shouldStop = false;
                if (typedMessage != null)
                {
                    if (shouldContinue(typedMessage))
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

                // If the message is accepted by the filter, remove it from the queue
                if (!shouldStop)
                {
                    // This should happen immediately (zero timespan). Something is wrong if this fails.
                    if (!InternalTryReceiveOne(out var removed, TimeSpan.Zero, CancellationToken.None, true))
                        throw new InvalidOperationException("[RACY] Could not dequeue an item from test queue.");
                    
                    // The removed item should be equal to the one peeked previously
                    if(!ReferenceEquals(envelope, removed))
                        throw new InvalidOperationException("[RACY] Dequeued item does not match earlier peeked item");
                }
                // If the message is rejected by the filter, stop the loop
                else
                {
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
