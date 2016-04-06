using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Remote.Transport.Streaming
{
    // TODO Work in progress
    public class SslStreamTransportSettings : NetworkStreamTransportSettings
    {
        private static readonly Config DefaultConfig = ConfigurationFactory.ParseString(@"
enabled-ssl-protocols = [Tls ,Tls11, Tls12]
check-server-certificate-revocation = true

certificate {
    store-location = LocalMachine
    //md5-thumbprint = ...
    //subject-name = ...
}");

        public X509Certificate Certificate { get; }

        public SslProtocols EnabledSslProtocols { get; }

        public bool CheckServerCertificateRevocation { get; }

        public RemoteCertificateValidationCallback CertificateValidationCallback { get; }

        public SslStreamTransportSettings(Config config)
            : base(config)
        {
            string enabledProtocolsParameter = "enabled-ssl-protocols";
            IList<string> enabledProtocols = config.GetStringList(enabledProtocolsParameter);

            foreach (string protocol in enabledProtocols)
            {
                SslProtocols flag;
                if (!Enum.TryParse(protocol, true, out flag))
                    throw new ArgumentException($"Invalid SslProtocol '{protocol}'", enabledProtocolsParameter);

                EnabledSslProtocols |= flag;
            }

            bool allowUntrusted = config.GetBoolean("insecure-allow-untrusted-server-certificate");
            if (allowUntrusted)
            {
                CheckServerCertificateRevocation = false;
                CertificateValidationCallback = (sender, certificate, chain, errors) => true;
            }
            else
            {
                CheckServerCertificateRevocation = config.GetBoolean("check-server-certificate-revocation", true);
                CertificateValidationCallback = null;
            }


            StoreLocation location;
            string storeLocationParameter = "store-location";
            string storeLocationString = config.GetString(storeLocationParameter);
            if (!Enum.TryParse(storeLocationString, out location))
                throw new ArgumentException($"Invalid StoreLocation '{storeLocationString}'", storeLocationParameter);

            //TODO Get the server certificate from thumbprint or subject name
        }

        public static X509Certificate2 GetCertificateFromThumbprint(StoreLocation location, string thumbprint)
        {
            if (thumbprint == null)
                return null;

            // When copy pasting from the Certificate UI, it often start with the Left-to-right mark
            // Remove it or we won't find the certificate.
            if (thumbprint[0] == 0x200E)
                thumbprint = thumbprint.Substring(1);

            X509Store store = new X509Store(StoreName.My, location);

            store.Open(OpenFlags.ReadOnly);

            var certificates = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false);

            X509Certificate2 result = null;

            if (certificates.Count > 0)
                result = certificates[0];

            store.Close();

            return result;
        }

        public static X509Certificate2 GetCertificateFromSubjectName(StoreLocation location, string subjectName)
        {
            X509Store store = new X509Store(StoreName.My, location);

            store.Open(OpenFlags.ReadOnly);

            var certificates = store.Certificates.Find(X509FindType.FindBySubjectName, subjectName, false);

            X509Certificate2 result = null;

            if (certificates.Count == 1)
            {
                result = certificates[0];
            }
            else if (certificates.Count > 1)
            {
                result = certificates.Cast<X509Certificate2>()
                                     .OrderByDescending(item => item.NotAfter)
                                     .First();
            }

            store.Close();

            return result;
        }
    }

    public class SslStreamTransport : NetworkStreamTransport
    {
        protected new SslStreamTransportSettings Settings { get; }

        public override string SchemeIdentifier
        {
            get { return "ssl.tcp"; }
        }

        public SslStreamTransport(ActorSystem system, Config config)
            :this(system, new SslStreamTransportSettings(config))
        { }

        public SslStreamTransport(ActorSystem system, SslStreamTransportSettings settings)
            : base(system, settings)
        {
            Settings = settings;
        }

        protected override async Task<AssociationHandle> CreateInboundAssociation(Stream stream, Address remoteAddress)
        {
            SslStream sslStream = new SslStream(stream, true);

            await sslStream.AuthenticateAsServerAsync(Settings.Certificate, false, Settings.EnabledSslProtocols, false);

            return await base.CreateInboundAssociation(sslStream, remoteAddress);
        }

        protected override async Task<AssociationHandle> CreateOutboundAssociation(Stream stream, Address localAddress, Address remoteAddress)
        {
            SslStream sslStream = new SslStream(stream, true, Settings.CertificateValidationCallback);

            await sslStream.AuthenticateAsClientAsync(remoteAddress.Host, null, Settings.EnabledSslProtocols, Settings.CheckServerCertificateRevocation);

            return await base.CreateOutboundAssociation(stream, localAddress, remoteAddress);
        }
    }
}