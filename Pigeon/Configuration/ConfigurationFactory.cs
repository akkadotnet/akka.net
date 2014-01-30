using Pigeon.Actor;
using Pigeon.Configuration;
using Pigeon.Configuration.Hocon;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Reflection;
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
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "Pigeon.Configuration.Pigeon.conf";

            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            using (StreamReader reader = new StreamReader(stream))
            {
                string result = reader.ReadToEnd();

                return ParseString(result);
            }
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
