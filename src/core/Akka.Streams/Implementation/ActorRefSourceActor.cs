//-----------------------------------------------------------------------
// <copyright file="ActorRefSourceActor.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Event;
using Akka.Streams.Actors;

namespace Akka.Streams.Implementation
{
    internal class ActorRefSourceActor<T> : Actors.ActorPublisher<T>
    {
        public static Props Props(int bufferSize, OverflowStrategy overflowStrategy, ActorMaterializerSettings settings)
        {
            if (overflowStrategy == OverflowStrategy.Backpressure)
                throw new NotSupportedException("Backpressure overflow strategy not supported");

            var maxFixedBufferSize = settings.MaxFixedBufferSize;
            return Actor.Props.Create(() => new ActorRefSourceActor<T>(bufferSize, overflowStrategy, maxFixedBufferSize));
        }

        protected readonly IBuffer<T> Buffer;

        public readonly int BufferSize;
        public readonly OverflowStrategy OverflowStrategy;
        private ILoggingAdapter _log;

        public ActorRefSourceActor(int bufferSize, OverflowStrategy overflowStrategy, int maxFixedBufferSize)
        {
            BufferSize = bufferSize;
            OverflowStrategy = overflowStrategy;
            Buffer = bufferSize != 0 ?  Implementation.Buffer.Create<T>(bufferSize, maxFixedBufferSize) : null;
        }

        protected ILoggingAdapter Log => _log ?? (_log = Context.GetLogger());

        protected override bool Receive(object message)
        {
            return DefaultReceive(message) || RequestElement(message) || (message is T && ReceiveElement((T)message));
        }

        protected bool DefaultReceive(object message)
        {
            if (message is Akka.Streams.Actors.Cancel)
                Context.Stop(Self);
            else if (message is Status.Success)
            {
                if (BufferSize == 0 || Buffer.IsEmpty)
                    Context.Stop(Self);  // will complete the stream successfully
                else
                    Context.Become(DrainBufferThenComplete);
            }
            else if (message is Status.Failure && IsActive)
                OnErrorThenStop(((Status.Failure)message).Cause);
            else
                return false;
            return true;
        }

        protected virtual bool RequestElement(object message)
        {
            if (message is Request)
            {
                // totalDemand is tracked by base
                if (BufferSize != 0)
                    while (TotalDemand > 0L && !Buffer.IsEmpty)
                        OnNext(Buffer.Dequeue());

                return true;
            }

            return false;
        }

        protected virtual bool ReceiveElement(T message)
        {
            if (IsActive)
            {
                if (TotalDemand > 0L)
                    OnNext(message);
                else if (BufferSize == 0)
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
                Context.Stop(Self);
            else if (message is Status.Failure && IsActive)
            {
                // errors must be signalled as soon as possible,
                // even if previously valid completion was requested via Status.Success
                OnErrorThenStop(((Status.Failure)message).Cause);
            }
            else if (message is Request)
            {
                // totalDemand is tracked by base
                while (TotalDemand > 0L && !Buffer.IsEmpty)
                    OnNext(Buffer.Dequeue());

                if (Buffer.IsEmpty)
                    Context.Stop(Self); // will complete the stream successfully
            }
            else if (IsActive)
            {
                Log.Debug("Dropping element because Status.Success received already, only draining already buffered elements: [{0}] (pending: [{1}])", message, Buffer.Used);
            }
            else
                return false;

            return true;
        }
    }
}