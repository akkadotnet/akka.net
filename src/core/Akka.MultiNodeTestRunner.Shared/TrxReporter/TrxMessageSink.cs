//-----------------------------------------------------------------------
// <copyright file="TrxMessageSink.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.MultiNodeTestRunner.Shared.Sinks;

namespace Akka.MultiNodeTestRunner.Shared.AzureDevOps
{
    public class TrxMessageSink : MessageSink
    {
        public TrxMessageSink(string suiteName)
            : base(Props.Create(() => new TrxSinkActor(suiteName, Environment.UserName, Environment.MachineName, true)))
        {
        }

        protected override void HandleUnknownMessageType(string message)
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("Unknown message: {0}", message);
            Console.ResetColor();
        }
    }
}
