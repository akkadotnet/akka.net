// //-----------------------------------------------------------------------
// // <copyright file="ShutdownLoggerSpec.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Tests.Loggers
{
    public class ShutdownLoggerSpec: AkkaSpec
    {
        private static readonly Config Config = ConfigurationFactory.ParseString(@"
akka.loglevel = OFF
akka.stdout-loglevel = OFF
akka.loglevel = OFF
akka.stdout-logger-class = ""Akka.Tests.Loggers.ThrowingLogger, Akka.Tests""");

        public ShutdownLoggerSpec(ITestOutputHelper output) : base(Config, output)
        {
        }

        [Fact(DisplayName = "StandardOutLogger should not be called during shutdown when stdout-loglevel is set to OFF")]
        public async Task StandardOutLoggerShouldNotBeCalled()
        {
            Logging.StandardOutLogger.Invoked += message => 
                throw new Exception("StandardOutLogger should not be called during shutdown when stdout-loglevel is OFF");
            
            await Sys.Terminate();

            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }
}