//-----------------------------------------------------------------------
// <copyright file="TestEventListener.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using Akka.Actor;
using Akka.Dispatch.SysMsg;
using Akka.Event;
using Akka.TestKit.TestEvent;

namespace Akka.TestKit
{
    /// <summary>
    /// EventListener for running tests, which allows selectively filtering out
    /// expected messages. To use it, include something like this in
    /// the configuration:
    /// <code>akka.loggers = ["Akka.TestKit.TestEventListener, Akka.TestKit"]</code>
    /// </summary>
    public class TestEventListener : DefaultLogger
    {
        private readonly List<IEventFilter> _filters = new List<IEventFilter>();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected override bool Receive(object message)
        {
            if(message is InitializeLogger)
            {
                var initLogger = (InitializeLogger)message;
                var bus = initLogger.LoggingBus;
                var self = Context.Self;
                bus.Subscribe(self, typeof(Mute));
                bus.Subscribe(self, typeof(Unmute));
                bus.Subscribe(self, typeof(DeadLetter));
                bus.Subscribe(self, typeof(UnhandledMessage));
                Sender.Tell(new LoggerInitialized());
            }
            else if(message is Mute)
            {
                var mute = (Mute)message;
                foreach(var filter in mute.Filters)
                {
                    AddFilter(filter);
                }
            }
            else if(message is Unmute)
            {
                var unmute = (Unmute)message;
                foreach(var filter in unmute.Filters)
                {
                    RemoveFilter(filter);
                }
            }
            else if(message is LogEvent)
            {
                var logEvent = (LogEvent)message;
                if(!ShouldFilter(logEvent))
                {
                    Print(logEvent);
                }
            }
            else if(message is DeadLetter)
            {
                HandleDeadLetter((DeadLetter)message);
            }
            else if(message is UnhandledMessage)
            {
                var un = (UnhandledMessage) message;
                var rcp = un.Recipient;
                var warning = new Warning(rcp.Path.ToString(), rcp.GetType(), "Unhandled message from " + un.Sender + ": " + un.Message);
                if(!ShouldFilter(warning))
                    Print(warning);
            }
            else
            {
                Print(new Debug(Context.System.Name,GetType(),message));
            }
            return true;
        }

        private void HandleDeadLetter(DeadLetter message)
        {
            var msg = message.Message;
            var rcp = message.Recipient;
            var snd = message.Sender;
            if(!(msg is Terminate))
            {
                var recipientPath = rcp.Path.ToString();
                var recipientType = rcp.GetType();
                var warning = new Warning(recipientPath, recipientType, message);
                if(!ShouldFilter(warning))
                {
                    var msgStr = (msg is ISystemMessage)
                        ? "Received dead system message: " + msg
                        : "Received dead letter from " + snd + ": " + msg;
                    var warning2 = new Warning(recipientPath, recipientType, new DeadLetter(msgStr,snd,rcp));
                    if(!ShouldFilter(warning2))
                    {
                        Print(warning2);
                    }
                }

            }
        }

        private void AddFilter(IEventFilter filter)
        {
            _filters.Add(filter);
        }

        private void RemoveFilter(IEventFilter filter)
        {
            _filters.Remove(filter);
        }

        private bool ShouldFilter(LogEvent message)
        {
            foreach(var filter in _filters)
            {
                try
                {
                    if(filter.Apply(message))
                        return true;
                }
                // ReSharper disable once EmptyGeneralCatchClause
                catch
                {
                }
            }
            return false;

        }
    }
}
