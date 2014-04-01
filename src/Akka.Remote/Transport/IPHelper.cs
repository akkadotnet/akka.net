using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;

namespace Akka.Remote.Transport
{
    /// <summary>
    /// INTERNAL API
    /// 
    /// Used to resolve the local,
    /// </summary>
    internal static class IPHelper
    {
        private static IList<string> _localAddresses;

        /// <summary>
        /// Gets a list of all available network-facing (non-LOCALHOST) ip addresses
        /// </summary>
        public static IList<string> UnicastAddresses
        {
            get
            {
                if (_localAddresses != null) return _localAddresses;
                _localAddresses = new List<string>();
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    var properties = nic.GetIPProperties();
                    foreach (var ip in properties.UnicastAddresses)
                    {
                        _localAddresses.Add(ip.Address.ToString());
                    }
                }

                return _localAddresses;
            }
        }

        /// <summary>
        /// Returns a default IP address
        /// </summary>
        public static string DefaultIp
        {
            get { return UnicastAddresses.First(); }
        }
    }
}
