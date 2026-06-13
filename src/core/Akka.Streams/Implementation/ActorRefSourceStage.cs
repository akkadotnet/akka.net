//-----------------------------------------------------------------------
// <copyright file="ActorRefSourceStage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Event;
using Akka.Streams.Implementation.Stages;
using Akka.Streams.Stage;

#nullable enable
namespace Akka.Streams.Implementation
{
    /// <summary>
    /// INTERNAL API
    ///
    /// Stream-native implementation of <c>Source.ActorRef&lt;T&gt;</c>. Materializes an <see cref="IActorRef"/>
    /// (a <c>FunctionRef</c> created eagerly on the stream supervisor); elements sent to that ref are emitted into
    /// the stream. Buffering and overflow handling live inside the stage, so there is no <c>ActorPublisher</c> hop
    /// on the ingress path.
    ///
    /// <para>
    /// Completion protocol: send <see cref="Status.Success"/> to drain buffered elements and complete, or
    /// <see cref="Status.Failure"/> to fail immediately (even while draining). Unlike the legacy
    /// <c>ActorPublisher</c>-backed source, <see cref="PoisonPill"/> and <see cref="Kill"/> are <b>not</b> honored;
    /// those lifecycle messages are ignored by the source.
    /// </para>
    /// </summary>
    /// <typeparam name="T">The type of element emitted by the source.</typeparam>
    internal sealed class ActorRefSourceStage<T> : GraphStageWithMaterializedValue<SourceShape<T>, IActorRef>
    {
        private readonly int _bufferSize;
        private readonly OverflowStrategy _overflowStrategy;

        public ActorRefSourceStage(int bufferSize, OverflowStrategy overflowStrategy)
        {
            if (bufferSize < 0)
                throw new ArgumentException("Buffer size must be greater than or equal to 0", nameof(bufferSize));
            if (overflowStrategy == OverflowStrategy.Backpressure)
                throw new NotSupportedException("Backpressure overflow strategy is not supported");

            _bufferSize = bufferSize;
            _overflowStrategy = overflowStrategy;
            Shape = new SourceShape<T>(Out);
        }

        public Outlet<T> Out { get; } = new("ActorRefSource.out");

        public override SourceShape<T> Shape { get; }

        protected override Attributes InitialAttributes => DefaultAttributes.ActorRefSource;

        // This stage needs the materializer to mint its IActorRef, so it is always materialized through the
        // materializer-aware overload below. The plain overload should never be reached during normal materialization.
        public override ILogicAndMaterializedValue<IActorRef> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
            => throw new NotSupportedException(
                $"{nameof(ActorRefSourceStage<T>)} must be materialized through the materializer-aware overload.");

        internal override ILogicAndMaterializedValue<IActorRef> CreateLogicAndMaterializedValue(
            Attributes inheritedAttributes, IMaterializer materializer)
        {
            if (materializer is null)
                throw new NotSupportedException(
                    $"{nameof(ActorRefSourceStage<T>)} requires a materializer to create its actor ref.");

            var logic = new Logic(this, inheritedAttributes, materializer);
            return new LogicAndMaterializedValue<IActorRef>(logic, logic.Ref);
        }

        private sealed class Logic : GraphStageLogic, IOutHandler
        {
            private readonly ActorRefSourceStage<T> _stage;
            private readonly IBuffer<T>? _buffer;
            private readonly ActorCell _cell;
            private readonly FunctionRef _functionRef;
            private bool _completionRequested;

            public IActorRef Ref => _functionRef;

            public Logic(ActorRefSourceStage<T> stage, Attributes inheritedAttributes, IMaterializer materializer)
                : base(stage.Shape)
            {
                _stage = stage;
                var name = inheritedAttributes.GetNameOrDefault("actorRefSource");
                _buffer = stage._bufferSize > 0 ? Buffer.Create<T>(stage._bufferSize, materializer) : null;

                SetHandler(stage.Out, this);

                // Mint the materialized ref eagerly, before the interpreter starts, so it can be handed back as the
                // materialized value. It is a plain FunctionRef (full IActorRef: watchable, path-addressable) wired
                // to a message-only async callback — the sender is unused, so this avoids boxing a (sender, message)
                // tuple on the per-element ingress path. Messages sent before the interpreter starts are buffered by
                // the async-callback machinery and delivered once the stage is running.
                _cell = ActorMaterializerHelper.GetSupervisorCell(ActorMaterializerHelper.Downcast(materializer));

                var callback = GetAsyncCallback<object>(OnMessage);
                _functionRef = _cell.AddFunctionRef((_, message) => callback(message), name);
            }

            // Stopping the ref terminates any watchers (Watch on the materialized ref yields Terminated when the
            // stream completes, fails, or is cancelled).
            public override void PostStop() => _cell.RemoveFunctionRef(_functionRef);

            public void OnPull()
            {
                if (_buffer != null && _buffer.NonEmpty)
                    Push(_stage.Out, _buffer.Dequeue());

                if (_completionRequested && (_buffer == null || _buffer.IsEmpty))
                    CompleteStage();
            }

            public void OnDownstreamFinish(Exception cause) => CompleteStage();

            private void OnMessage(object message)
            {
                switch (message)
                {
                    case Status.Success _:
                        // Drain already-buffered elements before completing.
                        if (_buffer == null || _buffer.IsEmpty)
                            CompleteStage();
                        else
                            _completionRequested = true;
                        break;
                    case Status.Failure failure:
                        // Errors are signaled immediately, even if a Status.Success was already received.
                        // Guard against a null cause, which would otherwise complete (not fail) the stream.
                        FailStage(failure.Cause ?? new ArgumentNullException(
                            nameof(Status.Failure.Cause), "Status.Failure was sent with a null Cause"));
                        break;
                    case PoisonPill _:
                    case Kill _:
                        // Lifecycle messages are not honored (see breaking change). Handled explicitly so they are
                        // ignored for every T — including T = object, where they would otherwise match `case T`.
                        break;
                    case T element:
                        OnElement(element);
                        break;
                    default:
                        // Anything else is not part of the Source.ActorRef protocol and is ignored.
                        break;
                }
            }

            private void OnElement(T element)
            {
                // After Status.Success only the existing buffer is drained; new elements are dropped.
                if (_completionRequested)
                {
                    Log.Debug("Dropping element because Status.Success was already received, only draining buffered elements: [{0}]", element);
                    return;
                }

                // Fast path: nothing is queued ahead and downstream is waiting — push directly and skip the
                // buffer enqueue/dequeue round-trip. Only buffer when there is no demand, or elements are
                // already queued (to preserve FIFO order).
                if ((_buffer == null || _buffer.IsEmpty) && IsAvailable(_stage.Out))
                {
                    Push(_stage.Out, element);
                }
                else if (_buffer != null)
                {
                    BufferElement(element);
                    if (IsAvailable(_stage.Out))
                        Push(_stage.Out, _buffer.Dequeue());
                }
                else
                {
                    // bufferSize == 0 and no downstream demand — drop the element.
                    Log.Debug("Dropping element because there is no downstream demand: [{0}]", element);
                }
            }

            private void BufferElement(T element)
            {
                if (!_buffer!.IsFull)
                {
                    _buffer.Enqueue(element);
                    return;
                }

                switch (_stage._overflowStrategy)
                {
                    case OverflowStrategy.DropHead:
                        Log.Debug("Dropping the head element because buffer is full and overflowStrategy is: [DropHead]");
                        _buffer.DropHead();
                        _buffer.Enqueue(element);
                        break;
                    case OverflowStrategy.DropTail:
                        Log.Debug("Dropping the tail element because buffer is full and overflowStrategy is: [DropTail]");
                        _buffer.DropTail();
                        _buffer.Enqueue(element);
                        break;
                    case OverflowStrategy.DropBuffer:
                        Log.Debug("Dropping all buffered elements because buffer is full and overflowStrategy is: [DropBuffer]");
                        _buffer.Clear();
                        _buffer.Enqueue(element);
                        break;
                    case OverflowStrategy.DropNew:
                        // Discard the incoming element.
                        Log.Debug("Dropping the new element because buffer is full and overflowStrategy is: [DropNew]");
                        break;
                    case OverflowStrategy.Fail:
                        Log.Error("Failing because buffer is full and overflowStrategy is: [Fail]");
                        FailStage(new BufferOverflowException($"Buffer overflow, max capacity was ({_stage._bufferSize})"));
                        break;
                    case OverflowStrategy.Backpressure:
                        // Unsupported for Source.ActorRef (guarded in the factory); no-op defensively.
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }
    }
}
