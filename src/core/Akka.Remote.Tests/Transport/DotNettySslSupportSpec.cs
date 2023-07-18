//-----------------------------------------------------------------------
// <copyright file="DotNettySslSupportSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Akka.TestKit.Xunit2.Attributes;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using static Akka.Util.RuntimeDetector;

namespace Akka.Remote.Tests.Transport
{
    public class DotNettySslSupportSpec : AkkaSpec
    {
        #region Setup / Config

        // valid to 01/01/2037
        private const string ValidCertPath = "Resources/akka-validcert.pfx";

        private const string Password = "password";

        private static Config TestConfig(string certPath, string password)
        {
            var enableSsl = !string.IsNullOrEmpty(certPath);
            var config = ConfigurationFactory.ParseString(@"
            akka {
                loglevel = DEBUG
                actor.provider = ""Akka.Remote.RemoteActorRefProvider,Akka.Remote""
                remote {
                    dot-netty.tcp {
                        port = 0
                        hostname = ""127.0.0.1""
                        enable-ssl = """ + enableSsl.ToString().ToLowerInvariant() + @"""
                        log-transport = true
                    }
                }
            }");
            return !enableSsl
                ? config
                : config.WithFallback(@"akka.remote.dot-netty.tcp.ssl {
                    suppress-validation = """ + enableSsl.ToString().ToLowerInvariant() + @"""
                    certificate {
                        path = """ + certPath + @"""
                        password = """ + password + @"""
                    }
                }");
        }

        private static Config TestConfig(bool enableSsl, string certPath, string password)
        {
            var config = ConfigurationFactory.ParseString(@"
            akka {
                loglevel = DEBUG
                actor.provider = ""Akka.Remote.RemoteActorRefProvider,Akka.Remote""
                remote {
                    dot-netty.tcp {
                        port = 0
                        hostname = ""127.0.0.1""
                        enable-ssl = """ + enableSsl.ToString().ToLowerInvariant() + @"""
                        log-transport = true
                    }
                }
            }");
            return !enableSsl
                ? config
                : config.WithFallback(@"akka.remote.dot-netty.tcp.ssl {
                    suppress-validation = """ + enableSsl.ToString().ToLowerInvariant() + @"""
                    certificate {
                        path = """ + certPath + @"""
                        password = """ + password + @"""
                    }
                }");
        }

        private static Config TestThumbprintConfig(string thumbPrint)
        {
            var config = ConfigurationFactory.ParseString(@"
            akka {
                loglevel = DEBUG
                actor.provider = ""Akka.Remote.RemoteActorRefProvider,Akka.Remote""
                remote {
                    dot-netty.tcp {
                        port = 0
                        hostname = ""127.0.0.1""
                        enable-ssl = ""true""
                        log-transport = true
                    }
                }
            }");
            return false
                ? config
                : config.WithFallback(@"akka.remote.dot-netty.tcp.ssl {
                    suppress-validation = ""true""
                    certificate {
                        use-thumprint-over-file = true
                        thumbprint = """ + thumbPrint + @"""
                        store-location = ""current-user""
                        store-name = ""My""
                    }
                }");
        }

        private ActorSystem _sys2;
        private Address _address1;
        private Address _address2;

        private ActorPath _echoPath;

        private void Setup(string certPath, string password)
        {
            _sys2 = ActorSystem.Create("sys2", TestConfig(certPath, password));
            InitializeLogger(_sys2);

            var echo = _sys2.ActorOf(Props.Create<Echo>(), "echo");

            _address1 = RARP.For(Sys).Provider.DefaultAddress;
            _address2 = RARP.For(_sys2).Provider.DefaultAddress;
            _echoPath = new RootActorPath(_address2) / "user" / "echo";
        }

        private void Setup(bool enableSsl, string certPath, string password)
        {
            _sys2 = ActorSystem.Create("sys2", TestConfig(enableSsl, certPath, password));
            InitializeLogger(_sys2);

            var echo = _sys2.ActorOf(Props.Create<Echo>(), "echo");

            _address1 = RARP.For(Sys).Provider.DefaultAddress;
            _address2 = RARP.For(_sys2).Provider.DefaultAddress;
            _echoPath = new RootActorPath(_address2) / "user" / "echo";
        }

        private void SetupThumbprint(string certPath, string password)
        {
            InstallCert();
            _sys2 = ActorSystem.Create("sys2", TestThumbprintConfig(Thumbprint));
            InitializeLogger(_sys2);

            var echo = _sys2.ActorOf(Props.Create<Echo>(), "echo");

            _address1 = RARP.For(Sys).Provider.DefaultAddress;
            _address2 = RARP.For(_sys2).Provider.DefaultAddress;
            _echoPath = new RootActorPath(_address2) / "user" / "echo";
        }

        #endregion

        // WARNING: YOU NEED TO RUN TEST IN ADMIN MODE IN ORDER TO ADD/REMOVE CERTIFICATES TO CERT STORE!
        public DotNettySslSupportSpec(ITestOutputHelper output) : base(TestConfig(ValidCertPath, Password), output)
        {
        }

        private string Thumbprint { get; set; }



        [Fact]
        public async Task Secure_transport_should_be_possible_between_systems_sharing_the_same_certificate()
        {
            // skip this test due to linux/mono certificate issues
            if (IsMono) return;

            Setup(ValidCertPath, Password);

            var probe = CreateTestProbe();

            await AwaitAssertAsync(async () =>
            {
                Sys.ActorSelection(_echoPath).Tell("hello", probe.Ref);
                await probe.ExpectMsgAsync("hello", TimeSpan.FromSeconds(3));
            }, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(100));
        }

        [LocalFact(SkipLocal = "Racy in Azure AzDo CI/CD")]
        public async Task Secure_transport_should_be_possible_between_systems_using_thumbprint()
        {
            // skip this test due to linux/mono certificate issues
            if (IsMono) return;
            try
            {
                SetupThumbprint(ValidCertPath, Password);

                var probe = CreateTestProbe();

                await WithinAsync(TimeSpan.FromSeconds(12), async () =>
                {
                    await AwaitAssertAsync(async () =>
                    {
                        Sys.ActorSelection(_echoPath).Tell("hello", probe.Ref);
                        await probe.ExpectMsgAsync("hello", TimeSpan.FromMilliseconds(100));
                    }, TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(100));
                });
            }
            finally
            {
                RemoveCert();
            }
        }

        [Fact]
        public async Task Secure_transport_should_NOT_be_possible_between_systems_using_SSL_and_one_not_using_it()
        {
            Setup(null, null);

            var probe = CreateTestProbe();
            await Assert.ThrowsAsync<RemoteTransportException>(async () =>
            {
                Sys.ActorSelection(_echoPath).Tell("hello", probe.Ref);
                await probe.ExpectNoMsgAsync();
            });
        }

        [Fact]
        public async Task If_EnableSsl_configuration_is_true_but_not_valid_certificate_is_provided_than_ArgumentNullException_should_be_thrown()
        {
            // skip this test due to linux/mono certificate issues
            if (IsMono) return;

            var aggregateException = await Assert.ThrowsAsync<AggregateException>(() => {
                Setup(true, null, Password);
                return Task.CompletedTask;
            });

            var realException = GetInnerMostException<ArgumentNullException>(aggregateException);
            Assert.NotNull(realException);
            Assert.Equal("Path to SSL certificate was not found (by default it can be found under `akka.remote.dot-netty.tcp.ssl.certificate.path`) (Parameter 'certificatePath')", realException.Message);
        }

        [Fact]
        public async Task If_EnableSsl_configuration_is_true_but_not_valid_certificate_password_is_provided_than_WindowsCryptographicException_should_be_thrown()
        {
            // skip this test due to linux/mono certificate issues
            if (IsMono) return;

            var aggregateException = await Assert.ThrowsAsync<AggregateException>(() => {
                Setup(true, ValidCertPath, null);
                return Task.CompletedTask;
            });

            var realException = GetInnerMostException<CryptographicException>(aggregateException);
            Assert.NotNull(realException);
            // TODO: this error message is not correct, but wanted to keep this assertion here in case someone else
            // wants to fix it in the future.
            //Assert.Equal("The specified network password is not correct.", realException.Message);
        }

        [Theory]
        [InlineData(ValidCertPath, null)]
        [InlineData(null, Password)]
        [InlineData(null, null)]
        [InlineData(ValidCertPath, Password)]
        public void If_EnableSsl_configuration_is_false_than_no_exception_should_be_thrown_even_no_cert_detail_were_provided(string certPath, string password)
        {
            Setup(false, certPath, password);
        }

        #region helper classes / methods

        protected override void AfterAll()
        {
            base.AfterAll();
            Shutdown(_sys2, TimeSpan.FromSeconds(3));
        }

        private void InstallCert()
        {
            using var store = new X509Store("My", StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            var cert = new X509Certificate2(ValidCertPath, Password);
            Thumbprint = cert.Thumbprint;
            store.Add(cert);
        }

        private void RemoveCert()
        {
            using var store = new X509Store("My", StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            var certs = store.Certificates.Find(X509FindType.FindByThumbprint, Thumbprint, false);
            if (certs.Count > 0)
            {
                store.Remove(certs[0]);
            }
        }

        private T GetInnerMostException<T>(Exception ex) where T : Exception
        {
            Exception currentEx = ex;
            while (currentEx.InnerException != null)
            {
                currentEx = currentEx.InnerException;
            }

            return currentEx as T;
        }

        private class Echo : ReceiveActor
        {
            public Echo()
            {
                Receive<string>(str => Sender.Tell(str));
            }
        }

        #endregion
    }
}
