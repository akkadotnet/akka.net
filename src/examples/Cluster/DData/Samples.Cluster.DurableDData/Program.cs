using System;
using System.Threading.Tasks;
using Akka.Configuration;

namespace Samples.Cluster.DurableDData
{
    class Program
    {
        static async Task Main(string[] args)
        {
            
        }

        public static readonly Config BaseConfig = @"
            akka{
                loglevel = DEBUG
                actor.provider = cluster
                cluster{
                    seed-nodes = [""akka.tcp://ClusterSys@localhost:11000""]
                    roles = [ddata]
                    distributed-data{
                        durable.keys = [*]
                    }
                }
            }
        ";

        public static Config ConfigureNode(string lmdbFolder, int port)
        {

        }
    }
}
