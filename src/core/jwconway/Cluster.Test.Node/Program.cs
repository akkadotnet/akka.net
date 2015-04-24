using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;

namespace Cluster.Test.Node
{
	class Program
	{
		static void Main(string[] args)
		{
			var config = ConfigurationFactory.ParseString(@"
			akka {
				actor {
				  provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
				}

				remote {
					enabled-transports = [""akka.remote.helios.udp""]
					helios.udp {
						transport-class = ""Akka.Remote.Transport.Helios.HeliosUdpTransport, Akka.Remote""
						transport-protocol = udp
						port = 0
						hostname = localhost
					}
				}

				cluster {
					seed-nodes = [""akka.udp://mycluster@localhost:50003""]
					roles = [plugin]
					auto-down-unreachable-after = 10s
				}
				}
			} 
			");
//			var config = ConfigurationFactory.ParseString(@"
//			akka {
//				actor {
//				  provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
//				}
//
//				remote {
//					enabled-transports = [""akka.remote.helios.tcp""]
//					helios.tcp {
//						transport-class = ""Akka.Remote.Transport.Helios.HeliosTcpTransport, Akka.Remote""
//						transport-protocol = tcp
//						port = 0
//						hostname = localhost
//					}
//				}
//
//				cluster {
//					seed-nodes = [""akka.tcp://mycluster@localhost:50003""]
//					roles = [plugin]
//					auto-down-unreachable-after = 10s
//				}
//				}
//			} 
//			");
			//Thread.Sleep(20000);
			for (int i = 0; i < 1; i++)
			{
				var system = ActorSystem.Create("mycluster", config);
			}
			Console.ReadLine();
		}
	}
}
