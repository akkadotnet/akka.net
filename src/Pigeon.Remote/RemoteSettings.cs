using Pigeon.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Pigeon.Remote
{
    public class RemoteSettings
    {

        public RemoteSettings(Configuration.Config config)
        {
            this.Config = config;
        }

        public Config Config { get;private set; }
    }
}
