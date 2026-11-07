//-----------------------------------------------------------------------
// <copyright file="ActorRefSourceActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Event;
using Akka.Streams.Actors;

#nullable enable
namespace Akka.Streams.Implementation
{
    /// <summary>
    /// Actor publisher that accepts elements as actor messages and emits them according to downstream demand.
    /// </summary>
    /// <typeparam name="T">The type of elements emitted by the publisher.</typeparam>
    internal class ActorRefSourceActor<T> : Actors.ActorPublisher<T>
    {
        /// <summary>
        /// Creates actor properties for a source actor using the materializer's maximum fixed buffer size.
        /// </summary>
        /// <param name="bufferSize">The requested buffer capacity; non-positive values disable buffering.</param>
        /// <param name="overflowStrategy">The policy used when the buffer is full. Backpressure is unsupported.</param>
        /// <param name="settings">Materializer settings used to constrain fixed-size buffer allocation.</param>
        /// <exception cref="NotSupportedException">
        /// This exception is thrown when the specified <paramref name="overflowStrategy"/> is <see cref="Akka.Streams.OverflowStrategy.Backpressure"/>.
        /// </exception>
        /// <returns>Actor properties for creating the source actor.</returns>
        public static Props Props(int bufferSize, OverflowStrategy overflowStrategy, ActorMaterializerSettings settings)
        {
            if (overflowStrategy == OverflowStrategy.Backpressure)
                throw new NotSupportedException("Backpressure overflow strategy not supported");

            var maxFixedBufferSize = settings.MaxFixedBufferSize;
            return Actor.Props.Create<ActorRefSourceActor<T>>(bufferSize, overflowStrategy, maxFixedBufferSize);
        }

        /// <summary>
        /// Buffer used for elements received without downstream demand; null when buffering is disabled.
        /// </summary>
        protected readonly IBuffer<T>? Buffer;

        /// <summary>
        /// Gets the configured capacity of the buffer, or a non-positive value when buffering is disabled.
        /// </summary>
        public readonly int BufferSize;
        /// <summary>
        /// Gets the policy applied when an incoming element cannot fit in the buffer.
        /// </summary>
        public readonly OverflowStrategy OverflowStrategy;

        /// <summary>
        /// Creates a source actor with the specified buffering behavior.
        /// </summary>
        /// <param name="bufferSize">The requested buffer capacity; non-positive values disable buffering.</param>
        /// <param name="overflowStrategy">The policy used when the buffer is full.</param>
        /// <param name="maxFixedBufferSize">The upper bound used when creating a fixed-size buffer.</param>
        /// If this changes you must also change <see cref="ActorRefSourceActor{T}.Props"/> as well!
        public ActorRefSourceActor(int bufferSize, OverflowStrategy overflowStrategy, int maxFixedBufferSize)
        {
            BufferSize = bufferSize;
            OverflowStrategy = overflowStrategy;
            Buffer = bufferSize > 0 ? Implementation.Buffer.Create<T>(bufferSize, maxFixedBufferSize) : null;
        }

        /// <summary>
        /// Gets the actor logger used for dropped-element and overflow diagnostics.
        /// </summary>
        protected ILoggingAdapter Log { get; } = Context.GetLogger();

        /// <summary>
        /// Handles built-in publisher messages and treats messages of type <typeparamref name="T"/> as elements.
        /// </summary>
        /// <param name="message">The actor message to process.</param>
        /// <returns><see langword="true"/> when a built-in message or element was handled.</returns>
        protected override bool Receive(object message)
            => DefaultReceive(message) || RequestElement(message) || (message is T message1 && ReceiveElement(message1));

        /// <summary>
        /// Handles cancellation and terminal-status messages shared by source actor implementations.
        /// </summary>
        /// <param name="message">The actor message to inspect.</param>
        /// <returns><see langword="true"/> when the message was handled; otherwise <see langword="false"/>.</returns>
        protected bool DefaultReceive(object message)
        {
            if (message is Actors.Cancel)
                Context.Stop(Self);
            else if (message is Status.Success)
            {
                if (Buffer is null || Buffer.IsEmpty)
                    OnCompleteThenStop(); // will complete the stream successfully
                else
                    Context.Become(DrainBufferThenComplete);
            }
            else if (message is Status.Failure failure && IsActive)
                OnErrorThenStop(failure.Cause);
            else
                return false;
            return true;
        }

        /// <summary>
        /// Emits buffered elements when downstream demand arrives.
        /// </summary>
        /// <param name="message">The actor message to inspect.</param>
        /// <returns><see langword="true"/> for a request message, whether or not elements were buffered.</returns>
        protected virtual bool RequestElement(object message)
        {
            if (message is Request)
            {
                // totalDemand is tracked by base
                if (Buffer is not null)
                    while (TotalDemand > 0L && !Buffer.IsEmpty)
                        OnNext(Buffer.Dequeue());

                return true;
            }

            return false;
        }

        /// <summary>
        /// Emits an element when demand is available; otherwise buffers, drops it, or fails the publisher according to the overflow policy.
        /// </summary>
        /// <param name="message">The element received by the actor.</param>
        /// <returns><see langword="true"/> when the active publisher handled the element; otherwise <see langword="false"/>.</returns>
        protected virtual bool ReceiveElement(T message)
        {
            if (IsActive)
            {
                if (TotalDemand > 0L)
                    OnNext(message);
                else if (Buffer is null)
                    Log.Debug("Dropping element because there is no downstream demand: [{0}]", message);
                else if (!Buffer.IsFull)
                    Buffer.Enqueue(message);
                else
                {
                    switch (OverflowStrategy)
                    {
                        case OverflowStrategy.DropHead:
                            Log.Debug("Dropping the head element because buffer is full and overflowStrategy is: [DropHead]");
                            Buffer.DropHead();
                            Buffer.Enqueue(message);
                            break;
                        case OverflowStrategy.DropTail:
                            Log.Debug("Dropping the tail element because buffer is full and overflowStrategy is: [DropTail]");
                            Buffer.DropTail();
                            Buffer.Enqueue(message);
                            break;
                        case OverflowStrategy.DropBuffer:
                            Log.Debug("Dropping all the buffered elements because buffer is full and overflowStrategy is: [DropBuffer]");
                            Buffer.Clear();
                            Buffer.Enqueue(message);
                            break;
                        case OverflowStrategy.DropNew:
                            // do not enqueue new element if the buffer is full
                            Log.Debug("Dropping the new element because buffer is full and overflowStrategy is: [DropNew]");
                            break;
                        case OverflowStrategy.Fail:
                            Log.Error("Failing because buffer is full and overflowStrategy is: [Fail]");
                            OnErrorThenStop(new BufferOverflowException($"Buffer overflow, max capacity was ({BufferSize})"));
                            break;
                        case OverflowStrategy.Backpressure:
                            // there is a precondition check in Source.actorRefSource factory method
                            Log.Debug("Backpressuring because buffer is full and overflowStrategy is: [Backpressure]");
                            break;
                    }
                }

                return true;
            }

            return false;
        }

        private bool DrainBufferThenComplete(object message)
        {
            if (message is Cancel)
            {
                Context.Stop(Self);
            }
            else if (message is Status.Failure failure && IsActive)
            {
                // errors must be signaled as soon as possible,
                // even if previously valid completion was requested via Status.Success
                OnErrorThenStop(failure.Cause);
            }
            else if (message is Request && Buffer is not null)
            {
                // totalDemand is tracked by base
                while (TotalDemand > 0L && !Buffer.IsEmpty)
                    OnNext(Buffer.Dequeue());

                if (Buffer.IsEmpty)
                    OnCompleteThenStop(); // will complete the stream successfully
            }
            else if (IsActive)
                Log.Debug(
                    "Dropping element because Status.Success received already, only draining already buffered elements: [{0}] (pending: [{1}])",
                    message, Buffer?.Used ?? 0);
            else
                return false;

            return true;
        }
    }
}
