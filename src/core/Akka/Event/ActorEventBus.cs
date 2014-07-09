using Akka.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Event
{
    /// <summary>
    /// Class ActorEventBus.
    /// </summary>
    /// <typeparam name="TEvent">The type of the t event.</typeparam>
    /// <typeparam name="TClassifier">The type of the t classifier.</typeparam>
    public abstract class ActorEventBus<TEvent, TClassifier> : EventBus<TEvent, TClassifier, ActorRef>
    {
    }
}
