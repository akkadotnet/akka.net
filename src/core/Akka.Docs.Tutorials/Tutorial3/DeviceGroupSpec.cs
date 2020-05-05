//-----------------------------------------------------------------------
// <copyright file="DeviceGroupSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using Xunit;

namespace Tutorials.Tutorial3
{
    public static partial class MainDeviceGroup
    {
        public class DeviceGroupSpec : TestKit
        {
            #region device-group-test-registration
            [Fact]
            public void DeviceGroup_actor_must_be_able_to_register_a_device_actor()
            {
                var probe = CreateTestProbe();
                var groupActor = Sys.ActorOf(DeviceGroup.Props("group"));

                groupActor.Tell(new RequestTrackDevice("group", "device1"), probe.Ref);
                probe.ExpectMsg<DeviceRegistered>();
                var deviceActor1 = probe.LastSender;

                groupActor.Tell(new RequestTrackDevice("group", "device2"), probe.Ref);
                probe.ExpectMsg<DeviceRegistered>();
                var deviceActor2 = probe.LastSender;
                deviceActor1.Should().NotBe(deviceActor2);

                // Check that the device actors are working
                deviceActor1.Tell(new RecordTemperature(requestId: 0, value: 1.0), probe.Ref);
                probe.ExpectMsg<TemperatureRecorded>(s => s.RequestId == 0);
                deviceActor2.Tell(new RecordTemperature(requestId: 1, value: 2.0), probe.Ref);
                probe.ExpectMsg<TemperatureRecorded>(s => s.RequestId == 1);
            }

            [Fact]
            public void DeviceGroup_actor_must_ignore_requests_for_wrong_groupId()
            {
                var probe = CreateTestProbe();
                var groupActor = Sys.ActorOf(DeviceGroup.Props("group"));

                groupActor.Tell(new RequestTrackDevice("wrongGroup", "device1"), probe.Ref);
                probe.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
            }
            #endregion

            #region device-group-test3
            [Fact]
            public void DeviceGroup_actor_must_return_same_actor_for_same_deviceId()
            {
                var probe = CreateTestProbe();
                var groupActor = Sys.ActorOf(DeviceGroup.Props("group"));

                groupActor.Tell(new RequestTrackDevice("group", "device1"), probe.Ref);
                probe.ExpectMsg<DeviceRegistered>();
                var deviceActor1 = probe.LastSender;

                groupActor.Tell(new RequestTrackDevice("group", "device1"), probe.Ref);
                probe.ExpectMsg<DeviceRegistered>();
                var deviceActor2 = probe.LastSender;

                deviceActor1.Should().Be(deviceActor2);
            }
            #endregion

            #region device-group-list-terminate-test
            [Fact]
            public void DeviceGroup_actor_must_be_able_to_list_active_devices()
            {
                var probe = CreateTestProbe();
                var groupActor = Sys.ActorOf(DeviceGroup.Props("group"));

                groupActor.Tell(new RequestTrackDevice("group", "device1"), probe.Ref);
                probe.ExpectMsg<DeviceRegistered>();

                groupActor.Tell(new RequestTrackDevice("group", "device2"), probe.Ref);
                probe.ExpectMsg<DeviceRegistered>();

                groupActor.Tell(new RequestDeviceList(requestId: 0), probe.Ref);
                probe.ExpectMsg<ReplyDeviceList>(s => s.RequestId == 0 
                    && s.Ids.Contains("device1")
                    && s.Ids.Contains("device2"));
            }

            [Fact]
            public void DeviceGroup_actor_must_be_able_to_list_active_devices_after_one_shuts_down()
            {
                var probe = CreateTestProbe();
                var groupActor = Sys.ActorOf(DeviceGroup.Props("group"));

                groupActor.Tell(new RequestTrackDevice("group", "device1"), probe.Ref);
                probe.ExpectMsg<DeviceRegistered>();
                var toShutDown = probe.LastSender;

                groupActor.Tell(new RequestTrackDevice("group", "device2"), probe.Ref);
                probe.ExpectMsg<DeviceRegistered>();

                groupActor.Tell(new RequestDeviceList(requestId: 0), probe.Ref);
                probe.ExpectMsg<ReplyDeviceList>(s => s.RequestId == 0
                                                      && s.Ids.Contains("device1")
                                                      && s.Ids.Contains("device2"));

                probe.Watch(toShutDown);
                toShutDown.Tell(PoisonPill.Instance);
                probe.ExpectTerminated(toShutDown);

                // using awaitAssert to retry because it might take longer for the groupActor
                // to see the Terminated, that order is undefined
                probe.AwaitAssert(() =>
                {
                    groupActor.Tell(new RequestDeviceList(requestId: 1), probe.Ref);
                    probe.ExpectMsg<ReplyDeviceList>(s => s.RequestId == 1 && s.Ids.Contains("device2"));
                });
            }
            #endregion
        }
    }
}
