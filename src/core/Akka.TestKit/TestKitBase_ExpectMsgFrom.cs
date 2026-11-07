//-----------------------------------------------------------------------
// <copyright file="TestKitBase_ExpectMsgFrom.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Nito.AsyncEx.Synchronous;

namespace Akka.TestKit
{
    /// <summary>
    /// Message expectations that also verify the sender of each received message.
    /// </summary>
    public abstract partial class TestKitBase
    {
        /// <summary>
        /// Receive one message from the test actor and assert that it is of the specified type
        /// and was sent by the specified sender
        /// Wait time is bounded by the given duration if specified.
        /// If not specified, wait time is bounded by remaining time for execution of the innermost enclosing 'within'
        /// block, if inside a 'within' block; otherwise by the config value 
        /// "akka.test.single-expect-default".
        /// </summary>
        /// <typeparam name="T">The expected message type.</typeparam>
        /// <param name="sender">The actor reference expected to have sent the message.</param>
        /// <param name="duration">The maximum wait duration, or null to use the current test timeout.</param>
        /// <param name="hint">Additional context to include in an assertion failure.</param>
        /// <param name="cancellationToken"></param>
        /// <returns>The received message from <paramref name="sender"/>.</returns>
        public T ExpectMsgFrom<T>(
            IActorRef sender,
            [AutoDilate] TimeSpan? duration = null,
            string hint = null,
            CancellationToken cancellationToken = default)
        {
            return ExpectMsgFromAsync<T>(
                    sender: sender,
                    duration: duration,
                    hint: hint,
                    cancellationToken: cancellationToken)
                .AsTask().WaitAndUnwrapException();
        }

        public ValueTask<T> ExpectMsgFromAsync<T>(
            IActorRef sender,
            [AutoDilate] TimeSpan? duration = null,
            string hint = null,
            CancellationToken cancellationToken = default)
        {
            return InternalExpectMsgAsync<T>(
                    timeout: RemainingOrDilated(duration),
                    msgAssert: null,
                    senderAssert: s => _assertions.AssertEqual(
                        expected: sender,
                        actual: s,
                        format: FormatWrongSenderMessage(s,sender.ToString(), hint)),
                    hint: null,
                    cancellationToken: cancellationToken);
        }

        /// <summary>
        /// Receive one message of the specified type from the test actor and assert that it
        /// equals the <paramref name="message"/> and was sent by the specified sender
        /// Wait time is bounded by the given duration if specified.
        /// If not specified, wait time is bounded by remaining time for execution of the innermost enclosing 'within'
        /// block, if inside a 'within' block; otherwise by the config value 
        /// "akka.test.single-expect-default".
        /// </summary>
        /// <typeparam name="T">The expected message type.</typeparam>
        /// <param name="sender">The actor reference expected to have sent the message.</param>
        /// <param name="message">The expected message value.</param>
        /// <param name="timeout">The maximum wait duration, or null to use the current test timeout.</param>
        /// <param name="hint">Additional context to include in an assertion failure.</param>
        /// <param name="cancellationToken"></param>
        /// <returns>The received message after it is verified against <paramref name="message"/> and <paramref name="sender"/>.</returns>
        public T ExpectMsgFrom<T>(
            IActorRef sender,
            T message,
            [AutoDilate] TimeSpan? timeout = null,
            string hint = null,
            CancellationToken cancellationToken = default)
        {
            return ExpectMsgFromAsync(
                    sender: sender,
                    message: message,
                    timeout: timeout,
                    hint: hint,
                    cancellationToken: cancellationToken)
                .AsTask().WaitAndUnwrapException();
        }

        public ValueTask<T> ExpectMsgFromAsync<T>(
            IActorRef sender,
            T message,
            [AutoDilate] TimeSpan? timeout = null,
            string hint = null,
            CancellationToken cancellationToken = default)
        {
            return InternalExpectMsgAsync<T>(
                    timeout: RemainingOrDilated(timeout),
                    msgAssert: m => _assertions.AssertEqual(message, m),
                    senderAssert: s => _assertions.AssertEqual(
                        expected: sender,
                        actual: s,
                        format: FormatWrongSenderMessage(s, sender.ToString(), hint)),
                    hint: hint,
                    cancellationToken: cancellationToken);
        }

        /// <summary>
        /// Receive one message of the specified type from the test actor and assert that the given
        /// predicate accepts it and was sent by the specified sender
        /// Wait time is bounded by the given duration if specified.
        /// If not specified, wait time is bounded by remaining time for execution of the innermost enclosing 'within'
        /// block, if inside a 'within' block; otherwise by the config value 
        /// "akka.test.single-expect-default".
        /// Use this variant to implement more complicated or conditional processing.
        /// </summary>
        /// <typeparam name="T">The expected message type.</typeparam>
        /// <param name="sender">The actor reference expected to have sent the message.</param>
        /// <param name="isMessage">The predicate that must accept the received message.</param>
        /// <param name="timeout">The maximum wait duration, or null to use the current test timeout.</param>
        /// <param name="hint">Additional context to include in an assertion failure.</param>
        /// <param name="cancellationToken"></param>
        /// <returns>The received message after it is verified by <paramref name="isMessage"/> and <paramref name="sender"/>.</returns>
        public T ExpectMsgFrom<T>(
            IActorRef sender,
            Predicate<T> isMessage,
            [AutoDilate] TimeSpan? timeout = null,
            string hint = null,
            CancellationToken cancellationToken = default)
        {
            return ExpectMsgFromAsync(
                    sender: sender,
                    isMessage: isMessage,
                    timeout: timeout,
                    hint: hint,
                    cancellationToken: cancellationToken)
                .AsTask().WaitAndUnwrapException();
        }

        public ValueTask<T> ExpectMsgFromAsync<T>(
            IActorRef sender,
            Predicate<T> isMessage,
            [AutoDilate] TimeSpan? timeout = null,
            string hint = null,
            CancellationToken cancellationToken = default)
        {
            return InternalExpectMsgAsync<T>(
                    timeout: RemainingOrDilated(timeout),
                    assert: (m, s) =>
                    {
                        _assertions.AssertEqual(sender, s, FormatWrongSenderMessage(s, sender.ToString(), hint));
                        if(isMessage != null)
                            AssertPredicateIsTrueForMessage(isMessage, m, hint);
                    },
                    hint: hint,
                    cancellationToken: cancellationToken);
        }

        /// <summary>
        /// Receive one message of the specified type from the test actor and assert that the given
        /// predicate accepts it and was sent by a sender that matches the <paramref name="isSender"/> predicate.
        /// Wait time is bounded by the given duration if specified.
        /// If not specified, wait time is bounded by remaining time for execution of the innermost enclosing 'within'
        /// block, if inside a 'within' block; otherwise by the config value 
        /// "akka.test.single-expect-default".
        /// Use this variant to implement more complicated or conditional processing.
        /// </summary>
        /// <typeparam name="T">The expected message type.</typeparam>
        /// <param name="isSender">The predicate that must accept the received message's sender.</param>
        /// <param name="isMessage">The predicate that must accept the received message.</param>
        /// <param name="timeout">The maximum wait duration, or null to use the current test timeout.</param>
        /// <param name="hint">Additional context to include in an assertion failure.</param>
        /// <param name="cancellationToken"></param>
        /// <returns>The received message after both predicates accept it.</returns>
        public T ExpectMsgFrom<T>(
            Predicate<IActorRef> isSender, 
            Predicate<T> isMessage,
            [AutoDilate] TimeSpan? timeout = null,
            string hint = null,
            CancellationToken cancellationToken = default)
        {
            return ExpectMsgFromAsync(
                    isSender: isSender,
                    isMessage: isMessage,
                    timeout: timeout,
                    hint: hint,
                    cancellationToken: cancellationToken)
                .AsTask().WaitAndUnwrapException();
        }

        public ValueTask<T> ExpectMsgFromAsync<T>(
            Predicate<IActorRef> isSender,
            Predicate<T> isMessage,
            [AutoDilate] TimeSpan? timeout = null,
            string hint = null,
            CancellationToken cancellationToken = default)
        {
            return InternalExpectMsgAsync<T>(
                timeout: RemainingOrDilated(timeout),
                assert: (m, sender) =>
                {
                    if(isSender != null)
                        AssertPredicateIsTrueForSender(isSender, sender, hint, m);
                    if(isMessage != null)
                        AssertPredicateIsTrueForMessage(isMessage, m, hint);
                },
                hint: hint,
                cancellationToken: cancellationToken);
        }

        private static string FormatWrongSenderMessage(IActorRef actualSender, string expectedSender, string hint)
        {
            return $"Sender does not match. Got a message from sender {actualSender}. But expected {expectedSender} {hint}";
        }

        private void AssertPredicateIsTrueForSender(
            Predicate<IActorRef> isSender,
            IActorRef sender,
            string hint,
            object message)
        {
            _assertions.AssertTrue(
                isSender(sender),
                FormatWrongSenderMessage(sender, hint ?? "the predicate to return true", null) + $" The message was {{{message}}}");
        }

        /// <summary>
        /// Receive one message of the specified type from the test actor, verifies that the sender is the specified
        /// and calls the action that performs extra assertions.
        /// Wait time is bounded by the given duration if specified.
        /// If not specified, wait time is bounded by remaining time for execution of the innermost enclosing 'within'
        /// block, if inside a 'within' block; otherwise by the config value 
        /// "akka.test.single-expect-default".
        /// Use this variant to implement more complicated or conditional processing.
        /// </summary>
        /// <typeparam name="T">The expected message type.</typeparam>
        /// <param name="sender">The actor reference expected to have sent the message.</param>
        /// <param name="assertMessage">The action that performs assertions on the received message.</param>
        /// <param name="timeout">The maximum wait duration, or null to use the current test timeout.</param>
        /// <param name="hint">Additional context to include in an assertion failure.</param>
        /// <param name="cancellationToken"></param>
        /// <returns>The received message after it is verified against <paramref name="sender"/> and <paramref name="assertMessage"/> completes.</returns>
        public T ExpectMsgFrom<T>(
            IActorRef sender,
            Action<T> assertMessage,
            [AutoDilate] TimeSpan? timeout = null,
            string hint = null,
            CancellationToken cancellationToken = default)
        {
            return ExpectMsgFromAsync(
                    sender: sender,
                    assertMessage: assertMessage,
                    timeout: timeout,
                    hint: hint,
                    cancellationToken: cancellationToken)
                .AsTask().WaitAndUnwrapException();
        }

        public ValueTask<T> ExpectMsgFromAsync<T>(
            IActorRef sender,
            Action<T> assertMessage,
            [AutoDilate] TimeSpan? timeout = null,
            string hint = null,
            CancellationToken cancellationToken = default)
        {
            return InternalExpectMsgAsync(
                    timeout: RemainingOrDilated(timeout),
                    msgAssert: assertMessage,
                    senderAssert: s => _assertions.AssertEqual(sender, s, hint),
                    hint: hint,
                    cancellationToken: cancellationToken);
        }

        /// <summary>
        /// Receive one message of the specified type from the test actor and calls the 
        /// action that performs extra assertions.
        /// Wait time is bounded by the given duration if specified.
        /// If not specified, wait time is bounded by remaining time for execution of the innermost enclosing 'within'
        /// block, if inside a 'within' block; otherwise by the config value 
        /// "akka.test.single-expect-default".
        /// Use this variant to implement more complicated or conditional processing.
        /// </summary>
        /// <typeparam name="T">The expected message type.</typeparam>
        /// <param name="assertSender">The action that performs assertions on the sender.</param>
        /// <param name="assertMessage">The action that performs assertions on the received message.</param>
        /// <param name="timeout">The maximum wait duration, or null to use the current test timeout.</param>
        /// <param name="hint">Additional context to include in an assertion failure.</param>
        /// <param name="cancellationToken"></param>
        /// <returns>The received message after both assertion actions complete.</returns>
        public T ExpectMsgFrom<T>(
            Action<IActorRef> assertSender, 
            Action<T> assertMessage,
            [AutoDilate] TimeSpan? timeout = null,
            string hint = null,
            CancellationToken cancellationToken = default)
        {
            return ExpectMsgFromAsync(
                    assertSender: assertSender,
                    assertMessage: assertMessage,
                    timeout: timeout,
                    hint: hint,
                    cancellationToken: cancellationToken)
                .AsTask().WaitAndUnwrapException();
        }
        
        public ValueTask<T> ExpectMsgFromAsync<T>(
            Action<IActorRef> assertSender, 
            Action<T> assertMessage,
            [AutoDilate] TimeSpan? timeout = null,
            string hint = null,
            CancellationToken cancellationToken = default)
        {
            return InternalExpectMsgAsync(
                timeout: RemainingOrDilated(timeout),
                msgAssert: assertMessage,
                senderAssert: assertSender,
                hint: hint,
                cancellationToken: cancellationToken);
        }
    }
}
