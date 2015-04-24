using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;

namespace Cluster.Test
{
	class Program
	{
		static void Main(string[] args)
		{
			var config = ConfigurationFactory.ParseString(@"
			akka {
				log-config-on-start = on
				actor {
				  provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
				}
				remote {
					enabled-transports = [""akka.remote.helios.udp""]
					helios.udp {
						transport-class = ""Akka.Remote.Transport.Helios.HeliosUdpTransport, Akka.Remote""
						transport-protocol = udp
						port = 50003
						hostname = localhost
					}
				}
				 cluster {
					seed-nodes = [""akka.udp://mycluster@localhost:50003""]
					    roles = [host]
					    auto-down-unreachable-after = 10s
				  }
				}
			}
			");
//			var config = ConfigurationFactory.ParseString(@"
//			akka {
//				log-config-on-start = on
//				actor {
//				  provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
//				}
//				remote {
//					enabled-transports = [""akka.remote.helios.tcp""]
//					helios.tcp {
//						transport-class = ""Akka.Remote.Transport.Helios.HeliosTcpTransport, Akka.Remote""
//						transport-protocol = tcp
//						port = 50003
//						hostname = localhost
//					}
//				}
//				 cluster {
//					seed-nodes = [""akka.tcp://mycluster@localhost:50003""]
//					    roles = [host]
//					    auto-down-unreachable-after = 10s
//				  }
//				}
//			}
//			");
			//Thread.Sleep(20000);
			using(var system = ActorSystem.Create("mycluster", config))
			{
				Console.ReadLine();	
			}
		}
	}
}
