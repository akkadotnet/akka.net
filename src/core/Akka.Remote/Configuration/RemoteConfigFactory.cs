using System.Diagnostics;
using System.IO;
using System.Reflection;
using Akka.Configuration;

namespace Akka.Remote.Configuration
{
    /// <summary>
    /// Internal class used for loading remote configuration values
    /// </summary>
    internal static class RemoteConfigFactory
    {
        /// <summary>
        /// Defaults this instance.
        /// </summary>
        /// <returns>Config.</returns>
        public static Config Default()
        {
            return FromResource("Akka.Remote.Configuration.Remote.conf");
        }

        /// <summary>
        /// Froms the resource.
        /// </summary>
        /// <param name="resourceName">Name of the resource.</param>
        /// <returns>Config.</returns>
        internal static Config FromResource(string resourceName)
        {
            Assembly assembly = typeof (RemoteConfigFactory).Assembly;

            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                Debug.Assert(stream != null, "stream != null");
                using (var reader = new StreamReader(stream))
                {
                    string result = reader.ReadToEnd();

                    return ConfigurationFactory.ParseString(result);
                }
            }
        }
    }
}
