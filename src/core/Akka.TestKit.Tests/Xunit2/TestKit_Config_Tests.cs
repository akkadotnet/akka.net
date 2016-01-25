//-----------------------------------------------------------------------
// <copyright file="TestKit_Config_Tests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Xunit;

namespace Akka.TestKit.Tests.Xunit2
{
    // ReSharper disable once InconsistentNaming
    public class TestKit_Config_Tests : TestKit.Xunit2.TestKit
    {
        [Fact]
        public void DefaultValues_should_be_correct()
        {
            TestKitSettings.DefaultTimeout.ShouldBe(TimeSpan.FromSeconds(5));
            TestKitSettings.SingleExpectDefault.ShouldBe(TimeSpan.FromSeconds(3));
            TestKitSettings.TestEventFilterLeeway.ShouldBe(TimeSpan.FromSeconds(3));
            TestKitSettings.TestTimeFactor.ShouldBe(1);
            var callingThreadDispatcherTypeName = typeof(CallingThreadDispatcherConfigurator).FullName + ", " + typeof(CallingThreadDispatcher).Assembly.GetName().Name;
            Sys.Settings.Config.GetString("akka.test.calling-thread-dispatcher.type").ShouldBe(callingThreadDispatcherTypeName);
            Sys.Settings.Config.GetString("akka.test.test-actor.dispatcher.type").ShouldBe(callingThreadDispatcherTypeName);
            CallingThreadDispatcher.Id.ShouldBe("akka.test.calling-thread-dispatcher");


        }
    }
}

