// -----------------------------------------------------------------------
// <copyright file="ArteryTlsRotationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Event;
using Akka.IO;
using Akka.Remote.Artery;
using Akka.Remote.Tests.Transport;
using Akka.TestKit;
using Akka.Tests.IO;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// The Artery twin of <see cref="DotNettyTlsRotationSpec"/>. A node asks its <see cref="TlsCertificateSource"/>
    /// for a certificate each time it builds a connection, so an operator rotates by adding the renewed certificate
    /// beside the old one. Each behaviour runs against every way to hold certificates (a folder of PKCS#12 files, a
    /// folder of PEM files, a store and a callback), using the fixtures the <c>TlsCertificateSource</c> specs use.
    /// Nodes use mutual TLS unless a spec says otherwise. A peer records the certificate it was shown through its
    /// peer policy, which trusts the test CA.
    /// </summary>
    public sealed class ArteryTlsRotationSpec : AkkaSpec
    {
        private static readonly string[] Both = { TlsTestCertificates.ServerAuth, TlsTestCertificates.ClientAuth };

        private readonly TestCertificateAuthority _ca = TestCertificateAuthority.Create();
        private readonly List<SourceFixture> _fixtures = new List<SourceFixture>();
        private readonly List<X509Certificate2> _certificates = new List<X509Certificate2>();
        private readonly List<ActorSystem> _systems = new List<ActorSystem>();
        private int _nodeCount;

        // The sources keep the real 10 second interval: a spec that adds a certificate runs a look-up (RefreshAsync)
        // before it expects the next connection to use it.
        public ArteryTlsRotationSpec(ITestOutputHelper output) : base("akka.loglevel = INFO", output)
        {
        }

        public static TheoryData<string> AllKinds => SourceFixture.AllKinds;

        public static TheoryData<string> SkippingKinds => SourceFixture.SkippingKinds;

        // A callback hands over one certificate and cannot fall back to another one, so "skip the bad newer
        // certificate" does not apply to it.
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

        // ---- the per-connection look-up, without a network ----

        [Theory(DisplayName = "Should_Ask_The_Source_For_The_Same_Certificate_In_Both_Directions_When_Each_Connection_Is_Built")]
        [MemberData(nameof(AllKinds))]
        public async Task Should_ask_the_source_for_the_same_certificate_in_both_directions_when_each_connection_is_built(string kind)
        {
            var fixture = Open(kind);
            var oldLeaf = Leaf(fixture, 60);
            var newLeaf = Leaf(fixture, 180);
            fixture.Add(oldLeaf);
            var trust = TlsPeerPolicy.CustomTrust((_, _, _, _, _) => true);
            var settings = ArteryTlsSettings.Mutual(fixture.Source, trust);
            await settings.CertificateSource!.CheckAtStartupAsync(new RecordingLog());
            var log = new RecordingLog();

            Presented(settings, log).Should().Be((oldLeaf.Thumbprint, oldLeaf.Thumbprint));

            fixture.Add(newLeaf);
            await fixture.RefreshAsync();

            // The next connection in each direction uses the new certificate; the change is logged once.
            Presented(settings, log).Should().Be((newLeaf.Thumbprint, newLeaf.Thumbprint));
            Presented(settings, log).Should().Be((newLeaf.Thumbprint, newLeaf.Thumbprint));
            log.Infos.Count(i => i == $"TLS client and server certificate changed from {Text(oldLeaf)} to {Text(newLeaf)}")
                .Should().Be(1);
        }

        [Fact(DisplayName = "Should_Present_No_Client_Certificate_And_Ask_The_Source_For_A_Server_Certificate_When_Mutual_Tls_Is_Off")]
        public async Task Should_present_no_client_certificate_and_ask_the_source_for_a_server_certificate_when_mutual_tls_is_off()
        {
            var fixture = Open("pkcs12-folder");
            var serverOnly = Leaf(fixture, 60, TlsTestCertificates.ServerAuth);
            fixture.Add(serverOnly);
            var settings = ArteryTlsSettings.ServerOnly(fixture.Source, TlsPeerPolicy.CustomTrust((_, _, _, _, _) => true));
            settings.CertificateSource!.Role.Should().Be(TlsCertificateRole.Server);
            await settings.CertificateSource.CheckAtStartupAsync(new RecordingLog());
            var log = new RecordingLog();

            var server = settings.ServerSettings.CreateAuthenticationOptions("peer", log);
            var client = settings.ClientSettings.CreateAuthenticationOptions("localhost", "peer", log);

            ((X509Certificate2)server.ServerCertificate!).Thumbprint.Should().Be(serverOnly.Thumbprint);
            server.ClientCertificateRequired.Should().BeFalse();
            client.ClientCertificates.Should().BeNull();
            client.LocalCertificateSelectionCallback.Should().BeNull();
        }

        [Fact(DisplayName = "Should_Log_The_Exact_Error_And_Throw_The_Original_Failure_Wrapped_When_The_Source_Has_No_Certificate")]
        public void Should_log_the_exact_error_and_throw_the_original_failure_wrapped_when_the_source_has_no_certificate()
        {
            var failure = new InvalidOperationException("the callback failed");
            var source = TlsCertificateSource.FromCallback(() => throw failure);
            var settings = ArteryTlsSettings.Mutual(source, TlsPeerPolicy.CustomTrust((_, _, _, _, _) => true));
            var log = new RecordingLog();

            var outbound = Assert.Throws<TlsCertificateUnavailableException>(
                () => settings.CertificateSource!.Present(TlsConnectionDirection.Outbound, "peer:1", log));
            var inbound = Assert.Throws<TlsCertificateUnavailableException>(
                () => settings.CertificateSource!.Present(TlsConnectionDirection.Inbound, "peer:2", log));

            // Akka.IO fails the connection with the wrapper: the same message, and the original inside it, unchanged.
            foreach (var thrown in new[] { outbound, inbound })
            {
                thrown.Message.Should().Be("the callback failed");
                thrown.InnerException.Should().BeSameAs(failure);
            }

            failure.Data.Count.Should().Be(0, "the callback's own exception is not changed");
            log.Errors.Should().Equal(
                "Cannot build a TLS connection to [peer:1]: the callback failed",
                "Cannot build a TLS connection for the incoming connection from [peer:2]: the callback failed");
        }

        [Fact(DisplayName = "Should_Keep_The_Source_When_The_Handshake_Timeout_Or_Protocols_Change")]
        public async Task Should_keep_the_source_when_the_handshake_timeout_or_protocols_change()
        {
            var fixture = Open("pkcs12-folder");
            var leaf = Leaf(fixture, 60);
            fixture.Add(leaf);
            var settings = ArteryTlsSettings.Mutual(fixture.Source, TlsPeerPolicy.CustomTrust((_, _, _, _, _) => true))
                .WithHandshakeTimeout(TimeSpan.FromSeconds(5))
                .WithProtocols(System.Security.Authentication.SslProtocols.Tls12);
            await settings.CertificateSource!.CheckAtStartupAsync(new RecordingLog());

            settings.CertificateSource.Source.Should().BeSameAs(fixture.Source);
            Presented(settings, new RecordingLog()).Should().Be((leaf.Thumbprint, leaf.Thumbprint));
            settings.HandshakeTimeout.Should().Be(TimeSpan.FromSeconds(5));
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
            var infos = CreateTestProbe();
            node.Actors.EventStream.Subscribe(infos, typeof(Info));

            // The control: before the change the node presents the old certificate in both roles.
            (await PresentedAsync(node)).Should().Be((oldLeaf.Thumbprint, oldLeaf.Thumbprint));

            fixture.Add(newLeaf); // no restart
            await fixture.RefreshAsync(); // connections built after the next look-up use it

            (await PresentedAsync(node)).Should().Be((newLeaf.Thumbprint, newLeaf.Thumbprint));
            (await PresentedAsync(node)).Should().Be((newLeaf.Thumbprint, newLeaf.Thumbprint));

            // The change is logged once, not once per connection.
            var changed = $"TLS client and server certificate changed from {Text(oldLeaf)} to {Text(newLeaf)}";
            await infos.FishForMessageAsync<Info>(i => i.Message.ToString() == changed, TimeSpan.FromSeconds(5));
            await Assert.ThrowsAnyAsync<Exception>(async () => await infos.FishForMessageAsync<Info>(
                i => i.Message.ToString()!.Contains("certificate changed"), TimeSpan.FromSeconds(1)));
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
            await ExchangeAsync(node, peer);
            var nodeHandshakes = node.Seen.Count;
            var peerHandshakes = peer.Seen.Count;

            fixture.Add(newLeaf);
            await fixture.RefreshAsync();

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

            var skipped = await warnings.FishForMessageAsync<Warning>(
                w => w.Message.ToString()!.Contains(bad.Thumbprint), TimeSpan.FromSeconds(5));
            skipped.Message.ToString().Should().Contain(badCase == "server-use-only"
                ? "does not permit client authentication"
                : kind == "pem-folder" ? "it has no private key in the folder" : "it has no private key this process can open");
            await Assert.ThrowsAnyAsync<Exception>(async () => await warnings.FishForMessageAsync<Warning>(
                w => w.Message.ToString()!.Contains(bad.Thumbprint), TimeSpan.FromSeconds(1))); // logged once
        }

        [Theory(DisplayName = "Should_Pick_Up_A_Newer_Server_Only_Certificate_When_Mutual_Tls_Is_Off")]
        [MemberData(nameof(SkippingKinds))]
        public async Task Should_pick_up_a_newer_server_only_certificate_when_mutual_tls_is_off(string kind)
        {
            var fixture = Open(kind);
            var oldLeaf = Leaf(fixture, 60);
            var serverOnly = Leaf(fixture, 180, TlsTestCertificates.ServerAuth);
            fixture.Add(oldLeaf);
            var node = StartNode(policy => ArteryTlsSettings.ServerOnly(fixture.Source, policy));
            var peer = StartServerOnlyFixedNode();

            await ExchangeAsync(peer, node);
            peer.Seen.Distinct().Should().Equal(oldLeaf.Thumbprint);

            fixture.Add(serverOnly); // a node that never presents a client certificate needs server use only
            await fixture.RefreshAsync();

            var freshPeer = StartServerOnlyFixedNode();
            await ExchangeAsync(freshPeer, node);
            freshPeer.Seen.Distinct().Should().Equal(serverOnly.Thumbprint);
        }

        // ---- hostname validation stays on for a certificate that arrives by rotation ----

        // Every certificate the test CA issues names "localhost" unless the spec says otherwise. The server listens on
        // "localhost", so a client that dials it checks the server certificate against that name with
        // TlsCertificateValidation.ValidateHostname. The server dials clients back by IP, so it checks only the chain.
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

            var wrongName = LeafForName(fixture, 180, OtherName);
            fixture.Add(wrongName); // no restart
            await fixture.RefreshAsync();

            var client = StartHostnameClient();
            await Assert.ThrowsAnyAsync<Exception>(() => ExchangeAsync(client, server, timeout: TimeSpan.FromSeconds(5)));
            client.Seen.Should().Contain(wrongName.Thumbprint, "the client was shown the new certificate and rejected it");
            AssertStaysUp(server, "a failed handshake of a peer does not shut the server down");
            AssertStaysUp(client, "unlike DotNetty, Artery does not shut down a node whose handshake failed");
        }

        [Theory(DisplayName = "Should_Connect_On_A_New_Connection_When_The_Source_Rotates_To_A_Newer_Certificate_With_The_Right_Name")]
        [MemberData(nameof(AllKinds))]
        public async Task Should_connect_on_a_new_connection_when_the_source_rotates_to_a_newer_certificate_with_the_right_name(string kind)
        {
            var fixture = Open(kind);
            fixture.Add(LeafForName(fixture, 60, OtherName)); // the node starts with a certificate that has the wrong name
            var server = StartHostnameServer(fixture);
            await Assert.ThrowsAnyAsync<Exception>(() =>
                ExchangeAsync(StartHostnameClient(), server, timeout: TimeSpan.FromSeconds(5))); // the control

            fixture.Add(LeafForName(fixture, 180, DialledName)); // no restart
            await fixture.RefreshAsync();

            await ExchangeAsync(StartHostnameClient(), server);
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

            var thrown = Assert.Throws<ConfigurationException>(() => StartSourceNode(fixture));

            thrown.Message.Should().Be(expected!.Message);
        }

        [Fact(DisplayName = "Should_Fail_At_Startup_When_Mutual_Tls_Needs_A_Client_Certificate_And_Only_A_Server_Certificate_Exists")]
        public void Should_fail_at_startup_when_mutual_tls_needs_a_client_certificate_and_only_a_server_certificate_exists()
        {
            var fixture = Open("pkcs12-folder");
            fixture.Add(Leaf(fixture, 60, TlsTestCertificates.ServerAuth));

            Assert.Throws<ConfigurationException>(() => StartSourceNode(fixture)).Message.Should().Contain("client");

            // Without mutual TLS the node never presents a client certificate, so it starts.
            StartNode(policy => ArteryTlsSettings.ServerOnly(fixture.Source, policy));
        }

        [Fact(DisplayName = "Should_Fail_At_Startup_With_A_Configuration_Exception_That_Carries_The_Message_When_The_Callback_Throws")]
        public void Should_fail_at_startup_with_a_configuration_exception_that_carries_the_message_when_the_callback_throws()
        {
            // Not an InvalidOperationException: any failure of the source stops the node at startup.
            var source = TlsCertificateSource.FromCallback(() => throw new ArgumentException("the callback broke"));

            Assert.Throws<ConfigurationException>(() => StartNode(policy => ArteryTlsSettings.Mutual(source, policy)))
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

            AssertStaysUp(node);
            AssertStaysUp(peer);
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

            // Inbound: the node closes a connection that arrives, with a clear error.
            await ConnectAndExpectCloseAsync(node);
            var inboundError = await errors.FishForMessageAsync<Error>(
                e => e.Message.ToString()!.Contains("incoming connection"), TimeSpan.FromSeconds(5));
            inboundError.Cause.Should().BeAssignableTo<InvalidOperationException>();

            AssertStaysUp(node);
            AssertStaysUp(peer);
            AssertStaysUp(outboundPeer);

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
            fixture.Add(Leaf(fixture, 60));
            var node = StartSourceNode(fixture);
            var errors = CreateTestProbe();
            node.Actors.EventStream.Subscribe(errors, typeof(Error));
            var warnings = CreateTestProbe();
            node.Actors.EventStream.Subscribe(warnings, typeof(Warning));
            fixture.Source.UtcNow = () => DateTime.UtcNow.AddHours(2); // the only certificate has expired
            await fixture.RefreshAsync(); // and a look-up has found nothing else

            // Each connection that arrives fails the same way. The node says so once, not once per connection.
            for (var i = 0; i < 3; i++)
                await ConnectAndExpectCloseAsync(node);

            var first = await errors.FishForMessageAsync<Error>(
                e => e.Message.ToString()!.Contains("incoming connection"), TimeSpan.FromSeconds(5));
            first.Message.ToString().Should().StartWith("Cannot build a TLS connection for the incoming connection from [");
            await Assert.ThrowsAnyAsync<Exception>(async () => await errors.FishForMessageAsync<Error>(
                e => e.Message.ToString()!.Contains("incoming connection"), TimeSpan.FromSeconds(2)));

            // The certificate source already said why, so Akka.IO does not repeat it at Warning for each connection.
            var ioWarnings = (await warnings
                    .ReceiveWhileAsync<Warning>(_ => true, max: TimeSpan.FromSeconds(3), idle: TimeSpan.FromSeconds(1))
                    .ToListAsync())
                .Where(w => w.Message.ToString()!.Contains("Could not initialize TCP transport"))
                .ToList();
            ioWarnings.Should().BeEmpty("the failure was already reported by the certificate source");
            AssertStaysUp(node);
        }

        [Fact(DisplayName = "Should_Log_The_No_Certificate_Error_For_Each_Node_When_Two_Nodes_Share_One_Settings_Object")]
        public async Task Should_log_the_no_certificate_error_for_each_node_when_two_nodes_share_one_settings_object()
        {
            var fixture = Open("pkcs12-folder");
            fixture.Add(Leaf(fixture, 60));
            var shared = ArteryTlsSettings.Mutual(fixture.Source, TlsPeerPolicy.CustomTrust((c, _, _, _, _) => c is not null && _ca.Trusts(c)));
            var first = StartNode(_ => shared);
            var second = StartNode(_ => shared);
            var firstErrors = CreateTestProbe();
            first.Actors.EventStream.Subscribe(firstErrors, typeof(Error));
            var secondErrors = CreateTestProbe();
            second.Actors.EventStream.Subscribe(secondErrors, typeof(Error));
            fixture.Source.UtcNow = () => DateTime.UtcNow.AddHours(2); // the only certificate has expired
            await fixture.RefreshAsync(); // and a look-up has found nothing else

            // One node's limiter must not hide the other node's error.
            await ConnectAndExpectCloseAsync(first);
            await ConnectAndExpectCloseAsync(second);

            foreach (var errors in new[] { firstErrors, secondErrors })
            {
                var error = await errors.FishForMessageAsync<Error>(
                    e => e.Message.ToString()!.Contains("incoming connection"), TimeSpan.FromSeconds(5));
                error.Message.ToString().Should().StartWith("Cannot build a TLS connection for the incoming connection from [");
            }
        }

        // ---- the once-a-minute poll ----

        [Fact(DisplayName = "Should_Look_For_A_Renewed_Certificate_Once_A_Minute_When_The_Node_Builds_No_Connection")]
        public async Task Should_look_for_a_renewed_certificate_once_a_minute_when_the_node_builds_no_connection()
        {
            var fixture = Open("pkcs12-folder");
            var oldLeaf = Leaf(fixture, 60);
            var newLeaf = Leaf(fixture, 180);
            fixture.Add(oldLeaf);
            var node = StartNode(policy => ArteryTlsSettings.Mutual(fixture.Source, policy), TestSchedulerHocon);
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
            ArteryTlsSettings.Mutual(leaf, TlsPeerPolicy.CustomTrust((_, _, _, _, _) => true))
                .CertificateSource.Should().BeNull("a fixed certificate never changes");

            var calls = 0;
            var source = TlsCertificateSource.FromCallback(() =>
            {
                Interlocked.Increment(ref calls);
                return leaf;
            });
            ArteryTlsSettings? callbackSettings = null;
            var callbackNode = StartNode(policy => callbackSettings = ArteryTlsSettings.Mutual(source, policy), TestSchedulerHocon);
            callbackSettings!.CertificateSource!.NeedsPolling.Should().BeFalse("a callback is called for connections only");
            var atStart = Volatile.Read(ref calls);
            atStart.Should().BeGreaterThan(0, "the startup check called it");

            ((TestScheduler)callbackNode.Actors.Scheduler).Advance(TimeSpan.FromMinutes(5));
            await Task.Yield();
            Volatile.Read(ref calls).Should().Be(atStart, "no scheduled call reached the callback");

            // A folder or a store is the case that polls.
            var fixture = Open("pkcs12-folder");
            fixture.Add(Leaf(fixture, 60));
            ArteryTlsSettings.Mutual(fixture.Source, TlsPeerPolicy.CustomTrust((_, _, _, _, _) => true))
                .CertificateSource!.NeedsPolling.Should().BeTrue();
        }

        [Fact(DisplayName = "Should_Not_Throw_From_The_Scheduled_Call_When_The_Source_Has_Nothing_To_Present")]
        public async Task Should_not_throw_from_the_scheduled_call_when_the_source_has_nothing_to_present()
        {
            var fixture = Open("pkcs12-folder");
            var leaf = Leaf(fixture, 60);
            fixture.Add(leaf);
            var node = StartNode(policy => ArteryTlsSettings.Mutual(fixture.Source, policy), TestSchedulerHocon);
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
            AssertStaysUp(node);
        }

        [Fact(DisplayName = "Should_Not_Poll_The_Source_When_The_Bind_Fails")]
        public async Task Should_not_poll_the_source_when_the_bind_fails()
        {
            var fixture = Open("pkcs12-folder");
            fixture.Add(Leaf(fixture, 60));
            var source = fixture.Source;
            using var taken = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); // the port the node asks for is in use
            taken.Start();
            var port = ((IPEndPoint)taken.LocalEndpoint).Port;
            var before = CapturingTestScheduler.Created.Count;

            Assert.ThrowsAny<Exception>(() => StartNode(policy => ArteryTlsSettings.Mutual(source, policy),
                $"{CapturingTestSchedulerHocon}\nakka.remote.artery.canonical.port = {port}"));

            var scheduler = CapturingTestScheduler.Created.Skip(before).Single();
            var scans = source.ScanCount;
            scans.Should().Be(1, "the startup check ran before the bind");
            source.UtcNow = () => DateTime.UtcNow.AddSeconds(11); // a look-up would be due
            scheduler.Advance(TimeSpan.FromMinutes(2));
            await source.LastRefresh;
            source.ScanCount.Should().Be(scans, "the poll is scheduled only when the bind worked");
        }

        // ---- helpers ----

        private const string TestSchedulerHocon = @"akka.scheduler.implementation = ""Akka.TestKit.TestScheduler, Akka.TestKit""";

        private const string CapturingTestSchedulerHocon =
            @"akka.scheduler.implementation = ""Akka.Remote.Tests.Artery.CapturingTestScheduler, Akka.Remote.Tests""";

        private static string Text(X509Certificate2 certificate) =>
            $"{certificate.Thumbprint} (subject '{certificate.Subject}', expires " +
            $"{certificate.NotAfter.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture)})";

        // What one inbound and one outbound connection built now would present: the server and the client certificate.
        private static (string Server, string Client) Presented(ArteryTlsSettings settings, ILoggingAdapter log)
        {
            var server = settings.ServerSettings.CreateAuthenticationOptions("peer", log);
            var client = settings.ClientSettings.CreateAuthenticationOptions("localhost", "peer", log);
            return (((X509Certificate2)server.ServerCertificate!).Thumbprint,
                ((X509Certificate2)client.ClientCertificates![0]).Thumbprint);
        }

        // A raw socket stands in for a peer: the node closes it at once when it has nothing to present.
        private static async Task ConnectAndExpectCloseAsync(Node node)
        {
            using var tcp = new TcpClient();
            var port = RARP.For(node.Actors).Provider.DefaultAddress.Port!.Value;
            // The node rejects the connection and closes it at once. Depending on timing the client sees the close on
            // its first read, or has its connect reset; either is the expected close.
            var closed = Record.ExceptionAsync(async () =>
            {
                await tcp.ConnectAsync("127.0.0.1", port);
                await tcp.GetStream().ReadAsync(new byte[1], 0, 1);
            }).AsTask();
            (await Task.WhenAny(closed, Task.Delay(TimeSpan.FromSeconds(5)))).Should().BeSameAs(closed, "the node must close it at once");
        }

        // A node that stays up has not terminated and has not started a coordinated shutdown either: a shutdown that
        // has started but not finished would still leave WhenTerminated incomplete.
        private static void AssertStaysUp(Node node, string because = "")
        {
            CoordinatedShutdown.Get(node.Actors).ShutdownReason.Should().BeNull(because);
            node.Actors.WhenTerminated.IsCompleted.Should().BeFalse(because);
        }

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

        // A node that listens on "localhost" and takes its certificate from the source.
        private Node StartHostnameServer(SourceFixture fixture) =>
            StartNode(policy => ArteryTlsSettings.Mutual(fixture.Source, policy),
                $"akka.remote.artery.canonical.hostname = \"{DialledName}\"");

        // A node that checks the name of the server it dials, as well as the chain.
        private Node StartHostnameClient()
        {
            var leaf = _ca.Issue("peer-" + Guid.NewGuid().ToString("N"), -60, 60, Both);
            _certificates.Add(leaf);
            return StartNode(policy => ArteryTlsSettings.Mutual(leaf, policy,
                policy.And(TlsCertificateValidation.ValidateHostname())));
        }

        private Node StartSourceNode(SourceFixture fixture) =>
            StartNode(policy => ArteryTlsSettings.Mutual(fixture.Source, policy));

        // A peer with one fixed certificate, the way every node was set up before sources existed.
        private Node StartFixedNode()
        {
            var leaf = _ca.Issue("peer-" + Guid.NewGuid().ToString("N"), -60, 60, Both);
            _certificates.Add(leaf);
            return StartNode(policy => ArteryTlsSettings.Mutual(leaf, policy));
        }

        private Node StartServerOnlyFixedNode()
        {
            var leaf = _ca.Issue("peer-" + Guid.NewGuid().ToString("N"), -60, 60, new[] { TlsTestCertificates.ServerAuth });
            _certificates.Add(leaf);
            return StartNode(policy => ArteryTlsSettings.ServerOnly(leaf, policy));
        }

        // Starts an Artery node with the TLS settings that tls builds from a peer policy. The policy records each
        // certificate a peer shows and trusts the test CA.
        private Node StartNode(Func<TlsPeerPolicy, ArteryTlsSettings> tls, string hocon = "")
        {
            var node = new Node();
            var policy = TlsPeerPolicy.CustomTrust((certificate, _, _, _, _) =>
            {
                if (certificate is null)
                    return false;
                node.Seen.Enqueue(certificate.Thumbprint);
                return _ca.Trusts(certificate);
            });

            var config = ConfigurationFactory.ParseString(hocon).WithFallback(ConfigurationFactory.ParseString(@"
                akka {
                    loglevel = INFO
                    actor.provider = remote
                    remote.artery {
                        enabled = on
                        canonical.hostname = ""127.0.0.1""
                        canonical.port = 0
                    }
                }"));
            var name = "node" + Interlocked.Increment(ref _nodeCount);
            node.Actors = ActorSystem.Create(name, ActorSystemSetup.Create(
                BootstrapSetup.Create().WithConfig(config), new ArteryTlsSetup(tls(policy))));
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
    /// A <see cref="TestScheduler"/> that remembers each instance, so a spec can reach the scheduler of an
    /// ActorSystem that failed to start.
    /// </summary>
    internal sealed class CapturingTestScheduler : TestScheduler
    {
        public static readonly ConcurrentQueue<CapturingTestScheduler> Created = new ConcurrentQueue<CapturingTestScheduler>();

        public CapturingTestScheduler(Config schedulerConfig, ILoggingAdapter log) : base(schedulerConfig, log) =>
            Created.Enqueue(this);
    }
}
