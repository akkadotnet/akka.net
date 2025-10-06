//-----------------------------------------------------------------------
// <copyright file="TlsConfigurationSample.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;

namespace Akka.Docs.Tests.Configuration
{
    /// <summary>
    /// TLS configuration examples for Akka.Remote documentation
    /// </summary>
    public class TlsConfigurationSample
    {
        #region MutualTlsConfig
        public static Config MutualTlsConfiguration = ConfigurationFactory.ParseString(@"
            akka.remote.dot-netty.tcp {
                enable-ssl = true
                ssl {
                    suppress-validation = false
                    require-mutual-authentication = true  # Both client and server authenticate
                    certificate {
                        path = ""path/to/certificate.pfx""
                        password = ""certificate-password""
                    }
                }
            }
        ");
        #endregion

        #region StandardTlsConfig
        public static Config StandardTlsConfiguration = ConfigurationFactory.ParseString(@"
            akka.remote.dot-netty.tcp {
                enable-ssl = true
                ssl {
                    suppress-validation = false
                    require-mutual-authentication = false  # Server authentication only
                    certificate {
                        path = ""path/to/certificate.pfx""
                        password = ""certificate-password""
                    }
                }
            }
        ");
        #endregion

        #region WindowsCertStoreConfig
        public static Config WindowsCertificateStoreConfiguration = ConfigurationFactory.ParseString(@"
            akka.remote.dot-netty.tcp {
                enable-ssl = true
                ssl {
                    suppress-validation = false
                    require-mutual-authentication = true
                    certificate {
                        use-thumbprint-over-file = true
                        thumbprint = ""2531c78c51e5041d02564697a88af8bc7a7ce3e3""
                        store-name = ""My""
                        store-location = ""local-machine""  # or ""current-user""
                    }
                }
            }
        ");
        #endregion

        #region DevTlsConfig
        // WARNING: Development only - never use suppress-validation = true in production!
        public static Config DevelopmentTlsConfiguration = ConfigurationFactory.ParseString(@"
            akka.remote.dot-netty.tcp {
                enable-ssl = true
                ssl {
                    suppress-validation = true  # INSECURE: Accepts any certificate
                    require-mutual-authentication = false
                    certificate {
                        path = ""self-signed-dev-cert.pfx""
                        password = ""password""
                    }
                }
            }
        ");
        #endregion
    }
}