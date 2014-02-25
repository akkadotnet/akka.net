using Akka.Actor;
using Akka.Configuration;
using Akka.Configuration.Hocon;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Configuration
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
            return FromResource("Akka.Configuration.Pigeon.conf");
        }

        private static Config FromResource(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();

            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            using (StreamReader reader = new StreamReader(stream))
            {
                string result = reader.ReadToEnd();

                return ParseString(result);
            }
        }

        public static Config Empty
        {
            get
            {
                return ConfigurationFactory.ParseString("");
            }
        }
    }       
}
