using System.IO;
using System.Reflection;
using Akka.Configuration.Hocon;

namespace Akka.Configuration
{
    public class ConfigurationFactory
    {
        public static Config Empty
        {
            get { return ParseString(""); }
        }

        public static Config ParseString(string json)
        {
            HoconValue res = Parser.Parse(json);
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
            Assembly assembly = Assembly.GetExecutingAssembly();

            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            using (var reader = new StreamReader(stream))
            {
                string result = reader.ReadToEnd();

                return ParseString(result);
            }
        }
    }
}