//-----------------------------------------------------------------------
// <copyright file="TestOutputLogger.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.IO;
using Akka.Actor;
using Akka.Event;
using Akka.Util;

namespace Akka.TestKit.TUnit.Internals;

internal sealed class TestOutputLogger : ReceiveActor
{
    private readonly TextWriter _output;

    public TestOutputLogger(TextWriter output)
    {
        _output = output;

        Receive<Debug>(HandleLogEvent);
        Receive<Info>(HandleLogEvent);
        Receive<Warning>(HandleLogEvent);
        Receive<Error>(HandleLogEvent);
        Receive<InitializeLogger>(message =>
        {
            message.LoggingBus.Subscribe(Self, typeof(LogEvent));
            Sender.Tell(new LoggerInitialized());
        });
    }

    private void HandleLogEvent(LogEvent logEvent)
    {
        try
        {
            _output.WriteLine(logEvent.ToString());
        }
        catch (FormatException exception) when (logEvent.Message is LogMessage message)
        {
            var details =
                $"Received a malformed formatted message. Log level: [{logEvent.LogLevel()}], Template: [{message.Format}], args: [{string.Join(",", message.Unformatted())}]";
            if (logEvent.Cause is not null)
                throw new AggregateException(details, exception, logEvent.Cause);
            throw new FormatException(details, exception);
        }
        catch (InvalidOperationException exception)
        {
            StandardOutWriter.WriteLine(
                $"Received InvalidOperationException: {exception} - probably because the test had completed executing.");
            Context.Stop(Self);
        }
    }
}
