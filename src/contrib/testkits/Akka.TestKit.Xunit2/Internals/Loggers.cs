//-----------------------------------------------------------------------
// <copyright file="Loggers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Event;
using Xunit.Abstractions;

namespace Akka.TestKit.Xunit2.Internals
{
    /// <summary>
    /// TBD
    /// </summary>
    public class TestOutputLogger : ReceiveActor
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="output">TBD</param>
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