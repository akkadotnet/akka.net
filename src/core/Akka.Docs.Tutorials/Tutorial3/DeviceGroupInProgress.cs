//-----------------------------------------------------------------------
// <copyright file="DeviceGroupInProgress.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using Akka.Actor;
using Akka.Event;
using static Tutorials.Tutorial3.MainDeviceGroup;

namespace Tutorials.Tutorial3
{
    public static class DeviceGroupInProgress
    {
        #region device-group-register
        public class DeviceGroup : UntypedActor
        {
            private Dictionary<string, IActorRef> deviceIdToActor = new Dictionary<string, IActorRef>();

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
                    case RequestTrackDevice trackMsg when trackMsg.GroupId.Equals(GroupId):
                        if (deviceIdToActor.TryGetValue(trackMsg.DeviceId, out var actorRef))
                        {
                            actorRef.Forward(trackMsg);
                        }
                        else
                        {
                            Log.Info($"Creating device actor for {trackMsg.DeviceId}");
                            var deviceActor = Context.ActorOf(Device.Props(trackMsg.GroupId, trackMsg.DeviceId), $"device-{trackMsg.DeviceId}");
                            deviceIdToActor.Add(trackMsg.DeviceId, deviceActor);
                            deviceActor.Forward(trackMsg);
                        }
                        break;
                    case RequestTrackDevice trackMsg:
                        Log.Warning($"Ignoring TrackDevice request for {trackMsg.GroupId}. This actor is responsible for {GroupId}.");
                        break;
                }
            }

            public static Props Props(string groupId) => Akka.Actor.Props.Create(() => new DeviceGroup(groupId));
        }
        #endregion
    }

    public static class DeviceGroupInProgress2
    {
        #region device-group-remove
        public class DeviceGroup : UntypedActor
        {
            private Dictionary<string, IActorRef> deviceIdToActor = new Dictionary<string, IActorRef>();
            private Dictionary<IActorRef, string> actorToDeviceId = new Dictionary<IActorRef, string>();

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
                    case RequestTrackDevice trackMsg when trackMsg.GroupId.Equals(GroupId):
                        if (deviceIdToActor.TryGetValue(trackMsg.DeviceId, out var actorRef))
                        {
                            actorRef.Forward(trackMsg);
                        }
                        else
                        {
                            Log.Info($"Creating device actor for {trackMsg.DeviceId}");
                            var deviceActor = Context.ActorOf(Device.Props(trackMsg.GroupId, trackMsg.DeviceId), $"device-{trackMsg.DeviceId}");
                            Context.Watch(deviceActor);
                            actorToDeviceId.Add(deviceActor, trackMsg.DeviceId);
                            deviceIdToActor.Add(trackMsg.DeviceId, deviceActor);
                            deviceActor.Forward(trackMsg);
                        }
                        break;
                    case RequestTrackDevice trackMsg:
                        Log.Warning($"Ignoring TrackDevice request for {trackMsg.GroupId}. This actor is responsible for {GroupId}.");
                        break;
                    case Terminated t:
                        var deviceId = actorToDeviceId[t.ActorRef];
                        Log.Info($"Device actor for {deviceId} has been terminated");
                        actorToDeviceId.Remove(t.ActorRef);
                        deviceIdToActor.Remove(deviceId);
                        break;
                }
            }

            public static Props Props(string groupId) => Akka.Actor.Props.Create(() => new DeviceGroup(groupId));
        }
        #endregion
    }
}
