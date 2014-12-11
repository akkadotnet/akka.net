using Akka.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.DI.Core
{
    public interface IContainerConfiguration
    {
        Type GetType(string ActorName);
        Func<ActorBase> CreateActor(string ActorName);
        ActorSystem CreateActorSystem(string SystemName);
    }
}
