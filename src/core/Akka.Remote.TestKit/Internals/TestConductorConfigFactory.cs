using System.Diagnostics;
using System.IO;
using Akka.Configuration;

namespace Akka.Remote.TestKit.Internals
{
    /// <summary>
    /// Loads required Multi-Node TestKit configuration values embedded into the assembly
    /// and injects them in relevant tests.
    /// </summary>
    internal static class TestConductorConfigFactory
    {
        /// <summary>
        /// Defaults this instance.
        /// </summary>
        /// <returns>Config.</returns>
        public static Config Default()
        {
            return FromResource("Akka.Remote.TestKit.Internals.Reference.conf");
        }

        /// <summary>
        /// Froms the resource.
        /// </summary>
        /// <param name="resourceName">Name of the resource.</param>
        /// <returns>Config.</returns>
        internal static Config FromResource(string resourceName)
        {
            var assembly = typeof(TestConductorConfigFactory).Assembly;

            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                Debug.Assert(stream != null, "stream != null");
                using (var reader = new StreamReader(stream))
                {
                    var result = reader.ReadToEnd();

                    return ConfigurationFactory.ParseString(result);
                }
            }
        }
    }
}
