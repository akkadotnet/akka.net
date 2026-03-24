//-----------------------------------------------------------------------
// <copyright file="ExitCodeContainer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.MultiNode.TestAdapter.Internal.Sinks;

namespace Akka.MultiNode.TestAdapter.Internal
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

