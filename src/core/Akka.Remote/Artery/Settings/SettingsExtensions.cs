using Akka.Configuration;
using System.Net;
using System.Net.Sockets;

namespace Akka.Remote.Artery.Settings
{
    internal static class SettingsExtensions
    {
        public static string GetHostname(this Config config, string key)
        {
            var value = config.GetString(key);
            switch (value)
            {
                case "<getHostAddress>":
                    var host = Dns.GetHostEntry(Dns.GetHostName());
                    foreach (var ip in host.AddressList)
                    {
                        if (ip.AddressFamily == AddressFamily.InterNetwork || ip.AddressFamily == AddressFamily.InterNetworkV6)
                            return ip.ToString();
                    }
                    throw new ConfigurationException("No network adapter with an IPv4 nor IPv6 address found in host machine.");

                case "<getHostName>":
                    return Dns.GetHostName();

                default:
                    return value;
            }
        }

        public static Transport GetTransport(this Config config, string key)
        {
            var transport = config.GetString(key).ToLowerInvariant();
            switch (transport)
            {
                case "aeron-udp":
                    throw new ConfigurationException("Aeron transport is not supported yet.");
                    // return Transport.AeronUpd;
                case "tcp": return Transport.Tcp;
                case "tls-tcp": return Transport.TlsTcp;
                default:
                    throw new ConfigurationException($"Unknown transport [{transport}], possible values: \"aeron-udp\", \"tcp\", or \"tls-tcp\"");
            }
        }

    }
}
