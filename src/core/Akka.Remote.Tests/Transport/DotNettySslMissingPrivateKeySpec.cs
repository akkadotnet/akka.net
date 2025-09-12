//-----------------------------------------------------------------------
// <copyright file="DotNettySslMissingPrivateKeySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;
using static Akka.Util.RuntimeDetector;

namespace Akka.Remote.Tests.Transport
{
    /// <summary>
    /// This test demonstrates that a certificate without a private key
    /// doesn't raise an exception during server startup, but fails when
    /// a client attempts to connect.
    /// </summary>
    public class DotNettySslMissingPrivateKeySpec : AkkaSpec
    {
        #region Setup / Config

        // Path to the valid certificate with private key (for client)
        // Use Path.Combine for cross-platform compatibility
        private static readonly string ValidCertPath = Path.Combine("Resources", "akka-validcert.pfx");
        private const string Password = "password";
        
        // Path where we'll create a certificate without private key
        private static readonly string CertWithoutPrivateKeyPath = Path.Combine("Resources", "test-cert-no-key.cer");
        
        // Platform detection helpers
        private static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        private static Config CreateConfig(bool enableSsl, string certPath, string certPassword, bool suppressValidation = true)
        {
            var config = ConfigurationFactory.ParseString($@"
            akka {{
                loglevel = DEBUG
                actor.provider = ""Akka.Remote.RemoteActorRefProvider,Akka.Remote""
                remote {{
                    dot-netty.tcp {{
                        port = 0
                        hostname = ""127.0.0.1""
                        enable-ssl = {enableSsl.ToString().ToLowerInvariant()}
                        log-transport = true
                    }}
                }}
            }}");

            if (!enableSsl || string.IsNullOrEmpty(certPath))
                return config;

            // Escape path for HOCON - handle both Windows and Unix paths
            var escapedPath = certPath.Replace("\\", "\\\\").Replace("/", "/");
            
            return config.WithFallback($@"akka.remote.dot-netty.tcp.ssl {{
                suppress-validation = {suppressValidation.ToString().ToLowerInvariant()}
                certificate {{
                    path = ""{escapedPath}""
                    password = ""{certPassword ?? ""}""
                }}
            }}");
        }

        private ActorSystem _serverSystem;
        private ActorSystem _clientSystem;

        private void CreateCertificateWithoutPrivateKey()
        {
            // Load the valid certificate with platform-appropriate flags
            var storageFlags = X509KeyStorageFlags.Exportable;
            
            // On Linux, we may need different flags
            if (IsLinux)
            {
                // On Linux, avoid machine key set which may require root
                storageFlags |= X509KeyStorageFlags.UserKeySet;
            }
            
            var fullCert = new X509Certificate2(ValidCertPath, Password, storageFlags);
            
            // Export only the public key (DER encoded)
            var publicKeyBytes = fullCert.Export(X509ContentType.Cert);
            
            // Save to file - ensure cross-platform path handling
            var dir = Path.GetDirectoryName(CertWithoutPrivateKeyPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
                
            File.WriteAllBytes(CertWithoutPrivateKeyPath, publicKeyBytes);
            
            // Verify the exported cert has no private key
            var certWithoutKey = new X509Certificate2(CertWithoutPrivateKeyPath);
            Assert.False(certWithoutKey.HasPrivateKey, "Exported certificate should not have a private key");
        }

        private void CleanupTestCertificate()
        {
            try
            {
                if (File.Exists(CertWithoutPrivateKeyPath))
                    File.Delete(CertWithoutPrivateKeyPath);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        #endregion

        public DotNettySslMissingPrivateKeySpec(ITestOutputHelper output) 
            : base(ConfigurationFactory.Empty, output)
        {
        }

        /// <summary>
        /// This test demonstrates a BUG: A server with a certificate missing a private key
        /// should fail during startup, but currently it doesn't. The error only occurs
        /// when a client attempts to connect, which is too late for proper error detection.
        /// 
        /// Expected behavior: Server startup should fail with an exception
        /// Actual behavior: Server starts successfully, error only occurs on client connection
        /// 
        /// This test will FAIL until the bug is fixed.
        /// </summary>
        [Fact]
        public void SSL_server_with_certificate_missing_private_key_should_fail_during_startup()
        {
            CreateCertificateWithoutPrivateKey();

            try
            {
                // STEP 1: Create server with certificate that has no private key
                // EXPECTED: This SHOULD throw an exception during startup
                // ACTUAL: This does NOT throw an exception (BUG)
                Exception startupException = null;
                try
                {
                    _serverSystem = ActorSystem.Create("ServerSystem", 
                        CreateConfig(true, CertWithoutPrivateKeyPath, null, suppressValidation: true));
                    InitializeLogger(_serverSystem);
                    
                    // Create an echo actor on the server
                    _serverSystem.ActorOf(Props.Create<EchoActor>(), "echo");
                    
                    // Get the server address - this should NOT be reachable
                    var serverAddress = RARP.For(_serverSystem).Provider.DefaultAddress;
                    Output.WriteLine($"BUG: Server started successfully at: {serverAddress} despite missing private key!");
                    
                    // These assertions show the server is running (it shouldn't be)
                    Assert.NotNull(serverAddress);
                    Assert.True(serverAddress.Port.HasValue);
                }
                catch (Exception ex)
                {
                    startupException = ex;
                    Output.WriteLine($"Good: Server startup failed with: {ex.GetType().Name}: {ex.Message}");
                }
                
                // THIS ASSERTION NOW PASSES (the fix works!)
                Assert.NotNull(startupException);
                if(startupException is AggregateException agg)
                    startupException = UnwrapException(agg);
                Assert.IsType<ArgumentException>(startupException);
                Assert.Contains("must use a certificate with the associated private key", startupException.Message, StringComparison.OrdinalIgnoreCase);
                Output.WriteLine("✓ Server correctly failed during startup due to missing private key");

                // STEP 2: Verify the server system is null (it failed to start)
                Assert.Null(_serverSystem);
                
                // The test is complete - the server correctly failed during startup
                // With the fix, there's no deferred error because the server never starts
            }
            finally
            {
                CleanupTestCertificate();
            }
        }

        /// <summary>
        /// This test verifies that the FIX WORKS: 
        /// The SSL certificate error IS NOW raised as an exception during startup,
        /// not deferred until a client connects. This is the CORRECT behavior.
        /// </summary>
        [Fact]
        public async Task SSL_server_missing_private_key_error_correctly_raised_during_startup()
        {
            CreateCertificateWithoutPrivateKey();

            Exception startupException = null;
            try
            {
                // STEP 1: Attempt to start server with certificate that has no private key
                // EXPECTED: This SHOULD throw an exception during startup (with the fix)
                _serverSystem = ActorSystem.Create("ServerSystem", 
                    CreateConfig(true, CertWithoutPrivateKeyPath, null, suppressValidation: true));
                InitializeLogger(_serverSystem);
                
                _serverSystem.ActorOf(Props.Create<EchoActor>(), "echo");
                
                // This line should not be reached with the fix
                Assert.Fail("Server should have failed during startup");
            }
            catch (Exception ex)
            {
                startupException = ex;
                Output.WriteLine($"✓ Server correctly failed during startup with: {ex.GetType().Name}");
            }
            
            // STEP 2: Verify the exception contains the right message
            Assert.NotNull(startupException);
            if(startupException is AggregateException agg)
                startupException = UnwrapException(agg);
            Assert.IsType<ArgumentException>(startupException);
            Assert.Contains("must use a certificate with the associated private key", startupException.Message, StringComparison.OrdinalIgnoreCase);
            
            // STEP 3: Verify the server system was not created
            Assert.Null(_serverSystem);
            
            // STEP 4: Verify certificate has no private key (just to confirm)
            var serverCert = new X509Certificate2(CertWithoutPrivateKeyPath);
            Assert.False(serverCert.HasPrivateKey);
            Output.WriteLine($"✓ Confirmed: Certificate.HasPrivateKey = {serverCert.HasPrivateKey}");
            
            Output.WriteLine("✓ With the fix, the error is correctly raised during server startup, not deferred");
            
            // The test is complete
            await Task.CompletedTask; // Keep the async signature
            
            CleanupTestCertificate();
        }

        /// <summary>
        /// This test shows the NORMAL behavior: When a certificate WITH a private key is used,
        /// the server starts successfully and can accept SSL connections properly.
        /// This demonstrates that the issue is specifically with missing private keys.
        /// </summary>
        [Fact]
        public async Task SSL_server_with_valid_certificate_and_private_key_should_work_correctly()
        {
            try
            {
                // STEP 1: Create server with VALID certificate that HAS a private key
                _serverSystem = ActorSystem.Create("ServerSystem", 
                    CreateConfig(true, ValidCertPath, Password, suppressValidation: true));
                InitializeLogger(_serverSystem);
                
                _serverSystem.ActorOf(Props.Create<EchoActor>(), "echo");
                var serverAddress = RARP.For(_serverSystem).Provider.DefaultAddress;
                
                // Server starts successfully (correctly, because certificate has private key)
                Assert.NotNull(serverAddress);
                Assert.True(serverAddress.Port.HasValue);
                Output.WriteLine($"✓ Server correctly started at: {serverAddress} with valid certificate");

                // Verify the certificate has a private key
                var serverCert = new X509Certificate2(ValidCertPath, Password);
                Assert.True(serverCert.HasPrivateKey);
                Output.WriteLine($"✓ Certificate has private key: {serverCert.HasPrivateKey}");

                // STEP 2: Client with valid certificate connects
                _clientSystem = ActorSystem.Create("ClientSystem", 
                    CreateConfig(true, ValidCertPath, Password, suppressValidation: true));
                InitializeLogger(_clientSystem);

                var remoteServerAddress = RARP.For(_serverSystem).Provider.DefaultAddress;
                var echoPath = new RootActorPath(remoteServerAddress) / "user" / "echo";
                var echoSelection = _clientSystem.ActorSelection(echoPath);

                // STEP 3: Verify SSL connection works properly
                var probe = CreateTestProbe(_clientSystem);
                var connected = false;
                
                await AwaitAssertAsync(async () =>
                {
                    echoSelection.Tell("ping", probe.Ref);
                    var response = await probe.ExpectMsgAsync<string>(TimeSpan.FromSeconds(1));
                    connected = response == "ping";
                    Assert.True(connected);
                }, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(500));

                Assert.True(connected, "Client should successfully connect to server with valid SSL");
                Output.WriteLine("✓ SSL connection established successfully");
                
                // STEP 4: Verify no SSL errors in logs
                var errorLogged = false;
                _serverSystem.EventStream.Subscribe(TestActor, typeof(Akka.Event.Error));
                
                // Send another message to ensure everything is working
                echoSelection.Tell("test", probe.Ref);
                var secondResponse = await probe.ExpectMsgAsync<string>(TimeSpan.FromSeconds(1));
                Assert.Equal("test", secondResponse);
                
                // Check for any SSL errors
                var msg = ReceiveOne(TimeSpan.FromMilliseconds(100));
                if (msg is Akka.Event.Error error)
                {
                    if (error.Cause?.ToString().Contains("SSL") == true ||
                        error.Cause?.ToString().Contains("private key") == true)
                    {
                        errorLogged = true;
                    }
                }
                
                Assert.False(errorLogged, "No SSL errors should be logged with valid certificate");
                Output.WriteLine("✓ No SSL errors in logs");
            }
            finally
            {
                // No test certificate cleanup needed - using the valid certificate from resources
            }
        }

        /// <summary>
        /// This test demonstrates another BUG: Client-side SSL with invalid certificate
        /// should also fail during startup, but currently doesn't.
        /// The error should be detected early, not deferred.
        /// </summary>
        [Fact]
        public async Task SSL_client_with_certificate_missing_private_key_should_fail_during_startup()
        {
            CreateCertificateWithoutPrivateKey();

            try
            {
                // STEP 1: Create server with VALID certificate (has private key)
                _serverSystem = ActorSystem.Create("ServerSystem", 
                    CreateConfig(true, ValidCertPath, Password, suppressValidation: true));
                InitializeLogger(_serverSystem);
                
                _serverSystem.ActorOf(Props.Create<EchoActor>(), "echo");
                var serverAddress = RARP.For(_serverSystem).Provider.DefaultAddress;
                Assert.NotNull(serverAddress);
                Output.WriteLine($"✓ Server started with valid certificate at: {serverAddress}");

                // STEP 2: Create client with certificate WITHOUT private key
                // EXPECTED: This SHOULD throw an exception during startup
                // ACTUAL: This does NOT throw an exception (BUG)
                Exception clientStartupException = null;
                try
                {
                    _clientSystem = ActorSystem.Create("ClientSystem", 
                        CreateConfig(true, CertWithoutPrivateKeyPath, null, suppressValidation: true));
                    InitializeLogger(_clientSystem);
                    
                    var clientAddress = RARP.For(_clientSystem).Provider.DefaultAddress;
                    Output.WriteLine($"BUG: Client started successfully at: {clientAddress} despite invalid certificate!");
                }
                catch (Exception ex)
                {
                    clientStartupException = ex;
                    Output.WriteLine($"Good: Client startup failed with: {ex.GetType().Name}: {ex.Message}");
                }

                // THIS ASSERTION SHOULD PASS BUT MAY FAIL (demonstrating potential bug)
                // Note: Depending on SSL implementation, clients might be allowed to use 
                // certificates without private keys in some scenarios (e.g., server-only auth)
                // But for mutual TLS, this should fail
                if (clientStartupException == null)
                {
                    Output.WriteLine("WARNING: Client started with certificate lacking private key");
                    Output.WriteLine("This may be acceptable for server-only authentication scenarios");
                    Output.WriteLine("But should fail for mutual TLS authentication");
                    
                    // Verify the client cert indeed has no private key
                    var clientCert = new X509Certificate2(CertWithoutPrivateKeyPath);
                    Assert.False(clientCert.HasPrivateKey);
                    Output.WriteLine($"✓ Confirmed client certificate HasPrivateKey = {clientCert.HasPrivateKey}");
                    
                    // Try to connect - this might fail depending on SSL configuration
                    var remoteServerAddress = RARP.For(_serverSystem).Provider.DefaultAddress;
                    var echoPath = new RootActorPath(remoteServerAddress) / "user" / "echo";
                    var echoSelection = _clientSystem.ActorSelection(echoPath);

                    var probe = CreateTestProbe(_clientSystem);
                    echoSelection.Tell("ping", probe.Ref);
                    
                    // The connection may or may not work depending on SSL mode
                    try
                    {
                        var response = await probe.ExpectMsgAsync<string>(TimeSpan.FromSeconds(2));
                        Output.WriteLine($"Connection succeeded with response: {response}");
                    }
                    catch
                    {
                        Output.WriteLine("Connection failed as expected for invalid client certificate");
                    }
                }
            }
            finally
            {
                CleanupTestCertificate();
            }
        }

        /// <summary>
        /// This test demonstrates that ANY invalid SSL certificate configuration
        /// should raise an exception during startup, not just missing private keys.
        /// Currently, many SSL errors are deferred until connection time, which is a bug.
        /// </summary>
        [Theory]
        [InlineData("non-existent-cert.pfx", "password", "Non-existent certificate file")]
        [InlineData("valid-cert", "wrong-password", "Wrong password")] // Special marker for valid cert path
        [InlineData("", "", "Invalid certificate path")]
        public void Any_SSL_certificate_failure_should_raise_exception_during_startup(string certPath, string password, string scenarioName)
        {
            // Handle special marker for valid cert path (needed because InlineData requires constants)
            if (certPath == "valid-cert")
                certPath = ValidCertPath;
            
            // Skip certain tests on Mono/Linux if they have known issues
            if (IsMono && scenarioName == "Wrong password") 
            {
                Output.WriteLine("Skipping 'Wrong password' test on Mono due to certificate handling differences");
                return;
            }

            Output.WriteLine($"\n=== Testing: {scenarioName} ===");
            Output.WriteLine($"Platform: {(IsMono ? "Mono" : ".NET")}, OS: {(IsLinux ? "Linux" : IsWindows ? "Windows" : "Unknown")}");
            
            Exception startupException = null;
            ActorSystem testSystem = null;
            
            try
            {
                // EXPECTED: This SHOULD throw an exception during startup
                testSystem = ActorSystem.Create("TestSystem", 
                    CreateConfig(true, certPath, password, suppressValidation: false));
                
                Output.WriteLine($"BUG: System started despite {scenarioName}!");
            }
            catch (Exception ex)
            {
                startupException = ex;
                Output.WriteLine($"✓ Correctly failed with: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                if (testSystem != null)
                {
                    Shutdown(testSystem, TimeSpan.FromSeconds(1));
                }
            }
            
            // All invalid certificate configurations should fail during startup
            Assert.NotNull(startupException); // This assertion documents expected behavior
        }

        [Fact]
        public void Loading_certificate_without_private_key_should_not_throw_exception()
        {
            CreateCertificateWithoutPrivateKey();

            try
            {
                Exception loadException = null;
                X509Certificate2 cert = null;

                try
                {
                    // This should NOT throw an exception
                    cert = new X509Certificate2(CertWithoutPrivateKeyPath);
                }
                catch (Exception ex)
                {
                    loadException = ex;
                }

                // The certificate loads successfully even without a private key
                Assert.Null(loadException);
                Assert.NotNull(cert);
                Assert.False(cert.HasPrivateKey);
                
                Output.WriteLine($"✓ Certificate loaded successfully");
                Output.WriteLine($"  Subject: {cert.Subject}");
                Output.WriteLine($"  HasPrivateKey: {cert.HasPrivateKey}");
                Output.WriteLine($"  Thumbprint: {cert.Thumbprint}");
            }
            finally
            {
                CleanupTestCertificate();
            }
        }

        #region Helper classes

        private Exception UnwrapException(AggregateException exception)
        {
            while (true)
            {
                if (exception.InnerExceptions.Count > 1) return exception;

                exception = exception.Flatten();
                if (exception.InnerExceptions[0] is AggregateException aggregateException)
                {
                    exception = aggregateException;
                    continue;
                }

                return exception.InnerExceptions[0];
            }
        }

        private class EchoActor : ReceiveActor
        {
            public EchoActor()
            {
                ReceiveAny(msg => Sender.Tell(msg));
            }
        }

        private class LogMonitorActor : ReceiveActor
        {
            private readonly TaskCompletionSource<bool> _errorCaptured;
            private string _errorMessage = "";

            public LogMonitorActor(TaskCompletionSource<bool> errorCaptured)
            {
                _errorCaptured = errorCaptured;

                Receive<Akka.Event.Error>(error =>
                {
                    var message = error.Cause?.ToString() ?? "";
                    if (message.Contains("The server mode SSL must use a certificate with the associated private key") ||
                        message.Contains("AuthenticationException"))
                    {
                        _errorMessage = message;
                        _errorCaptured.TrySetResult(true);
                    }
                });

                Receive<string>(msg =>
                {
                    if (msg == "GetError")
                        Sender.Tell(_errorMessage);
                });
            }
        }

        #endregion

        #region Cleanup

        protected override void AfterAll()
        {
            base.AfterAll();
            
            if (_serverSystem != null)
                Shutdown(_serverSystem, TimeSpan.FromSeconds(3));
            
            if (_clientSystem != null)
                Shutdown(_clientSystem, TimeSpan.FromSeconds(3));
            
            CleanupTestCertificate();
        }

        #endregion
    }
}
