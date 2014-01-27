using Pigeon.Actor;
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
            //HACK: add braces
            json = "{" + Environment.NewLine + json + Environment.NewLine + "}";
            dynamic res = JsonConfig.Config.ApplyJson(json);
            return new Config(res);
        }

        public static Config Load()
        {
            return ParseString("");
        }

        public static Config Default()
        {
            var json = @"
                Pigeon : {
                    Actor : {
                        Provider : """",
                        Serializers : {
                            json : ""Pigeon.Serialization.JsonSerializer"",
                            java : ""Pigeon.Serialization.JavaSerializer"",
                            proto : ""Pigeon.Remote.Serialization.ProtobufSerializer""
                        }, 
                        DefaultDispatcher: {
                            Throughput : 100
                        }              
                    },
                    Remote : {
                        Server : {
                            Host : ""127.0.0.1"",
                            Port : 8080
                        }
                    }
                }
            ";

            return ParseString(json);
        }
    }

    public class Config
    {
        public dynamic Data { get; private set; }

        public Config(dynamic res)
        {
            this.Data = res;
        }

    }

    public class Settings
    {
        public T GetOrDefault<T>(Func<dynamic,dynamic> setting, T defaultValue)
        {
            dynamic res = setting(Config.Data);
            if (res is T)
            {
                return (T)res;
            }
            return defaultValue;
        }
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
