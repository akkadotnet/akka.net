using Pigeon.Messaging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Pigeon.Actor
{
    public partial class ActorContext : ActorRefFactory
    {
        protected ConcurrentDictionary<string, LocalActorRef> Children = new ConcurrentDictionary<string, LocalActorRef>();

        public override LocalActorRef Child(string name)
        {
            LocalActorRef actorRef = null;
            Children.TryGetValue(name, out actorRef);
            return actorRef;
        }

        public override ActorRef ActorSelection(string actorPath)
        {
            return ActorSelection(new ActorPath(actorPath));
        }

        public ActorSelection ActorSelection(ActorPath actorPath)
        {
            //remote path
            if (actorPath.First.StartsWith("pigeon."))
            {
                var actorRef = new RemoteActorRef(this, actorPath);
                return new ActorSelection(actorRef);
            }

            //local absolute
            if (actorPath.First.StartsWith("pigeon:"))
            {
                actorPath = new ActorPath(actorPath.Skip(1));
            }

            //standard
            var currentContext = this;
            foreach (var part in actorPath)
            {
                if (part == "..")
                {
                    currentContext = ((ActorCell)currentContext).Parent;
                }
                else if (part == "." || part == "")
                {
                    currentContext = currentContext.System;
                }
                else if (part == "*")
                {
                    var actorRef = new ActorSelection(currentContext.Children.Values.ToArray());
                    return actorRef;
                }
                else
                {
                    currentContext = ((LocalActorRef)this.Child(part)).Cell;
                }
            }
            
            return new ActorSelection( ((ActorCell)currentContext).Self);
        }

        public override LocalActorRef ActorOf<TActor>(string name = null)
        {
            return ActorOf(new Props(typeof(TActor)), name);
        }

        public override LocalActorRef ActorOf(Props props, string name = null)
        {
            if (name == null)
            {
                name = props.Type.Name;
                if (name.EndsWith("Actor"))
                    name = name.Substring(0, name.Length - 5);

                name = name + "#" + Guid.NewGuid();
            }

            var cell = new ActorCell(this,props, name);

            NewActor(cell);
            return cell.Self;
        }

        protected void NewActor(ActorCell cell)
        {
            //set the thread static context or things will break
            cell.UseThreadContext( () =>
            {
                var instance = cell.Props.NewActor();
                instance.AroundPreStart();
                Children.TryAdd(cell.Self.Path.Name, cell.Self);
            });
        }

        /// <summary>
        /// May be called from anyone
        /// </summary>
        /// <returns></returns>
        internal IEnumerable<LocalActorRef> GetChildren()
        {
            return this.Children.Values.ToArray();
        }        
    }    
}
