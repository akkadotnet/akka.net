//-----------------------------------------------------------------------
// <copyright file="ConfigTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Linq;
using Akka.TestKit;
using Xunit;

namespace Akka.Testkit.Tests.TestEventListenerTests
{
    public class ConfigTests : TestKit.Xunit.TestKit
    {
        [Fact]
        public void TestEventListener_is_in_config_by_default()
        {
            var configLoggers = Sys.Settings.Config.GetStringList("akka.loggers");
            configLoggers.Any(logger => logger.Contains("Akka.TestKit.TestEventListener")).ShouldBeTrue();
            configLoggers.Any(logger => logger.Contains("Akka.Event.DefaultLogger")).ShouldBeFalse();
        }
    }
}
