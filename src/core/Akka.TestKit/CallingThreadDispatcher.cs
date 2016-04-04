//-----------------------------------------------------------------------
// <copyright file="CallingThreadDispatcher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Configuration;
using Akka.Dispatch;

namespace Akka.TestKit
{
    public class CallingThreadDispatcherConfigurator : MessageDispatcherConfigurator
    {
        public CallingThreadDispatcherConfigurator(Config config, IDispatcherPrerequisites prerequisites) : base(config, prerequisites)
        {
        }

        public override MessageDispatcher Dispatcher()
        {
            return new CallingThreadDispatcher(this);
        }
    }

    public class CallingThreadDispatcher : MessageDispatcher
    {
        public static string Id = "akka.test.calling-thread-dispatcher";

        public CallingThreadDispatcher(MessageDispatcherConfigurator configurator) : base(configurator)
        {
        }

        public override void Schedule(Action run)
        {
            run();
        }
    }

}

