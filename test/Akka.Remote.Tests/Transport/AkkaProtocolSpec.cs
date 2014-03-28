using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Configuration;
using Akka.Tests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Akka.Remote.Tests.Transport
{
    [TestClass]
    public class AkkaProtocolSpec : AkkaSpec
    {
        #region Setup / Config

        public class TestFailureDetector : FailureDetector
        {
            internal volatile bool isAvailable = true;
            public override bool IsAvailable
            {
                get { return isAvailable; }
            }

            internal volatile bool called = false;
            public override bool IsMonitoring
            {
                get { return called; }
            }

            public override void HeartBeat()
            {
                called = true;
            }
        }

        private Config config = ConfigurationFactory.ParseString(
        @"akka.remote {

            transport-failure-detector {
              implementation-class = ""akka.remote.PhiAccrualFailureDetector""
              threshold = 7.0
              max-sample-size = 100
              min-std-deviation = 100 ms
              acceptable-heartbeat-pause = 3 s
              heartbeat-interval = 1 s
            }

            backoff-interval = 1 s

            require-cookie = off

            secure-cookie = ""abcde""

            shutdown-timeout = 5 s

            startup-timeout = 5 s

            use-passive-connections = on
        }");

        #endregion

    }
}
