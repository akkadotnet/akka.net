//-----------------------------------------------------------------------
// <copyright file="ActorRefBackpressureSinkStage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
    /// <typeparam name="TIn">TBD</typeparam>
    internal class ActorRefBackpressureSinkStage<TIn> : GraphStage<SinkShape<TIn>>
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

        private readonly Inlet<TIn> _inlet = new Inlet<TIn>("ActorRefBackpressureSink.in");

        private readonly IActorRef _actorRef;
        private readonly object _onInitMessage;
        private readonly object _ackMessage;
        private readonly object _onCompleteMessage;
        private readonly Func<Exception, object> _onFailureMessage;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actorRef">TBD</param>
        /// <param name="onInitMessage">TBD</param>
        /// <param name="ackMessage">TBD</param>
        /// <param name="onCompleteMessage">TBD</param>
        /// <param name="onFailureMessage">TBD</param>
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
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.ActorRefWithAck;

        /// <summary>
        /// TBD
        /// </summary>
        public override SinkShape<TIn> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <exception cref="ArgumentException">TBD</exception>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            var maxBuffer = inheritedAttributes.GetAttribute(new Attributes.InputBuffer(16, 16)).Max;
            if(maxBuffer <= 0)
                throw new ArgumentException("Buffer size mst be greater than 0");

            return new Logic(this, maxBuffer);
        }
    }
}
