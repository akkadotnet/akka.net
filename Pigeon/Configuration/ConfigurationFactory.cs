using Pigeon.Actor;
using Pigeon.Configuration;
using Pigeon.Configuration.Hocon;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Configuration
{
    public class ConfigurationFactory
    {
        public static Config ParseString(string json)
        {
            var res = Parser.Parse(json);
            return new Config(res);
        }

        public static Config Load()
        {
            return ParseString("");
        }

        public static Config Default()
        {
            var json = @"
                Pigeon {
                    Actor {
                        Provider = """"
                        Serializers {
                            json = ""Pigeon.Serialization.JsonSerializer""
                            java = ""Pigeon.Serialization.JavaSerializer""
                            proto = ""Pigeon.Remote.Serialization.ProtobufSerializer""
                        }
                        DefaultDispatcher {
                            Throughput = 100
                        }              
                    }
                    Remote {
                        Server {
                            Host = ""127.0.0.1""
                            Port = 8080
                        }
                    }
                }
            ";

            return ParseString(json);
        }
    }

    

    public class Settings
    {        
        public Settings(ActorSystem system,Config config)
        {
            if (config == null)
                config = ConfigurationFactory.Default();
            this.System = system;                     
            this.Config = config;
        }

        public ActorSystem System { get;private set; }
        public Config Config { get; set; }
    }
}
