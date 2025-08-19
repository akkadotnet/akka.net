using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Tests
{
    public class Issue3462Spec : TestKit.Xunit2.TestKit
    {
        private IActorRef m_actor;
		private Supervisor m_actorConcrete;

        public Issue3462Spec(ITestOutputHelper output) : base((ActorSystem)null, output)
        {
            var testActor = ActorOfAsTestActorRef<Supervisor>(Props.Create(() => new Supervisor()));
            m_actor = testActor.Ref;
            m_actorConcrete = testActor.UnderlyingActor;
        }

		[Fact]
		public void StartStashedWhenNotYetInitialized()
		{
			using (var mre = new ManualResetEventSlim(false))
			{
				m_actorConcrete.Done += mre.Set;

				var itemId = Guid.NewGuid();
				m_actor.Tell(new Confirmable<Started>(123, new Started(itemId)));
				m_actor.Tell(new Confirmable<Pending>(124, new Pending(new List<Guid> {itemId})));

                mre.Wait(TimeSpan.FromSeconds(1)).ShouldBe(false);
                m_actor.Tell(new Confirmable<Started>(123, new Started(itemId)));
                mre.Wait(TimeSpan.FromSeconds(1)).ShouldBe(true);

				ExpectMsg<DeliveryConfirmation>(x => x.MessageId == 123);
				ExpectMsg<DeliveryConfirmation>(x => x.MessageId == 124);
			}
		}

		internal sealed class Confirmable<T>
		{
			public Confirmable(long messageId, T message)
			{
				MessageId = messageId;
				Message = message;
			}

			public long MessageId { get; }

			public T Message { get; }
		}

		internal sealed class DeliveryConfirmation
		{
			public DeliveryConfirmation(long messageId)
			{
				MessageId = messageId;
			}

			public long MessageId { get; }
		}

		internal sealed class Pending
		{
			public Pending(IEnumerable<Guid> information)
			{
				Ids = information ?? throw new ArgumentNullException(nameof(information));
			}

			public IEnumerable<Guid> Ids { get; private set; }
		}

		internal sealed class Started
		{
			public Started(Guid id)
			{
				Id = id;
			}

			public Guid Id { get; private set; }
		}

		internal abstract class ConfirmablePersistentActor : AtLeastOnceDeliveryReceiveActor
		{
			protected ConfirmablePersistentActor()
			{
				Recover<DeliveryConfirmation>(OnReceive, null);
				Command<DeliveryConfirmation>(OnCommand, null);
				Recover<SnapshotOffer>(offer => offer.Snapshot is AtLeastOnceDeliverySnapshot, offer =>
				{
					var snapshot = offer.Snapshot as AtLeastOnceDeliverySnapshot;
					SetDeliverySnapshot(snapshot);
				});
				Command<SaveSnapshotSuccess>(saved =>
				{
					var seqNo = saved.Metadata.SequenceNr;
					DeleteSnapshots(new SnapshotSelectionCriteria(seqNo, saved.Metadata.Timestamp.AddMilliseconds(-1)));
				});
			}
			
			private void OnCommand(DeliveryConfirmation message)
			{
				Persist(message, OnReceive);
			}
			
			private void OnReceive(DeliveryConfirmation message)
			{
				ConfirmDelivery(message.MessageId);
			}
			
			protected void ConfirmedSend<T>(IActorRef destination, T message)
			{
				Deliver(destination.Path, id => new Confirmable<T>(id, message));
				SaveSnapshot(GetDeliverySnapshot());
			}

			/*protected override void OnPersistFailure(Exception cause, object @event, long sequenceNr)
			{
				base.OnPersistFailure(cause, @event, sequenceNr);
			}
	
			protected override void OnPersistRejected(Exception cause, object @event, long sequenceNr)
			{
				base.OnPersistRejected(cause, @event, sequenceNr);
			}
	
			protected override void Unhandled(object message)
			{
				base.Unhandled(message);
			}*/
			
			protected void RegisterConfirmable<T>(Action<T> handler)
			{
				Action<Confirmable<T>> confirmHandler = confirmable =>
				{
					Confirm(confirmable.MessageId);
					handler(confirmable.Message);
				};
				Recover<Confirmable<T>>(confirmHandler);
				Command<Confirmable<T>>(confirmable => Persist(confirmable, confirmHandler));
			}
			
			private void Confirm(long messageId)
			{
				Sender.Tell(new DeliveryConfirmation(messageId), Self);
			}
		}

		internal sealed class Supervisor : ConfirmablePersistentActor, IWithUnboundedStash
			// ReceiveActor, IWithUnboundedStash
		{
			private readonly List<Guid> m_ids;

			public Supervisor()
			{
				m_ids = new List<Guid>();
				
				/*Receive<Confirmable<Pending>>(OnReceive2, null);
				Receive<Confirmable<Started>>(OnReceive2, null);*/
				
				PersistenceId = "Supervisor";
				RegisterConfirmable<Pending>(OnReceive);
				RegisterConfirmable<Started>(OnReceive);
			}

			public event Action Done;

			public override string PersistenceId { get; }
			
			/*public new IStash Stash
			{
				get;
				set;
			}*/

			protected override void PreStart()
			{
				base.PreStart();
				m_ids.Clear();
			}

			private bool StashIfNotYetInitialized()
			{
				if (m_ids.Any())
				{
					return false;
				}

				Stash.Stash();
				return true;
			}

			/*private void OnReceive2(Confirmable<Pending> msg)
			{
				OnReceive(msg.Message);
			}
			private void OnReceive2(Confirmable<Started> msg)
			{
				OnReceive(msg.Message);
			}*/

			private void OnReceive(Pending pending)
			{
				foreach (var pendingId in pending.Ids)
				{
					if (m_ids.Contains(pendingId))
					{
						continue;
					}

					m_ids.Add(pendingId);
				}
				Stash.UnstashAll();
			}

			private void OnReceive(Started started)
			{
				if (StashIfNotYetInitialized())
				{
					return;
				}
				Done?.Invoke();
			}
		}
    }
}
