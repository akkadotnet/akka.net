using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Pigeon.Configuration;
using Pigeon.Routing;

namespace Pigeon.Actor
{
    public class Deploy
    {
        private static readonly string NoDispatcherGiven = null;
        private static readonly string NoMailboxGiven = null;
        private static readonly Scope NoScopeGiven = null;
        /*
         path: String = "",
  config: Config = ConfigFactory.empty,
  routerConfig: RouterConfig = NoRouter,
  scope: Scope = NoScopeGiven,
  dispatcher: String = Deploy.NoDispatcherGiven,
  mailbox: String = Deploy.NoMailboxGiven)
         */

        public Deploy()
        {
            Path = "";
            Config = ConfigurationFactory.Empty;
            RouterConfig = RouterConfig.NoRouter;
            Scope = Deploy.NoScopeGiven;
            Dispatcher = Deploy.NoDispatcherGiven;
            Mailbox = Deploy.NoMailboxGiven;
        }

        public string Path { get;private set; }
        public Config Config { get;private set; }
        public RouterConfig RouterConfig { get;private set; }
        public Scope Scope { get;private set; }
        public string Mailbox { get;private set; }
        public string Dispatcher { get;private set; }

    }

    public class Scope
    {
        public static readonly LocalScope Local = new LocalScope();
        private Scope fallback;

        public Scope WithFallback(Scope other)
        {
            var copy = Copy();
            copy.fallback = other;
            return copy;
        }

        private Scope Copy()
        {
            return new Scope()
            {
                fallback = this.fallback,
            };
        }
    }

    public class LocalScope : Scope
    {
        
    }

    public class RemoteScope : Scope
    {
        public RemoteScope(Address address)
        {
            this.Address = address;

        }

        public Address Address { get;private set; }
    }
}
