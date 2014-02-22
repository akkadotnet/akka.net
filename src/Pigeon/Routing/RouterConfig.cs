using Pigeon.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Routing
{
    public abstract class RouterConfig
    {
        public abstract RoutingLogic GetLogic();

        public abstract IEnumerable<Routee> GetRoutees(ActorSystem system);

        public static readonly RouterConfig NoRouter = new NoRouter();

        public virtual RouterConfig WithFallback(RouterConfig routerConfig)
        {
            return this;
        }
    }

    public class NoRouter : RouterConfig
    {
        public override RoutingLogic GetLogic()
        {
            throw new NotImplementedException();
        }

        public override IEnumerable<Routee> GetRoutees(ActorSystem system)
        {
            throw new NotImplementedException();
        }
    }

    public abstract class Group : RouterConfig
    {
        private string[] paths;

        public Group(IEnumerable<string> paths)
        {
            this.paths = paths.ToArray();
        }

        protected Group(IEnumerable<ActorRef> routees)
        {
            this.paths = routees.Select(x => x.Path.ToStringWithoutAddress()).ToArray();
        }

        public override IEnumerable<Routee> GetRoutees(ActorSystem system)
        {
            foreach(var path in paths)
            {
                var actor = system.ActorSelection(path);
                yield return new ActorSelectionRoutee(actor);
            }
        }
    }
}
