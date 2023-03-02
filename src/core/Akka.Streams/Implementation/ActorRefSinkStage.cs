//-----------------------------------------------------------------------
// <copyright file="ActorRefSinkStage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Annotations;
using Akka.Event;
using Akka.Streams.Implementation.Stages;
using Akka.Streams.Stage;

namespace Akka.Streams.Implementation
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    [InternalApi]
    public sealed class ActorRefSinkStage<T> : GraphStage<SinkShape<T>>
    {
        #region Logic

        private class Logic : GraphStageLogic, IInHandler
        {
            private readonly ActorRefSinkStage<T> _stage;
            private bool _completionSignalled;

            public Logic(ActorRefSinkStage<T> stage)
                : base(stage.Shape)
            {
                _stage = stage;
                SetHandler(_stage.In, this);
            }

            public override void PreStart()
            {
                base.PreStart();

                GetStageActor(tuple =>
                {
                    switch (tuple.Item2)
                    {
                        case Terminated terminated when ReferenceEquals(terminated.ActorRef, _stage._actorRef):
                            CompleteStage();
                            break;
                        case object msg:
                            Log.Error("Unexpected message to stage actor {0}", msg.GetType().Name);
                            break;
                    }
                }).Watch(_stage._actorRef);
                Pull(_stage.In);
            }

            public override void PostStop()
            {
                if (!_completionSignalled)
                    _stage._actorRef.Tell(_stage._onFailureMessage(new AbruptStageTerminationException(this)));
                base.PostStop();
            }

            public void OnPush()
            {
                var next = Grab(_stage.In);
                _stage._actorRef.Tell(next, ActorRefs.NoSender);
                Pull(_stage.In);
            }

            public void OnUpstreamFinish()
            {
                _completionSignalled = true;
                _stage._actorRef.Tell(_stage._onCompleteMessage, ActorRefs.NoSender);
                CompleteStage();
            }

            public void OnUpstreamFailure(Exception ex)
            {
                _completionSignalled = true;
                _stage._actorRef.Tell(_stage._onFailureMessage(ex), ActorRefs.NoSender);
                FailStage(ex);
            }
        }

        #endregion

        private readonly IActorRef _actorRef;
        private readonly object _onCompleteMessage;
        private readonly Func<Exception, object> _onFailureMessage;

        public Inlet<T> In { get; } = new Inlet<T>("ActorRefSink.in");

        public ActorRefSinkStage(IActorRef actorRef, object onCompleteMessage, Func<Exception, object> onFailureMessage)
        {
            _actorRef = actorRef;
            _onCompleteMessage = onCompleteMessage;
            _onFailureMessage = onFailureMessage;

            Shape = new SinkShape<T>(In);
        }

        protected override Attributes InitialAttributes => DefaultAttributes.ActorRefSink;

        public override SinkShape<T> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
    }
}
