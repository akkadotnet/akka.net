using Pigeon.Actor;
using Pigeon.Hocon;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Actor
{
    public class ConfigurationFactory
    {
        public static Config ParseString(string json)
        {
            var res = Pigeon.Hocon.Parser.Parse(json);
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

    public class Config
    {
        private Node node;

        public Config(Node node)
        {
            this.node = node;
        }

        private Node GetNode(string path)
        {
            var elements = path.Split('.');
            var node = this.node;
            foreach(var part in elements)
            {
                node = node.Children.FirstOrDefault(n => n.Id == part);
                if (node == null)
                    return null;
            }
            return node;
        }

        public int GetInt(string path)
        {
            var node = GetNode(path);
            if (node == null)
                return 100;

            return int.Parse(node.Value);
        }

        public string GetString(string path)
        {
            var node = GetNode(path);
            return node.Value;
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
