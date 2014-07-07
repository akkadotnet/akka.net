using System.Collections.Generic;

namespace Akka.Actor
{
    public interface IActorContext : IActorRefFactory
    {
        ActorRef Self { get; }
        Props Props { get; }
        ActorRef Sender { get; }
        ActorSystem System { get; }
        ActorRef Parent { get; }
        void Become(Receive receive, bool discardOld = true);
        void Unbecome();
        ActorRef Child(string name);
        IEnumerable<ActorRef> GetChildren();
        void Watch(ActorRef subject);
        void Unwatch(ActorRef subject);

        /*
  def self: ActorRef
  def props: Props
  def receiveTimeout: Duration  def setReceiveTimeout(timeout: Duration): Unit
  def become(behavior: Actor.Receive, discardOld: Boolean = true): Unit
  def unbecome(): Unit
  def sender: ActorRef
  def children: immutable.Iterable[ActorRef]
  def child(name: String): Option[ActorRef]
  implicit def dispatcher: ExecutionContext
  implicit def system: ActorSystem
  def parent: ActorRef
  def watch(subject: ActorRef): ActorRef
  def unwatch(subject: ActorRef): ActorRef
         */

        void Stop(ActorRef child);
    }
}