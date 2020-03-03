//-----------------------------------------------------------------------
// <copyright file="DeviceGroupQueryInProgress.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Event;
using static Tutorials.Tutorial4.MainDeviceGroup;

namespace Tutorials.Tutorial4
{
    public static class DeviceGroupQueryInProgress
    {
        #region query-outline
        public class DeviceGroupQuery : UntypedActor
        {
            private ICancelable queryTimeoutTimer;

            public DeviceGroupQuery(Dictionary<IActorRef, string> actorToDeviceId, long requestId, IActorRef requester, TimeSpan timeout)
            {
                ActorToDeviceId = actorToDeviceId;
                RequestId = requestId;
                Requester = requester;
                Timeout = timeout;

                queryTimeoutTimer = Context.System.Scheduler.ScheduleTellOnceCancelable(timeout, Self, CollectionTimeout.Instance, Self);
            }

            protected override void PreStart()
            {
                foreach (var deviceActor in ActorToDeviceId.Keys)
                {
                    Context.Watch(deviceActor);
                    deviceActor.Tell(new ReadTemperature(0));
                }
            }

            protected override void PostStop()
            {
                queryTimeoutTimer.Cancel();
            }

            protected ILoggingAdapter Log { get; } = Context.GetLogger();
            public Dictionary<IActorRef, string> ActorToDeviceId { get; }
            public long RequestId { get; }
            public IActorRef Requester { get; }
            public TimeSpan Timeout { get; }

            protected override void OnReceive(object message)
            {

            }

            public static Props Props(Dictionary<IActorRef, string> actorToDeviceId, long requestId, IActorRef requester, TimeSpan timeout) =>
                Akka.Actor.Props.Create(() => new DeviceGroupQuery(actorToDeviceId, requestId, requester, timeout));
        }
        #endregion
    }

    public static class DeviceGroupInProgress2
    {
        #region query-added
        public class DeviceGroup : UntypedActor
        {
            private Dictionary<string, IActorRef> deviceIdToActor = new Dictionary<string, IActorRef>();
            private Dictionary<IActorRef, string> actorToDeviceId = new Dictionary<IActorRef, string>();
            private long nextCollectionId = 0L;

            public DeviceGroup(string groupId)
            {
                GroupId = groupId;
            }

            protected override void PreStart() => Log.Info($"Device group {GroupId} started");
            protected override void PostStop() => Log.Info($"Device group {GroupId} stopped");

            protected ILoggingAdapter Log { get; } = Context.GetLogger();
            protected string GroupId { get; }

            protected override void OnReceive(object message)
            {
                switch (message)
                {
                    case RequestAllTemperatures r:
                        Context.ActorOf(DeviceGroupQuery.Props(actorToDeviceId, r.RequestId, Sender, TimeSpan.FromSeconds(3)));
                        break;
                }
            }

            public static Props Props(string groupId) => Akka.Actor.Props.Create(() => new DeviceGroup(groupId));
        }
        #endregion
    }
}
