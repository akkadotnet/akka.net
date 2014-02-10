using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Pigeon.Configuration;

namespace Pigeon.Actor
{
    public class Deploy
    {
        /*
         path: String = "",
  config: Config = ConfigFactory.empty,
  routerConfig: RouterConfig = NoRouter,
  scope: Scope = NoScopeGiven,
  dispatcher: String = Deploy.NoDispatcherGiven,
  mailbox: String = Deploy.NoMailboxGiven)
         */

        public string Path { get;private set; }
        public Config Config { get;private set; }
        public Config RouterConfig { get;private set; }
        public string Mailbox { get;private set; }
        public string Dispatcher { get;private set; }

    }
}
