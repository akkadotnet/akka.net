//-----------------------------------------------------------------------
// <copyright file="ActorMaterializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Event;
using Akka.Streams.Implementation;
using Akka.Streams.Implementation.Fusing;
using Akka.Util;

namespace Akka.Streams
{
    public class PhasedFusingActorMaterializer : ExtendedActorMaterializer
    {
        private ActorSystem actorSystem;
        private ActorMaterializerSettings settings;
        private Attributes defaultAttributes;
        private Dispatchers dispatchers;
        private IActorRef supervisor;
        private AtomicBoolean haveShutDown;
        private EnumerableActorName flowNames;

        public PhasedFusingActorMaterializer(
            ActorSystem actorSystem,
            ActorMaterializerSettings settings,
            Attributes defaultAttributes,
            Dispatchers dispatchers,
            IActorRef supervisor,
            AtomicBoolean haveShutDown,
            EnumerableActorName flowNames)
        {
            this.actorSystem = actorSystem;
            this.settings = settings;
            this.defaultAttributes = defaultAttributes;
            this.dispatchers = dispatchers;
            this.supervisor = supervisor;
            this.haveShutDown = haveShutDown;
            this.flowNames = flowNames;
        }

        public override ActorMaterializerSettings Settings => throw new NotImplementedException();

        public override bool IsShutdown => throw new NotImplementedException();

        public override ActorSystem System => throw new NotImplementedException();

        public override ILoggingAdapter Logger => throw new NotImplementedException();

        public override IActorRef Supervisor => throw new NotImplementedException();

        public override MessageDispatcher ExecutionContext => throw new NotImplementedException();

        public override ActorMaterializerSettings EffectiveSettings(Attributes attributes)
        {
            throw new NotImplementedException();
        }

        public override ILoggingAdapter MakeLogger(object logSource)
        {
            throw new NotImplementedException();
        }

        public override TMat Materialize<TMat>(IGraph<ClosedShape, TMat> runnable, Func<GraphInterpreterShell, IActorRef> subFlowFuser)
        {
            throw new NotImplementedException();
        }

        public override TMat Materialize<TMat>(IGraph<ClosedShape, TMat> runnable, Func<GraphInterpreterShell, IActorRef> subFlowFuser, Attributes initialAttributes)
        {
            throw new NotImplementedException();
        }

        public override TMat Materialize<TMat>(IGraph<ClosedShape, TMat> runnable)
        {
            throw new NotImplementedException();
        }

        public override TMat Materialize<TMat>(IGraph<ClosedShape, TMat> runnable, Attributes defaultAttributes)
        {
            throw new NotImplementedException();
        }

        public override ICancelable ScheduleAtFixedRate(TimeSpan initialDelay, TimeSpan interval, Action action)
        {
            throw new NotImplementedException();
        }

        public override ICancelable ScheduleOnce(TimeSpan delay, Action action)
        {
            throw new NotImplementedException();
        }

        public override ICancelable SchedulePeriodically(TimeSpan initialDelay, TimeSpan interval, Action action)
        {
            throw new NotImplementedException();
        }

        public override ICancelable ScheduleWithFixedDelay(TimeSpan initialDelay, TimeSpan delay, Action action)
        {
            throw new NotImplementedException();
        }

        public override void Shutdown()
        {
            throw new NotImplementedException();
        }

        public override IMaterializer WithNamePrefix(string name)
        {
            throw new NotImplementedException();
        }
    }
}
