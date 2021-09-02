using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Util.Internal;

namespace Akka.ChildResolve
{
    public sealed class Child : UntypedActor
    {
        protected override void OnReceive(object message)
        {
        }

        protected override void PreStart()
        {
            if (Self.Path.Name.Length > 1)
            {
                // recursively create children using the previous name segments
                var nextName = new string(Self.Path.Name.Skip(1).ToArray());
                Context.ActorOf(Props.Create(() => new Child()), nextName);
            }
        }
    }

    public sealed class ActorWithChild : UntypedActor
    {
        public sealed class Get
        {
            public Get(string name)
            {
                Name = name;
            }

            public string Name { get; }
        }

        public sealed class Create
        {
            public Create(string name)
            {
                Name = name;
            }

            public string Name { get; }
        }

        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case Get g:
                {
                    var child = Context.Child(g.Name);
                    Sender.Tell(child);
                    break;
                }
                case Create c:
                {
                    var child = Context.ActorOf(Props.Create(() => new Child()), c.Name);
                    Sender.Tell(child);
                    break;
                }
                default:
                    Unhandled(message);
                    break;
            }
        }
    }

    class Program
    {
        static async Task Main(string[] args)
        {
            TimeSpan _timeout;
            ActorSystem _system;
            IActorRef _parentActor;

            ActorWithChild.Get _getMessage = new ActorWithChild.Get("food");
            ActorWithChild.Create _createMessage = new ActorWithChild.Create("food");

            IActorContext _cell;
            RepointableActorRef _repointableActorRef;
            LocalActorRef _localActorRef;

            List<string> _rpChildQueryPath = new List<string>() { "food", "ood", "od" };
            List<string> _lclChildQueryPath = new List<string>() { "ood", "od", "d" };


            _timeout = TimeSpan.FromMinutes(1);
            _system = ActorSystem.Create("system");
            _parentActor = _system.ActorOf(Props.Create(() => new ActorWithChild()), "parent");
            _localActorRef = (LocalActorRef)await _parentActor.Ask<IActorRef>(_createMessage, _timeout);

            _cell = _parentActor.AsInstanceOf<ActorRefWithCell>().Underlying.AsInstanceOf<ActorCell>();
            _repointableActorRef = (RepointableActorRef)_parentActor;


            foreach (var i in Enumerable.Range(0, 1_000_000))
                _cell.Child(_getMessage.Name);

            foreach (var i in Enumerable.Range(0, 100_000_000))
                _repointableActorRef.GetChild(_rpChildQueryPath);
        }
    }
}