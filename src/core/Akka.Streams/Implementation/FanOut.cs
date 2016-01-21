using System;
using System.Collections.Immutable;
using System.Reactive.Streams;
using Akka.Actor;

namespace Akka.Streams.Implementation
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal static class FanOut
    {
        [Serializable]
        public struct SubstreamRequestMore : INoSerializationVerificationNeeded
        {
            public readonly int Id;
            public readonly long Demand;

            public SubstreamRequestMore(int id, long demand)
            {
                Id = id;
                Demand = demand;
            }
        }

        [Serializable]
        public struct SubstreamCancel : INoSerializationVerificationNeeded
        {
            public readonly int Id;

            public SubstreamCancel(int id)
            {
                Id = id;
            }
        }

        [Serializable]
        public struct SubstreamSubscribePending : INoSerializationVerificationNeeded
        {
            public readonly int Id;

            public SubstreamSubscribePending(int id)
            {
                Id = id;
            }
        }

        public class SubstreamSubscription : ISubscription
        {
            private readonly IActorRef _parent;
            private readonly int _id;

            public SubstreamSubscription(IActorRef parent, int id)
            {
                _parent = parent;
                _id = id;
            }

            public void Request(long elements) => _parent.Tell(new SubstreamRequestMore(_id, elements));

            public void Cancel() => _parent.Tell(new SubstreamCancel(_id));

            public override string ToString() => "SubstreamSubscription" + GetHashCode();
        }

        public class FanoutOutputs<T> : SimpleOutputs<T>
        {
            private readonly int _id;

            public FanoutOutputs(int id, IActorRef actor, IPump pump) : base(actor, pump)
            {
                _id = id;
            }

            protected new ISubscription CreateSubscription() => new SubstreamSubscription(Actor, _id);
        }

        [Serializable]
        public struct ExposedPublishers<T> : INoSerializationVerificationNeeded
        {
            public readonly ImmutableList<ActorPublisher<T>> Publishers;

            public ExposedPublishers(ImmutableList<ActorPublisher<T>> publishers)
            {
                Publishers = publishers;
            }
        }

        public class OutputBunch<T>
        {
            private readonly int _outputCount;
            private bool _bunchCancelled;
            private readonly FanoutOutputs<T>[] _outputs;
            private readonly bool[] _marked;
            private int _markedCount;
            private readonly bool[] _pending;
            private int _markedPending;
            private readonly bool[] _cancelled;
            private int _markedCanceled;
            private readonly bool[] _completed;
            private readonly bool[] _errored;
            private bool _unmarkCancelled = true;
            private int _preferredId;

            public OutputBunch(int outputCount, IActorRef impl, IPump pump)
            {
                _outputCount = outputCount;
                _outputs = new FanoutOutputs<T>[outputCount];
                for (var i = 0; i < outputCount; i++)
                    _outputs[i] = new FanoutOutputs<T>(i, impl, pump);

                _marked = new bool[outputCount];
                _pending = new bool[outputCount];
                _cancelled = new bool[outputCount];
                _completed = new bool[outputCount];
                _errored = new bool[outputCount];
            }

            public bool IsPending(int output) => _pending[output];

            public bool IsCompleted(int output) => _completed[output];

            public bool IsCancelled(int output) => _cancelled[output];

            public bool IsErrored(int output) => _errored[output];

            public void Complete()
            {
                if (!_bunchCancelled)
                {
                    _bunchCancelled = true;

                    for (var i = 0; i < _outputs.Length; i++)
                        Complete(i);
                }
            }

            public void Complete(int output)
            {
                if (!_completed[output] && !_errored[output] && !_cancelled[output])
                {
                    _outputs[output].Complete();
                    _completed[output] = true;
                    UnmarkOutput(output);
                }
            }

            public void Cancel(Exception e)
            {
                if (!_bunchCancelled)
                {
                    _bunchCancelled = true;
                    for (int i = 0; i < _outputs.Length; i++)
                        Error(i, e);
                }
            }

            public void Error(int output, Exception e)
            {
                if (!_errored[output] && !_cancelled[output] && !_completed[output])
                {
                    _outputs[output].Error(e);
                    _errored[output] = true;
                    UnmarkOutput(output);
                }
            }

            public void MarkOutput(int output)
            {
                if (!_marked[output])
                {
                    if (_cancelled[output])
                        _markedCanceled += 1;
                    if (_pending[output])
                        _markedPending += 1;

                    _marked[output] = true;
                    _markedCount += 1;
                }
            }

            public void UnmarkOutput(int output)
            {
                if (_marked[output])
                {
                    if (_cancelled[output])
                        _markedCanceled -= 1;
                    if (_pending[output])
                        _markedPending -= 1;

                    _marked[output] = false;
                    _markedCount -= 1;
                }
            }

            public void MarkAllOutputs()
            {
                for (var i = 0; i < _outputCount; i++)
                    MarkOutput(i);
            }

            public void UnmarkAllOutputs()
            {
                for (int i = 0; i < _outputCount; i++)
                    UnmarkOutput(i);
            }

            public void UnmarkCancelledOutputs(bool enabled) => _unmarkCancelled = enabled;

            public int IdToEnqueue()
            {
                var id = _preferredId;

                while (!(_marked[id] && _pending[id]))
                {
                    id += 1;
                    if (id == _outputCount)
                        id = 0;

                    if (id != _preferredId)
                        throw new ArgumentException("Tried to equeue without waiting for any demand");
                }

                return id;
            }

            public void Enqueue(int id, T element)
            {
                var output = _outputs[id];
                output.EnqueueOutputElement(element);

                if (!output.IsDemandAvailable)
                {
                    if (_marked[id])
                        _markedPending -= 1;

                    _pending[id] = false;
                }
            }

            public void EnqueueMarked(T element)
            {
                for (var id = 0; id < _outputCount; id++)
                {
                    if (_marked[id])
                    {
                        Enqueue(id, element);
                    }
                }
            }

            public int IdToEnqueueAndYield()
            {
                var id = IdToEnqueue();
                _preferredId = id + 1;

                if (_preferredId == _outputCount)
                    _preferredId = 0;

                return id;
            }

            public void EnqueueAndYield(T element) => Enqueue(IdToEnqueueAndYield(), element);

            public void EnqueueAndPrefer(T element, int preferred)
            {
                var id = IdToEnqueue();
                _preferredId = preferred;
                Enqueue(id, element);
            }

            public void OnCancel(int output) { }

            public TransferState DemandAvailableFor(int id)
                => new AnonymousTransferState(_pending[id], _cancelled[id] || _completed[id] || _errored[id]);

            public TransferState DemandOrCancelAvailableFor(int id)
                => new AnonymousTransferState(_pending[id] || _cancelled[id], false);
        }
    }
}
