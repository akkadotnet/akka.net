//-----------------------------------------------------------------------
// <copyright file="TlsConfigurationSample.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Security.Cryptography.X509Certificates;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Remote.Transport.DotNetty;

namespace Akka.Docs.Tests.Configuration
{
    /// <summary>
    /// TLS configuration examples for Akka.Remote documentation
    /// </summary>
    public class TlsConfigurationSample
    {
        private static X509Certificate2 LoadCertificate(string path, string password)
        {
#if NET10_0_OR_GREATER
            return X509CertificateLoader.LoadPkcs12FromFile(path, password);
#else
            return new X509Certificate2(path, password);
#endif
        }

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

        #region ProgrammaticMutualTlsSetup
        /// <summary>
        /// Example of programmatic mutual TLS setup using DotNettySslSetup with custom validation.
        /// This allows full programmatic control over certificate validation logic.
        /// </summary>
        public static void ProgrammaticMutualTlsSetup()
        {
            // Load or obtain your certificate
            var certificate = LoadCertificate("path/to/certificate.pfx", "password");

            // Create custom validator combining multiple validation strategies
            var customValidator = CertificateValidation.Combine(
                // Validate the certificate chain
                CertificateValidation.ValidateChain(),
                // Also pin against known thumbprints for additional security
                CertificateValidation.PinnedCertificate(certificate.Thumbprint)
            );

            // Setup SSL with custom validator taking precedence over HOCON config
            var sslSetup = new DotNettySslSetup(
                certificate: certificate,
                suppressValidation: false,
                requireMutualAuthentication: true,
                customValidator: customValidator
            );
        }
        #endregion

        #region CertificatePinningExample
        /// <summary>
        /// Example of certificate pinning - only accept certificates with specific thumbprints.
        /// Useful for preventing man-in-the-middle attacks with compromised CAs.
        /// </summary>
        public static void CertificatePinningSetup()
        {
            var certificate = LoadCertificate("path/to/certificate.pfx", "password");

            // Allow only specific certificates by thumbprint
            var validator = CertificateValidation.PinnedCertificate(
                "2531c78c51e5041d02564697a88af8bc7a7ce3e3",  // Production cert
                "abc123def456789ghi012jkl345mno678pqr901stu"  // Backup cert
            );

            var sslSetup = new DotNettySslSetup(
                certificate: certificate,
                suppressValidation: false,
                requireMutualAuthentication: true,
                customValidator: validator
            );
        }
        #endregion

        #region CustomValidationLogicExample
        /// <summary>
        /// Example of custom certificate validation logic combined with standard validation.
        /// Allows complete control over what certificates are accepted.
        /// </summary>
        public static void CustomValidationLogicSetup()
        {
            var certificate = LoadCertificate("path/to/certificate.pfx", "password");

            // Start with standard chain validation, then add custom logic
            var validator = CertificateValidation.ChainPlusThen(
                // Custom validation - check certificate subject matches expected peer
                (cert, chain, peer) =>
                {
                    // Accept only certificates from authorized-peer
                    if (cert?.Subject != null && cert.Subject.Contains("CN=authorized-peer"))
                    {
                        return true;  // Accept this certificate
                    }
                    return false;  // Reject all others
                }
            );

            var sslSetup = new DotNettySslSetup(
                certificate: certificate,
                suppressValidation: false,
                requireMutualAuthentication: true,
                customValidator: validator
            );
        }
        #endregion

        #region HostnameValidationExample
        /// <summary>
        /// Example of enabling traditional hostname validation for client-server architectures.
        /// Use when all nodes share the same certificate with matching CN/SAN.
        /// </summary>
        public static void HostnameValidationSetup()
        {
            var certificate = LoadCertificate("path/to/certificate.pfx", "password");

            // Enable both chain validation and hostname validation
            var sslSetup = new DotNettySslSetup(
                certificate: certificate,
                suppressValidation: false,
                requireMutualAuthentication: true,
                validateCertificateHostname: true  // Enable traditional TLS hostname validation
            );
        }
        #endregion

        #region SubjectValidationExample
        /// <summary>
        /// Example of subject DN validation - only accept certificates with specific subject names.
        /// Useful for verifying peer identity based on certificate subject.
        /// Supports wildcards: "CN=Akka-Node-*" matches "CN=Akka-Node-001"
        /// </summary>
        public static void SubjectValidationSetup()
        {
            var certificate = LoadCertificate("path/to/certificate.pfx", "password");

            // Accept certificates matching the subject pattern
            // Wildcards are supported: CN=Akka-Node-* matches CN=Akka-Node-001
            var validator = CertificateValidation.ValidateSubject(
                "CN=Akka-Node-*"  // Pattern to match
            );

            var sslSetup = new DotNettySslSetup(
                certificate: certificate,
                suppressValidation: false,
                requireMutualAuthentication: true,
                customValidator: validator
            );
        }
        #endregion
    }
}