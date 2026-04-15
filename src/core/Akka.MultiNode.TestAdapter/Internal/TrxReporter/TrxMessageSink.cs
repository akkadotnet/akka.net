// -----------------------------------------------------------------------
//  <copyright file="AzureDevOpsClientActor.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.MultiNode.TestAdapter.Configuration;
using Akka.MultiNode.TestAdapter.Internal.Sinks;

namespace Akka.MultiNode.TestAdapter.Internal.TrxReporter
{
    internal class TrxMessageSink : MessageSink
    {
        public TrxMessageSink(string suiteName, MultiNodeTestRunnerOptions options)
            : base(Props.Create(() => 
                new TrxSinkActor(suiteName, Environment.UserName, Environment.MachineName, true, options)))
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
