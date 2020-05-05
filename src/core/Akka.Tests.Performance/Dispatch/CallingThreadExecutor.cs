//-----------------------------------------------------------------------
// <copyright file="CallingThreadExecutor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Dispatch;

namespace Akka.Tests.Performance.Dispatch
{
    public class CallingThreadExecutor : ExecutorService
    {
        public CallingThreadExecutor(string id) : base(id)
        {
        }

        public override void Execute(IRunnable run)
        {
            run.Run();
        }

        public override void Shutdown()
        {
            
        }
    }

    public class CallingThreadExecutorConfigurator : ExecutorServiceConfigurator
    {
        public CallingThreadExecutorConfigurator(Config config, IDispatcherPrerequisites prerequisites) : base(config, prerequisites)
        {
        }

        public override ExecutorService Produce(string id)
        {
            return new CallingThreadExecutor(id);
        }
    }
}

