using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Configuration;

namespace Akka.Remote.TestKit
{
    /// <summary>
    /// Configure the role names and participants of the test, including configuration settings
    /// </summary>
    public abstract class MultiNodeConfig
    {
        private Config _commonConf = null;
    }
}
