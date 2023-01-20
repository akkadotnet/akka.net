//-----------------------------------------------------------------------
// <copyright file="TestKitBase_ExpectMsgFrom.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
    /// TBD
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
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="sender">TBD</param>
        /// <param name="duration">TBD</param>
        /// <param name="hint">TBD</param>
        /// <param name="cancellationToken"></param>
        /// <returns>TBD</returns>
        public T ExpectMsgFrom<T>(
            IActorRef sender,
            TimeSpan? duration = null,
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

        public async ValueTask<T> ExpectMsgFromAsync<T>(
            IActorRef sender,
            TimeSpan? duration = null,
            string hint = null,
            CancellationToken cancellationToken = default)
        {
            return await InternalExpectMsgAsync<T>(
                    timeout: RemainingOrDilated(duration),
                    msgAssert: null,
                    senderAssert: s => _assertions.AssertEqual(
                        expected: sender,
                        actual: s,
                        format: FormatWrongSenderMessage(s,sender.ToString(), hint)), 
                    hint: null, 
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Receive one message of the specified type from the test actor and assert that it
        /// equals the <paramref name="message"/> and was sent by the specified sender
        /// Wait time is bounded by the given duration if specified.
        /// If not specified, wait time is bounded by remaining time for execution of the innermost enclosing 'within'
        /// block, if inside a 'within' block; otherwise by the config value 
        /// "akka.test.single-expect-default".
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="sender">TBD</param>
        /// <param name="message">TBD</param>
        /// <param name="timeout">TBD</param>
        /// <param name="hint">TBD</param>
        /// <param name="cancellationToken"></param>
        /// <returns>TBD</returns>
        public T ExpectMsgFrom<T>(
            IActorRef sender,
            T message,
            TimeSpan? timeout = null,
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

        public async ValueTask<T> ExpectMsgFromAsync<T>(
            IActorRef sender,
            T message,
            TimeSpan? timeout = null,
            string hint = null,
            CancellationToken cancellationToken = default)
        {
            return await InternalExpectMsgAsync<T>(
                    timeout: RemainingOrDilated(timeout),
                    msgAssert: m => _assertions.AssertEqual(message, m),
                    senderAssert: s => _assertions.AssertEqual(
                        expected: sender,
                        actual: s,
                        format: FormatWrongSenderMessage(s, sender.ToString(), hint)), 
                    hint: hint,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
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
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="sender">TBD</param>
        /// <param name="isMessage">TBD</param>
        /// <param name="timeout">TBD</param>
        /// <param name="hint">TBD</param>
        /// <param name="cancellationToken"></param>
        /// <returns>TBD</returns>
        public T ExpectMsgFrom<T>(
            IActorRef sender,
            Predicate<T> isMessage,
            TimeSpan? timeout = null,
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

        public async ValueTask<T> ExpectMsgFromAsync<T>(
            IActorRef sender,
            Predicate<T> isMessage,
            TimeSpan? timeout = null,
            string hint = null,
            CancellationToken cancellationToken = default)
        {
            return await InternalExpectMsgAsync<T>(
                    timeout: RemainingOrDilated(timeout), 
                    assert: (m, s) =>
                    {
                        _assertions.AssertEqual(sender, s, FormatWrongSenderMessage(s, sender.ToString(), hint));
                        if(isMessage != null)
                            AssertPredicateIsTrueForMessage(isMessage, m, hint);
                    }, 
                    hint: hint, 
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
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
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="isSender">TBD</param>
        /// <param name="isMessage">TBD</param>
        /// <param name="timeout">TBD</param>
        /// <param name="hint">TBD</param>
        /// <param name="cancellationToken"></param>
        /// <returns>TBD</returns>
        public T ExpectMsgFrom<T>(
            Predicate<IActorRef> isSender, 
            Predicate<T> isMessage,
            TimeSpan? timeout = null,
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

        public async ValueTask<T> ExpectMsgFromAsync<T>(
            Predicate<IActorRef> isSender,
            Predicate<T> isMessage,
            TimeSpan? timeout = null,
            string hint = null,
            CancellationToken cancellationToken = default)
        {
            return await InternalExpectMsgAsync<T>(
                timeout: RemainingOrDilated(timeout),
                assert: (m, sender) =>
                {
                    if(isSender != null)
                        AssertPredicateIsTrueForSender(isSender, sender, hint, m);
                    if(isMessage != null)
                        AssertPredicateIsTrueForMessage(isMessage, m, hint);
                }, 
                hint: hint,
                cancellationToken: cancellationToken)
                .ConfigureAwait(false);
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
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="sender">TBD</param>
        /// <param name="assertMessage">TBD</param>
        /// <param name="timeout">TBD</param>
        /// <param name="hint">TBD</param>
        /// <param name="cancellationToken"></param>
        /// <returns>TBD</returns>
        public T ExpectMsgFrom<T>(
            IActorRef sender,
            Action<T> assertMessage,
            TimeSpan? timeout = null,
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

        public async ValueTask<T> ExpectMsgFromAsync<T>(
            IActorRef sender,
            Action<T> assertMessage,
            TimeSpan? timeout = null,
            string hint = null,
            CancellationToken cancellationToken = default)
        {
            return await InternalExpectMsgAsync(
                    timeout: RemainingOrDilated(timeout),
                    msgAssert: assertMessage, 
                    senderAssert: s => _assertions.AssertEqual(sender, s, hint), 
                    hint: hint,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
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
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="assertSender">TBD</param>
        /// <param name="assertMessage">TBD</param>
        /// <param name="timeout">TBD</param>
        /// <param name="hint">TBD</param>
        /// <param name="cancellationToken"></param>
        /// <returns>TBD</returns>
        public T ExpectMsgFrom<T>(
            Action<IActorRef> assertSender, 
            Action<T> assertMessage,
            TimeSpan? timeout = null,
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
        
        public async ValueTask<T> ExpectMsgFromAsync<T>(
            Action<IActorRef> assertSender, 
            Action<T> assertMessage,
            TimeSpan? timeout = null,
            string hint = null,
            CancellationToken cancellationToken = default)
        {
            return await InternalExpectMsgAsync(
                timeout: RemainingOrDilated(timeout),
                msgAssert: assertMessage, 
                senderAssert: assertSender,
                hint: hint, 
                cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
