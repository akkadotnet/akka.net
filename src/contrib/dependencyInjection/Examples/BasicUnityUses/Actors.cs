using System;
using Akka.Actor;
using Akka.Routing;

namespace BasicUnityUses
{
	public class AnotherMessage
	{
		public string Name { get; set; }
		public int Id { get; set; }

		public override string ToString()
		{
			return string.Format("{0} {1}", Id, Name);
		}


	}
	public class TypedActorMessage : IConsistentHashable
	{
		public string Name { get; set; }
		public int Id { get; set; }

		public override string ToString()
		{
			return string.Format("{0} {1}", Id, Name);
		}

		public object ConsistentHashKey
		{
			get { return Id; }
		}
	}
	public class TypedWorker : TypedActor, IHandle<TypedActorMessage>, IHandle<AnotherMessage>
	{
		public TypedWorker()
		{
			//
			Console.WriteLine("Created {0}", Guid.NewGuid().ToString());
		}

		public void Handle(TypedActorMessage message)
		{
			Console.WriteLine("{0} received {1}", Self.Path.Name, message);
		}


		public void Handle(AnotherMessage message)
		{
			Console.WriteLine("{0} received other {1}", Self.Path.Name, message);
		}
	}
}