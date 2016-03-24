using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Streams.Implementation.Stages;
using Akka.Streams.Stage;

namespace Akka.Streams.Implementation
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class ActorRefBackpressureSinkStage<TIn> : GraphStage<SinkShape<TIn>>
    {
        #region internal classes 

        private sealed class ActorRefBackpressureLogic : GraphStageLogic
        {
            private bool _acknowledgementReceived;
            private bool _completeReceived;
            private readonly ActorRefBackpressureSinkStage<TIn> _stage;
            private readonly int _maxBuffer;
            private readonly List<TIn> _buffer;
            private readonly Type _ackType;
            
            public ActorRefBackpressureLogic(ActorRefBackpressureSinkStage<TIn> stage, int maxBuffer) : base(stage.Shape)
            {
                _stage = stage;
                _ackType = _stage._ackMessage.GetType();
                _maxBuffer = maxBuffer;

                _buffer = new List<TIn>();

                SetHandler(_stage._inlet, onPush: () =>
                {
                    _buffer.Add(Grab(_stage._inlet));
                    if (_acknowledgementReceived)
                    {
                        DequeueAndSend();
                        _acknowledgementReceived = false;
                    }
                    if(_buffer.Count < maxBuffer)
                        Pull(stage._inlet);
                }, onUpstreamFinish: () =>
                {
                    if (_buffer.Count == 0)
                        Finish();
                    else
                        _completeReceived = true;
                }, onUpstreamFailure: ex =>
                {
                    stage._actorRef.Tell(stage._onFailureMessage(ex));
                    FailStage(ex);
                });
            }

            private void Receive(Tuple<IActorRef, object> evt)
            {
                var msg = evt.Item2;

                if (msg.GetType() == _ackType)
                {
                    if (_buffer.Count == 0)
                        _acknowledgementReceived = true;
                    else
                    {
                        // onPush might have filled the buffer up and
                        // stopped pulling, so we pull here
                        if(_buffer.Count == _maxBuffer)
                            TryPull(_stage._inlet);

                        DequeueAndSend();
                    }
                    return;
                }

                var t = msg as Terminated;
                if (t != null && Equals(t.ActorRef, _stage._actorRef))
                    CompleteStage();

                //ignore all other messages
            }

            public override void PreStart()
            {
                SetKeepGoing(true);
                GetStageActorRef(Receive).Watch(_stage._actorRef);
                _stage._actorRef.Tell(_stage._onInitMessage);
                Pull(_stage._inlet);
            }

            private void DequeueAndSend()
            {
                var msg = _buffer[0];
                _buffer.RemoveAt(0);
                _stage._actorRef.Tell(msg);
                if (_buffer.Count == 0 && _completeReceived)
                    Finish();
            }

            private void Finish()
            {
                _stage._actorRef.Tell(_stage._onCompleteMessage);
                CompleteStage();
            }

            public override string ToString() => "ActorRefBackpressureSink";
        }

        #endregion

        private readonly Inlet<TIn> _inlet = new Inlet<TIn>("ActorRefBackpressureSink.in");

        private readonly IActorRef _actorRef;
        private readonly object _onInitMessage;
        private readonly object _ackMessage;
        private readonly object _onCompleteMessage;
        private readonly Func<Exception, object> _onFailureMessage;

        public ActorRefBackpressureSinkStage(IActorRef actorRef, object onInitMessage, object ackMessage,
            object onCompleteMessage, Func<Exception, object> onFailureMessage)
        {
            _actorRef = actorRef;
            _onInitMessage = onInitMessage;
            _ackMessage = ackMessage;
            _onCompleteMessage = onCompleteMessage;
            _onFailureMessage = onFailureMessage;

            Shape = new SinkShape<TIn>(_inlet);
        }

        protected override Attributes InitialAttributes { get; } = DefaultAttributes.ActorRefWithAck;

        public override SinkShape<TIn> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            var maxBuffer = inheritedAttributes.GetAttribute(new Attributes.InputBuffer(16, 16)).Max;
            if(maxBuffer <= 0)
                throw new ArgumentException("Buffer size mst be greater than 0");

            return new ActorRefBackpressureLogic(this, maxBuffer);
        }
    }
}
