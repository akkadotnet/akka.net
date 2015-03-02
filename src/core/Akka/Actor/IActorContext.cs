using System;
using System.Collections.Generic;

namespace Akka.Actor
{
    public interface ICanWatch
    {
		/// <summary>
		/// Monitors the specified actor for termination. When the <paramref name="subject"/> terminates
		/// the instance watching will receive a <see cref="Terminated"/> message.
		/// <remarks>Note that if the <see cref="Terminated"/> message isn't handled by the actor,
		/// by default the actor will crash by throwing a <see cref="DeathPactException"/>. To change
		/// the default behavior, override <see cref="ActorBase.Unhandled"/>.
		/// </remarks>
		/// </summary>
		/// <param name="subject">The actor to monitor for termination.</param>
		/// <returns>Returns the provided subject</returns>
        ActorRef Watch(ActorRef subject);

		/// <summary>
		/// Stops monitoring the <paramref name="subject"/> for termination.
		/// </summary>
		/// <param name="subject">The actor to stop monitor for termination.</param>
		/// <returns>Returns the provided subject</returns>
        ActorRef Unwatch(ActorRef subject);
    }

    public interface IActorContext : ActorRefFactory, ICanWatch
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

        /// <summary>
        /// <para>
        /// Defines the inactivity timeout after which the sending of a <see cref="ReceiveTimeout"/> message is triggered.
        /// When specified, the receive function should be able to handle a <see cref="ReceiveTimeout"/> message.
        /// </para>
        /// 
        /// <para>
        /// Please note that the receive timeout might fire and enqueue the <see cref="ReceiveTimeout"/> message right after
        /// another message was enqueued; hence it is not guaranteed that upon reception of the receive
        /// timeout there must have been an idle period beforehand as configured via this method.
        /// </para>
        /// 
        /// <para>
        /// Once set, the receive timeout stays in effect (i.e. continues firing repeatedly after inactivity
        /// periods). Pass in <c>null</c> to switch off this feature.
        /// </para>
        /// </summary>
        /// <param name="timeout">The timeout. Pass in <c>null</c> to switch off this feature.</param>
        void SetReceiveTimeout(TimeSpan? timeout);

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