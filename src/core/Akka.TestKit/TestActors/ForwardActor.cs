using Akka.Actor;

namespace Akka.TestKit.TestActors
{
    /// <summary>
    /// ForwardActor forwards all messages as-is to specified ActorRef.
    /// </summary>
    public class ForwardActor : ReceiveActor
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="target">ActorRef to forward messages to</param>
        public ForwardActor(IActorRef target)
        {
            ReceiveAny(target.Forward);
        }

        /// <summary>
        /// Returns a <see cref="Props(Akka.Actor.IActorRef)"/> object that can be used to create an <see cref="ForwardActor"/>.
        /// </summary>
        /// <param name="target">ActorRef to forward messages to</param>
        /// <returns>TBD</returns>
        public static Props Props(IActorRef target) => Actor.Props.Create(() => new ForwardActor(target));
    }
}