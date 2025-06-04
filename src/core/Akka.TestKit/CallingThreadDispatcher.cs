//-----------------------------------------------------------------------
// <copyright file="CallingThreadDispatcher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using Akka.Configuration;
using Akka.Dispatch;

namespace Akka.TestKit
{
    /// <summary>
    /// INTERNAL API
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
    /// INTERNAL API
    ///
    /// Used to run an actor on the foreground thread.
    /// </summary>
    public class CallingThreadDispatcher : MessageDispatcher
    {
        /// <summary>
        /// HOCON id of the CallingThreadDispatcher
        /// </summary>
        public new static string Id = "akka.test.calling-thread-dispatcher";
        
        public CallingThreadDispatcher(MessageDispatcherConfigurator configurator) : base(configurator)
        {
        }
        
        protected override void ExecuteTask(IRunnable run)
        {
            var currentSyncContext = SynchronizationContext.Current;

            try
            {
                // Actors should not run with ActorCellKeepingSynchronizationContext
                // (or any sync context that wraps ActorCellKeepingSynchronizationContext, e.g. Xunit's AsyncTestSyncContext)
                // otherwise continuations in async message handlers will use ActorCellKeepingSynchronizationContext
                // instead of ActorTaskScheduler which causes ActorContext to be incorrect.
                SynchronizationContext.SetSynchronizationContext(null);

                run.Run();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(currentSyncContext);
            }
        }
        
        protected override void Shutdown()
        {
            // do nothing
        }
    }
}
