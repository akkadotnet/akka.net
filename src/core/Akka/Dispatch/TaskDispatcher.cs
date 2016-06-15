//-----------------------------------------------------------------------
// <copyright file="TaskDispatcher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;

namespace Akka.Dispatch
{
    /// <summary>
    /// Task based dispatcher
    /// </summary>
    public class TaskDispatcher : MessageDispatcher
    {
        /// <summary>
        /// Takes a <see cref="MessageDispatcherConfigurator"/>
        /// </summary>
        public TaskDispatcher(MessageDispatcherConfigurator configurator) : base(configurator)
        {
        }
        // cache the delegate used for execution to prevent allocations
        protected static readonly Action<object> Executor = t => { ((IRunnable)t).Run(); };

        public override void Schedule(IRunnable run)
        {
            var t = new Task(Executor, run);
            t.Start(TaskScheduler.Default);
        }

        protected override void Shutdown()
        {
            // do nothing
        }
    }    
}

