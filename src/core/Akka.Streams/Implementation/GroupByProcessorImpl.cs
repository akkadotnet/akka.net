using System;
using System.Collections.Generic;
using System.Reactive.Streams;
using Akka.Actor;
using Akka.Pattern;
using Akka.Streams.Dsl;
using Decider = Akka.Streams.Supervision.Decider;
using Directive = Akka.Streams.Supervision.Directive;

namespace Akka.Streams.Implementation
{
    internal sealed class GroupByProcessorImpl : MultiStreamOutputProcessor<object>
    {
        public static Props Props(ActorMaterializerSettings settings, int maxSubstreams, Func<object, object> keyFor)
        {
            return Actor.Props.Create(() => new GroupByProcessorImpl(settings, maxSubstreams, keyFor)).WithDeploy(Deploy.Local);
        }

        private readonly int _maxSubstreams;
        private readonly Func<object, object> _keyFor;
        private readonly Decider _decider;
        private readonly IDictionary<object, SubstreamOutput> _keyToSubstreamOutput = new Dictionary<object, SubstreamOutput>();
        // No substream is open yet. If downstream cancels now, we are complete
        // some substreams are open now. If downstream cancels, we still continue until the substreams are closed
        private readonly TransferPhase _waitNext;

        private SubstreamOutput _pendingSubstreamOutput;

        private GroupByProcessorImpl(ActorMaterializerSettings settings, int maxSubstreams, Func<object, object> keyFor) : base(settings)
        {
            _maxSubstreams = maxSubstreams;
            _keyFor = keyFor;
            _decider = settings.SupervisionDecider;
            var waitFirst = new TransferPhase(PrimaryInputs.NeedsInput.And(PrimaryOutputs.NeedsDemand), () =>
            {
                var element = PrimaryInputs.DequeueInputElement();
                object key;
                if (TryKeyFor(element, out key))
                    NextPhase(OpenSubstream(element, key));
            });
            _waitNext = new TransferPhase(PrimaryInputs.NeedsInput, () =>
            {
                var element = PrimaryInputs.DequeueInputElement();
                object key;
                if (TryKeyFor(element, out key))
                {
                    SubstreamOutput substream;
                    if (_keyToSubstreamOutput.TryGetValue(key, out substream))
                    {
                        if (substream.IsOpen) NextPhase(DispatchToSubstream(element, substream));
                    }
                    else if (PrimaryOutputs.IsOpen) NextPhase(OpenSubstream(element, key));
                }
            });

            InitialPhase(1, waitFirst);
        }

        protected override void InvalidateSubstreamOutput(SubstreamKey substream)
        {
            if (!ReferenceEquals(_pendingSubstreamOutput, null) && substream == _pendingSubstreamOutput.Key)
            {
                _pendingSubstreamOutput = null;
                NextPhase(_waitNext);
            }

            base.InvalidateSubstreamOutput(substream);
        }

        private bool TryKeyFor(object key, out object element)
        {
            try
            {
                element = _keyFor(key);
                return true;
            }
            catch (Exception cause)
            {
                if (_decider(cause) != Directive.Stop)
                {
                    element = null;
                    if (Settings.IsDebugLogging)
                        Log.Debug("Dropped element [{0}] due to exception from groupBy function: {1}", key, cause.Message);
                    return false;
                }
                throw;
            }
        }

        private TransferPhase OpenSubstream(object element, object key)
        {
            return new TransferPhase(PrimaryOutputs.NeedsDemandOrCancel, () =>
            {
                if (PrimaryOutputs.IsClosed)
                {
                    // Just drop, we do not open any more substreams
                    NextPhase(_waitNext);
                }
                else
                {
                    if (_keyToSubstreamOutput.Count == _maxSubstreams)
                        throw new IllegalStateException(string.Format("Cannot open substream for key '{0}': too many substreams open", key));
                    var substreamOutput = CreateSubstreamOutput();
                    var substreamFlow = Source.FromPublisher<object, Unit>(substreamOutput);
                    PrimaryOutputs.EnqueueOutputElement(substreamFlow);

                    if (_keyToSubstreamOutput.ContainsKey(key)) _keyToSubstreamOutput[key] = substreamOutput;
                    else _keyToSubstreamOutput.Add(key, substreamOutput);

                    NextPhase(DispatchToSubstream(element, substreamOutput));
                }
            });
        }

        private TransferPhase DispatchToSubstream(object element, SubstreamOutput substream)
        {
            _pendingSubstreamOutput = substream;
            return new TransferPhase(substream.NeedsDemand, () =>
            {
                substream.EnqueueOutputElement(element);
                _pendingSubstreamOutput = null;
                NextPhase(_waitNext);
            });
        }
    }
}