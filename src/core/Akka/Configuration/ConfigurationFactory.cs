using System.Diagnostics;
using System.IO;
using System.Reflection;
using Akka.Configuration.Hocon;

namespace Akka.Configuration
{
    /// <summary>
    /// Class ConfigurationFactory.
    /// </summary>
    public class ConfigurationFactory
    {
        /// <summary>
        /// Gets the empty.
        /// </summary>
        /// <value>The empty.</value>
        public static Config Empty
        {
            get { return ParseString(""); }
        }

        /// <summary>
        /// Parses the string.
        /// </summary>
        /// <param name="hocon">The json.</param>
        /// <returns>Config.</returns>
        public static Config ParseString(string hocon)
        {
            HoconValue res = Parser.Parse(hocon);
            return new Config(res);
        }

        /// <summary>
        /// Loads this instance.
        /// </summary>
        /// <returns>Config.</returns>
        public static Config Load()
        {
            return ParseString("");
        }

        /// <summary>
        /// Defaults this instance.
        /// </summary>
        /// <returns>Config.</returns>
        public static Config Default()
        {
            return FromResource("Akka.Configuration.Pigeon.conf");
        }

        /// <summary>
        /// Froms the resource.
        /// </summary>
        /// <param name="resourceName">Name of the resource.</param>
        /// <returns>Config.</returns>
        internal static Config FromResource(string resourceName)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();

            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                Debug.Assert(stream != null, "stream != null");
                using (var reader = new StreamReader(stream))
                {
                    string result = reader.ReadToEnd();

                    return ParseString(result);
                }
            }
        }
    }
}