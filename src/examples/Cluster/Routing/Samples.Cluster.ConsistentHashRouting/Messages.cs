﻿//-----------------------------------------------------------------------
// <copyright file="Messages.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Routing;

namespace Samples.Cluster.ConsistentHashRouting
{
    public class FrontendCommand : IConsistentHashable {
        public string Message { get; set; }

        public string JobId { get; set; }

        public object ConsistentHashKey { get { return JobId; } }
    }

    public class StartCommand
    {
        public StartCommand(string commandText)
        {
            CommandText = commandText;
        }

        public string CommandText { get; private set; }

        public override string ToString()
        {
            return CommandText;
        }
    }

    public class CommandComplete
    {
    }
}

