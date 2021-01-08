//-----------------------------------------------------------------------
// <copyright file="CallingThreadDispatcher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Configuration;
using Akka.Dispatch;

namespace Akka.TestKit
{
    /// <summary>
    /// TBD
    /// </summary>
    public class CallingThreadDispatcherConfigurator : MessageDispatcherConfigurator
    {
        /// <summary>
        /// TBD 
        /// </summary>
        /// <param name="config">TBD</param>
        /// <param name="prerequisites">TBD</param>
        public CallingThreadDispatcherConfigurator(Config config, IDispatcherPrerequisites prerequisites) : base(config, prerequisites)
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override MessageDispatcher Dispatcher()
        {
            return new CallingThreadDispatcher(this);
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public class CallingThreadDispatcher : MessageDispatcher
    {
        /// <summary>
        /// TBD 
        /// </summary>
        public static string Id = "akka.test.calling-thread-dispatcher";

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="configurator">TBD</param>
        public CallingThreadDispatcher(MessageDispatcherConfigurator configurator) : base(configurator)
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="run">TBD</param>
        protected override void ExecuteTask(IRunnable run)
        {
            run.Run();
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override void Shutdown()
        {
            // do nothing
        }
    }
}
