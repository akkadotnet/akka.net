//-----------------------------------------------------------------------
// <copyright file="ExitCodeContainer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.MultiNodeTestRunner.Shared.Sinks;

namespace Akka.MultiNodeTestRunner.Shared
{
    /// <summary>
    /// Global state for hanging onto the exit code used by the process.
    /// 
    /// The <see cref="SinkCoordinator"/> sets this value once during shutdown.
    /// </summary>
    public static class ExitCodeContainer
    {
        public static int ExitCode = 0;
    }
}

