//-----------------------------------------------------------------------
// <copyright file="DeviceGroupQuerySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.TestKit.Xunit2;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;

namespace Tutorials.Tutorial4
{
    public static partial class MainDeviceGroup
    {
        public class DeviceGroupQuerySpec : TestKit
        {
            #region query-test-normal
            [Fact]
            public void DeviceGroupQuery_must_return_temperature_value_for_working_devices()
            {
                var requester = CreateTestProbe();

                var device1 = CreateTestProbe();
                var device2 = CreateTestProbe();

                var queryActor = Sys.ActorOf(DeviceGroupQuery.Props(
                    actorToDeviceId: new Dictionary<IActorRef, string> { [device1.Ref] = "device1", [device2.Ref] = "device2" },
                    requestId: 1,
                    requester: requester.Ref,
                    timeout: TimeSpan.FromSeconds(3)
                ));

                device1.ExpectMsg<ReadTemperature>(read => read.RequestId == 0);
                device2.ExpectMsg<ReadTemperature>(read => read.RequestId == 0);

                queryActor.Tell(new RespondTemperature(requestId: 0, value: 1.0), device1.Ref);
                queryActor.Tell(new RespondTemperature(requestId: 0, value: 2.0), device2.Ref);

                requester.ExpectMsg<RespondAllTemperatures>(msg => 
                    msg.Temperatures["device1"].AsInstanceOf<Temperature>().Value == 1.0 &&
                    msg.Temperatures["device2"].AsInstanceOf<Temperature>().Value == 2.0 &&
                    msg.RequestId == 1);
            }
            #endregion

            #region query-test-no-reading
            [Fact]
            public void DeviceGroupQuery_must_return_TemperatureNotAvailable_for_devices_with_no_readings()
            {
                var requester = CreateTestProbe();

                var device1 = CreateTestProbe();
                var device2 = CreateTestProbe();

                var queryActor = Sys.ActorOf(DeviceGroupQuery.Props(
                    actorToDeviceId: new Dictionary<IActorRef, string> { [device1.Ref] = "device1", [device2.Ref] = "device2" },
                    requestId: 1,
                    requester: requester.Ref,
                    timeout: TimeSpan.FromSeconds(3)
                ));

                device1.ExpectMsg<ReadTemperature>(read => read.RequestId == 0);
                device2.ExpectMsg<ReadTemperature>(read => read.RequestId == 0);

                queryActor.Tell(new RespondTemperature(requestId: 0, value: null), device1.Ref);
                queryActor.Tell(new RespondTemperature(requestId: 0, value: 2.0), device2.Ref);

                requester.ExpectMsg<RespondAllTemperatures>(msg =>
                    msg.Temperatures["device1"] is TemperatureNotAvailable &&
                    msg.Temperatures["device2"].AsInstanceOf<Temperature>().Value == 2.0 &&
                    msg.RequestId == 1);
            }
            #endregion

            #region query-test-stopped
            [Fact]
            public void DeviceGroupQuery_must_return_return_DeviceNotAvailable_if_device_stops_before_answering()
            {
                var requester = CreateTestProbe();

                var device1 = CreateTestProbe();
                var device2 = CreateTestProbe();

                var queryActor = Sys.ActorOf(DeviceGroupQuery.Props(
                    actorToDeviceId: new Dictionary<IActorRef, string> { [device1.Ref] = "device1", [device2.Ref] = "device2" },
                    requestId: 1,
                    requester: requester.Ref,
                    timeout: TimeSpan.FromSeconds(3)
                ));

                device1.ExpectMsg<ReadTemperature>(read => read.RequestId == 0);
                device2.ExpectMsg<ReadTemperature>(read => read.RequestId == 0);

                queryActor.Tell(new RespondTemperature(requestId: 0, value: 1.0), device1.Ref);
                device2.Tell(PoisonPill.Instance);

                requester.ExpectMsg<RespondAllTemperatures>(msg =>
                    msg.Temperatures["device1"].AsInstanceOf<Temperature>().Value == 1.0 &&
                    msg.Temperatures["device2"] is DeviceNotAvailable &&
                    msg.RequestId == 1);
            }
            #endregion

            #region query-test-stopped-later
            [Fact]
            public void DeviceGroupQuery_must_return_temperature_reading_even_if_device_stops_after_answering()
            {
                var requester = CreateTestProbe();

                var device1 = CreateTestProbe();
                var device2 = CreateTestProbe();

                var queryActor = Sys.ActorOf(DeviceGroupQuery.Props(
                    actorToDeviceId: new Dictionary<IActorRef, string> { [device1.Ref] = "device1", [device2.Ref] = "device2" },
                    requestId: 1,
                    requester: requester.Ref,
                    timeout: TimeSpan.FromSeconds(3)
                ));

                device1.ExpectMsg<ReadTemperature>(read => read.RequestId == 0);
                device2.ExpectMsg<ReadTemperature>(read => read.RequestId == 0);

                queryActor.Tell(new RespondTemperature(requestId: 0, value: 1.0), device1.Ref);
                queryActor.Tell(new RespondTemperature(requestId: 0, value: 2.0), device2.Ref);
                device2.Tell(PoisonPill.Instance);

                requester.ExpectMsg<RespondAllTemperatures>(msg =>
                    msg.Temperatures["device1"].AsInstanceOf<Temperature>().Value == 1.0 &&
                    msg.Temperatures["device2"].AsInstanceOf<Temperature>().Value == 2.0 &&
                    msg.RequestId == 1);
            }
            #endregion

            #region query-test-timeout
            [Fact]
            public void DeviceGroupQuery_must_return_DeviceTimedOut_if_device_does_not_answer_in_time()
            {
                var requester = CreateTestProbe();

                var device1 = CreateTestProbe();
                var device2 = CreateTestProbe();

                var queryActor = Sys.ActorOf(DeviceGroupQuery.Props(
                    actorToDeviceId: new Dictionary<IActorRef, string> { [device1.Ref] = "device1", [device2.Ref] = "device2" },
                    requestId: 1,
                    requester: requester.Ref,
                    timeout: TimeSpan.FromSeconds(1)
                ));

                device1.ExpectMsg<ReadTemperature>(read => read.RequestId == 0);
                device2.ExpectMsg<ReadTemperature>(read => read.RequestId == 0);

                queryActor.Tell(new RespondTemperature(requestId: 0, value: 1.0), device1.Ref);

                requester.ExpectMsg<RespondAllTemperatures>(msg =>
                    msg.Temperatures["device1"].AsInstanceOf<Temperature>().Value == 1.0 &&
                    msg.Temperatures["device2"] is DeviceTimedOut &&
                    msg.RequestId == 1);
            }
            #endregion
        }
    }
}
