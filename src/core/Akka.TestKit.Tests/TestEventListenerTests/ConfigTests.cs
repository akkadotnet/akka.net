//-----------------------------------------------------------------------
// <copyright file="ConfigTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Linq;
using Xunit;

namespace Akka.TestKit.Tests.Xunit2.TestEventListenerTests
{
    public class ConfigTests : TestKit.Xunit2.TestKit
    {
        [Fact]
        public void TestEventListener_is_in_config_by_default()
        {
            var configLoggers = Sys.Settings.Config.GetStringList("akka.loggers", new string[] { });
            configLoggers.Any(logger => logger.Contains("Akka.TestKit.TestEventListener")).ShouldBeTrue();
            configLoggers.Any(logger => logger.Contains("Akka.Event.DefaultLogger")).ShouldBeFalse();
        }
    }
}

