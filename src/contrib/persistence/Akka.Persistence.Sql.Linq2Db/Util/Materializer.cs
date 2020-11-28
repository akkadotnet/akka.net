using System;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Implementation;
using Akka.Util;

namespace Akka.Persistence.Sql.Linq2Db.Utility
{
    public static class Materializer
    {
        
        private static ActorSystem ActorSystemOf(IActorRefFactory context)
        {
            if (context is ExtendedActorSystem)
                return (ActorSystem)context;
            if (context is IActorContext)
                return ((IActorContext)context).System;
            if (context == null)
                throw new ArgumentNullException(nameof(context), "IActorRefFactory must be defined");

            throw new ArgumentException($"ActorRefFactory context must be a ActorSystem or ActorContext, got [{context.GetType()}]");
        }
        public static ActorMaterializer CreateSystemMaterializer(ExtendedActorSystem context, ActorMaterializerSettings settings = null, string namePrefix = null)
        {
            var haveShutDown = new AtomicBoolean();
            var system = ActorSystemOf(context);
            system.Settings.InjectTopLevelFallback(ActorMaterializer.DefaultConfig());
            settings = settings ?? ActorMaterializerSettings.Create(system);

            return new ActorMaterializerImpl(
                system: system,
                settings: settings,
                dispatchers: system.Dispatchers,
                supervisor: context.SystemActorOf(StreamSupervisor.Props(settings, haveShutDown).WithDispatcher(settings.Dispatcher), StreamSupervisor.NextName()),
                haveShutDown: haveShutDown,
                flowNames: EnumerableActorName.Create(namePrefix ?? "Flow"));
        }
    }
}