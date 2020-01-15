//-----------------------------------------------------------------------
// <copyright file="Watch.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Streams.Implementation.Stages;
using Akka.Streams.Stage;

namespace Akka.Streams.Implementation.Fusing
{
    internal sealed class Watch<T> : SimpleLinearGraphStage<T>
    {
        #region logic

        private sealed class Logic : GraphStageLogic
        {
            private readonly Lazy<StageActor> _self;
            private readonly IActorRef _targetRef;

            public Logic(Watch<T> stage) : base(stage.Shape)
            {
                _targetRef = stage.ActorRef;
                _self = new Lazy<StageActor>(() => GetStageActor(tuple =>
                {
                    if (tuple.Item2 is Terminated terminated && terminated.ActorRef.Equals(_targetRef))
                        FailStage(new WatchedActorTerminatedException("watch", terminated.ActorRef));
                }));

                SetHandler(stage.Outlet, onPull: () => Pull(stage.Inlet));
                SetHandler(stage.Inlet, onPush: () => Push(stage.Outlet, Grab(stage.Inlet)));
            }

            public override void PreStart() => _self.Value.Watch(_targetRef);
        }

        #endregion

        public IActorRef ActorRef { get; }

        public Watch(IActorRef actorRef)
        {
            ActorRef = actorRef;
        }

        protected override Attributes InitialAttributes { get; } = DefaultAttributes.Watch;
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
    }
}
