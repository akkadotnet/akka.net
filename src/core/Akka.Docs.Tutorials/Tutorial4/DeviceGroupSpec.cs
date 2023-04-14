//-----------------------------------------------------------------------
// <copyright file="DeviceGroupSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.TestKit.Xunit2;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;

namespace Tutorials.Tutorial4
{
    public static partial class MainDeviceGroup
    {
        public class DeviceGroupSpec : TestKit
        {
            #region group-query-integration-test
            [Fact]
            public void DeviceGroup_actor_must_be_able_to_collect_temperatures_from_all_active_devices()
            {
                var probe = CreateTestProbe();
                var groupActor = Sys.ActorOf(DeviceGroup.Props("group"));

                groupActor.Tell(new RequestTrackDevice("group", "device1"), probe.Ref);
                probe.ExpectMsg<DeviceRegistered>();
                var deviceActor1 = probe.LastSender;

                groupActor.Tell(new RequestTrackDevice("group", "device2"), probe.Ref);
                probe.ExpectMsg<DeviceRegistered>();
                var deviceActor2 = probe.LastSender;

                groupActor.Tell(new RequestTrackDevice("group", "device3"), probe.Ref);
                probe.ExpectMsg<DeviceRegistered>();
                var deviceActor3 = probe.LastSender;

                // Check that the device actors are working
                deviceActor1.Tell(new RecordTemperature(requestId: 0, value: 1.0), probe.Ref);
                probe.ExpectMsg<TemperatureRecorded>(s => s.RequestId == 0);
                deviceActor2.Tell(new RecordTemperature(requestId: 1, value: 2.0), probe.Ref);
                probe.ExpectMsg<TemperatureRecorded>(s => s.RequestId == 1);
                // No temperature for device3

                groupActor.Tell(new RequestAllTemperatures(0), probe.Ref);
                probe.ExpectMsg<RespondAllTemperatures>(msg =>
                  msg.Temperatures["device1"].AsInstanceOf<Temperature>().Value == 1.0 &&
                  msg.Temperatures["device2"].AsInstanceOf<Temperature>().Value == 2.0 &&
                  msg.Temperatures["device3"] is TemperatureNotAvailable &&
                  msg.RequestId == 0);
            }
            #endregion
        }
    }
}
