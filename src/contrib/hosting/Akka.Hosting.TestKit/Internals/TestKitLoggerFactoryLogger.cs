// -----------------------------------------------------------------------
//  <copyright file="LoggerFactoryLogger.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;
using Akka.Event;
using Akka.Hosting.Logging;

namespace Akka.Hosting.TestKit.Internals
{
    public class TestKitLoggerFactoryLogger: LoggerFactoryLogger
    {
        protected override bool Receive(object message)
        {
            switch (message)
            { 
                case InitializeLogger init:
                    InternalLogger.Info($"{nameof(TestKitLoggerFactoryLogger)} started");
                    ((EventStream)init.LoggingBus).Subscribe<LogEvent>(Self);
                    // Only reply if there's an actual sender waiting (not NoSender)
                    if (!Sender.Equals(ActorRefs.NoSender))
                        Sender.Tell(new LoggerInitialized());
                    return true;
                
                default:
                    return base.Receive(message);
            }
        }
    }
}