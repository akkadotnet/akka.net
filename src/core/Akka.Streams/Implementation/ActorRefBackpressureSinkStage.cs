//-----------------------------------------------------------------------
// <copyright file="ActorRefBackpressureSinkStage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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
    /// <typeparam name="TIn">The element type sent to the target actor.</typeparam>
    internal sealed class ActorRefBackpressureSinkStage<TIn> : GraphStage<SinkShape<TIn>>
    {
        #region internal classes 

        private sealed class Logic : InGraphStageLogic
        {
            private bool _acknowledgementReceived;
            private bool _completeReceived;
            private bool _completionSignalled;
            private readonly ActorRefBackpressureSinkStage<TIn> _stage;
            private readonly int _maxBuffer;
            private readonly List<TIn> _buffer;
            private readonly Type _ackType;

            public IActorRef Self => StageActor.Ref;

            public Logic(ActorRefBackpressureSinkStage<TIn> stage, int maxBuffer) : base(stage.Shape)
            {
                _stage = stage;
                _ackType = _stage._ackMessage.GetType();
                _maxBuffer = maxBuffer;

                _buffer = new List<TIn>();

                SetHandler(_stage._inlet, this);
            }

            public override void OnPush()
            {
                _buffer.Add(Grab(_stage._inlet));
                if (_acknowledgementReceived)
                {
                    DequeueAndSend();
                    _acknowledgementReceived = false;
                }
                if (_buffer.Count < _maxBuffer)
                    Pull(_stage._inlet);
            }

            public override void OnUpstreamFinish()
            {
                if (_buffer.Count == 0)
                    Finish();
                else
                    _completeReceived = true;
            }

            public override void OnUpstreamFailure(Exception ex)
            {
                _stage._actorRef.Tell(_stage._onFailureMessage(ex), Self);
                _completionSignalled = true;
                FailStage(ex);
            }

            private void Receive((IActorRef, object) evt)
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
                GetStageActor(Receive).Watch(_stage._actorRef);
                _stage._actorRef.Tell(_stage._onInitMessage, Self);
                Pull(_stage._inlet);
            }

            private void DequeueAndSend()
            {
                var msg = _buffer[0];
                _buffer.RemoveAt(0);
                _stage._actorRef.Tell(msg, Self);
                if (_buffer.Count == 0 && _completeReceived)
                    Finish();
            }

            private void Finish()
            {
                _stage._actorRef.Tell(_stage._onCompleteMessage, Self);
                _completionSignalled = true;
                CompleteStage();
            }

            public override void PostStop()
            {
                if(!_completionSignalled)
                    Self.Tell(_stage._onFailureMessage(new AbruptStageTerminationException(this)));
            }

            public override string ToString() => "ActorRefBackpressureSink";
        }

        #endregion

        private readonly Inlet<TIn> _inlet = new("ActorRefBackpressureSink.in");

        private readonly IActorRef _actorRef;
        private readonly object _onInitMessage;
        private readonly object _ackMessage;
        private readonly object _onCompleteMessage;
        private readonly Func<Exception, object> _onFailureMessage;

        /// <summary>
        /// Creates a sink stage that sends stream elements to an actor one at a time, waiting for an acknowledgement before sending the next buffered element.
        /// </summary>
        /// <param name="actorRef">The actor that receives initialization, elements, and terminal notifications.</param>
        /// <param name="onInitMessage">The message sent to the actor when the stage starts.</param>
        /// <param name="ackMessage">The message type that acknowledges one sent element.</param>
        /// <param name="onCompleteMessage">The message sent after upstream completes and all buffered elements have been acknowledged.</param>
        /// <param name="onFailureMessage">Creates the message sent when upstream fails or the stage stops abruptly.</param>
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

        /// <summary>
        /// Supplies the default attributes for this actor-backed sink.
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.ActorRefWithAck;

        /// <summary>
        /// Gets the single-input sink shape.
        /// </summary>
        public override SinkShape<TIn> Shape { get; }

        /// <summary>
        /// Creates stage logic using the maximum input-buffer size from the inherited attributes.
        /// </summary>
        /// <param name="inheritedAttributes">Attributes applied to this stage, including its input-buffer setting.</param>
        /// <exception cref="ArgumentException">The configured maximum input-buffer size is zero or negative.</exception>
        /// <returns>The logic that buffers input and sends elements in response to acknowledgements.</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            var maxBuffer = inheritedAttributes.GetAttribute(new Attributes.InputBuffer(16, 16)).Max;
            if(maxBuffer <= 0)
                throw new ArgumentException("Buffer size mst be greater than 0");

            return new Logic(this, maxBuffer);
        }
    }
}
