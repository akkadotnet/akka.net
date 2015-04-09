//-----------------------------------------------------------------------
// <copyright file="TaskDispatcher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
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

        public override void Schedule(Action run)
        {
            Task.Run(run);
        }
    }    
}

