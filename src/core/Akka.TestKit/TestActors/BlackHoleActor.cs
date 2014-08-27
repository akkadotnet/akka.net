using System.Dynamic;
using Akka.Actor;

namespace Akka.TestKit.TestActors
{
    /// <summary>
    /// A <see cref="BlackHoleActor"/> is an actor that silently 
    /// accepts any messages sent to it.
    /// </summary>
    public class BlackHoleActor : ActorBase
    {
        protected override bool Receive(object message)
        {
            return true;
        }

        /// <summary>
        /// Returns a <see cref="Props"/> object that can be used to create a <see cref="BlackHoleActor"/>
        /// </summary>
        public static Props Props { get { return Props.Create<BlackHoleActor>(); } }
    }
}