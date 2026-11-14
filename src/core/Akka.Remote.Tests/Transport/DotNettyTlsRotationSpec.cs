// -----------------------------------------------------------------------
// <copyright file="DotNettyTlsRotationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Event;
using Akka.IO;
using Akka.Remote.Configuration;
using Akka.Remote.Transport;
using Akka.Remote.Transport.DotNetty;
using Akka.Tests.IO;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Transport
{
    /// <summary>
    /// A node asks its <see cref="TlsCertificateSource"/> for a certificate each time it builds a connection, so an
    /// operator rotates by adding the renewed certificate beside the old one. Each behaviour is written once as a
    /// theory and runs against every way to hold certificates (a folder of PKCS#12 files, a folder of PEM files, a
    /// store and a callback), using the fixtures that the <c>TlsCertificateSource</c> specs use.
    /// All nodes use mutual TLS. A peer shows the certificate it was given through its custom validator.
    /// </summary>
    public sealed class DotNettyTlsRotationSpec : AkkaSpec
    {
        private static readonly string[] Both = { TlsTestCertificates.ServerAuth, TlsTestCertificates.ClientAuth };

        private readonly TestCertificateAuthority _ca = TestCertificateAuthority.Create();
        private readonly List<SourceFixture> _fixtures = new List<SourceFixture>();
        private readonly List<X509Certificate2> _certificates = new List<X509Certificate2>();
        private readonly List<ActorSystem> _systems = new List<ActorSystem>();
        private int _nodeCount;

        // The sources keep the real 10 second interval: a spec that adds a certificate runs a look-up (RefreshAsync)
        // before it expects the next connection to use it. A node built from HOCON makes its own source: a spec
        // reaches it with SourceOf.
        public DotNettyTlsRotationSpec(ITestOutputHelper output) : base("akka.loglevel = INFO", output)
        {
        }

        public static TheoryData<string> AllKinds => SourceFixture.AllKinds;

        public static TheoryData<string> SkippingKinds => SourceFixture.SkippingKinds;

        // A callback hands over one certificate and cannot fall back to another one, so "skip the bad newer
        // certificate" does not apply to it. What a callback does with a certificate it cannot use is covered by
        // Should_Keep_Serving_..._When_The_Only_Certificate_Is_Lost and
        // Should_Fail_That_Connection_..._When_The_Only_Certificate_Has_Expired, which run on all four kinds.
        public static TheoryData<string, string> BadNewerCertificateCases
        {
            get
            {
                var data = new TheoryData<string, string>();
                foreach (var kind in SourceFixture.SkippingKindNames)
                {
                    data.Add(kind, "no-private-key");
                    data.Add(kind, "server-use-only");
                }

                return data;
            }
        }

        // ---- 1 and 2: a new connection uses the new certificate; an open one keeps working ----

        [Theory(DisplayName = "Should_Present_The_New_Certificate_On_New_Connections_When_A_Certificate_Is_Added")]
        [MemberData(nameof(AllKinds))]
        public async Task Should_present_the_new_certificate_on_new_connections_when_a_certificate_is_added(string kind)
        {
            var fixture = Open(kind);
            var oldLeaf = Leaf(fixture, 60);
            var newLeaf = Leaf(fixture, 180);
            fixture.Add(oldLeaf);
            var node = StartSourceNode(fixture);

            // The control: before the change the node presents the old certificate in both roles.
            (await PresentedAsync(node)).Should().Be((oldLeaf.Thumbprint, oldLeaf.Thumbprint));

            fixture.Add(newLeaf); // no restart
            await fixture.RefreshAsync(); // connections built after the next look-up use it

            (await PresentedAsync(node)).Should().Be((newLeaf.Thumbprint, newLeaf.Thumbprint));
        }

        [Theory(DisplayName = "Should_Keep_A_Connection_That_Was_Open_Before_The_Change_Working")]
        [MemberData(nameof(AllKinds))]
        public async Task Should_keep_a_connection_that_was_open_before_the_change_working(string kind)
        {
            var fixture = Open(kind);
            var oldLeaf = Leaf(fixture, 60);
            var newLeaf = Leaf(fixture, 180);
            fixture.Add(oldLeaf);
            var node = StartSourceNode(fixture);
            var peer = StartFixedNode();
            await ExchangeAsync(peer, node);
            var nodeHandshakes = node.Seen.Count;
            var peerHandshakes = peer.Seen.Count;

            fixture.Add(newLeaf);

            await ExchangeAsync(peer, node, "after the change");
            await ExchangeAsync(node, peer, "and back");
            // Nobody built a new connection: no handshake happened, and the old certificate is still the one in use.
            node.Seen.Count.Should().Be(nodeHandshakes);
            peer.Seen.Count.Should().Be(peerHandshakes);
            peer.Seen.Distinct().Should().Equal(oldLeaf.Thumbprint);
        }

        // ---- 3: old and new certificate meet, issued by a two-level test CA, a different leaf per node ----

        [Theory(DisplayName = "Should_Connect_In_Both_Directions_Between_A_Node_On_The_Old_Certificate_And_A_Node_On_The_New_One")]
        [MemberData(nameof(AllKinds))]
        public async Task Should_connect_in_both_directions_between_a_node_on_the_old_certificate_and_a_node_on_the_new_one(string kind)
        {
            var rotatedFixture = Open(kind);
            var oldFixture1 = Open(kind);
            var oldFixture2 = Open(kind);
            var rotatedOld = Leaf(rotatedFixture, 60);
            var rotatedNew = Leaf(rotatedFixture, 180);
            var old1 = Leaf(oldFixture1, 120);
            var old2 = Leaf(oldFixture2, 120);
            new[] { rotatedOld.Thumbprint, rotatedNew.Thumbprint, old1.Thumbprint, old2.Thumbprint }
                .Distinct().Should().HaveCount(4, "every node has its own leaf");
            rotatedFixture.Add(rotatedOld);
            oldFixture1.Add(old1);
            oldFixture2.Add(old2);
            var rotated = StartSourceNode(rotatedFixture);
            var oldNode1 = StartSourceNode(oldFixture1);
            var oldNode2 = StartSourceNode(oldFixture2);

            rotatedFixture.Add(rotatedNew); // the rotated node is now on the new certificate, the others are not
            await rotatedFixture.RefreshAsync();

            await ExchangeAsync(rotated, oldNode1); // the new certificate connects out, as a client
            await ExchangeAsync(oldNode2, rotated); // and accepts a connection, as a server

            oldNode1.Seen.Distinct().Should().Equal(rotatedNew.Thumbprint);
            oldNode2.Seen.Distinct().Should().Equal(rotatedNew.Thumbprint);
            rotated.Seen.Distinct().Should().BeEquivalentTo(old1.Thumbprint, old2.Thumbprint);
        }

        // ---- 4: a bad newer certificate is skipped ----

        [Theory(DisplayName = "Should_Skip_A_Bad_Newer_Certificate_And_Keep_Using_The_Old_One")]
        [MemberData(nameof(BadNewerCertificateCases))]
        public async Task Should_skip_a_bad_newer_certificate_and_keep_using_the_old_one(string kind, string badCase)
        {
            var fixture = Open(kind);
            var oldLeaf = Leaf(fixture, 60);
            fixture.Add(oldLeaf);
            var node = StartSourceNode(fixture);

            var bad = Leaf(fixture, 180, badCase == "server-use-only" ? new[] { TlsTestCertificates.ServerAuth } : Both);
            fixture.Add(bad, withKey: badCase != "no-private-key");
            await fixture.RefreshAsync();

            // Under mutual TLS one certificate serves both directions and must allow both uses, so the bad newer
            // one is skipped in both, and the skip is logged once.
            var warnings = CreateTestProbe();
            node.Actors.EventStream.Subscribe(warnings, typeof(Warning));
            (await PresentedAsync(node)).Should().Be((oldLeaf.Thumbprint, oldLeaf.Thumbprint));
            (await PresentedAsync(node)).Should().Be((oldLeaf.Thumbprint, oldLeaf.Thumbprint));

            // A PEM certificate with no key in the folder is reported too: a renewal that arrived without its key.
            var skipped = await warnings.FishForMessageAsync<Warning>(
                w => w.Message.ToString()!.Contains(bad.Thumbprint), TimeSpan.FromSeconds(5));
            skipped.Message.ToString().Should().Contain(badCase == "server-use-only"
                ? "does not permit client authentication"
                : kind == "pem-folder" ? "it has no private key in the folder" : "it has no private key this process can open");

            await warnings.ExpectNoMsgAsync(TimeSpan.FromSeconds(1)); // logged once, not once per connection
        }

        [Theory(DisplayName = "Should_Pick_Up_A_Newer_Server_Only_Certificate_When_Mutual_Tls_Is_Off")]
        [MemberData(nameof(SkippingKinds))]
        public async Task Should_pick_up_a_newer_server_only_certificate_when_mutual_tls_is_off(string kind)
        {
            var fixture = Open(kind);
            var oldLeaf = Leaf(fixture, 60);
            var serverOnly = Leaf(fixture, 180, TlsTestCertificates.ServerAuth);
            fixture.Add(oldLeaf);
            var node = StartSourceNode(fixture, requireMutualAuthentication: false);
            var peer = StartFixedNode(requireMutualAuthentication: false);

            await ExchangeAsync(peer, node);
            peer.Seen.Distinct().Should().Equal(oldLeaf.Thumbprint);

            fixture.Add(serverOnly); // a node that never presents a client certificate needs server use only
            await fixture.RefreshAsync();

            var freshPeer = StartFixedNode(requireMutualAuthentication: false);
            await ExchangeAsync(freshPeer, node);
            freshPeer.Seen.Distinct().Should().Equal(serverOnly.Thumbprint);
        }

        // ---- hostname validation stays on for a certificate that arrives by rotation ----

        // Every certificate the test CA issues names "localhost" unless the spec says otherwise. These nodes listen on
        // "localhost", so a client that dials one checks the server certificate against that name. They leave the
        // custom validator out, so the validate-certificate-hostname setting itself does the check. A failed handshake
        // shuts the dialling ActorSystem down.
        private const string DialledName = "localhost";
        private const string OtherName = "other.example.com";

        [Theory(DisplayName = "Should_Connect_When_Hostname_Validation_Is_On_And_The_Certificate_Names_The_Address_Dialled")]
        [MemberData(nameof(AllKinds))]
        public async Task Should_connect_when_hostname_validation_is_on_and_the_certificate_names_the_address_dialled(string kind)
        {
            var fixture = Open(kind);
            fixture.Add(Leaf(fixture, 60));
            var server = StartHostnameServer(fixture);
            var client = StartHostnameClient();

            await ExchangeAsync(client, server);
        }

        [Theory(DisplayName = "Should_Fail_The_Handshake_Of_A_New_Connection_When_The_Source_Rotates_To_A_Newer_Certificate_With_Another_Name")]
        [MemberData(nameof(AllKinds))]
        public async Task Should_fail_the_handshake_of_a_new_connection_when_the_source_rotates_to_a_newer_certificate_with_another_name(string kind)
        {
            var fixture = Open(kind);
            fixture.Add(Leaf(fixture, 60));
            var server = StartHostnameServer(fixture);
            await ExchangeAsync(StartHostnameClient(), server); // the control: the old certificate names the address

            fixture.Add(LeafForName(fixture, 180, OtherName)); // no restart
            await fixture.RefreshAsync();

            var client = StartHostnameClient();
            client.Actors.ActorSelection(server.Echo).Tell("ping");
            await AwaitConditionAsync(() => Task.FromResult(client.Actors.WhenTerminated.IsCompleted), TimeSpan.FromSeconds(15),
                message: "the handshake with the new certificate must fail, and a failed handshake shuts the dialling system down");
            server.Actors.WhenTerminated.IsCompleted.Should().BeFalse("a failed handshake of a peer does not shut the server down");
        }

        [Theory(DisplayName = "Should_Connect_On_A_New_Connection_When_The_Source_Rotates_To_A_Newer_Certificate_With_The_Right_Name")]
        [MemberData(nameof(AllKinds))]
        public async Task Should_connect_on_a_new_connection_when_the_source_rotates_to_a_newer_certificate_with_the_right_name(string kind)
        {
            var fixture = Open(kind);
            fixture.Add(LeafForName(fixture, 60, OtherName)); // the node starts with a certificate that has the wrong name
            var server = StartHostnameServer(fixture);
            var failing = StartHostnameClient();
            failing.Actors.ActorSelection(server.Echo).Tell("ping");
            await AwaitConditionAsync(() => Task.FromResult(failing.Actors.WhenTerminated.IsCompleted), TimeSpan.FromSeconds(15),
                message: "the control: the certificate with the wrong name fails the handshake");

            fixture.Add(LeafForName(fixture, 180, DialledName)); // no restart
            await fixture.RefreshAsync();

            await ExchangeAsync(StartHostnameClient(), server);
        }

        // ---- 5: three nodes, rotated one at a time while they keep talking ----

        [Theory(DisplayName = "Should_Keep_Three_Nodes_Talking_While_They_Are_Rotated_One_At_A_Time")]
        [MemberData(nameof(AllKinds))]
        public async Task Should_keep_three_nodes_talking_while_they_are_rotated_one_at_a_time(string kind)
        {
            var fixtures = new[] { Open(kind), Open(kind), Open(kind) };
            var newLeaves = new X509Certificate2[3];
            var nodes = new Node[3];
            for (var i = 0; i < 3; i++)
            {
                fixtures[i].Add(Leaf(fixtures[i], 60));
                newLeaves[i] = Leaf(fixtures[i], 180);
                nodes[i] = StartSourceNode(fixtures[i]);
            }

            await ExchangeAllPairsAsync(nodes);

            // The three keep exchanging messages in the background while each one is rotated.
            using var stop = new CancellationTokenSource();
            var failures = new ConcurrentQueue<string>();
            var rounds = 0;
            var chatter = Task.Run(async () =>
            {
                while (!stop.IsCancellationRequested)
                {
                    try
                    {
                        await ExchangeAllPairsAsync(nodes);
                        Interlocked.Increment(ref rounds);
                    }
                    catch (Exception e)
                    {
                        failures.Enqueue(e.Message);
                    }

                    await Task.Delay(50);
                }
            });

            for (var i = 0; i < 3; i++)
            {
                fixtures[i].Add(newLeaves[i]);
                await fixtures[i].RefreshAsync();
                // A node that connects now gets the new certificate at once, while the old connections carry on.
                (await PresentedAsync(nodes[i])).Should().Be((newLeaves[i].Thumbprint, newLeaves[i].Thumbprint));
            }

            var roundsAfterRotation = Volatile.Read(ref rounds);
            await AwaitConditionAsync(() => Task.FromResult(Volatile.Read(ref rounds) > roundsAfterRotation + 1),
                TimeSpan.FromSeconds(20));
            stop.Cancel();
            await chatter;
            failures.Should().BeEmpty();

            // A node that connects fresh afterwards works in both directions.
            var freshFixture = Open(kind);
            freshFixture.Add(Leaf(freshFixture, 240));
            var fresh = StartSourceNode(freshFixture);
            await ExchangeAsync(fresh, nodes[0]); // the fresh node connects out and sees nodes[0]'s new certificate
            await ExchangeAsync(nodes[1], fresh); // nodes[1] connects to the fresh node
            await ExchangeAsync(nodes[2], fresh); // so does nodes[2]
            fresh.Seen.Distinct().Should().BeEquivalentTo(newLeaves.Select(c => c.Thumbprint));

            foreach (var node in nodes.Concat(new[] { fresh }))
                node.Actors.WhenTerminated.IsCompleted.Should().BeFalse("no ActorSystem may terminate");
        }

        // ---- 6: HOCON ----

        [Theory(DisplayName = "Should_Build_A_Node_That_Rotates_From_Hocon_When_A_Common_Name_Or_A_Folder_Is_Given")]
        [MemberData(nameof(SkippingKinds))]
        public async Task Should_build_a_node_that_rotates_from_hocon_when_a_common_name_or_a_folder_is_given(string kind)
        {
            var fixture = Open(kind);
            var oldLeaf = Leaf(fixture, 60);
            var newLeaf = Leaf(fixture, 180);
            fixture.Add(oldLeaf);

            var node = StartNode(CertificateHocon(fixture, kind));

            (await PresentedAsync(node)).Should().Be((oldLeaf.Thumbprint, oldLeaf.Thumbprint));
            fixture.Add(newLeaf);
            await SourceOf(node.Actors).RefreshAsync(); // the next look-up of the source HOCON built finds it
            (await PresentedAsync(node)).Should().Be((newLeaf.Thumbprint, newLeaf.Thumbprint));
        }

        [Theory(DisplayName = "Should_Reuse_The_Startup_Pick_For_The_Interval_And_Pick_Up_A_New_Certificate_After_It")]
        [MemberData(nameof(SkippingKinds))]
        public async Task Should_reuse_the_startup_pick_for_the_interval_and_pick_up_a_new_certificate_after_it(string kind)
        {
            var fixture = Open(kind);
            var oldLeaf = Leaf(fixture, 60);
            var newLeaf = Leaf(fixture, 180);
            fixture.Add(oldLeaf);

            // The source HOCON built keeps the real interval. Only the clock is the spec's, so the spec does not sleep.
            var node = StartNode(CertificateHocon(fixture, kind));
            var source = SourceOf(node.Actors);
            source.ScanInterval.Should().Be(TimeSpan.FromSeconds(10));
            source.ScanCount.Should().Be(1, "the startup check read the source");
            var clock = DateTime.UtcNow;
            source.UtcNow = () => clock;

            // The startup check recorded its pick and seeded the reuse, so the first connections do not read again.
            (await PresentedAsync(node)).Should().Be((oldLeaf.Thumbprint, oldLeaf.Thumbprint));
            source.ScanCount.Should().Be(1);

            fixture.Add(newLeaf); // inside the interval, the node does not look
            (await PresentedAsync(node)).Should().Be((oldLeaf.Thumbprint, oldLeaf.Thumbprint));
            source.ScanCount.Should().Be(1);

            // The interval has passed: the next caller starts a look-up in the background and gets the pick it had.
            clock = clock.AddSeconds(10);
            source.GetCertificateWithoutLogging(TlsCertificateRole.ClientAndServer).Thumbprint.Should().Be(oldLeaf.Thumbprint);
            await source.LastRefresh;
            source.ScanCount.Should().Be(2);

            // Connections built after the look-up finished use the new certificate.
            (await PresentedAsync(node)).Should().Be((newLeaf.Thumbprint, newLeaf.Thumbprint));
            source.ScanCount.Should().Be(2);
        }

        [Theory(DisplayName = "Should_Log_The_First_Pick_When_The_Startup_Check_Runs_And_Let_The_Certificate_Property_Reuse_It")]
        [MemberData(nameof(SkippingKinds))]
        public async Task Should_log_the_first_pick_when_the_startup_check_runs_and_let_the_certificate_property_reuse_it(string kind)
        {
            var fixture = Open(kind);
            var leaf = Leaf(fixture, 60);
            fixture.Add(leaf);
            var clock = DateTime.UtcNow;
            fixture.Source.UtcNow = () => clock;
            fixture.Source.ScanInterval = TimeSpan.FromSeconds(10);
            var settings = SslSettings.FromSource(fixture.Source, suppressValidation: false,
                requireMutualAuthentication: true, validateCertificateHostname: false, customValidator: null);

            // The source logs through the adapter it is handed, during the call: a RecordingLog is read right after it.
            var startup = new RecordingLog();
            await settings.ValidateCertificateAsync(startup); // what the transport runs when it starts
            startup.Infos.Count(i => i.Contains("changed from none to " + leaf.Thumbprint)).Should().Be(1);

            // Reading the property logs nothing, does not look again and returns the instance the connections use.
            var reading = new RecordingLog();
            settings.Certificate.Should().BeSameAs(settings.Certificate);
            settings.Certificate!.Thumbprint.Should().Be(leaf.Thumbprint);
            settings.GetCertificate(reading).Should().BeSameAs(settings.Certificate);
            reading.Infos.Count(i => i.Contains("certificate changed")).Should().Be(0);

            fixture.Source.ScanCount.Should().Be(1);
        }

        [Fact(DisplayName = "Should_Load_A_Config_With_No_Common_Name_And_No_Folder_As_A_Fixed_Certificate")]
        public void Should_load_a_config_with_no_common_name_and_no_folder_as_a_fixed_certificate()
        {
            var defaults = RemoteConfigFactory.Default().GetConfig("akka.remote.dot-netty.tcp.ssl");
            defaults.GetString("certificate.common-name").Should().BeEmpty();

            var config = ConfigurationFactory.ParseString(@"
                suppress-validation = on
                certificate { path = ""Resources/akka-validcert.pfx"", password = ""password"" }").WithFallback(defaults);
            var settings = SslSettings.Create(config);

            using var expected = CertificateHelper.LoadPkcs12("Resources/akka-validcert.pfx", "password");
            settings.Certificate!.Thumbprint.Should().Be(expected.Thumbprint);
            settings.Certificate.Should().BeSameAs(settings.Certificate); // one certificate, read at startup
            settings.RequireMutualAuthentication.Should().BeTrue();
            settings.SuppressValidation.Should().BeTrue();
        }

        [Fact(DisplayName = "Should_Warn_When_Both_A_Setup_And_A_Common_Name_Are_Present")]
        public async Task Should_warn_when_both_a_setup_and_a_common_name_are_present()
        {
            var leaf = _ca.Issue("warn-test", -60, 60, Both);
            _certificates.Add(leaf);
            var system = ActorSystem.Create("warn", ActorSystemSetup.Empty
                .And(BootstrapSetup.Create().WithConfig(NodeConfig(@"akka.remote.dot-netty.tcp.ssl.certificate.common-name = ""x""")))
                .And(new DotNettySslSetup(leaf, true)));
            _systems.Add(system);
            var probe = CreateTestProbe();
            system.EventStream.Subscribe(probe, typeof(Warning));

            DotNettyTransportSettings.Create(system);

            await probe.FishForMessageAsync<Warning>(w => w.Message.ToString()!.Contains("Both DotNettySslSetup"),
                TimeSpan.FromSeconds(5));
        }

        // ---- 7: no usable certificate ----

        [Theory(DisplayName = "Should_Fail_At_Startup_With_A_Configuration_Exception_When_No_Certificate_Qualifies")]
        [MemberData(nameof(AllKinds))]
        public async Task Should_fail_at_startup_with_a_configuration_exception_when_no_certificate_qualifies(string kind)
        {
            var fixture = Open(kind); // holds nothing, so nothing qualifies
            await fixture.RefreshAsync();
            var expected = Record.Exception(() => fixture.Source.GetCertificateWithoutLogging(TlsCertificateRole.ClientAndServer));
            expected.Should().BeAssignableTo<InvalidOperationException>();

            var thrown = Assert.Throws<AggregateException>(() => StartSourceNode(fixture));

            var configurationException = thrown.Flatten().InnerExceptions.OfType<ConfigurationException>().Single();
            configurationException.Message.Should().Be(expected!.Message);
        }

        [Fact(DisplayName = "Should_Fail_At_Startup_When_Mutual_Tls_Needs_A_Client_Certificate_And_Only_A_Server_Certificate_Exists")]
        public void Should_fail_at_startup_when_mutual_tls_needs_a_client_certificate_and_only_a_server_certificate_exists()
        {
            var fixture = Open("pkcs12-folder");
            fixture.Add(Leaf(fixture, 60, TlsTestCertificates.ServerAuth));

            var thrown = Assert.Throws<AggregateException>(() => StartSourceNode(fixture));
            thrown.Flatten().InnerExceptions.OfType<ConfigurationException>().Single()
                .Message.Should().Contain("client");

            // Without mutual TLS the node never presents a client certificate, so it starts.
            StartSourceNode(fixture, requireMutualAuthentication: false);
        }

        [Fact(DisplayName = "Should_Fail_At_Startup_With_A_Configuration_Exception_That_Carries_The_Message_When_The_Callback_Throws")]
        public void Should_fail_at_startup_with_a_configuration_exception_that_carries_the_message_when_the_callback_throws()
        {
            // Not an InvalidOperationException: any failure of the source stops the node at startup.
            var source = TlsCertificateSource.FromCallback(() => throw new ArgumentException("the callback broke"));

            var thrown = Assert.Throws<AggregateException>(() => StartNode("", validator =>
                DotNettySslSetup.FromSource(source, suppressValidation: false, requireMutualAuthentication: true,
                    validateCertificateHostname: false, validator)));

            thrown.Flatten().InnerExceptions.OfType<ConfigurationException>().Single()
                .Message.Should().Be("the callback broke");
        }

        [Theory(DisplayName = "Should_Keep_Serving_With_The_Previous_Certificate_When_The_Only_Certificate_Is_Lost")]
        [MemberData(nameof(AllKinds))]
        public async Task Should_keep_serving_with_the_previous_certificate_when_the_only_certificate_is_lost(string kind)
        {
            var fixture = Open(kind);
            var leaf = Leaf(fixture, 60);
            fixture.Add(leaf);
            var node = StartSourceNode(fixture);
            var peer = StartFixedNode();
            await ExchangeAsync(node, peer);
            var warnings = CreateTestProbe();
            node.Actors.EventStream.Subscribe(warnings, typeof(Warning));

            fixture.Remove(leaf); // the node has no certificate left to find, but the one it had is still in date
            await fixture.RefreshAsync();

            // Both directions still work, with the certificate the node had.
            var outboundPeer = StartFixedNode();
            await ExchangeAsync(node, outboundPeer);
            var inboundPeer = StartFixedNode();
            await ExchangeAsync(inboundPeer, node);
            outboundPeer.Seen.Distinct().Should().Equal(leaf.Thumbprint);
            inboundPeer.Seen.Distinct().Should().Equal(leaf.Thumbprint);

            // It says so once, not once per connection.
            await warnings.FishForMessageAsync<Warning>(
                w => w.Message.ToString()!.Contains("keeps using the previous certificate " + leaf.Thumbprint),
                TimeSpan.FromSeconds(5));
            await Assert.ThrowsAnyAsync<Exception>(async () => await warnings.FishForMessageAsync<Warning>(
                w => w.Message.ToString()!.Contains("keeps using the previous certificate"), TimeSpan.FromSeconds(1)));

            node.Actors.WhenTerminated.IsCompleted.Should().BeFalse();
            peer.Actors.WhenTerminated.IsCompleted.Should().BeFalse();
        }

        [Theory(DisplayName = "Should_Fail_That_Connection_And_Stay_Up_When_The_Only_Certificate_Has_Expired")]
        [MemberData(nameof(AllKinds))]
        public async Task Should_fail_that_connection_and_stay_up_when_the_only_certificate_has_expired(string kind)
        {
            var fixture = Open(kind);
            var leaf = Leaf(fixture, 60);
            fixture.Add(leaf);
            var node = StartSourceNode(fixture);
            var peer = StartFixedNode();
            await ExchangeAsync(node, peer);
            var errors = CreateTestProbe();
            node.Actors.EventStream.Subscribe(errors, typeof(Error));

            // The node's clock runs two hours ahead, so the certificate it had has expired and it has no other.
            fixture.Source.UtcNow = () => DateTime.UtcNow.AddHours(2);

            // Outbound: the node cannot build a connection to a fresh peer. The attempt fails with a clear error.
            var outboundPeer = StartFixedNode();
            await Assert.ThrowsAnyAsync<Exception>(() => ExchangeAsync(node, outboundPeer, timeout: TimeSpan.FromSeconds(3)));
            var outboundError = await errors.FishForMessageAsync<Error>(
                e => e.Message.ToString()!.Contains("Cannot build a TLS connection to"), TimeSpan.FromSeconds(5));
            outboundError.Cause.Should().BeAssignableTo<InvalidOperationException>();

            // Inbound: the node closes a connection that arrives, with a clear error. (A raw socket stands in for a
            // peer: a real peer's outbound handshake fails when the node closes on it, and that shuts the peer down.)
            using (var tcp = new TcpClient())
            {
                await tcp.ConnectAsync("127.0.0.1", RARP.For(node.Actors).Provider.DefaultAddress.Port!.Value);
                var buffer = new byte[1];
                var read = Record.ExceptionAsync(() => tcp.GetStream().ReadAsync(buffer, 0, 1)).AsTask();
                (await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(5)))).Should().BeSameAs(read, "the node must close it");
            }

            var inboundError = await errors.FishForMessageAsync<Error>(
                e => e.Message.ToString()!.Contains("incoming connection"), TimeSpan.FromSeconds(5));
            inboundError.Cause.Should().BeAssignableTo<InvalidOperationException>();

            // Neither failure is a TLS handshake failure, so nothing shut a system down.
            node.Actors.WhenTerminated.IsCompleted.Should().BeFalse();
            peer.Actors.WhenTerminated.IsCompleted.Should().BeFalse();
            outboundPeer.Actors.WhenTerminated.IsCompleted.Should().BeFalse();

            // The clock is right again, the certificate is in date again, and the node serves again, with no restart:
            // once a look-up has run, as a connection does not wait for one.
            fixture.Source.UtcNow = () => DateTime.UtcNow;
            await fixture.RefreshAsync();
            await ExchangeAsync(StartFixedNode(), node);
        }

        [Fact(DisplayName = "Should_Log_The_No_Certificate_Error_At_Most_Once_A_Minute_For_Incoming_Connections")]
        public async Task Should_log_the_no_certificate_error_at_most_once_a_minute_for_incoming_connections()
        {
            var fixture = Open("pkcs12-folder");
            var leaf = Leaf(fixture, 60);
            fixture.Add(leaf);
            var node = StartSourceNode(fixture);
            var errors = CreateTestProbe();
            node.Actors.EventStream.Subscribe(errors, typeof(Error));
            fixture.Source.UtcNow = () => DateTime.UtcNow.AddHours(2); // the only certificate has expired
            await fixture.RefreshAsync(); // and a look-up has found nothing else

            // Each connection that arrives fails the same way. The node says so once, not once per connection.
            for (var i = 0; i < 3; i++)
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync("127.0.0.1", RARP.For(node.Actors).Provider.DefaultAddress.Port!.Value);
                var read = Record.ExceptionAsync(() => tcp.GetStream().ReadAsync(new byte[1], 0, 1)).AsTask();
                (await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(5)))).Should().BeSameAs(read, "the node must close it");
            }

            await errors.FishForMessageAsync<Error>(e => e.Message.ToString()!.Contains("incoming connection"), TimeSpan.FromSeconds(5));
            await Assert.ThrowsAnyAsync<Exception>(async () => await errors.FishForMessageAsync<Error>(
                e => e.Message.ToString()!.Contains("incoming connection"), TimeSpan.FromSeconds(2)));
            node.Actors.WhenTerminated.IsCompleted.Should().BeFalse();
        }

        // ---- review round 4 ----

        [Fact(DisplayName = "Should_Fail_An_Incoming_Connection_At_Once_When_The_Node_Has_Nothing_To_Present_While_A_Look_Up_Runs")]
        public async Task Should_fail_an_incoming_connection_at_once_when_the_node_has_nothing_to_present_while_a_look_up_runs()
        {
            var fixture = Open("pem-folder");
            fixture.Add(Leaf(fixture, 60));
            var node = StartSourceNode(fixture);
            var errors = CreateTestProbe();
            node.Actors.EventStream.Subscribe(errors, typeof(Error));

            // The next look-up blocks: it pairs a certificate it has not seen, which asks IsWindows. And the
            // certificate the node has is out of date, so the node has nothing to present until that look-up ends.
            using var gate = new BlockingGate();
            fixture.Add(Leaf(fixture, 60));
            fixture.Source.IsWindows = () => gate.Block(false);
            fixture.Source.UtcNow = () => DateTime.UtcNow.AddHours(2);

            // The connection fails at once, with the reason of the last look-up. The node does not hold the I/O thread
            // while the look-up runs.
            await ConnectAndExpectCloseAsync(node);
            var error = await errors.FishForMessageAsync<Error>(
                e => e.Message.ToString()!.Contains("incoming connection"), TimeSpan.FromSeconds(5));
            error.Message.ToString().Should().Contain("is no longer in date or was disposed, and no other was found.");
            node.Actors.WhenTerminated.IsCompleted.Should().BeFalse();
        }

        [Fact(DisplayName = "Should_Log_Each_Different_Cause_Of_A_Failed_Tls_Connection_With_The_Cause_In_The_Text")]
        public async Task Should_log_each_different_cause_of_a_failed_tls_connection_with_the_cause_in_the_text()
        {
            var leaf = _ca.Issue("callback-node", -240, 60, Both);
            _certificates.Add(leaf);
            var cause = new[] { "" };
            var source = TlsCertificateSource.FromCallback(() => Volatile.Read(ref cause[0]) switch
            {
                "" => leaf,
                "the first cause" => throw new InvalidOperationException("the first cause"),
                var other => throw new ArgumentException(other)
            });
            var node = StartNode("", validator => DotNettySslSetup.FromSource(source, suppressValidation: false,
                requireMutualAuthentication: true, validateCertificateHostname: false, validator));
            var errors = CreateTestProbe();
            node.Actors.EventStream.Subscribe(errors, typeof(Error));
            source.UtcNow = () => DateTime.UtcNow.AddHours(2); // the last pick is out of date: nothing to fall back to

            Task<Error> NextErrorAsync(string text) => errors.FishForMessageAsync<Error>(e =>
                e.Message.ToString()!.StartsWith("Cannot build a TLS connection for the incoming connection from [") &&
                e.Message.ToString()!.EndsWith("]: " + text), TimeSpan.FromSeconds(5)).AsTask();

            Volatile.Write(ref cause[0], "the first cause");
            await ConnectAndExpectCloseAsync(node);
            await NextErrorAsync("the first cause");

            // A different cause inside the same minute is logged too, with its own text. A callback's own exception
            // counts as a different cause when its type differs.
            Volatile.Write(ref cause[0], "the second cause");
            await ConnectAndExpectCloseAsync(node);
            await NextErrorAsync("the second cause");

            // The same cause again inside the minute is not.
            await ConnectAndExpectCloseAsync(node);
            await Assert.ThrowsAnyAsync<Exception>(() => NextErrorAsync("the second cause"));
        }

        // ---- review round 5 ----

        [Fact(DisplayName = "Should_Log_A_Real_Cause_When_A_Callback_Fails_With_A_Different_Text_On_Every_Call")]
        public async Task Should_log_a_real_cause_when_a_callback_fails_with_a_different_text_on_every_call()
        {
            var leaf = _ca.Issue("callback-node", -240, 60, Both);
            _certificates.Add(leaf);
            var calls = 0;
            var realCause = 0;
            var source = TlsCertificateSource.FromCallback(() =>
            {
                var call = Interlocked.Increment(ref calls);
                if (call == 1)
                    return leaf; // the startup check
                if (Volatile.Read(ref realCause) == 1)
                    throw new ArgumentException("the real cause");
                throw new InvalidOperationException($"request {call} failed");
            });
            var node = StartNode("", validator => DotNettySslSetup.FromSource(source, suppressValidation: false,
                requireMutualAuthentication: true, validateCertificateHostname: false, validator));
            var errors = CreateTestProbe();
            node.Actors.EventStream.Subscribe(errors, typeof(Error));
            source.UtcNow = () => DateTime.UtcNow.AddHours(2); // the last pick is out of date: nothing to fall back to

            // 110 connections fail, each with its own text: the cause is the same, so it is logged once.
            for (var i = 0; i < 110; i++)
                await ConnectAndExpectCloseAsync(node);
            var first = await errors.FishForMessageAsync<Error>(e => e.Message.ToString()!.Contains("incoming connection"),
                TimeSpan.FromSeconds(5));
            first.Message.ToString().Should().EndWith(" failed"); // the logged text keeps the callback's message
            await Assert.ThrowsAnyAsync<Exception>(async () => await errors.FishForMessageAsync<Error>(
                e => e.Message.ToString()!.Contains("incoming connection") || e.Message.ToString()!.Contains("were not logged"),
                TimeSpan.FromSeconds(1)));

            // A different cause inside the minute is still logged.
            Volatile.Write(ref realCause, 1);
            await ConnectAndExpectCloseAsync(node);
            await errors.FishForMessageAsync<Error>(e => e.Message.ToString()!.EndsWith("]: the real cause"),
                TimeSpan.FromSeconds(5));
        }

        [Fact(DisplayName = "Should_Look_For_A_Renewed_Certificate_Once_A_Minute_When_The_Node_Builds_No_Connection")]
        public async Task Should_look_for_a_renewed_certificate_once_a_minute_when_the_node_builds_no_connection()
        {
            var fixture = Open("pkcs12-folder");
            var oldLeaf = Leaf(fixture, 60);
            var newLeaf = Leaf(fixture, 180);
            fixture.Add(oldLeaf);
            var node = StartNode(TestSchedulerHocon, validator => DotNettySslSetup.FromSource(fixture.Source,
                suppressValidation: false, requireMutualAuthentication: true, validateCertificateHostname: false, validator));
            var scheduler = (TestScheduler)node.Actors.Scheduler;
            var source = fixture.Source;
            source.ScanCount.Should().Be(1, "the startup check read the folder");
            var infos = CreateTestProbe();
            node.Actors.EventStream.Subscribe(infos, typeof(Info));

            // The renewed certificate arrives, and the startup look-up is more than 10 seconds old.
            fixture.Add(newLeaf);
            source.UtcNow = () => DateTime.UtcNow.AddSeconds(11);

            scheduler.Advance(TimeSpan.FromSeconds(59));
            source.ScanCount.Should().Be(1, "the node looks once a minute");
            scheduler.Advance(TimeSpan.FromSeconds(1));
            await source.LastRefresh;
            source.ScanCount.Should().Be(2);

            // The node logs the change by itself, and the new certificate is the pick, with no connection built.
            await infos.FishForMessageAsync<Info>(i => i.Message.ToString() ==
                    $"TLS client and server certificate changed from {Text(oldLeaf)} to {Text(newLeaf)}",
                TimeSpan.FromSeconds(5));
            source.GetCertificateWithoutLogging(TlsCertificateRole.ClientAndServer).Thumbprint.Should().Be(newLeaf.Thumbprint);
            node.Seen.Should().BeEmpty();

            // The node stops looking when its transport shuts down.
            var terminated = node.Actors.Terminate();
            (await Task.WhenAny(terminated, Task.Delay(TimeSpan.FromSeconds(10)))).Should().BeSameAs(terminated);
            source.UtcNow = () => DateTime.UtcNow.AddSeconds(22);
            scheduler.Advance(TimeSpan.FromMinutes(1));
            await source.LastRefresh;
            source.ScanCount.Should().Be(2);
        }

        [Fact(DisplayName = "Should_Not_Look_Up_On_A_Schedule_When_The_Certificate_Is_Fixed_Or_Comes_From_A_Callback")]
        public async Task Should_not_look_up_on_a_schedule_when_the_certificate_is_fixed_or_comes_from_a_callback()
        {
            var leaf = _ca.Issue("fixed-node", -240, 60, Both);
            _certificates.Add(leaf);
            var fixedNode = StartNode(TestSchedulerHocon, validator => new DotNettySslSetup(leaf, suppressValidation: false,
                requireMutualAuthentication: true, validateCertificateHostname: false, validator));
            SslSettingsOf(fixedNode.Actors).NeedsPolling.Should().BeFalse("a fixed certificate never changes");

            var calls = 0;
            var source = TlsCertificateSource.FromCallback(() =>
            {
                Interlocked.Increment(ref calls);
                return leaf;
            });
            var callbackNode = StartNode(TestSchedulerHocon, validator => DotNettySslSetup.FromSource(source,
                suppressValidation: false, requireMutualAuthentication: true, validateCertificateHostname: false, validator));
            SslSettingsOf(callbackNode.Actors).NeedsPolling.Should().BeFalse("a callback is called for connections only");
            var atStart = Volatile.Read(ref calls);

            ((TestScheduler)callbackNode.Actors.Scheduler).Advance(TimeSpan.FromMinutes(5));
            await Task.Yield();
            Volatile.Read(ref calls).Should().Be(atStart, "no scheduled call reached the callback");

            // A folder or a store is the case that polls.
            var fixture = Open("pkcs12-folder");
            fixture.Add(Leaf(fixture, 60));
            var folderNode = StartNode(TestSchedulerHocon, validator => DotNettySslSetup.FromSource(fixture.Source,
                suppressValidation: false, requireMutualAuthentication: true, validateCertificateHostname: false, validator));
            SslSettingsOf(folderNode.Actors).NeedsPolling.Should().BeTrue();
        }

        [Fact(DisplayName = "Should_Not_Throw_From_The_Scheduled_Call_When_The_Source_Has_Nothing_To_Present")]
        public async Task Should_not_throw_from_the_scheduled_call_when_the_source_has_nothing_to_present()
        {
            var fixture = Open("pkcs12-folder");
            var leaf = Leaf(fixture, 60);
            fixture.Add(leaf);
            var node = StartNode(TestSchedulerHocon, validator => DotNettySslSetup.FromSource(fixture.Source,
                suppressValidation: false, requireMutualAuthentication: true, validateCertificateHostname: false, validator));
            var scheduler = (TestScheduler)node.Actors.Scheduler;
            var source = fixture.Source;

            // The only certificate is gone and the node's pick is out of date: nothing to present.
            fixture.Remove(leaf);
            source.UtcNow = () => DateTime.UtcNow.AddHours(2);

            // A throw out of the scheduled call would come out of Advance.
            scheduler.Advance(TimeSpan.FromMinutes(1));
            await source.LastRefresh;
            source.ScanCount.Should().Be(2);
            scheduler.Advance(TimeSpan.FromMinutes(1)); // and it keeps running
            node.Actors.WhenTerminated.IsCompleted.Should().BeFalse();
        }

        [Fact(DisplayName = "Should_Log_One_Error_When_A_Callback_Returns_A_Different_Unusable_Certificate_On_Every_Call")]
        public async Task Should_log_one_error_when_a_callback_returns_a_different_unusable_certificate_on_every_call()
        {
            var leaf = _ca.Issue("callback-node", -240, 60, Both);
            _certificates.Add(leaf);
            var calls = 0;
            var source = TlsCertificateSource.FromCallback(() =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                    return leaf; // the startup check
                var expired = _ca.Issue("callback-node", -240, -120, Both); // each call: another thumbprint
                _certificates.Add(expired);
                return expired;
            });
            var node = StartNode("", validator => DotNettySslSetup.FromSource(source, suppressValidation: false,
                requireMutualAuthentication: true, validateCertificateHostname: false, validator));
            var errors = CreateTestProbe();
            node.Actors.EventStream.Subscribe(errors, typeof(Error));
            source.UtcNow = () => DateTime.UtcNow.AddHours(2); // the last pick is out of date: nothing to fall back to

            // Each text names another thumbprint, but the failure is the same: one line, not one for each call.
            for (var i = 0; i < 4; i++)
                await ConnectAndExpectCloseAsync(node);
            var first = await errors.FishForMessageAsync<Error>(e => e.Message.ToString()!.Contains("incoming connection"),
                TimeSpan.FromSeconds(5));
            first.Message.ToString().Should().Contain("returned a certificate");
            await Assert.ThrowsAnyAsync<Exception>(async () => await errors.FishForMessageAsync<Error>(
                e => e.Message.ToString()!.Contains("incoming connection"), TimeSpan.FromSeconds(1)));
        }

        [Fact(DisplayName = "Should_Not_Ask_The_Source_When_Listen_Fails_Before_It_Binds")]
        public async Task Should_not_ask_the_source_when_listen_fails_before_it_binds()
        {
            var fixture = Open("pkcs12-folder");
            fixture.Add(Leaf(fixture, 60));
            var source = fixture.Source;
            // A system without remoting, only to carry the setup and the test scheduler to a transport that is never bound.
            var system = ActorSystem.Create("listen-fails", ActorSystemSetup.Empty
                .And(BootstrapSetup.Create().WithConfig(ConfigurationFactory.ParseString(TestSchedulerHocon)))
                .And(DotNettySslSetup.FromSource(source, suppressValidation: false, requireMutualAuthentication: true,
                    validateCertificateHostname: false, (_, _, _, _, _) => true)));
            _systems.Add(system);
            var scheduler = (TestScheduler)system.Scheduler;
            var config = ConfigurationFactory.ParseString("enable-ssl = on\nhostname = \"127.0.0.1\"\nport = 70000")
                .WithFallback(RemoteConfigFactory.Default().GetConfig("akka.remote.dot-netty.tcp"));
            var transport = new TcpTransport(system, config);
            try
            {
                await Assert.ThrowsAnyAsync<Exception>(() => transport.Listen()); // 70000 is no port: the endpoint throws
                var scans = source.ScanCount;

                source.UtcNow = () => DateTime.UtcNow.AddSeconds(11); // a look-up would be due
                scheduler.Advance(TimeSpan.FromMinutes(2));
                await source.LastRefresh;
                source.ScanCount.Should().Be(scans, "the poll is scheduled only when the bind worked");
            }
            finally
            {
                await transport.Shutdown();
            }
        }

        private const string TestSchedulerHocon = @"akka.scheduler.implementation = ""Akka.TestKit.TestScheduler, Akka.TestKit""";

        private static string Text(X509Certificate2 certificate) =>
            $"{certificate.Thumbprint} (subject '{certificate.Subject}', expires " +
            $"{certificate.NotAfter.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture)})";

        // A raw socket stands in for a peer: a real peer's outbound handshake fails when the node closes on it.
        private static async Task ConnectAndExpectCloseAsync(Node node)
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync("127.0.0.1", RARP.For(node.Actors).Provider.DefaultAddress.Port!.Value);
            var read = Record.ExceptionAsync(() => tcp.GetStream().ReadAsync(new byte[1], 0, 1)).AsTask();
            (await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(5)))).Should().BeSameAs(read, "the node must close it at once");
        }

        // ---- helpers ----

        private SourceFixture Open(string kind)
        {
            var fixture = SourceFixture.Open(kind);
            _fixtures.Add(fixture);
            return fixture;
        }

        // A leaf issued by the test CA for this fixture's common name. A leaf that expires later was issued later:
        // a source picks the certificate issued last, so the leaf's start date follows its end date.
        private X509Certificate2 Leaf(SourceFixture fixture, int notAfterMinutes, params string[] ekus)
        {
            var leaf = _ca.Issue(fixture.CommonName, notAfterMinutes - 300, notAfterMinutes, ekus.Length == 0 ? Both : ekus);
            _certificates.Add(leaf);
            return leaf;
        }

        // A leaf that names this DNS name, issued like Leaf.
        private X509Certificate2 LeafForName(SourceFixture fixture, int notAfterMinutes, string dnsName)
        {
            var leaf = _ca.IssueForDnsName(fixture.CommonName, notAfterMinutes - 300, notAfterMinutes, dnsName, Both);
            _certificates.Add(leaf);
            return leaf;
        }

        // A node that listens on "localhost", takes its certificate from the source, and checks the hostname.
        // Chain validation is off, as the test CA is not in the OS trust store: this spec is about the name.
        private Node StartHostnameServer(SourceFixture fixture) =>
            StartNode($"akka.remote.dot-netty.tcp.hostname = \"{DialledName}\"",
                _ => DotNettySslSetup.FromSource(fixture.Source, suppressValidation: true,
                    requireMutualAuthentication: true, validateCertificateHostname: true, customValidator: null));

        private Node StartHostnameClient()
        {
            var leaf = _ca.Issue("peer-" + Guid.NewGuid().ToString("N"), -60, 60, Both);
            _certificates.Add(leaf);
            return StartNode("", _ => new DotNettySslSetup(leaf, suppressValidation: true,
                requireMutualAuthentication: true, validateCertificateHostname: true, customValidator: null));
        }

        // The certificate block of a node that HOCON builds a source for. A common name wins over the other keys,
        // which here would each fail if they were used.
        private static string CertificateHocon(SourceFixture fixture, string kind)
        {
            var certificate = fixture is FolderFixture folder
                ? $"path = \"{folder.Folder.Replace("\\", "\\\\")}\"\n password = \"{(kind == "pkcs12-folder" ? SourceFixture.Password : "")}\"\n flags = [ exportable ]"
                : $"common-name = \"{fixture.CommonName}\"\n use-thumbprint-over-file = true\n thumbprint = \"\"\n path = \"/no/such/file.pfx\"";
            return $@"akka.remote.dot-netty.tcp.ssl {{
                suppress-validation = on
                require-mutual-authentication = on
                certificate {{ {certificate} }}
            }}";
        }

        // The source a node built from HOCON asks. The transport keeps its settings, so the spec follows the
        // transport the node started: the protocol transport wraps the adapters, which wrap the DotNetty transport.
        private static TlsCertificateSource SourceOf(ActorSystem system) => SslSettingsOf(system).Source!;

        private static SslSettings SslSettingsOf(ActorSystem system)
        {
            const BindingFlags instance = BindingFlags.NonPublic | BindingFlags.Instance;
            var remoting = RARP.For(system).Provider.Transport;
            var mapping = (IDictionary<string, HashSet<ProtocolTransportAddressPair>>)
                typeof(Remoting).GetField("_transportMapping", instance)!.GetValue(remoting)!;
            Akka.Remote.Transport.Transport transport = mapping.Values.SelectMany(pairs => pairs).Single().ProtocolTransport;
            var wrapped = typeof(AbstractTransportAdapter).GetField("WrappedTransport", instance)!;
            while (transport is AbstractTransportAdapter adapter)
                transport = (Akka.Remote.Transport.Transport)wrapped.GetValue(adapter)!;

            return ((DotNettyTransport)transport).Settings.Ssl;
        }

        private static Config NodeConfig(string extra = "") => ConfigurationFactory.ParseString(extra).WithFallback(
            ConfigurationFactory.ParseString(@"
                akka {
                    loglevel = INFO
                    actor.provider = ""Akka.Remote.RemoteActorRefProvider,Akka.Remote""
                    remote.dot-netty.tcp {
                        port = 0
                        hostname = ""127.0.0.1""
                        enable-ssl = on
                    }
                }"));

        private Node StartSourceNode(SourceFixture fixture, bool requireMutualAuthentication = true) =>
            StartNode("", validator => DotNettySslSetup.FromSource(fixture.Source, suppressValidation: false,
                requireMutualAuthentication, validateCertificateHostname: false, validator));

        // A peer with one fixed certificate, the way every node was set up before sources existed.
        private Node StartFixedNode(bool requireMutualAuthentication = true)
        {
            var leaf = _ca.Issue("peer-" + Guid.NewGuid().ToString("N"), -60, 60, Both);
            _certificates.Add(leaf);
            return StartNode("", validator => new DotNettySslSetup(leaf, suppressValidation: false,
                requireMutualAuthentication, validateCertificateHostname: false, validator));
        }

        private Node StartNode(string hocon, Func<CertificateValidationCallback, DotNettySslSetup>? ssl = null)
        {
            var node = new Node();
            CertificateValidationCallback validator = (certificate, _, _, _, _) =>
            {
                if (certificate is null)
                    return false;
                node.Seen.Enqueue(certificate.Thumbprint);
                return _ca.Trusts(certificate);
            };

            var setup = ActorSystemSetup.Empty.And(BootstrapSetup.Create().WithConfig(NodeConfig(hocon)));
            if (ssl is not null)
                setup = setup.And(ssl(validator));

            var name = "node" + Interlocked.Increment(ref _nodeCount);
            node.Actors = ActorSystem.Create(name, setup);
            _systems.Add(node.Actors);
            InitializeLogger(node.Actors, $"[{name}] ");
            node.Actors.ActorOf(Props.Create(() => new Echo()), "echo");
            return node;
        }

        private static async Task ExchangeAsync(Node from, Node to, string message = "ping", TimeSpan? timeout = null)
        {
            var reply = await from.Actors.ActorSelection(to.Echo).Ask<string>(message, timeout ?? TimeSpan.FromSeconds(15));
            reply.Should().Be(message);
        }

        private static async Task ExchangeAllPairsAsync(Node[] nodes)
        {
            foreach (var from in nodes)
            {
                foreach (var to in nodes.Where(n => n != from))
                    await ExchangeAsync(from, to);
            }
        }

        // Connects one fresh peer to the node and has the node connect to another fresh peer. Returns the thumbprint
        // each peer saw: the node's server certificate, and its client certificate.
        private async Task<(string Server, string Client)> PresentedAsync(Node node)
        {
            var connectsIn = StartFixedNode();
            await ExchangeAsync(connectsIn, node);
            var connectedTo = StartFixedNode();
            await ExchangeAsync(node, connectedTo);
            return (connectsIn.Seen.Distinct().Single(), connectedTo.Seen.Distinct().Single());
        }

        public override async ValueTask DisposeAsync()
        {
            foreach (var system in _systems)
                await Task.WhenAny(system.Terminate(), Task.Delay(TimeSpan.FromSeconds(10)));
            foreach (var fixture in _fixtures)
                fixture.Dispose();
            foreach (var certificate in _certificates)
                certificate.Dispose();
            _ca.Dispose();
            await base.DisposeAsync();
        }

        private sealed class Node
        {
            public ActorSystem Actors { get; set; } = null!;

            /// <summary>Thumbprints of the certificates peers presented to this node, one per validation.</summary>
            public ConcurrentQueue<string> Seen { get; } = new ConcurrentQueue<string>();

            public ActorPath Echo => new RootActorPath(RARP.For(Actors).Provider.DefaultAddress) / "user" / "echo";
        }

        private sealed class Echo : ReceiveActor
        {
            public Echo() => ReceiveAny(message => Sender.Tell(message));
        }
    }

    /// <summary>
    /// A root CA and an issuing CA under it, which sign leaf certificates. The OS does not trust them, so
    /// <see cref="Trusts"/> builds the chain to this root itself, the way a custom validator does.
    /// </summary>
    internal sealed class TestCertificateAuthority : IDisposable
    {
        private readonly List<IDisposable> _keep = new List<IDisposable>();
        private X509Certificate2 _root = null!;
        private X509Certificate2 _issuing = null!;

        public static TestCertificateAuthority Create()
        {
            var ca = new TestCertificateAuthority();
            var now = DateTimeOffset.UtcNow;

            var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var rootRequest = new CertificateRequest("CN=Akka TLS Test Root", rootKey, HashAlgorithmName.SHA256);
            AddCaExtensions(rootRequest, pathLength: 1);
            ca._root = rootRequest.CreateSelfSigned(now.AddDays(-2), now.AddDays(2));

            var issuingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var issuingRequest = new CertificateRequest("CN=Akka TLS Test Issuing CA", issuingKey, HashAlgorithmName.SHA256);
            AddCaExtensions(issuingRequest, pathLength: 0);
            using var issuingPublic = issuingRequest.Create(ca._root, now.AddDays(-2), now.AddDays(2), TlsTestCertificates.NewSerial());
            ca._issuing = issuingPublic.CopyWithPrivateKey(issuingKey);

            ca._keep.AddRange(new IDisposable[] { rootKey, issuingKey, ca._root, ca._issuing });
            return ca;
        }

        /// <summary>Issues a leaf with an exportable key, so a folder fixture can write it to files.</summary>
        public X509Certificate2 Issue(string commonName, int notBeforeMinutes, int notAfterMinutes, string[] ekus) =>
            TlsTestCertificates.CreateIssued(_issuing, commonName, notBeforeMinutes, notAfterMinutes, ekus);

        /// <summary>Like <see cref="Issue"/>, but the leaf names <paramref name="dnsName"/> instead of <c>localhost</c>.</summary>
        public X509Certificate2 IssueForDnsName(string commonName, int notBeforeMinutes, int notAfterMinutes, string dnsName, string[] ekus) =>
            TlsTestCertificates.CreateIssuedForDnsName(_issuing, commonName, notBeforeMinutes, notAfterMinutes, dnsName, ekus);

        /// <summary>True when the certificate chains to the test root through the issuing CA.</summary>
        public bool Trusts(X509Certificate2 certificate)
        {
            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
            chain.ChainPolicy.ExtraStore.Add(_issuing);
            chain.ChainPolicy.ExtraStore.Add(_root);
            return chain.Build(certificate)
                   && chain.ChainElements.Count == 3
                   && chain.ChainElements[1].Certificate.Thumbprint == _issuing.Thumbprint
                   && chain.ChainElements[2].Certificate.Thumbprint == _root.Thumbprint;
        }

        public void Dispose()
        {
            foreach (var item in _keep)
                item.Dispose();
        }

        private static void AddCaExtensions(CertificateRequest request, int pathLength)
        {
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, pathLength, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        }
    }
}
