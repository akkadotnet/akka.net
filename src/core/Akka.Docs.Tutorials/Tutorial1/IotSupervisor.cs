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
