﻿using System.Collections.Generic;
using Akka.Actor;
using Akka.Event;

namespace Tutorials.Tutorial3
{
    public static partial class MainDeviceGroup
    {
        #region device-group-full
        public sealed class RequestDeviceList
        {
            public RequestDeviceList(long requestId)
            {
                RequestId = requestId;
            }

            public long RequestId { get; }
        }

        public sealed class ReplyDeviceList
        {
            public ReplyDeviceList(long requestId, ISet<string> ids)
            {
                RequestId = requestId;
                Ids = ids;
            }

            public long RequestId { get; }
            public ISet<string> Ids { get; }
        }

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
                    case RequestTrackDevice trackMsg when trackMsg.GroupId.Equals("groupId"):
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
                    case RequestDeviceList deviceList:
                        Sender.Tell(new ReplyDeviceList(deviceList.RequestId, new HashSet<string>(deviceIdToActor.Keys)));
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
