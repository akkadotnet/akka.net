﻿using Akka.Actor;
using Akka.Event;

namespace Tutorials.Tutorial3
{
    public static partial class MainDeviceGroup
    {
        #region device-with-register
        public sealed class RecordTemperature
        {
            public RecordTemperature(long requestId, double value)
            {
                RequestId = requestId;
                Value = value;
            }

            public long RequestId { get; }
            public double Value { get; }
        }

        public sealed class TemperatureRecorded
        {
            public TemperatureRecorded(long requestId)
            {
                RequestId = requestId;
            }

            public long RequestId { get; }
        }

        public sealed class ReadTemperature
        {
            public ReadTemperature(long requestId)
            {
                RequestId = requestId;
            }

            public long RequestId { get; }
        }

        public sealed class RespondTemperature
        {
            public RespondTemperature(long requestId, double? value)
            {
                RequestId = requestId;
                Value = value;
            }

            public long RequestId { get; }
            public double? Value { get; }
        }

        public class Device : UntypedActor
        {
            private double? _lastTemperatureReading = null;

            public Device(string groupId, string deviceId)
            {
                GroupId = groupId;
                DeviceId = deviceId;
            }

            protected override void PreStart() => Log.Info($"Device actor {GroupId}-{DeviceId} started");
            protected override void PostStop() => Log.Info($"Device actor {GroupId}-{DeviceId} stopped");

            protected ILoggingAdapter Log { get; } = Context.GetLogger();
            protected string GroupId { get; }
            protected string DeviceId { get; }

            protected override void OnReceive(object message)
            {
                switch (message)
                {
                    case RequestTrackDevice req when req.GroupId.Equals("groupId") && req.DeviceId.Equals("deviceId"):
                        Sender.Tell(DeviceRegistered.Instance);
                        break;
                    case RequestTrackDevice req:
                        Log.Warning($"Ignoring TrackDevice request for {req.GroupId}-{req.DeviceId}.This actor is responsible for {GroupId}-{DeviceId}.");
                        break;
                    case RecordTemperature rec:
                        Log.Info($"Recorded temperature reading {rec.Value} with {rec.RequestId}");
                        _lastTemperatureReading = rec.Value;
                        Sender.Tell(new TemperatureRecorded(rec.RequestId));
                        break;
                    case ReadTemperature read:
                        Sender.Tell(new RespondTemperature(read.RequestId, _lastTemperatureReading));
                        break;
                }
            }

            public static Props Props(string groupId, string deviceId) => Akka.Actor.Props.Create(() => new Device(groupId, deviceId));
        }
        #endregion
    }
}
