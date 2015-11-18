//-----------------------------------------------------------------------
// <copyright file="Loggers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Event;
using Xunit.Abstractions;

namespace Akka.TestKit.Xunit2.Internals
{
    public class TestOutputLogger : ReceiveActor
    {
        public TestOutputLogger(ITestOutputHelper output)
        {
            Receive<Debug>(e => output.WriteLine(e.ToString()));
            Receive<Info>(e => output.WriteLine(e.ToString()));
            Receive<Warning>(e => output.WriteLine(e.ToString()));
            Receive<Error>(e => output.WriteLine(e.ToString()));
            Receive<InitializeLogger>(e =>
            {
                e.LoggingBus.Subscribe(Self, typeof (LogEvent));
            });
        }
    }
}