//-----------------------------------------------------------------------
// <copyright file="DeviceInProgress.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;

namespace Tutorials.Tutorial2
{
    public static class DeviceInProgress1
    {
        #region read-protocol-1
        public sealed class ReadTemperature
        {
            public static ReadTemperature Instance { get; } = new ReadTemperature();
            private ReadTemperature() { }
        }

        public sealed class RespondTemperature
        {
            public RespondTemperature(double? value)
            {
                Value = value;
            }

            public double? Value { get; }
        }
        #endregion
    }

    public static class DeviceInProgress2
    {
        #region read-protocol-2
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
        #endregion

        #region device-with-read
        public class Device : UntypedActor
        {
            private  double? _lastTemperatureReading = null;

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
                    case MainDevice.ReadTemperature read:
                        Sender.Tell(new RespondTemperature(read.RequestId, _lastTemperatureReading));
                        break;
                }
            }

            public static Props Props(string groupId, string deviceId) =>
                Akka.Actor.Props.Create(() => new Device(groupId, deviceId));
        }
        #endregion
    }

    public static class DeviceInProgress3
    {
        #region write-protocol-1
        public sealed class RecordTemperature
        {
            public RecordTemperature(double value)
            {
                Value = value;
            }

            public double Value { get; }
        }
        #endregion
    }
}
