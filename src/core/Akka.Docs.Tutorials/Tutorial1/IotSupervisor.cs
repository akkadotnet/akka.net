//-----------------------------------------------------------------------
// <copyright file="IotSupervisor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Event;

namespace Tutorials.Tutorial1
{
    #region iot-supervisor
    public class IotSupervisor : UntypedActor
    {
        public ILoggingAdapter Log { get; } = Context.GetLogger();

        protected override void PreStart() => Log.Info("IoT Application started");
        protected override void PostStop() => Log.Info("IoT Application stopped");

        // No need to handle any messages
        protected override void OnReceive(object message)
        {
        }

        public static Props Props() => Akka.Actor.Props.Create<IotSupervisor>();
    }
    #endregion
}
