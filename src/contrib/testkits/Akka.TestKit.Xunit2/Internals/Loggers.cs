//-----------------------------------------------------------------------
// <copyright file="Loggers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
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
        private readonly ITestOutputHelper _output;

        /// <summary>
        /// Initializes a new instance of the <see cref="TestOutputLogger"/> class.
        /// </summary>
        /// <param name="output">The provider used to write test output.</param>
        public TestOutputLogger(ITestOutputHelper output)
        {
            _output = output;

            Receive<Debug>(HandleLogEvent);
            Receive<Info>(HandleLogEvent);
            Receive<Warning>(HandleLogEvent);
            Receive<Error>(HandleLogEvent);
            Receive<InitializeLogger>(e =>
            {
                e.LoggingBus.Subscribe(Self, typeof (LogEvent));
            });
        }

        private void HandleLogEvent(LogEvent e)
        {
            try
            {
                _output.WriteLine(e.ToString());
            }
            catch (FormatException ex)
            {
                if (e.Message is LogMessage msg)
                {
                    var message =
                        $"Received a malformed formatted message. Log level: [{e.LogLevel()}], Template: [{msg.Format}], args: [{string.Join(",", msg.Args)}]";
                    if(e.Cause != null)
                        throw new AggregateException(message, ex, e.Cause);
                    throw new FormatException(message, ex);
                }
                throw;
            }
        }
    }
}
