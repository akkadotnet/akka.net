//-----------------------------------------------------------------------
// <copyright file="ConfigTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Akka.Hosting.TestKit.Tests.TestEventListenerTests
{
    public class ConfigTests : TestKit
    {
        protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
        {
        }
        
        [Fact]
        public void TestEventListener_is_in_config_by_default()
        {
            var configLoggers = Sys.Settings.Config.GetStringList("akka.loggers", new string[] { });
            configLoggers.Any(logger => logger.Contains("Akka.TestKit.TestEventListener")).Should().BeTrue();
            configLoggers.Any(logger => logger.Contains("Akka.Event.DefaultLogger")).Should().BeFalse();
        }
    }
}

