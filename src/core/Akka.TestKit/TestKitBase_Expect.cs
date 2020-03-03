//-----------------------------------------------------------------------
// <copyright file="TestKitBase_Expect.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.TestKit.Internal;
using Akka.Util;

namespace Akka.TestKit
{
    /// <summary>
    /// TBD
    /// </summary>
    public abstract partial class TestKitBase
    {
        /// <summary>
        /// Receive one message from the test actor and assert that it is of the specified type.
        /// Wait time is bounded by the given duration, if specified; otherwise
        /// wait time is bounded by remaining time for execution of the innermost enclosing 'within'
        /// block, if inside a 'within' block; otherwise by the config value 
        /// "akka.test.single-expect-default".
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="duration">TBD</param>
        /// <param name="hint">TBD</param>
        /// <returns>TBD</returns>
        public T ExpectMsg<T>(TimeSpan? duration = null, string hint = null)
        {
            return InternalExpectMsg<T>(RemainingOrDilated(duration), (Action<T>)null, hint);
        }

        /// <summary>
        /// Receive one message of the specified type from the test actor and assert that it
        /// equals the <paramref name="message"/>.
        /// Wait time is bounded by the given duration, if specified; otherwise
        /// wait time is bounded by remaining time for execution of the innermost enclosing 'within'
        /// block, if inside a 'within' block; otherwise by the config value 
        /// "akka.test.single-expect-default".
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="message">TBD</param>
        /// <param name="timeout">TBD</param>
        /// <param name="hint">TBD</param>
        /// <returns>TBD</returns>
        public T ExpectMsg<T>(T message, TimeSpan? timeout = null, string hint = null)
        {
            return InternalExpectMsg<T>(RemainingOrDilated(timeout), m => _assertions.AssertEqual(message, m), hint);
        }

        /// <summary>
        /// Receive one message of the specified type from the test actor and assert that the given
        /// predicate accepts it.
        /// Use this variant to implement more complicated or conditional processing.
        /// 
        /// Wait time is bounded by the given duration, if specified; otherwise
        /// wait time is bounded by remaining time for execution of the innermost enclosing 'within'
        /// block, if inside a 'within' block; otherwise by the config value 
        /// "akka.test.single-expect-default".
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="isMessage">TBD</param>
        /// <param name="timeout">TBD</param>
        /// <param name="hint">TBD</param>
        /// <returns>TBD</returns>
        public T ExpectMsg<T>(Predicate<T> isMessage, TimeSpan? timeout = null, string hint = null)
        {
            return InternalExpectMsg<T>(RemainingOrDilated(RemainingOrDilated(timeout)), (m, sender) =>
            {
                if (isMessage != null)
                    AssertPredicateIsTrueForMessage(isMessage, m, hint);
            }, hint);
        }


        /// <summary>
        /// Receive one message of the specified type from the test actor and calls the 
        /// action that performs extra assertions.
        /// Use this variant to implement more complicated or conditional processing.
        /// 
        /// Wait time is bounded by the given duration, if specified; otherwise
        /// wait time is bounded by remaining time for execution of the innermost enclosing 'within'
        /// block, if inside a 'within' block; otherwise by the config value 
        /// "akka.test.single-expect-default".
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="assert">TBD</param>
        /// <param name="timeout">TBD</param>
        /// <param name="hint">TBD</param>
        /// <returns>TBD</returns>
        public T ExpectMsg<T>(Action<T> assert, TimeSpan? timeout = null, string hint = null)
        {
            return InternalExpectMsg(RemainingOrDilated(timeout), assert, hint);
        }

        /// <summary>
        /// Receive one message of the specified type from the test actor and assert that the given
        /// predicate accepts it.
        /// Use this variant to implement more complicated or conditional processing.
        /// 
        /// Wait time is bounded by the given duration, if specified; otherwise
        /// wait time is bounded by remaining time for execution of the innermost enclosing 'within'
        /// block, if inside a 'within' block; otherwise by the config value 
        /// "akka.test.single-expect-default".
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="isMessageAndSender">TBD</param>
        /// <param name="timeout">TBD</param>
        /// <param name="hint">TBD</param>
        /// <returns>TBD</returns>
        public T ExpectMsg<T>(Func<T, IActorRef, bool> isMessageAndSender, TimeSpan? timeout = null, string hint = null)
        {
            return InternalExpectMsg<T>(RemainingOrDilated(RemainingOrDilated(timeout)), (m, sender) =>
            {
                _assertions.AssertTrue(isMessageAndSender(m, sender), "Got a message of the expected type <{2}> from {4}. Also expected {0} but the message {{{1}}} of type <{3}> did not match", hint ?? "the predicate to return true", m, typeof(T).FullName, m.GetType().FullName, sender);
            }, hint);
        }

        /// <summary>
        /// Receive one message of the specified type from the test actor calls the 
        /// action that performs extra assertions.
        /// Use this variant to implement more complicated or conditional processing.
        /// 
        /// Wait time is bounded by the given duration, if specified; otherwise
        /// wait time is bounded by remaining time for execution of the innermost enclosing 'within'
        /// block, if inside a 'within' block; otherwise by the config value 
        /// "akka.test.single-expect-default".
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="assertMessageAndSender">TBD</param>
        /// <param name="timeout">TBD</param>
        /// <param name="hint">TBD</param>
        /// <returns>TBD</returns>
        public T ExpectMsg<T>(Action<T, IActorRef> assertMessageAndSender, TimeSpan? timeout = null, string hint = null)
        {
            return InternalExpectMsg<T>(RemainingOrDilated(RemainingOrDilated(timeout)), assertMessageAndSender, hint);
        }


        /// <summary>
        /// Receive one message from the test actor and assert that it is equal to the expected value,
        /// according to the specified comparer function.
        /// 
        /// Wait time is bounded by remaining time for execution of the innermost enclosing 'within'
        /// block, if inside a 'within' block; otherwise by the config value 
        /// "akka.test.single-expect-default".
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="expected">TBD</param>
        /// <param name="comparer">TBD</param>
        /// <param name="timeout">TBD</param>
        /// <param name="hint">TBD</param>
        /// <returns>TBD</returns>
        public T ExpectMsg<T>(T expected, Func<T, T, bool> comparer, TimeSpan? timeout = null, string hint = null)
        {
            return InternalExpectMsg<T>(RemainingOrDilated(timeout), actual => _assertions.AssertEqual(expected, actual, comparer, hint), hint);
        }

        /// <summary>
        /// Receive one message from the test actor and assert that it is the Terminated message of the given ActorRef.
        /// 
        /// Wait time is bounded by the given duration, if specified; otherwise
        /// wait time is bounded by remaining time for execution of the innermost enclosing 'within'
        /// block, if inside a 'within' block; otherwise by the config value 
        /// "akka.test.single-expect-default".
        /// </summary>
        /// <param name="target">TBD</param>
        /// <param name="timeout">TBD</param>
        /// <param name="hint">TBD</param>
        /// <returns>TBD</returns>
        public Terminated ExpectTerminated(IActorRef target, TimeSpan? timeout = null, string hint = null)
        {
            var msg = string.Format("Terminated {0}. {1}", target, hint ?? "");
            return InternalExpectMsg<Terminated>(RemainingOrDilated(timeout), terminated =>
            {
                _assertions.AssertEqual(target, terminated.ActorRef, msg);
            }, msg);
        }


        private void AssertPredicateIsTrueForMessage<T>(Predicate<T> isMessage, T m, string hint)
        {
            _assertions.AssertTrue(isMessage(m), "Got a message of the expected type <{2}>. Also expected {0} but the message {{{1}}} of type <{3}> did not match", hint ?? "the predicate to return true", m, typeof(T).FullName, m.GetType().FullName);
        }

        /// <summary>
        /// Receive one message of the specified type from the test actor and calls the optional 
        /// action that performs extra assertions. Wait time is bounded by the given duration.
        /// Use this variant to implement more complicated or conditional processing.
        /// </summary>
        private T InternalExpectMsg<T>(TimeSpan? timeout, Action<T> msgAssert, string hint)
        {
            var envelope = InternalExpectMsgEnvelope<T>(timeout, msgAssert, null, hint);
            return (T)envelope.Message;
        }

        private T InternalExpectMsg<T>(TimeSpan? timeout, Action<T> msgAssert, Action<IActorRef> senderAssert, string hint)
        {
            var envelope = InternalExpectMsgEnvelope<T>(timeout, msgAssert, senderAssert, hint);
            return (T)envelope.Message;
        }

        private T InternalExpectMsg<T>(TimeSpan? timeout, Action<T, IActorRef> assert, string hint)
        {
            var envelope = InternalExpectMsgEnvelope<T>(timeout, assert, hint);
            return (T)envelope.Message;
        }

        private MessageEnvelope InternalExpectMsgEnvelope<T>(TimeSpan? timeout, Action<T> msgAssert, Action<IActorRef> senderAssert, string hint)
        {
            msgAssert = msgAssert ?? (m => { });
            senderAssert = senderAssert ?? (sender => { });
            Action<T, IActorRef> combinedAssert = (m, sender) =>
            {
                senderAssert(sender);
                msgAssert(m);
            };
            var envelope = InternalExpectMsgEnvelope<T>(timeout, combinedAssert, hint);
            return envelope;
        }

        private MessageEnvelope InternalExpectMsgEnvelope<T>(TimeSpan? timeout, Action<T, IActorRef> assert, string hint, bool shouldLog=false)
        {
            MessageEnvelope envelope;
            ConditionalLog(shouldLog, "Expecting message of type {0}. {1}", typeof(T), hint);
            var success = TryReceiveOne(out envelope, timeout);

            if (!success)
            {
                const string failMessage = "Failed: Timeout {0} while waiting for a message of type {1} {2}";
                ConditionalLog(shouldLog, failMessage, GetTimeoutOrDefault(timeout), typeof(T), hint ?? "");
                _assertions.Fail(failMessage, GetTimeoutOrDefault(timeout), typeof(T), hint ?? "");
            }

            var message = envelope.Message;
            var sender = envelope.Sender;
            var messageIsT = message is T;
            if(!messageIsT)
            {
                const string failMessage2 = "Failed: Expected a message of type {0}, but received {{{2}}} (type {1}) instead {3} from {4}";
                _assertions.Fail(failMessage2, typeof(T), message.GetType(), message, hint ?? "", sender);
                ConditionalLog(shouldLog, failMessage2, typeof(T), message.GetType(), message, hint ?? "", sender);
            }
            var tMessage = (T)message;
            if (assert != null)
                assert(tMessage, sender);
            return envelope;
        }


        /// <summary>
        /// Assert that no message is received.
        /// 
        /// Wait time is bounded by remaining time for execution of the innermost enclosing 'within'
        /// block, if inside a 'within' block; otherwise by the config value 
        /// "akka.test.single-expect-default".
        /// </summary>
        public void ExpectNoMsg()
        {
            InternalExpectNoMsg(RemainingOrDefault);
        }

        /// <summary>
        /// Assert that no message is received for the specified time.
        /// </summary>
        /// <param name="duration">TBD</param>
        public void ExpectNoMsg(TimeSpan duration)
        {
            InternalExpectNoMsg(Dilated(duration));
        }

        /// <summary>
        /// Assert that no message is received for the specified time in milliseconds.
        /// </summary>
        /// <param name="milliseconds">TBD</param>
        public void ExpectNoMsg(int milliseconds)
        {
            ExpectNoMsg(TimeSpan.FromMilliseconds(milliseconds));
        }

        private void InternalExpectNoMsg(TimeSpan duration)
        {
            MessageEnvelope t;
            var start = Now;
            ConditionalLog("Expecting no messages during {0}", duration);

            bool didReceiveMessage = InternalTryReceiveOne(out t, duration, CancellationToken.None, false);
            if (didReceiveMessage)
            {
                const string failMessage = "Failed: Expected no messages during {0}, instead we received {1} after {2}";
                var elapsed = Now - start;
                ConditionalLog(failMessage, duration,t, elapsed);
                _assertions.Fail(failMessage,duration, t, elapsed);
            }
        }

        /// <summary>
        /// Receive a message from the test actor and assert that it equals 
        /// one of the given <paramref name="messages"/>. Wait time is bounded by 
        /// <see cref="RemainingOrDefault"/> as duration, with an assertion exception being thrown in case of timeout.
        /// </summary>
        /// <typeparam name="T">The type of the messages</typeparam>
        /// <param name="messages">The messages.</param>
        /// <returns>The received messages in received order</returns>
        public T ExpectMsgAnyOf<T>(params T[] messages)
        {
            return InternalExpectMsgAnyOf<T>(RemainingOrDefault, messages);
        }

        private T InternalExpectMsgAnyOf<T>(TimeSpan max, T[] messages)
        {
            var o = ReceiveOne(max);
            _assertions.AssertTrue(o != null, string.Format("Timeout {0} during waiting for ExpectMsgAnyOf waiting for ({1})", max, StringFormat.SafeJoin(",", messages)));
            _assertions.AssertTrue(messages.Contains((T)o), "ExpectMsgAnyOf found unexpected {0}", o);

            return (T)o;
        }

        /// <summary>
        /// Receive a number of messages from the test actor matching the given
        /// number of objects and assert that for each given object one is received
        /// which equals it and vice versa. This construct is useful when the order in
        /// which the objects are received is not fixed. Wait time is bounded by 
        /// <see cref="RemainingOrDefault"/> as duration, with an assertion exception being thrown in case of timeout.
        /// 
        /// <code>
        ///   dispatcher.Tell(SomeWork1())
        ///   dispatcher.Tell(SomeWork2())
        ///   ExpectMsgAllOf(TimeSpan.FromSeconds(1), Result1(), Result2())
        /// </code>
        /// </summary>
        /// <typeparam name="T">The type of the messages</typeparam>
        /// <param name="messages">The messages.</param>
        /// <returns>The received messages in received order</returns>
        public IReadOnlyCollection<T> ExpectMsgAllOf<T>(params T[] messages)
        {
            return InternalExpectMsgAllOf(RemainingOrDefault, messages);
        }


        /// <summary>
        /// Receive a number of messages from the test actor matching the given
        /// number of objects and assert that for each given object one is received
        /// which equals it and vice versa. This construct is useful when the order in
        /// which the objects are received is not fixed. Wait time is bounded by the
        /// given duration, with an assertion exception being thrown in case of timeout.
        /// 
        /// <code>
        ///   dispatcher.Tell(SomeWork1())
        ///   dispatcher.Tell(SomeWork2())
        ///   ExpectMsgAllOf(TimeSpan.FromSeconds(1), Result1(), Result2())
        /// </code>
        /// The deadline is scaled by "akka.test.timefactor" using <see cref="Dilated"/>.
        /// </summary>
        /// <typeparam name="T">The type of the messages</typeparam>
        /// <param name="max">The deadline. The deadline is scaled by "akka.test.timefactor" using <see cref="Dilated"/>.</param>
        /// <param name="messages">The messages.</param>
        /// <returns>The received messages in received order</returns>
        public IReadOnlyCollection<T> ExpectMsgAllOf<T>(TimeSpan max, params T[] messages)
        {
            max.EnsureIsPositiveFinite("max");
            var dilated = Dilated(max);
            return InternalExpectMsgAllOf(dilated, messages);
        }

        private IReadOnlyCollection<T> InternalExpectMsgAllOf<T>(TimeSpan max, IReadOnlyCollection<T> messages, Func<T, T, bool> areEqual = null, bool shouldLog=false)
        {
            ConditionalLog(shouldLog, "Expecting {0} messages during {1}", messages.Count, max);
            areEqual = areEqual ?? ((x, y) => Equals(x, y));
            var start = Now;
            var receivedMessages = InternalReceiveN(messages.Count, max, shouldLog).ToList();
            var missing = messages.Where(m => !receivedMessages.Any(r => r is T && areEqual((T)r, m))).ToList();
            var unexpected = receivedMessages.Where(r => !messages.Any(m => r is T && areEqual((T)r, m))).ToList();
            CheckMissingAndUnexpected(missing, unexpected, "not found", "found unexpected", shouldLog, string.Format("Expected {0} messages during {1}. Failed after {2}. ", messages.Count, max, Now-start));
            return receivedMessages.Cast<T>().ToList();
        }


        private void CheckMissingAndUnexpected<TMissing, TUnexpected>(IReadOnlyCollection<TMissing> missing, IReadOnlyCollection<TUnexpected> unexpected, string missingMessage, string unexpectedMessage, bool shouldLog, string hint)
        {
            var missingIsEmpty = missing.Count == 0;
            var unexpectedIsEmpty = unexpected.Count == 0;
            var bothEmpty = missingIsEmpty && unexpectedIsEmpty;
            if(!bothEmpty)
            {
                var msg = (missingIsEmpty ? "" : missingMessage + " [" + string.Join(", ", missing) + "] ") +
                             (unexpectedIsEmpty ? "" : unexpectedMessage + " [" + string.Join(", ", unexpected) + "]");
                ConditionalLog(shouldLog, "{0} {1}", hint,msg);
                _assertions.Fail(msg);
            }
        }

        private void ConditionalLog(bool shouldLog, string format, params object[] args)
        {
            if (shouldLog)
                ConditionalLog(format, args);
        }

        private void ConditionalLog(string format, params object[] args)
        {
            if (_testState.TestKitSettings.LogTestKitCalls)
            {
                _testState.Log.Debug(format, args);
            }
        }
    }
}
