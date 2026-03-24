using System.Linq;
using System.Net;
using System.Net.Sockets;
using Akka.Configuration;

#nullable enable
namespace Akka.MultiNode.TestAdapter.Configuration
{
    /// <summary>
    /// MultiNodeTestRunnerOptions
    /// </summary>
    public class MultiNodeTestRunnerOptions
    {
        /// <summary>
        /// Default options
        /// </summary>
        public static readonly MultiNodeTestRunnerOptions Default = new MultiNodeTestRunnerOptions();

        /// <summary>
        /// File output directory
        /// </summary>
        public string OutputDirectory { get; set; } = "TestResults";

        /// <summary>
        /// Subdirectory to store failed specs logs
        /// </summary>
        public string FailedSpecsDirectory { get; set; } = "FAILED_SPECS_LOGS";

        /// <summary>
        /// MNTR controller listener address
        /// </summary>
        public string ListenAddress
        {
            get => ListenIpAddress.ToString();
            set
            {
                if (!IPAddress.TryParse(value, out var address))
                {
                    var addresses = Dns.GetHostAddresses(value);
                    address = 
                        addresses.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork) ??
                        addresses.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetworkV6) ??
                        throw new ConfigurationException($"Invalid ListenAddress [{value}]. ListenAddress must be IPv4, IPv6, or a host name");
                }

                ListenIpAddress = address;
            } 
        }
        
        public IPAddress ListenIpAddress { get; private set; } = IPAddress.Parse("127.0.0.1");

        /// <summary>
        /// MNTR controller listener port. Set 0 to use random available port
        /// </summary>
        public int ListenPort { get; set; }

        /// <summary>
        /// If set, performs output directory cleanup before running tests
        /// </summary>
        public bool AppendLogOutput { get; set; } = true;

        public string? Platform { get; set; }

        public bool UseBuiltInTrxReporter { get; set; }
    }
}