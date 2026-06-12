//-----------------------------------------------------------------------
// <copyright file="ActorRefSourceActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Diagnostics;
using Akka.Actor;
using Akka.Event;
using Akka.Streams.Actors;

#nullable enable
namespace Akka.Streams.Implementation
{
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    internal class ActorRefSourceActor<T> : Actors.ActorPublisher<T>
    {
        internal readonly struct TracedMessage
        {
            public TracedMessage(T message, ActivityContext context)
            {
                Message = message;
                Context = context;
            }

            public T Message { get; }
            public ActivityContext Context { get; }
        }

        private readonly struct BufferedMessage
        {
            public BufferedMessage(T message, ActivityContext? context)
            {
                Message = message;
                Context = context;
            }

            public T Message { get; }
            public ActivityContext? Context { get; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="bufferSize">TBD</param>
        /// <param name="overflowStrategy">TBD</param>
        /// <param name="settings">TBD</param>
        /// <exception cref="NotSupportedException">
        /// This exception is thrown when the specified <paramref name="overflowStrategy"/> is <see cref="Akka.Streams.OverflowStrategy.Backpressure"/>.
        /// </exception>
        /// <returns>TBD</returns>
        public static Props Props(int bufferSize, OverflowStrategy overflowStrategy, ActorMaterializerSettings settings)
        {
            if (overflowStrategy == OverflowStrategy.Backpressure)
                throw new NotSupportedException("Backpressure overflow strategy not supported");

            var maxFixedBufferSize = settings.MaxFixedBufferSize;
            return Actor.Props.Create<ActorRefSourceActor<T>>(bufferSize, overflowStrategy, maxFixedBufferSize);
        }

        /// <summary>
        /// TBD
        /// </summary>
        private readonly IBuffer<BufferedMessage>? _buffer;

        /// <summary>
        /// TBD
        /// </summary>
        public readonly int BufferSize;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly OverflowStrategy OverflowStrategy;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="bufferSize">TBD</param>
        /// <param name="overflowStrategy">TBD</param>
        /// <param name="maxFixedBufferSize">TBD</param>
        /// If this changes you must also change <see cref="ActorRefSourceActor{T}.Props"/> as well!
        public ActorRefSourceActor(int bufferSize, OverflowStrategy overflowStrategy, int maxFixedBufferSize)
        {
            BufferSize = bufferSize;
            OverflowStrategy = overflowStrategy;
            _buffer = bufferSize > 0 ? Implementation.Buffer.Create<BufferedMessage>(bufferSize, maxFixedBufferSize) : null;
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected ILoggingAdapter Log { get; } = Context.GetLogger();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected override bool Receive(object message)
            => DefaultReceive(message) || RequestElement(message) || ReceiveTracedElement(message) || (message is T message1 && ReceiveElement(message1, null));

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected bool DefaultReceive(object message)
        {
            if (message is Actors.Cancel)
                Context.Stop(Self);
            else if (message is Status.Success)
            {
                if (_buffer is null || _buffer.IsEmpty)
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
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected virtual bool RequestElement(object message)
        {
            if (message is Request)
            {
                // totalDemand is tracked by base
                if (_buffer is not null)
                    while (TotalDemand > 0L && !_buffer.IsEmpty)
                        PushElement(_buffer.Dequeue());

                return true;
            }

            return false;
        }

        private bool ReceiveTracedElement(object message)
        {
            if (message is TracedMessage tracedMessage)
                return ReceiveElement(tracedMessage.Message, tracedMessage.Context);
            return false;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <param name="context">TBD</param>
        /// <returns>TBD</returns>
        protected virtual bool ReceiveElement(T message, ActivityContext? context)
        {
            if (IsActive)
            {
                if (TotalDemand > 0L)
                    PushElement(message, context);
                else if (_buffer is null)
                    Log.Debug("Dropping element because there is no downstream demand: [{0}]", message);
                else if (!_buffer.IsFull)
                {
                    _buffer.Enqueue(new BufferedMessage(message, context));
                }
                else
                {
                    switch (OverflowStrategy)
                    {
                        case OverflowStrategy.DropHead:
                            Log.Debug("Dropping the head element because buffer is full and overflowStrategy is: [DropHead]");
                            _buffer.DropHead();
                            _buffer.Enqueue(new BufferedMessage(message, context));
                            break;
                        case OverflowStrategy.DropTail:
                            Log.Debug("Dropping the tail element because buffer is full and overflowStrategy is: [DropTail]");
                            _buffer.DropTail();
                            _buffer.Enqueue(new BufferedMessage(message, context));
                            break;
                        case OverflowStrategy.DropBuffer:
                            Log.Debug("Dropping all the buffered elements because buffer is full and overflowStrategy is: [DropBuffer]");
                            _buffer.Clear();
                            _buffer.Enqueue(new BufferedMessage(message, context));
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
            else if (message is Request && _buffer is not null)
            {
                // totalDemand is tracked by base
                while (TotalDemand > 0L && !_buffer.IsEmpty)
                    PushElement(_buffer.Dequeue());

                if (_buffer.IsEmpty)
                    OnCompleteThenStop(); // will complete the stream successfully
            }
            else if (IsActive)
                Log.Debug(
                    "Dropping element because Status.Success received already, only draining already buffered elements: [{0}] (pending: [{1}])",
                    message, _buffer?.Used ?? 0);
            else
                return false;

            return true;
        }

        private void PushElement(BufferedMessage element)
            => PushElement(element.Message, element.Context);

        private void PushElement(T element, ActivityContext? context)
        {
            if (context.HasValue && StreamsDiagnostics.ActivitySource.HasListeners())
            {
                using var activity = StreamsDiagnostics.ActivitySource.StartActivity(
                    StreamsDiagnostics.OperationIngress,
                    ActivityKind.Internal,
                    context.Value);
                OnNext(element);
            }
            else
            {
                OnNext(element);
            }
        }
    }
}
