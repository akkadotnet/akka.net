//-----------------------------------------------------------------------
// <copyright file="DeviceGroupQuery.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Event;

namespace Tutorials.Tutorial4
{
    public static partial class MainDeviceGroup
    {
        #region query-full
        public sealed class CollectionTimeout
        {
            public static CollectionTimeout Instance { get; } = new CollectionTimeout();
            private CollectionTimeout() { }
        }

        public class DeviceGroupQuery : UntypedActor
        {
            private ICancelable queryTimeoutTimer;

            #region query-state
            public DeviceGroupQuery(Dictionary<IActorRef, string> actorToDeviceId, long requestId, IActorRef requester, TimeSpan timeout)
            {
                ActorToDeviceId = actorToDeviceId;
                RequestId = requestId;
                Requester = requester;
                Timeout = timeout;

                queryTimeoutTimer = Context.System.Scheduler.ScheduleTellOnceCancelable(timeout, Self, CollectionTimeout.Instance, Self);
                
                Become(WaitingForReplies(new Dictionary<string, ITemperatureReading>(), new HashSet<IActorRef>(ActorToDeviceId.Keys)));
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

            public UntypedReceive WaitingForReplies(
                Dictionary<string, ITemperatureReading> repliesSoFar,
                HashSet<IActorRef> stillWaiting)
            {
                return message =>
                {
                    switch (message)
                    {
                        case RespondTemperature response when response.RequestId == 0:
                            var deviceActor = Sender;
                            ITemperatureReading reading = null;
                            if (response.Value.HasValue)
                            {
                                reading = new Temperature(response.Value.Value);
                            }
                            else
                            {
                                reading = TemperatureNotAvailable.Instance;
                            }
                            ReceivedResponse(deviceActor, reading, stillWaiting, repliesSoFar);
                            break;
                        case Terminated t:
                            ReceivedResponse(t.ActorRef, DeviceNotAvailable.Instance, stillWaiting, repliesSoFar);
                            break;
                        case CollectionTimeout _:
                            var replies = new Dictionary<string, ITemperatureReading>(repliesSoFar);
                            foreach (var actor in stillWaiting)
                            {
                                var deviceId = ActorToDeviceId[actor];
                                replies.Add(deviceId, DeviceTimedOut.Instance);
                            }
                            Requester.Tell(new RespondAllTemperatures(RequestId, replies));
                            Context.Stop(Self);
                            break;
                    }
                };
            }
            #endregion

            #region query-collect-reply
            public void ReceivedResponse(
                IActorRef deviceActor,
                ITemperatureReading reading,
                HashSet<IActorRef> stillWaiting,
                Dictionary<string, ITemperatureReading> repliesSoFar)
            {
                Context.Unwatch(deviceActor);
                var deviceId = ActorToDeviceId[deviceActor];
                stillWaiting.Remove(deviceActor);

                repliesSoFar.Add(deviceId, reading);

                if (stillWaiting.Count == 0)
                {
                    Requester.Tell(new RespondAllTemperatures(RequestId, repliesSoFar));
                    Context.Stop(Self);
                }
                else
                {
                    Context.Become(WaitingForReplies(repliesSoFar, stillWaiting));
                }
            }
            #endregion   

            protected override void OnReceive(object message)
            {

            }

            public static Props Props(Dictionary<IActorRef, string> actorToDeviceId, long requestId, IActorRef requester, TimeSpan timeout) =>
                Akka.Actor.Props.Create(() => new DeviceGroupQuery(actorToDeviceId, requestId, requester, timeout));
        }
        #endregion
    }
}
