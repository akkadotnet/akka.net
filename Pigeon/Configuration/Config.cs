using Pigeon.Actor;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Configuration
{


    public class Settings
    {
        public T GetOrDefault<T>(Func<dynamic,dynamic> setting, T defaultValue)
        {
            dynamic res = setting(Config);
            if (res is T)
            {
                return (T)res;
            }
            return defaultValue;
        }
        public Settings(ActorSystem system)
        {
            //TODO: this is just in lack of something real
            this.System = system;
            var json = @"
            {
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
                        Host : ""127.0.0.1"",
                        Port : 0
                    }
                }
            }
            ";

            dynamic res = JsonConfig.Config.ApplyJson(json);            
            this.Config = res;
        }

        public ActorSystem System { get;private set; }
        public dynamic Config { get; set; }
    }
}
