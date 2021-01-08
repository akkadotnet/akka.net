//-----------------------------------------------------------------------
// <copyright file="Loggers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Event;
using Xunit.Abstractions;

namespace Akka.TestKit.Xunit2.Internals
{
    /// <summary>
    /// This class represents an actor that logs output from tests using an <see cref="ITestOutputHelper"/> provider.
    /// </summary>
    public class TestOutputLogger : ReceiveActor
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TestOutputLogger"/> class.
        /// </summary>
        /// <param name="output">The provider used to write test output.</param>
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
