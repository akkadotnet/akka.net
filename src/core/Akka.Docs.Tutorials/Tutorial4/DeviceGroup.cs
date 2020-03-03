//-----------------------------------------------------------------------
// <copyright file="DeviceGroup.cs" company="Akka.NET Project">
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

        #region query-protocol
        public sealed class RequestAllTemperatures
        {
            public RequestAllTemperatures(long requestId)
            {
                RequestId = requestId;
            }

            public long RequestId { get; }
        }

        public sealed class RespondAllTemperatures
        {
            public RespondAllTemperatures(long requestId, Dictionary<string, ITemperatureReading> temperatures)
            {
                RequestId = requestId;
                Temperatures = temperatures;
            }

            public long RequestId { get; }
            public Dictionary<string, ITemperatureReading> Temperatures { get; }
        }

        public interface ITemperatureReading
        {
        }

        public sealed class Temperature : ITemperatureReading
        {
            public Temperature(double value)
            {
                Value = value;
            }

            public double Value { get; }
        }

        public sealed class TemperatureNotAvailable : ITemperatureReading
        {
            public static TemperatureNotAvailable Instance { get; } = new TemperatureNotAvailable();
            private TemperatureNotAvailable() { }
        }

        public sealed class DeviceNotAvailable : ITemperatureReading
        {
            public static DeviceNotAvailable Instance { get; } = new DeviceNotAvailable();
            private DeviceNotAvailable() { }
        }

        public sealed class DeviceTimedOut : ITemperatureReading
        {
            public static DeviceTimedOut Instance { get; } = new DeviceTimedOut();
            private DeviceTimedOut() { }
        }
        #endregion

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
                    case RequestDeviceList deviceList:
                        Sender.Tell(new ReplyDeviceList(deviceList.RequestId, new HashSet<string>(deviceIdToActor.Keys)));
                        break;
                    case Terminated t:
                        var deviceId = actorToDeviceId[t.ActorRef];
                        Log.Info($"Device actor for {deviceId} has been terminated");
                        actorToDeviceId.Remove(t.ActorRef);
                        deviceIdToActor.Remove(deviceId);
                        break;
                    case RequestAllTemperatures r:
                        Context.ActorOf(DeviceGroupQuery.Props(actorToDeviceId, r.RequestId, Sender, TimeSpan.FromSeconds(3)));
                        break;
                }
            }

            public static Props Props(string groupId) => Akka.Actor.Props.Create(() => new DeviceGroup(groupId));
        }
        
    }
}
