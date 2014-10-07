using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit.Internal;

namespace Akka.TestKit
{
    public abstract partial class TestKitBase
    {
        /// <summary>
        /// Receive one message from the test actor and assert that it is of the specified type.
        /// Wait time is bounded by the given duration, if specified; otherwise
        /// wait time is bounded by remaining time for execution of the innermost enclosing 'within'
        /// block, if inside a 'within' block; otherwise by the config value 
        /// "akka.test.single-expect-default".
        /// </summary>
        public T ExpectMsg<T>(TimeSpan? duration=null, string hint = null)
        {
            return InternalExpectMsg<T>(RemainingOrDilated(duration), (Action<T>) null, hint);
        }

        /// <summary>
        /// Receive one message of the specified type from the test actor and assert that it
        /// equals the <paramref name="message"/>.
        /// Wait time is bounded by the given duration, if specified; otherwise
        /// wait time is bounded by remaining time for execution of the innermost enclosing 'within'
        /// block, if inside a 'within' block; otherwise by the config value 
        /// "akka.test.single-expect-default".
        /// </summary>
        public T ExpectMsg<T>(T message, TimeSpan? timeout=null, string hint = null)
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
        public T ExpectMsg<T>(Predicate<T> isMessage, TimeSpan? timeout = null, string hint = null)
        {
            return InternalExpectMsg<T>(RemainingOrDilated(RemainingOrDilated(timeout)), (m, sender) =>
            {
                if(isMessage != null)
                    AssertPredicateIsTrueForMessage(isMessage, m, hint);
            }, hint);
        }


        /// <summary>
        /// Receive one message of the specified type from the test actor and calls the optional 
        /// action that performs extra assertions.
        /// Use this variant to implement more complicated or conditional processing.
        /// 
        /// Wait time is bounded by the given duration, if specified; otherwise
        /// wait time is bounded by remaining time for execution of the innermost enclosing 'within'
        /// block, if inside a 'within' block; otherwise by the config value 
        /// "akka.test.single-expect-default".
        /// </summary>
        public T ExpectMsg<T>(Action<T> assert, TimeSpan? timeout = null, string hint = null)
        {
            return InternalExpectMsg(RemainingOrDilated(timeout), assert, hint);
        }


        /// <summary>
        /// Receive one message from the test actor and assert that it is equal to the expected value,
        /// according to the specified comparer function.
        /// 
        /// Wait time is bounded by remaining time for execution of the innermost enclosing 'within'
        /// block, if inside a 'within' block; otherwise by the config value 
        /// "akka.test.single-expect-default".
        /// </summary>
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
        public Terminated ExpectTerminated(ActorRef target, TimeSpan? timeout = null, string hint = null)
        {
            var msg = string.Format("Terminated {0}. {1}", target.Path, hint ?? "");
            return InternalExpectMsg<Terminated>(RemainingOrDilated(timeout), terminated => _assertions.AssertEqual(target, terminated.ActorRef, msg), msg);
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
            return (T) envelope.Message;
        }

        private T InternalExpectMsg<T>(TimeSpan? timeout, Action<T> msgAssert, Action<ActorRef> senderAssert, string hint)
        {
            var envelope = InternalExpectMsgEnvelope<T>(timeout, msgAssert, senderAssert, hint);
            return (T)envelope.Message;
        }

        private T InternalExpectMsg<T>(TimeSpan? timeout, Action<T, ActorRef> assert, string hint)
        {
            var envelope = InternalExpectMsgEnvelope<T>(timeout, assert, hint);
            return (T)envelope.Message;
        }

        private MessageEnvelope InternalExpectMsgEnvelope<T>(TimeSpan? timeout, Action<T> msgAssert, Action<ActorRef> senderAssert, string hint)
        {
            msgAssert=msgAssert ?? (m=>{});
            senderAssert=senderAssert ?? (sender=>{});
            Action<T, ActorRef> combinedAssert = (m, sender) =>
            {
                senderAssert(sender);
                msgAssert(m);
            };
            var envelope = InternalExpectMsgEnvelope<T>(timeout, combinedAssert, hint);
            return envelope;
        }

        private MessageEnvelope InternalExpectMsgEnvelope<T>(TimeSpan? timeout, Action<T, ActorRef> assert, string hint)
        {
            MessageEnvelope envelope;
            var success = TryReceiveOne(out envelope, timeout);

            if (!success)
                _assertions.Fail("Timeout {0} while waiting for a message of type {1} {2}", GetTimeoutOrDefault(timeout), typeof (T), hint ?? "");

            var message = envelope.Message;
            var sender = envelope.Sender;
            _assertions.AssertTrue(message is T, "expected a message of type {0}, but received {2} (type {1}) instead {3} from {4}", typeof (T), message.GetType(), message, hint ?? "", sender);
            var tMessage = (T) message;
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
        public void ExpectNoMsg(TimeSpan duration)
        {
            InternalExpectNoMsg(Dilated(duration));
        }

        private void InternalExpectNoMsg(TimeSpan duration)
        {
            MessageEnvelope t;
            bool success = TryReceiveOne(out t, duration);
            _assertions.AssertFalse(success, string.Format("Expected no messages during the duration, instead we received {0}", t));
        }

        /// <summary>
        /// Receive a number of messages from the test actor matching the given
        /// number of objects and assert that for each given object one is received
        /// which equals it and vice versa. This construct is useful when the order in
        /// which the objects are received is not fixed. Wait time is bounded by 
        /// <see cref="RemainingOrDefault"/> as duration, with an assertion exception being thrown in case of timeout.
        /// 
        /// <pre>
        ///   dispatcher.Tell(SomeWork1())
        ///   dispatcher.Tell(SomeWork2())
        ///   ExpectMsgAllOf(TimeSpan.FromSeconds(1), Result1(), Result2())
        /// </pre>
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
        /// <pre>
        ///   dispatcher.Tell(SomeWork1())
        ///   dispatcher.Tell(SomeWork2())
        ///   ExpectMsgAllOf(TimeSpan.FromSeconds(1), Result1(), Result2())
        /// </pre>
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

        private IReadOnlyCollection<T> InternalExpectMsgAllOf<T>(TimeSpan max, IReadOnlyCollection<T> messages, Func<T, T, bool> areEqual = null)
        {
            areEqual = areEqual ?? ((x, y) => Equals(x, y));
            var receivedMessages = InternalReceiveN(messages.Count, max).ToList();
            var missing = messages.Where(m => !receivedMessages.Any(r => r is T && areEqual((T)r, m))).ToList();
            var unexpected = receivedMessages.Where(r => !messages.Any(m => r is T && areEqual((T)r, m))).ToList();
            CheckMissingAndUnexpected(missing, unexpected, "not found", "found unexpected");
            return receivedMessages.Cast<T>().ToList();
        }


        private void CheckMissingAndUnexpected<TMissing, TUnexpected>(IReadOnlyCollection<TMissing> missing, IReadOnlyCollection<TUnexpected> unexpected, string missingMessage, string unexpectedMessage)
        {
            var missingIsEmpty = missing.Count == 0;
            var unexpectedIsEmpty = unexpected.Count == 0;
            _assertions.AssertTrue(missingIsEmpty && unexpectedIsEmpty,
                (missingIsEmpty ? "" : missingMessage + " [" + string.Join(", ", missing) + "] ") +
            (unexpectedIsEmpty ? "" : unexpectedMessage + " [" + string.Join(", ", unexpected) + "]"));

        }
    }
}