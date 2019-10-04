using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Xunit;

namespace Akka.Cluster.Tests
{
    public class Bugfix3943Specs
    {
       public static string[] Hocons =
            {
                "akka : {\r\n    stdout-loglevel : INFO\r\n    loglevel : INFO\r\n    log-config-on-start : on\r\n    loggers : [\"Akka.Logger.Serilog.SerilogLogger, Akka.Logger.Serilog\"]\r\n    actor : {\r\n      debug : {\r\n        receive : on\r\n        autoreceive : on\r\n        lifecycle : on\r\n        event-stream : on\r\n        unhandled : on\r\n      }\r\n    }\r\n  }",
                "akka : {\r\n    remote : {\r\n      dot-netty : {\r\n        tcp : {\r\n          log-transport : true\r\n          transport-class : \"Akka.Remote.Transport.DotNetty.TcpTransport, Akka.Remote\"\r\n          transport-protocol : tcp\r\n          hostname : 0.0.0.0\r\n          public-hostname : localhost\r\n          port : 9000\r\n        }\r\n      }\r\n    }\r\n  }",
                "akka : {\r\n    actor : {\r\n      provider : \"Akka.Cluster.ClusterActorRefProvider, Akka.Cluster\"\r\n    }\r\n    cluster : {\r\n      log-info : on\r\n      seed-nodes : [\"akka.tcp://System@localhost:9000\"]\r\n      roles : []\r\n      role : []\r\n    }\r\n  }"
            };

       [Fact]
       public async Task Bugfix3943_should_start_ActorSystem()
       {
           var config = ConfigurationFactory.ParseString("");

           foreach (var hocon in Hocons)
           {
               config = config.WithFallback(Akka.Configuration.ConfigurationFactory.ParseString(hocon));
           }

           // NullReferenceException
           var system = ActorSystem.Create("System", config);

           var tcs = new TaskCompletionSource<Done>();
           Cluster.Get(system).RegisterOnMemberUp(() =>
           {
               tcs.SetResult(Done.Instance);
           });
           var cts = new CancellationTokenSource();
           cts.Token.Register(tcs.SetCanceled);
           cts.CancelAfter(TimeSpan.FromSeconds(5));
           await tcs.Task;
       }
    }
}
