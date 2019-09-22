// -----------------------------------------------------------------------
//  <copyright file="AzureDevOpsClientActor.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

namespace Akka.MultiNodeTestRunner.Shared.AzureDevOps
{
    using System;
    using Actor;
    using Reporting;
    using Sinks;
    
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
