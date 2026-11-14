// -----------------------------------------------------------------------
// <copyright file="TlsCertificateSourceSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Akka.Event;
using Akka.IO;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using static Akka.Tests.IO.TlsTestCertificates;

namespace Akka.Tests.IO
{
    /// <summary>
    /// Each behaviour is written once as a theory and runs against every way to hold certificates:
    /// a folder of PKCS#12 files, a folder of PEM files, a certificate store and a callback.
    /// </summary>
    public sealed class TlsCertificateSourceSpec : AkkaSpec
    {
        private const string Server = "server";
        private const string Client = "client";
        private const string ClientAndServer = "client-and-server";
        private static readonly string[] Both = { ServerAuth, ClientAuth };

        // The sources keep the real 10 second interval. A spec that changes the certificates runs a look-up
        // (PickAsync, GetAsync or RefreshAsync) before it expects the change to show.
        public TlsCertificateSourceSpec(ITestOutputHelper output)
            : base("akka.loglevel = INFO", output)
        {
        }

        public static TheoryData<string> AllKinds => SourceFixture.AllKinds;

        public static TheoryData<string> FolderKinds => SourceFixture.FolderKinds;

        public static TheoryData<string> SkippingKinds => SourceFixture.SkippingKinds;

        // password, key and certificate in one file, traditional key labels, text before the first block
        public static TheoryData<string?, bool, bool, string> PemLayouts => new TheoryData<string?, bool, bool, string>
        {
            { null, true, false, "" },
            { null, false, true, "" },
            { "secret", false, false, "" },
            { null, false, false, "Bag Attributes\n    friendlyName: node\n    localKeyID: 01 02 03\nsubject=CN = node\nissuer=CN = ca\n" },
            { null, true, false, "Bag Attributes\r\n    localKeyID: 01 02 03\r\n" },
            { null, false, false, "# written by a script\n\n" }
        };

        public static TheoryData<string, string, string, string> RejectedCases
        {
            get
            {
                // case, role, text the failure must give as the reason
                var cases = new[]
                {
                    ("expired", Server, "expired"),
                    ("not-yet-valid", Server, "not valid until"),
                    ("wrong-usage-for-server", Server, "enhanced key usage"),
                    ("wrong-usage-for-client", Client, "enhanced key usage"),
                    ("no-private-key", Server, "private key"),
                    // Under mutual TLS one certificate must allow both uses, and the reason says which one it lacks.
                    ("wrong-usage-for-server", ClientAndServer, "does not permit server authentication"),
                    ("wrong-usage-for-client", ClientAndServer, "does not permit client authentication"),
                    ("no-private-key", ClientAndServer, "private key"),
                    // A CA certificate and its key in the folder must not become the node's identity.
                    ("ca-key-usage", Server, "its key usage does not permit digital signature"),
                    ("ca-key-usage", ClientAndServer, "its key usage does not permit digital signature")
                };
                var data = new TheoryData<string, string, string, string>();
                foreach (var kind in SourceFixture.AllKindNames)
                {
                    foreach (var (name, role, reason) in cases)
                        data.Add(kind, name, role, reason);
                }

                return data;
            }
        }

        public static TheoryData<string, string, string> AcceptedUsageCases
        {
            get
            {
                var data = new TheoryData<string, string, string>();
                foreach (var kind in SourceFixture.AllKindNames)
                {
                    foreach (var eku in new[] { "no-eku", "any-purpose", "both-uses" })
                    {
                        foreach (var role in new[] { Server, Client, ClientAndServer })
                            data.Add(kind, eku, role);
                    }
                }

                return data;
            }
        }

        private static TlsCertificateRole RoleOf(string role) => role switch
        {
            Server => TlsCertificateRole.Server,
            Client => TlsCertificateRole.Client,
            _ => TlsCertificateRole.ClientAndServer
        };

        private static SourceFixture Open(string kind) => SourceFixture.Open(kind);

        // The source logs through the adapter it is handed, during the call. A spec that counts log lines hands it a
        // RecordingLog and counts right after the call, instead of waiting out an EventFilter that expects none.
        private X509Certificate2 Get(SourceFixture fixture, string role = Server, ILoggingAdapter? log = null) =>
            fixture.Source.GetCertificate(RoleOf(role), log ?? Sys.Log);

        private string Pick(SourceFixture fixture, string role = Server, ILoggingAdapter? log = null) =>
            Get(fixture, role, log).Thumbprint;

        // Runs a look-up first, so the call sees the certificates as they are now.
        private async Task<X509Certificate2> GetAsync(SourceFixture fixture, string role = Server, ILoggingAdapter? log = null)
        {
            await fixture.RefreshAsync();
            return Get(fixture, role, log);
        }

        private async Task<string> PickAsync(SourceFixture fixture, string role = Server, ILoggingAdapter? log = null) =>
            (await GetAsync(fixture, role, log)).Thumbprint;

        // Checks how many lines at this level contain the text.
        private static void ExpectLines(RecordingLog log, LogLevel level, string contains, int count)
        {
            var lines = (level == LogLevel.WarningLevel ? log.Warnings : log.Infos).Where(l => l.Contains(contains)).ToList();
            lines.Should().HaveCount(count, $"lines at {level} that contain '{contains}'");
        }

        // Runs the body with a RecordingLog and checks the lines it holds afterwards.
        private static async Task ExpectLinesAsync(LogLevel level, string contains, int count, Func<RecordingLog, Task> body)
        {
            var log = new RecordingLog();
            await body(log);
            ExpectLines(log, level, contains, count);
        }

        // Asks the way the startup check does: a caller with nothing to present waits for the look-up. A connection
        // (Get) never waits, so a spec runs a look-up first (GetAsync, PickAsync or RefreshAsync) or asks this way.
        private Task<X509Certificate2> WaitAsync(SourceFixture fixture, string role = Server, ILoggingAdapter? log = null) =>
            fixture.Source.GetCertificateAsync(RoleOf(role), log ?? Sys.Log);

        private static void Sign(X509Certificate2 certificate)
        {
            var data = new byte[] { 1, 2, 3 };
            using var rsa = certificate.GetRSAPrivateKey();
            if (rsa is not null)
            {
                rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1).Should().NotBeEmpty();
                return;
            }

            using var ecdsa = certificate.GetECDsaPrivateKey();
            ecdsa!.SignData(data, HashAlgorithmName.SHA256).Should().NotBeEmpty();
        }

        // ---- 1 and 2: the pick ----

        [Theory(DisplayName = "Should_Pick_The_Valid_Certificate_That_Was_Issued_Last")]
        [MemberData(nameof(AllKinds))]
        public async Task Should_pick_the_valid_certificate_that_was_issued_last(string kind)
        {
            using var fixture = Open(kind);
            var oldest = fixture.Make(-180, 60, false, Both);
            var newest = fixture.Make(-60, 180, true, Both);
            var middle = fixture.Make(-120, 120, false, Both);
            fixture.Add(oldest);
            fixture.Add(newest);
            fixture.Add(middle);

            (await PickAsync(fixture)).Should().Be(newest.Thumbprint);
        }

        [Theory(DisplayName = "Should_Change_The_Pick_When_A_Later_Certificate_Is_Added_And_Change_It_Back_When_Removed")]
        [MemberData(nameof(AllKinds))]
        public async Task Should_change_the_pick_when_a_later_certificate_is_added_and_back_when_removed(string kind)
        {
            using var fixture = Open(kind);
            var first = fixture.Make(-60, 60, false, Both);
            var later = fixture.Make(-30, 180, false, Both);
            fixture.Add(first);
            (await PickAsync(fixture)).Should().Be(first.Thumbprint);

            fixture.Add(later);
            (await PickAsync(fixture)).Should().Be(later.Thumbprint);

            fixture.Remove(later);
            (await PickAsync(fixture)).Should().Be(first.Thumbprint);
        }

        // ---- 3 and 4: what is rejected and what is accepted ----

        [Theory(DisplayName = "Should_Reject_A_Certificate_That_Cannot_Be_Used")]
        [MemberData(nameof(RejectedCases))]
        public async Task Should_reject_a_certificate_that_cannot_be_used(string kind, string reasonCase, string role,
            string reasonText)
        {
            using var fixture = Open(kind);
            var valid = fixture.Make(-60, 60, false, Both);
            // Each unusable certificate but the expired one was issued after the valid one, so only the rejection
            // keeps it from winning.
            var unusable = reasonCase switch
            {
                "expired" => fixture.Make(-120, -10, false, Both),
                "not-yet-valid" => fixture.Make(30, 600, false, Both),
                "wrong-usage-for-server" => fixture.Make(-30, 600, false, ClientAuth),
                "wrong-usage-for-client" => fixture.Make(-30, 600, false, ServerAuth),
                "ca-key-usage" => fixture.Make(-30, 600, X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true),
                _ => fixture.Make(-30, 600, false, Both)
            };
            fixture.Add(valid);
            fixture.Add(unusable, withKey: reasonCase != "no-private-key");
            await fixture.RefreshAsync();

            if (fixture.FailsOnUnusableCertificate)
            {
                // A callback hands over one certificate. Leave it only the unusable one, so it must refuse it.
                fixture.Remove(valid);
                Action act = () => Get(fixture, role);
                act.Should().Throw<InvalidOperationException>()
                    .WithMessage($"*{unusable.Thumbprint}*{reasonText}*");
            }
            else
            {
                Pick(fixture, role).Should().Be(valid.Thumbprint);
            }
        }

        [Theory(DisplayName = "Should_Accept_A_Certificate_With_No_Eku_The_Any_Purpose_Eku_Or_Both_Uses_For_Every_Role")]
        [MemberData(nameof(AcceptedUsageCases))]
        public async Task Should_accept_a_certificate_with_no_eku_the_any_purpose_eku_or_both_uses_for_every_role(
            string kind, string eku, string role)
        {
            using var fixture = Open(kind);
            var certificate = eku switch
            {
                "no-eku" => fixture.Make(-60, 60, false),
                "any-purpose" => fixture.Make(-60, 60, false, AnyPurpose),
                _ => fixture.Make(-60, 60, false, Both)
            };
            fixture.Add(certificate);

            (await PickAsync(fixture, role)).Should().Be(certificate.Thumbprint);
        }

        // ---- 4: store names ----

        [Fact(DisplayName = "Should_Match_The_Store_Common_Name_In_Full_Ignoring_Case")]
        public async Task Should_match_the_store_common_name_in_full_ignoring_case()
        {
            using var fixture = Open("store");
            var shouted = fixture.Make(fixture.CommonName.ToUpperInvariant(), -60, 60, false, Both);
            fixture.Add(shouted);

            (await PickAsync(fixture)).Should().Be(shouted.Thumbprint);
        }

        [Fact(DisplayName = "Should_Not_Match_A_Store_Common_Name_That_Is_Only_A_Prefix")]
        public async Task Should_not_match_a_store_common_name_that_is_only_a_prefix()
        {
            using var fixture = Open("store");
            var longer = fixture.Make(fixture.CommonName + "-extra", -60, 600, false, Both);
            fixture.Add(longer);
            await fixture.RefreshAsync();

            Action act = () => Get(fixture);
            act.Should().Throw<InvalidOperationException>().WithMessage("*No certificate in store*");

            var exact = fixture.Make(-60, 60, false, Both);
            fixture.Add(exact);
            (await PickAsync(fixture)).Should().Be(exact.Thumbprint);
        }

        // ---- 5: folders ----

        [Fact(DisplayName = "Should_Pair_A_Pem_Certificate_With_The_Fitting_Key_When_File_Names_Do_Not_Correspond")]
        public async Task Should_pair_a_pem_certificate_with_the_fitting_key_when_file_names_do_not_correspond()
        {
            using var fixture = Open("pem-folder");
            // One RSA and one ECDSA certificate, each in its own file with its key in another file, all with
            // random names. A wrong pairing would leave the certificate without a usable key.
            var rsa = fixture.Make(-90, 60, true, Both);
            var ecdsa = fixture.Make(-30, 180, false, Both);
            var other = fixture.Make(-60, 120, true, Both);
            fixture.Add(rsa);
            fixture.Add(ecdsa);
            fixture.Add(other);

            var picked = await GetAsync(fixture);
            picked.Thumbprint.Should().Be(ecdsa.Thumbprint);
            Sign(picked);

            fixture.Remove(ecdsa);
            var next = await GetAsync(fixture);
            next.Thumbprint.Should().Be(other.Thumbprint);
            Sign(next);
        }

        [Theory(DisplayName = "Should_Read_A_Pem_Layout_With_Its_Private_Key")]
        [MemberData(nameof(PemLayouts))]
        public async Task Should_read_a_pem_layout_with_its_private_key(string? password, bool sameFile,
            bool traditionalKeys, string leadingText)
        {
            using var fixture = new PemFolderFixture(password, sameFile, traditionalKeys, leadingText);
            SkipIfNeeded(fixture);
            var rsa = fixture.Make(-30, 60, true, Both);
            var ecdsa = fixture.Make(-60, 30, false, Both);
            fixture.Add(rsa);
            fixture.Add(ecdsa);

            var picked = await GetAsync(fixture);
            picked.Thumbprint.Should().Be(rsa.Thumbprint);
            Sign(picked);

            fixture.Remove(rsa);
            picked = await GetAsync(fixture);
            picked.Thumbprint.Should().Be(ecdsa.Thumbprint);
            Sign(picked);
        }

        [Fact(DisplayName = "Should_Load_Pkcs12_Files_With_The_Given_Key_Storage_Flags")]
        public async Task Should_load_pkcs12_files_with_the_given_key_storage_flags()
        {
            using var fixture = new Pkcs12FolderFixture();
            var certificate = fixture.Make(-60, 60, false, Both);
            fixture.Add(certificate);

            // The public overload uses the default flags, so it is the same as passing DefaultKeySet.
            (await PickAsync(fixture)).Should().Be(certificate.Thumbprint);

            var exportable = TlsCertificateSource.FromFolder(fixture.Folder, SourceFixture.Password,
                X509KeyStorageFlags.DefaultKeySet | X509KeyStorageFlags.Exportable);
            await exportable.RefreshAsync();
            using var picked = new X509Certificate2(exportable.GetCertificate(TlsCertificateRole.Server, Sys.Log));
            picked.Thumbprint.Should().Be(certificate.Thumbprint);

            // Flags that cannot go together make the framework refuse the load, which shows they reach it. The
            // refusal is a failed file, not an exception that escapes the scan.
            var invalid = TlsCertificateSource.FromFolder(fixture.Folder, SourceFixture.Password,
                X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.PersistKeySet);
            await invalid.RefreshAsync();
            Action act = () => invalid.GetCertificate(TlsCertificateRole.Server, Sys.Log);
            act.Should().Throw<InvalidOperationException>().WithMessage("*Could not load*failed to load*");
        }

        [Fact(DisplayName = "Should_Skip_An_Encrypted_Pem_Key_With_A_Warning_When_The_Password_Is_Wrong")]
        public async Task Should_skip_an_encrypted_pem_key_with_a_warning_when_the_password_is_wrong()
        {
            using var fixture = new PemFolderFixture(password: "secret");
            SkipIfNeeded(fixture);
            fixture.Add(fixture.Make(-60, 60, true, Both));
            var wrong = TlsCertificateSource.FromFolder(fixture.Folder, "not-the-password");
            await wrong.RefreshAsync();

            await EventFilter.Warning(contains: "could not be opened with the password").ExpectOneAsync(() =>
            {
                Action act = () => wrong.GetCertificate(TlsCertificateRole.Server, Sys.Log);
                act.Should().Throw<InvalidOperationException>().WithMessage("*Could not load*");
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "Should_Ignore_A_Pem_File_With_No_Private_Key_Without_A_Warning_When_It_Would_Not_Be_The_Pick")]
        public async Task Should_ignore_a_pem_file_with_no_private_key_without_a_warning_when_it_would_not_be_the_pick()
        {
            using var fixture = Open("pem-folder");
            var valid = fixture.Make(-60, 60, false, Both);
            // A CA bundle such as ca.crt: it has no key anywhere, and was issued before the valid certificate.
            var authority = fixture.Make(-120, 600, false, Both);
            fixture.Add(valid);
            fixture.Add(authority, withKey: false);
            await fixture.RefreshAsync();

            var log = new RecordingLog();
            Pick(fixture, log: log).Should().Be(valid.Thumbprint);
            log.Warnings.Should().BeEmpty();
        }

        [Fact(DisplayName = "Should_Warn_About_A_Renewed_Pem_Certificate_That_Arrived_Without_Its_Key")]
        public async Task Should_warn_about_a_renewed_pem_certificate_that_arrived_without_its_key()
        {
            using var fixture = Open("pem-folder");
            var current = fixture.Make(-60, 60, false, Both);
            var renewed = fixture.Make(-30, 600, false, Both); // issued later: with its key it would be the pick
            fixture.Add(current);
            (await PickAsync(fixture)).Should().Be(current.Thumbprint);
            fixture.Add(renewed, withKey: false);

            await ExpectLinesAsync(LogLevel.WarningLevel, renewed.Thumbprint, 1, async log =>
            {
                (await PickAsync(fixture, log: log)).Should().Be(current.Thumbprint);
                Pick(fixture, log: log).Should().Be(current.Thumbprint);
            });
            await ExpectLinesAsync(LogLevel.WarningLevel, "has no private key in the folder", 0, async log =>
                (await PickAsync(fixture, log: log)).Should().Be(current.Thumbprint)); // repeats every 5 minutes at most
        }

        [Fact(DisplayName = "Should_Not_Throw_Format_Exception_For_A_Pem_Certificate_Whose_Data_Shares_The_Begin_Line")]
        public async Task Should_read_a_pem_certificate_whose_data_shares_the_begin_line()
        {
            using var fixture = new PemFolderFixture();
            SkipIfNeeded(fixture);
            var certificate = fixture.Make(-60, 60, false, Both);
            fixture.Add(certificate);
            foreach (var file in Directory.GetFiles(fixture.Folder, "crt-*"))
                File.WriteAllText(file, "-----BEGIN CERTIFICATE-----" +
                                        Convert.ToBase64String(certificate.RawData) + "-----END CERTIFICATE-----");

            // The base64 data is read where the PEM reader found it, not from the line after BEGIN.
            var picked = await GetAsync(fixture);
            picked.Thumbprint.Should().Be(certificate.Thumbprint);
            Sign(picked);
        }

        [Theory(DisplayName = "Should_Skip_A_Half_Written_File_With_A_Warning_And_Keep_The_Old_Certificate")]
        [MemberData(nameof(FolderKinds))]
        public async Task Should_skip_a_half_written_file_with_a_warning_and_keep_the_old_certificate(string kind)
        {
            using var fixture = Open(kind);
            var certificate = fixture.Make(-60, 60, false, Both);
            fixture.Add(certificate);
            (await PickAsync(fixture)).Should().Be(certificate.Thumbprint);

            fixture.AddBroken();

            await EventFilter.Warning(contains: "TLS certificate skipped").ExpectOneAsync(async () =>
            {
                (await PickAsync(fixture)).Should().Be(certificate.Thumbprint);
                (await PickAsync(fixture)).Should().Be(certificate.Thumbprint); // the same file does not warn again
            });
        }

        [Theory(DisplayName = "Should_Count_The_Same_Certificate_In_Two_Files_Once")]
        [MemberData(nameof(FolderKinds))]
        public async Task Should_count_the_same_certificate_in_two_files_once(string kind)
        {
            using var fixture = Open(kind);
            var expired = fixture.Make(-120, -10, false, Both);
            fixture.Add(expired);
            fixture.Add(expired);
            await fixture.RefreshAsync();

            // The failure lists each candidate, so a certificate counted twice would show up twice.
            Action act = () => Get(fixture);
            var message = act.Should().Throw<InvalidOperationException>().Which.Message;
            Regex.Matches(message, expired.Thumbprint).Count.Should().Be(1);

            var valid = fixture.Make(-60, 60, false, Both);
            fixture.Add(valid);
            fixture.Add(valid);
            (await PickAsync(fixture)).Should().Be(valid.Thumbprint);
        }

        [Theory(DisplayName = "Should_Ignore_Folder_Entries_That_Start_With_Two_Dots")]
        [MemberData(nameof(FolderKinds))]
        public async Task Should_ignore_folder_entries_that_start_with_two_dots(string kind)
        {
            using var fixture = (FolderFixture)Open(kind);
            var visible = fixture.Make(-60, 60, false, Both);
            var hidden = fixture.Make(-30, 600, false, Both);
            fixture.Add(visible);
            fixture.Add(hidden, withKey: true, prefix: "..");

            (await PickAsync(fixture)).Should().Be(visible.Thumbprint);
        }

        [Theory(DisplayName = "Should_Pick_Up_A_File_Swapped_Through_A_Symbolic_Link")]
        [MemberData(nameof(FolderKinds))]
        public async Task Should_pick_up_a_file_swapped_through_a_symbolic_link(string kind)
        {
            using var fixture = (FolderFixture)Open(kind);
            var first = fixture.Make(-60, 60, false, Both);
            var second = fixture.Make(-90, 30, false, Both); // issued earlier: only the swap can make it the pick
            try
            {
                fixture.Link("tls", first);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException ||
                                      e is PlatformNotSupportedException)
            {
                Assert.Skip($"This platform or target cannot create symbolic links: {e.Message}");
            }

            (await PickAsync(fixture)).Should().Be(first.Thumbprint);

            // Replace the link, as a Kubernetes secret mount does when the secret changes.
            fixture.Link("tls", second);
            (await PickAsync(fixture)).Should().Be(second.Thumbprint);
        }

        [Fact(DisplayName = "Should_Throw_When_The_Folder_Does_Not_Exist")]
        public async Task Should_throw_when_the_folder_does_not_exist()
        {
            var missing = Path.Combine(Path.GetTempPath(), "akka-tls-missing-" + Guid.NewGuid().ToString("N"));
            var source = TlsCertificateSource.FromFolder(missing, null);
            await source.RefreshAsync();

            Action act = () => source.GetCertificate(TlsCertificateRole.Server, Sys.Log);
            act.Should().Throw<InvalidOperationException>().WithMessage("*does not exist*");
        }

        // ---- 6: logging ----

        [Theory(DisplayName = "Should_Log_One_Info_Line_Per_Change_Of_Pick")]
        [MemberData(nameof(AllKinds))]
        public async Task Should_log_one_info_line_per_change_of_pick(string kind)
        {
            using var fixture = Open(kind);
            var first = fixture.Make(-60, 60, false, Both);
            var second = fixture.Make(-30, 180, false, Both);
            fixture.Add(first);
            await fixture.RefreshAsync();

            await EventFilter.Info(contains: "TLS server certificate changed from none to " + first.Thumbprint)
                .ExpectOneAsync(() =>
                {
                    Pick(fixture);
                    Pick(fixture); // no change, no line
                    return Task.CompletedTask;
                });

            fixture.Add(second);
            await EventFilter.Info(contains: $"changed from {first.Thumbprint}")
                .ExpectOneAsync(async () =>
                {
                    (await PickAsync(fixture)).Should().Be(second.Thumbprint);
                    Pick(fixture);
                });
        }

        [Theory(DisplayName = "Should_Warn_Once_About_A_Skipped_Certificate_That_Was_Issued_More_Recently_Than_The_Pick")]
        [MemberData(nameof(SkippingKinds))]
        public async Task Should_warn_once_about_a_skipped_certificate_that_was_issued_more_recently_than_the_pick(string kind)
        {
            using var fixture = Open(kind);
            var valid = fixture.Make(-120, 600, false, Both);
            // Issued after the pick and expiring before it: only the issue date makes it a better certificate.
            var clientOnly = fixture.Make(-30, 60, false, ClientAuth);
            // Issued before the pick, so it would not have won even if it could be used, although it expires later.
            var olderClientOnly = fixture.Make(-240, 700, false, ClientAuth);
            fixture.Add(valid);
            fixture.Add(clientOnly);
            fixture.Add(olderClientOnly);
            await fixture.RefreshAsync();

            var log = new RecordingLog();
            Pick(fixture, log: log).Should().Be(valid.Thumbprint);
            Pick(fixture, log: log);
            ExpectLines(log, LogLevel.WarningLevel, clientOnly.Thumbprint, 1);
            ExpectLines(log, LogLevel.WarningLevel, olderClientOnly.Thumbprint, 0);
        }

        [Theory(DisplayName = "Should_Warn_Once_Per_Role_And_Name_The_Missing_Use_When_The_Client_And_Server_Role_Skips_A_Certificate")]
        [MemberData(nameof(SkippingKinds))]
        public async Task Should_warn_once_per_role_and_name_the_missing_use_when_the_client_and_server_role_skips_a_certificate(string kind)
        {
            using var fixture = Open(kind);
            var valid = fixture.Make(-60, 60, false, Both);
            var serverOnly = fixture.Make(-30, 600, false, ServerAuth);
            fixture.Add(valid);
            fixture.Add(serverOnly);

            // The server role may use the server-only certificate: it has its own pick and its own warnings.
            (await PickAsync(fixture, Server)).Should().Be(serverOnly.Thumbprint);

            await EventFilter.Warning(contains: "does not permit client authentication").ExpectOneAsync(() =>
            {
                Pick(fixture, ClientAndServer).Should().Be(valid.Thumbprint);
                Pick(fixture, ClientAndServer);
                return Task.CompletedTask;
            });
        }

        [Theory(DisplayName = "Should_Not_Warn_About_A_Certificate_That_Is_Not_Valid_Yet_Or_Expired")]
        [MemberData(nameof(SkippingKinds))]
        public async Task Should_not_warn_about_a_certificate_that_is_not_valid_yet_or_expired(string kind)
        {
            using var fixture = Open(kind);
            var valid = fixture.Make(-60, 60, false, Both);
            var staged = fixture.Make(30, 600, false, Both); // a renewal staged ahead of time
            var expired = fixture.Make(-120, -10, false, Both);
            fixture.Add(valid);
            fixture.Add(staged);
            fixture.Add(expired);
            await fixture.RefreshAsync();

            var log = new RecordingLog();
            Pick(fixture, log: log).Should().Be(valid.Thumbprint);
            log.Warnings.Should().BeEmpty();
        }

        [Theory(DisplayName = "Should_Return_The_Pick_Without_Logging_And_Log_The_Change_On_The_Next_Real_Call")]
        [MemberData(nameof(AllKinds))]
        public async Task Should_return_the_pick_without_logging_and_log_the_change_on_the_next_real_call(string kind)
        {
            using var fixture = Open(kind);
            var certificate = fixture.Make(-60, 60, false, Both);
            fixture.Add(certificate);
            await fixture.RefreshAsync();

            fixture.Source.GetCertificateWithoutLogging(TlsCertificateRole.Server).Thumbprint
                .Should().Be(certificate.Thumbprint);

            // Nothing was logged so far: the next real call logs the change. Only a call with a logger can log.
            var log = new RecordingLog();
            Pick(fixture, log: log);
            ExpectLines(log, LogLevel.InfoLevel, "changed from none to " + certificate.Thumbprint, 1);
        }

        // ---- 7: nothing passes ----

        [Theory(DisplayName = "Should_Throw_And_Name_The_Reasons_When_No_Certificate_Passes")]
        [MemberData(nameof(AllKinds))]
        public async Task Should_throw_and_name_the_reasons_when_no_certificate_passes(string kind)
        {
            using var fixture = Open(kind);
            var expired = fixture.Make(-120, -10, false, Both);
            var clientOnly = fixture.Make(-60, 600, false, ClientAuth);
            fixture.Add(expired);
            fixture.Add(clientOnly);
            await fixture.RefreshAsync();

            Action act = () => Get(fixture);
            var message = act.Should().Throw<InvalidOperationException>().Which.Message;
            message.Should().Contain(clientOnly.Thumbprint).And.Contain("enhanced key usage");
            if (!fixture.FailsOnUnusableCertificate)
                message.Should().Contain(expired.Thumbprint).And.Contain("expired");
        }

        [Theory(DisplayName = "Should_Throw_When_There_Is_No_Certificate_At_All")]
        [MemberData(nameof(SkippingKinds))]
        public async Task Should_throw_when_there_is_no_certificate_at_all(string kind)
        {
            using var fixture = Open(kind);
            await fixture.RefreshAsync();

            Action act = () => Get(fixture);
            act.Should().Throw<InvalidOperationException>().WithMessage("*No certificate in*");
        }

        [Fact(DisplayName = "Should_Throw_When_The_Callback_Returns_Null")]
        public void Should_throw_when_the_callback_returns_null()
        {
            var source = TlsCertificateSource.FromCallback(() => null!);

            Action act = () => source.GetCertificate(TlsCertificateRole.Server, Sys.Log);
            act.Should().Throw<InvalidOperationException>().WithMessage("*returned null*");
        }

        // ---- ownership and caching ----

        [Theory(DisplayName = "Should_Not_Dispose_A_Returned_Certificate_When_A_Newer_One_Replaces_It")]
        [MemberData(nameof(AllKinds))]
        public async Task Should_not_dispose_a_returned_certificate_when_a_newer_one_replaces_it(string kind)
        {
            using var fixture = Open(kind);
            var old = fixture.Make(-60, 60, true, Both);
            var renewed = fixture.Make(-30, 180, true, Both);
            fixture.Add(old);
            var held = await GetAsync(fixture); // stands for a handshake in flight

            fixture.Add(renewed);
            (await GetAsync(fixture)).Thumbprint.Should().Be(renewed.Thumbprint);
            fixture.Remove(old); // the old file or store entry is gone too

            (await GetAsync(fixture)).Thumbprint.Should().Be(renewed.Thumbprint);
            Sign(held); // the key still works
        }

        [Theory(DisplayName = "Should_Return_The_Same_Instance_While_Nothing_Changes")]
        [MemberData(nameof(AllKinds))]
        public async Task Should_return_the_same_instance_while_nothing_changes(string kind)
        {
            using var fixture = Open(kind);
            fixture.Add(fixture.Make(-60, 60, false, Both));

            var first = await GetAsync(fixture);
            Get(fixture).Should().BeSameAs(first);
            (await GetAsync(fixture)).Should().BeSameAs(first, "a look-up keeps the instance it loaded while nothing changes");
        }

        [Fact(DisplayName = "Should_Reject_Null_And_Empty_Arguments")]
        public void Should_reject_null_and_empty_arguments()
        {
            Assert.Throws<ArgumentNullException>(() => TlsCertificateSource.FromFolder(null!, null));
            Assert.Throws<ArgumentException>(() => TlsCertificateSource.FromFolder(" ", null));
            Assert.Throws<ArgumentNullException>(() => TlsCertificateSource.FromCallback(null!));
            Assert.Throws<ArgumentException>(() =>
                TlsCertificateSource.FromStore(" ", "My", StoreLocation.CurrentUser));
        }


        // ---- review round: an empty file, the last good certificate, the pick, the scan interval ----

        private static DateTimeOffset Whole(DateTimeOffset time) =>
            new DateTimeOffset(time.Ticks - time.Ticks % TimeSpan.TicksPerSecond, time.Offset);

        [Theory(DisplayName = "Should_Skip_An_Empty_File_With_A_Warning_And_Keep_Using_The_Valid_Certificate")]
        [MemberData(nameof(FolderKinds))]
        public async Task Should_skip_an_empty_file_with_a_warning_and_keep_using_the_valid_certificate(string kind)
        {
            using var fixture = (FolderFixture)Open(kind);
            var certificate = fixture.Make(-60, 60, false, Both);
            fixture.Add(certificate);
            File.WriteAllBytes(Path.Combine(fixture.Folder, "empty-" + Guid.NewGuid().ToString("N")), new byte[0]);
            await fixture.RefreshAsync();

            await EventFilter.Warning(contains: "TLS certificate skipped").ExpectOneAsync(() =>
            {
                Pick(fixture).Should().Be(certificate.Thumbprint);
                Pick(fixture).Should().Be(certificate.Thumbprint); // the same file does not warn again
                return Task.CompletedTask;
            });
        }

        [Theory(DisplayName = "Should_Throw_The_Documented_Exception_When_The_Only_File_Is_Empty")]
        [MemberData(nameof(FolderKinds))]
        public async Task Should_throw_the_documented_exception_when_the_only_file_is_empty(string kind)
        {
            using var fixture = (FolderFixture)Open(kind);
            File.WriteAllBytes(Path.Combine(fixture.Folder, "empty"), new byte[0]);
            await fixture.RefreshAsync();

            Action act = () => Get(fixture);
            act.Should().Throw<InvalidOperationException>().WithMessage("*Could not load*file 'empty'*");
        }

        [Fact(DisplayName = "Should_Not_Swallow_A_Fatal_Exception_From_The_Callback")]
        public void Should_not_swallow_a_fatal_exception_from_the_callback()
        {
            using var certificate = Create("fatal", -60, 60, false, Both);
            var calls = 0;
            var source = TlsCertificateSource.FromCallback(() =>
                ++calls == 1 ? certificate : throw new OutOfMemoryException());
            source.GetCertificate(TlsCertificateRole.Server, Sys.Log); // a previous pick exists

            Assert.Throws<OutOfMemoryException>(() => source.GetCertificate(TlsCertificateRole.Server, Sys.Log));
        }

        // ---- the last good certificate ----

        [Theory(DisplayName = "Should_Keep_The_Previous_Certificate_And_Warn_Once_When_The_Look_Up_Finds_Nothing")]
        [MemberData(nameof(AllKinds))]
        public async Task Should_keep_the_previous_certificate_and_warn_once_when_the_look_up_finds_nothing(string kind)
        {
            using var fixture = Open(kind);
            var certificate = fixture.Make(-60, 60, false, Both);
            fixture.Add(certificate);
            (await PickAsync(fixture)).Should().Be(certificate.Thumbprint);

            fixture.Remove(certificate); // the look-up finds nothing now

            await EventFilter.Warning(contains: "keeps using the previous certificate " + certificate.Thumbprint)
                .ExpectOneAsync(async () =>
                {
                    (await PickAsync(fixture)).Should().Be(certificate.Thumbprint);
                    (await PickAsync(fixture)).Should().Be(certificate.Thumbprint); // the same cause does not warn again
                });

            if (kind == "callback")
            {
                // The callback keeps the pick per role: a role it never returned a certificate for fails as before.
                Action act = () => Get(fixture, ClientAndServer);
                act.Should().Throw<InvalidOperationException>();
            }
            else
            {
                // A look-up decides every role, so this role had a pick in the previous look-up and keeps it too.
                Pick(fixture, ClientAndServer).Should().Be(certificate.Thumbprint);
            }
        }

        [Theory(DisplayName = "Should_Keep_The_Previous_Certificate_When_Every_Candidate_Is_Rejected")]
        [MemberData(nameof(AllKinds))]
        public async Task Should_keep_the_previous_certificate_when_every_candidate_is_rejected(string kind)
        {
            using var fixture = Open(kind);
            var certificate = fixture.Make(-60, 60, false, Both);
            var clientOnly = fixture.Make(-30, 60, false, ClientAuth); // cannot serve
            fixture.Add(certificate);
            (await PickAsync(fixture)).Should().Be(certificate.Thumbprint);

            fixture.Remove(certificate);
            fixture.Add(clientOnly);

            await EventFilter.Warning(contains: "keeps using the previous certificate " + certificate.Thumbprint)
                .ExpectOneAsync(async () => (await PickAsync(fixture)).Should().Be(certificate.Thumbprint));
        }

        [Theory(DisplayName = "Should_Keep_The_Previous_Certificate_When_The_Folder_Goes_Missing")]
        [MemberData(nameof(FolderKinds))]
        public async Task Should_keep_the_previous_certificate_when_the_folder_goes_missing(string kind)
        {
            using var fixture = (FolderFixture)Open(kind);
            var certificate = fixture.Make(-60, 60, false, Both);
            fixture.Add(certificate);
            (await PickAsync(fixture)).Should().Be(certificate.Thumbprint);

            Directory.Delete(fixture.Folder, recursive: true);

            await EventFilter.Warning(contains: "The folder does not exist").ExpectOneAsync(async () =>
                (await PickAsync(fixture)).Should().Be(certificate.Thumbprint));
        }

        [Theory(DisplayName = "Should_Throw_When_The_Look_Up_Finds_Nothing_And_The_Previous_Certificate_Has_Expired")]
        [MemberData(nameof(AllKinds))]
        public async Task Should_throw_when_the_look_up_finds_nothing_and_the_previous_certificate_has_expired(string kind)
        {
            using var fixture = Open(kind);
            var certificate = fixture.Make(-60, 60, false, Both);
            fixture.Add(certificate);
            (await PickAsync(fixture)).Should().Be(certificate.Thumbprint);

            fixture.Remove(certificate);
            fixture.Source.UtcNow = () => DateTime.UtcNow.AddHours(2); // the previous pick is out of date now

            Action act = () => Get(fixture);
            act.Should().Throw<InvalidOperationException>();
        }

        // ---- the pick: the most recently issued certificate ----

        public static TheoryData<string, string> IssueOrderCases
        {
            get
            {
                // case: what separates the two candidates. The better one is always the second.
                var data = new TheoryData<string, string>();
                foreach (var kind in SourceFixture.AllKindNames)
                {
                    data.Add(kind, "issued-later-expires-at-the-same-time"); // a re-issue after a key leak
                    data.Add(kind, "issued-later-expires-earlier"); // a CA that shortened lifetimes
                    data.Add(kind, "issued-at-the-same-time-expires-later"); // ties on NotBefore go by NotAfter
                }

                return data;
            }
        }

        [Theory(DisplayName = "Should_Pick_By_Issue_Date_Then_Expiry_Date")]
        [MemberData(nameof(IssueOrderCases))]
        public async Task Should_pick_by_issue_date_then_expiry_date(string kind, string reasonCase)
        {
            using var fixture = Open(kind);
            var now = Whole(DateTimeOffset.UtcNow);
            var (first, second) = reasonCase switch
            {
                "issued-later-expires-at-the-same-time" => (
                    fixture.Make(now.AddMinutes(-120), now.AddMinutes(120), false, Both),
                    fixture.Make(now.AddMinutes(-60), now.AddMinutes(120), false, Both)),
                "issued-later-expires-earlier" => (
                    fixture.Make(now.AddMinutes(-120), now.AddMinutes(240), false, Both),
                    fixture.Make(now.AddMinutes(-60), now.AddMinutes(120), false, Both)),
                _ => (
                    fixture.Make(now.AddMinutes(-60), now.AddMinutes(60), false, Both),
                    fixture.Make(now.AddMinutes(-60), now.AddMinutes(120), false, Both))
            };
            fixture.Add(second);
            fixture.Add(first);

            (await PickAsync(fixture)).Should().Be(second.Thumbprint);
        }

        [Theory(DisplayName = "Should_Break_A_Tie_On_Both_Dates_By_Thumbprint_Whatever_The_Order")]
        [MemberData(nameof(SkippingKinds))]
        public async Task Should_break_a_tie_on_both_dates_by_thumbprint_whatever_the_order(string kind)
        {
            using var fixture = Open(kind);
            var now = Whole(DateTimeOffset.UtcNow);
            var tied = Enumerable.Range(0, 6)
                .Select(_ => fixture.Make(now.AddMinutes(-60), now.AddMinutes(60), false, Both))
                .ToList();
            foreach (var certificate in tied)
                fixture.Add(certificate);

            var expected = tied.Select(c => c.Thumbprint).OrderBy(t => t, StringComparer.OrdinalIgnoreCase).Last();
            (await PickAsync(fixture)).Should().Be(expected);
        }

        // ---- the scan interval ----

        [Fact(DisplayName = "Should_Scan_At_Most_Once_Every_Ten_Seconds_By_Default")]
        public void Should_scan_at_most_once_every_ten_seconds_by_default()
        {
            // The fixtures turn the reuse off, so look at sources made the way an application makes them.
            TlsCertificateSource.FromFolder("some-folder", null).ScanInterval.Should().Be(TimeSpan.FromSeconds(10));
            TlsCertificateSource.FromStore("name", "My", StoreLocation.CurrentUser).ScanInterval
                .Should().Be(TimeSpan.FromSeconds(10));
        }

        [Theory(DisplayName = "Should_Scan_Once_For_Calls_Inside_The_Interval_And_Again_After_It")]
        [MemberData(nameof(SkippingKinds))]
        public async Task Should_scan_once_for_calls_inside_the_interval_and_again_after_it(string kind)
        {
            using var fixture = Open(kind);
            var now = DateTime.UtcNow;
            fixture.Source.UtcNow = () => now;
            fixture.Source.ScanInterval = TimeSpan.FromSeconds(10);
            var first = fixture.Make(-60, 60, false, Both);
            var second = fixture.Make(-30, 60, false, Both);
            fixture.Add(first);

            (await PickAsync(fixture)).Should().Be(first.Thumbprint);
            Pick(fixture).Should().Be(first.Thumbprint);
            fixture.Source.ScanCount.Should().Be(1, "the second call is inside the interval");

            fixture.Add(second); // the next scan sees it, but not before
            now = now.AddSeconds(9.9);
            Pick(fixture).Should().Be(first.Thumbprint);
            fixture.Source.ScanCount.Should().Be(1);

            now = now.AddSeconds(0.1); // ten seconds after the scan
            // This call starts a look-up in the background and gets the pick it had, without waiting.
            Pick(fixture).Should().Be(first.Thumbprint);
            await fixture.Source.LastRefresh;
            fixture.Source.ScanCount.Should().Be(2);
            Pick(fixture).Should().Be(second.Thumbprint); // a call after the look-up finished sees it
            fixture.Source.ScanCount.Should().Be(2);
        }

        [Theory(DisplayName = "Should_Return_The_Same_Failure_Inside_The_Interval_And_Scan_Again_After_It")]
        [MemberData(nameof(SkippingKinds))]
        public async Task Should_return_the_same_failure_inside_the_interval_and_scan_again_after_it(string kind)
        {
            using var fixture = Open(kind);
            var now = DateTime.UtcNow;
            fixture.Source.UtcNow = () => now;
            fixture.Source.ScanInterval = TimeSpan.FromSeconds(10);
            await fixture.RefreshAsync();

            Action act = () => Get(fixture);
            var failure = act.Should().Throw<InvalidOperationException>().Which.Message;
            var certificate = fixture.Make(-60, 60, false, Both);
            fixture.Add(certificate);

            act.Should().Throw<InvalidOperationException>().WithMessage(failure);
            fixture.Source.ScanCount.Should().Be(1);

            now = now.AddSeconds(10);
            (await WaitAsync(fixture)).Thumbprint.Should().Be(certificate.Thumbprint);
            fixture.Source.ScanCount.Should().Be(2);
        }

        [Theory(DisplayName = "Should_Scan_Again_When_The_Certificate_Of_The_Last_Scan_Expired_Inside_The_Interval")]
        [MemberData(nameof(SkippingKinds))]
        public async Task Should_scan_again_when_the_certificate_of_the_last_scan_expired_inside_the_interval(string kind)
        {
            using var fixture = Open(kind);
            var now = Whole(DateTimeOffset.UtcNow);
            var clock = now.UtcDateTime;
            fixture.Source.UtcNow = () => clock;
            fixture.Source.ScanInterval = TimeSpan.FromSeconds(10);
            var shortLived = fixture.Make(now.AddHours(-1), now.AddSeconds(5), false, Both);
            fixture.Add(shortLived);

            (await PickAsync(fixture)).Should().Be(shortLived.Thumbprint);

            clock = now.UtcDateTime.AddSeconds(6); // inside the interval, after the certificate expired
            Func<Task> act = () => WaitAsync(fixture);
            await act.Should().ThrowAsync<InvalidOperationException>();
            fixture.Source.ScanCount.Should().Be(2, "an expired certificate is not returned from the last scan");
        }

        [Fact(DisplayName = "Should_Call_The_Callback_Every_Time")]
        public void Should_call_the_callback_every_time()
        {
            using var certificate = Create("callback", -60, 60, false, Both);
            var calls = 0;
            var source = TlsCertificateSource.FromCallback(() =>
            {
                calls++;
                return certificate;
            });
            source.ScanInterval = TimeSpan.FromSeconds(10);

            source.GetCertificate(TlsCertificateRole.Server, Sys.Log);
            source.GetCertificate(TlsCertificateRole.Server, Sys.Log);
            source.GetCertificate(TlsCertificateRole.Server, Sys.Log);

            calls.Should().Be(3);
        }

        // ---- review round 2 ----

        // A: one bad certificate never breaks a scan

        [Theory(DisplayName = "Should_Skip_A_Certificate_Whose_Enhanced_Key_Usage_Cannot_Be_Read_And_Still_Reuse_The_Scan")]
        [MemberData(nameof(SkippingKinds))]
        public async Task Should_skip_a_certificate_whose_enhanced_key_usage_cannot_be_read_and_still_reuse_the_scan(string kind)
        {
            using var fixture = Open(kind);
            var clock = DateTime.UtcNow;
            fixture.Source.UtcNow = () => clock;
            fixture.Source.ScanInterval = TimeSpan.FromSeconds(10);
            var good = fixture.Make(-120, 600, false, Both);
            var bad = fixture.MakeWithUndecodableEku(-30, 600); // issued later, so it would win
            fixture.Add(good);
            fixture.Add(bad);
            await fixture.RefreshAsync();

            await EventFilter.Warning(contains: bad.Thumbprint + " (subject 'CN=" + fixture.CommonName).ExpectOneAsync(() =>
            {
                Pick(fixture).Should().Be(good.Thumbprint);
                Pick(fixture).Should().Be(good.Thumbprint);
                return Task.CompletedTask;
            });

            fixture.Source.ScanCount.Should().Be(1, "the bad certificate did not stop the 10 second reuse");
        }

        [Theory(DisplayName = "Should_Keep_The_Previous_Certificate_When_The_Only_Candidate_Cannot_Be_Read")]
        [MemberData(nameof(AllKinds))]
        public async Task Should_keep_the_previous_certificate_when_the_only_candidate_cannot_be_read(string kind)
        {
            using var fixture = Open(kind);
            var clock = DateTime.UtcNow;
            fixture.Source.UtcNow = () => clock;
            fixture.Source.ScanInterval = TimeSpan.FromSeconds(10);
            var good = fixture.Make(-120, 600, false, Both);
            var bad = fixture.MakeWithUndecodableEku(-30, 600);
            fixture.Add(good);
            (await PickAsync(fixture)).Should().Be(good.Thumbprint);

            fixture.Remove(good);
            fixture.Add(bad);
            clock = clock.AddSeconds(11); // a look-up is due
            await fixture.RefreshAsync();

            await EventFilter.Warning(contains: "keeps using the previous certificate " + good.Thumbprint)
                .ExpectOneAsync(() =>
                {
                    Pick(fixture).Should().Be(good.Thumbprint);
                    Pick(fixture).Should().Be(good.Thumbprint);
                    return Task.CompletedTask;
                });

            if (kind != "callback")
                fixture.Source.ScanCount.Should().Be(2, "the failed look-up is reused for 10 seconds too");
        }

        [Theory(DisplayName = "Should_Throw_The_Documented_Exception_When_The_Only_Candidate_Cannot_Be_Read")]
        [MemberData(nameof(AllKinds))]
        public async Task Should_throw_the_documented_exception_when_the_only_candidate_cannot_be_read(string kind)
        {
            using var fixture = Open(kind);
            fixture.Add(fixture.MakeWithUndecodableEku(-30, 600));
            await fixture.RefreshAsync();

            Action act = () => Get(fixture);
            act.Should().Throw<InvalidOperationException>().WithMessage("*it could not be checked*");
        }

        // B: the fall-back warning repeats, one rule for each role

        [Fact(DisplayName = "Should_Warn_At_Most_Once_Every_Five_Minutes_Whatever_The_Cause")]
        public async Task Should_warn_at_most_once_every_five_minutes_whatever_the_cause()
        {
            using var certificate = Create("callback", -60, 600, false, Both);
            var clock = DateTime.UtcNow;
            var calls = 0;
            Func<X509Certificate2> next = () => certificate;
            var source = TlsCertificateSource.FromCallback(() => next());
            source.UtcNow = () => clock;
            source.GetCertificate(TlsCertificateRole.Server, Sys.Log).Should().BeSameAs(certificate);
            next = () => throw new ArgumentException("the cause is number " + Interlocked.Increment(ref calls));

            async Task Expect(int warnings)
            {
                await ExpectLinesAsync(LogLevel.WarningLevel, "keeps using the previous certificate", warnings, log =>
                {
                    for (var i = 0; i < 20; i++) // a new message each time
                        source.GetCertificate(TlsCertificateRole.Server, log).Should().BeSameAs(certificate);
                    return Task.CompletedTask;
                });
            }

            await Expect(1); // the first time
            clock = clock.AddMinutes(4).AddSeconds(59);
            await Expect(0);
            clock = clock.AddSeconds(1);
            await Expect(1); // five minutes after the first
            clock = clock.AddMinutes(5);
            await Expect(1);
        }

        [Theory(DisplayName = "Should_Repeat_The_Warning_Every_Five_Minutes_While_The_Node_Uses_The_Previous_Certificate")]
        [MemberData(nameof(AllKinds))]
        public async Task Should_repeat_the_warning_every_five_minutes_while_the_node_uses_the_previous_certificate(string kind)
        {
            using var fixture = Open(kind);
            var clock = DateTime.UtcNow;
            fixture.Source.UtcNow = () => clock;
            var certificate = fixture.Make(-60, 600, false, Both);
            fixture.Add(certificate);
            (await PickAsync(fixture)).Should().Be(certificate.Thumbprint);
            fixture.Remove(certificate); // from now on every look-up fails

            async Task Expect(int warnings)
            {
                await ExpectLinesAsync(LogLevel.WarningLevel, "keeps using the previous certificate " + certificate.Thumbprint,
                    warnings, async log =>
                    {
                        (await PickAsync(fixture, log: log)).Should().Be(certificate.Thumbprint);
                        Pick(fixture, log: log).Should().Be(certificate.Thumbprint);
                    });
            }

            await Expect(1);
            clock = clock.AddMinutes(4);
            await Expect(0);
            clock = clock.AddMinutes(1);
            await Expect(1);
            clock = clock.AddMinutes(1);
            await Expect(0);
            clock = clock.AddMinutes(4);
            await Expect(1);
        }

        // C: a file that failed is parsed again after 5 minutes, or at once when it changes

        [Fact(DisplayName = "Should_Not_Parse_A_File_That_Failed_To_Load_Again_Until_Five_Minutes_Have_Passed")]
        public async Task Should_not_parse_a_file_that_failed_to_load_again_until_five_minutes_have_passed()
        {
            using var fixture = new PemFolderFixture();
            SkipIfNeeded(fixture);
            var certificate = fixture.Make(-60, 60, true, Both);
            fixture.Add(certificate);

            // Pretend to be Windows with flags the framework refuses, so the import of the PEM certificate fails.
            var source = TlsCertificateSource.FromFolder(fixture.Folder, null,
                X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.PersistKeySet);
            var clock = DateTime.UtcNow;
            source.UtcNow = () => clock;
            source.IsWindows = () => true;
            await source.RefreshAsync();
            Action act = () => source.GetCertificate(TlsCertificateRole.Server, Sys.Log);
            act.Should().Throw<InvalidOperationException>().WithMessage("*failed to load*");

            // The fault passes, but the same content is not tried again for 5 minutes: a broken or crafted file
            // would otherwise cost its parse on every look-up.
            source.IsWindows = () => false;
            clock = clock.AddMinutes(4);
            await source.RefreshAsync();
            act.Should().Throw<InvalidOperationException>().WithMessage("*failed to load*");

            clock = clock.AddMinutes(1);
            await source.RefreshAsync();
            source.GetCertificate(TlsCertificateRole.Server, Sys.Log).Thumbprint.Should().Be(certificate.Thumbprint);
        }

        [Fact(DisplayName = "Should_Parse_A_File_That_Failed_To_Load_Again_At_Once_When_Its_Content_Changes")]
        public async Task Should_parse_a_file_that_failed_to_load_again_at_once_when_its_content_changes()
        {
            using var fixture = new Pkcs12FolderFixture();
            var certificate = fixture.Make(-60, 60, false, Both);
            var path = Path.Combine(fixture.Folder, "tls.pfx");
            File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
            await fixture.RefreshAsync();
            Action act = () => Get(fixture);
            act.Should().Throw<InvalidOperationException>().WithMessage("*file 'tls.pfx' failed to load*");

            File.WriteAllBytes(path, certificate.Export(X509ContentType.Pkcs12, SourceFixture.Password));
            (await PickAsync(fixture)).Should().Be(certificate.Thumbprint);
        }

        [Theory(DisplayName = "Should_Warn_Again_About_A_Failed_File_After_Five_Minutes_Or_When_The_Reason_Changes")]
        [MemberData(nameof(FolderKinds))]
        public async Task Should_warn_again_about_a_failed_file_after_five_minutes_or_when_the_reason_changes(string kind)
        {
            using var fixture = (FolderFixture)Open(kind);
            var clock = DateTime.UtcNow;
            fixture.Source.UtcNow = () => clock;
            var certificate = fixture.Make(-60, 600, false, Both);
            fixture.Add(certificate);
            var path = Path.Combine(fixture.Folder, "bad-file");
            File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });

            async Task Expect(int warnings)
            {
                await ExpectLinesAsync(LogLevel.WarningLevel, "TLS certificate skipped: file 'bad-file'", warnings, async log =>
                {
                    (await PickAsync(fixture, log: log)).Should().Be(certificate.Thumbprint);
                    (await PickAsync(fixture, log: log)).Should().Be(certificate.Thumbprint);
                });
            }

            await Expect(1);
            clock = clock.AddMinutes(4);
            await Expect(0);
            clock = clock.AddMinutes(1);
            await Expect(1);

            File.WriteAllBytes(path, new byte[0]); // another reason, so another warning at once
            await Expect(1);
        }

        // D: oversized files

        [Theory(DisplayName = "Should_Skip_A_File_Larger_Than_256_KB_As_Too_Large_To_Be_A_Certificate")]
        [MemberData(nameof(FolderKinds))]
        public async Task Should_skip_a_file_larger_than_256_kb_as_too_large_to_be_a_certificate(string kind)
        {
            using var fixture = (FolderFixture)Open(kind);
            var certificate = fixture.Make(-60, 600, false, Both);
            fixture.Add(certificate);
            File.WriteAllBytes(Path.Combine(fixture.Folder, "huge"), Enumerable.Repeat((byte)0x30, 256 * 1024 + 1).ToArray());

            await EventFilter.Warning(contains: "file 'huge' failed to load: the file is larger than 256 KB, so it is too large to be a certificate")
                .ExpectOneAsync(async () =>
                {
                    (await PickAsync(fixture)).Should().Be(certificate.Thumbprint);
                    (await PickAsync(fixture)).Should().Be(certificate.Thumbprint);
                });
        }

        [Theory(DisplayName = "Should_Read_A_File_Of_Exactly_256_KB_And_Name_A_Too_Large_File_When_Nothing_Else_Exists")]
        [MemberData(nameof(FolderKinds))]
        public async Task Should_read_a_file_of_exactly_256_kb_and_name_a_too_large_file_when_nothing_else_exists(string kind)
        {
            using var fixture = (FolderFixture)Open(kind);
            File.WriteAllBytes(Path.Combine(fixture.Folder, "limit"), Enumerable.Repeat((byte)0x30, 256 * 1024).ToArray());
            await fixture.RefreshAsync();
            Action atTheLimit = () => Get(fixture);
            atTheLimit.Should().Throw<InvalidOperationException>().Which.Message
                .Should().Contain("file 'limit' failed to load").And.NotContain("too large");

            File.WriteAllBytes(Path.Combine(fixture.Folder, "huge"), Enumerable.Repeat((byte)0x30, 256 * 1024 + 1).ToArray());
            File.WriteAllBytes(Path.Combine(fixture.Folder, "junk"), new byte[] { 1, 2, 3, 4 }); // listed after 'huge'
            await fixture.RefreshAsync();
            Action over = () => Get(fixture);
            over.Should().Throw<InvalidOperationException>().WithMessage("*Could not load*file 'huge'*too large to be a certificate*")
                .Which.Message.Should().Contain("file 'junk' failed to load").And.NotContain(".;", "a reason that ends in a full stop is not followed by another");
        }

        // E: one scan for all roles, and no waiting for a caller that has a pick

        [Theory(DisplayName = "Should_Scan_Once_For_All_Roles_And_Give_Each_Role_Its_Own_Pick")]
        [MemberData(nameof(SkippingKinds))]
        public async Task Should_scan_once_for_all_roles_and_give_each_role_its_own_pick(string kind)
        {
            using var fixture = Open(kind);
            var clock = DateTime.UtcNow;
            fixture.Source.UtcNow = () => clock;
            fixture.Source.ScanInterval = TimeSpan.FromSeconds(10);
            var both = fixture.Make(-60, 600, false, Both);
            var serverOnly = fixture.Make(-30, 600, false, ServerAuth); // issued later
            fixture.Add(both);
            fixture.Add(serverOnly);

            (await PickAsync(fixture, Server)).Should().Be(serverOnly.Thumbprint);
            Pick(fixture, Client).Should().Be(both.Thumbprint);
            Pick(fixture, ClientAndServer).Should().Be(both.Thumbprint);
            Pick(fixture, Server).Should().Be(serverOnly.Thumbprint);

            fixture.Source.ScanCount.Should().Be(1, "one read of the folder or the store serves every role");
        }

        [Fact(DisplayName = "Should_Not_Wait_For_A_Scan_When_The_Caller_Has_A_Pick_That_Is_Still_In_Date")]
        public async Task Should_not_wait_for_a_scan_when_the_caller_has_a_pick_that_is_still_in_date()
        {
            using var fixture = new PemFolderFixture();
            SkipIfNeeded(fixture);
            var first = fixture.Make(-60, 600, true, ServerAuth); // gives the client and server role no pick
            var second = fixture.Make(-30, 600, true, Both);
            fixture.Add(first);
            var source = fixture.Source;
            var clock = DateTime.UtcNow;
            source.UtcNow = () => clock;
            await source.RefreshAsync();
            source.GetCertificate(TlsCertificateRole.Server, Sys.Log).Thumbprint.Should().Be(first.Thumbprint);

            // The next look-up blocks inside the read: pairing a certificate it has not seen asks IsWindows.
            using var gate = new BlockingGate();
            fixture.Add(second);
            source.IsWindows = () => gate.Block(false);
            clock = clock.AddSeconds(11); // a look-up is due

            // The caller that starts the look-up, and any other with a pick, gets the pick at once.
            source.GetCertificate(TlsCertificateRole.Server, Sys.Log).Thumbprint.Should().Be(first.Thumbprint);
            await gate.EnteredAsync();
            source.GetCertificate(TlsCertificateRole.Server, Sys.Log).Thumbprint.Should().Be(first.Thumbprint);

            // A connection with no pick for its role fails at once, with the reason of the last look-up. The startup
            // check waits for the look-up.
            Record.Exception(() => source.GetCertificate(TlsCertificateRole.ClientAndServer, Sys.Log))
                .Should().BeAssignableTo<InvalidOperationException>().Which.Message.Should().Contain("does not permit client authentication");
            var waiting = source.GetCertificateAsync(TlsCertificateRole.ClientAndServer, Sys.Log);
            (await Task.WhenAny(waiting, Task.Delay(TimeSpan.FromMilliseconds(300)))).Should().NotBeSameAs(waiting);

            gate.Release();
            (await waiting).Thumbprint.Should().Be(second.Thumbprint);
            source.GetCertificate(TlsCertificateRole.Server, Sys.Log).Thumbprint.Should().Be(second.Thumbprint);
            source.ScanCount.Should().Be(2, "the waiting caller used the look-up that finished, and did not start another");
        }

        // F: startup seeds the source, the Certificate property does not scan

        [Theory(DisplayName = "Should_Reuse_The_Scan_For_A_Read_Without_Logging_And_Return_One_Instance")]
        [MemberData(nameof(SkippingKinds))]
        public async Task Should_reuse_the_scan_for_a_read_without_logging_and_return_one_instance(string kind)
        {
            using var fixture = Open(kind);
            var clock = DateTime.UtcNow;
            fixture.Source.UtcNow = () => clock;
            fixture.Source.ScanInterval = TimeSpan.FromSeconds(10);
            fixture.Add(fixture.Make(-60, 600, false, Both));
            await fixture.RefreshAsync();

            var first = fixture.Source.GetCertificateWithoutLogging(TlsCertificateRole.Server);
            var second = fixture.Source.GetCertificateWithoutLogging(TlsCertificateRole.Server);

            second.Should().BeSameAs(first, "a read must not make a new certificate that nobody disposes");
            fixture.Source.ScanCount.Should().Be(1);
        }

        [Theory(DisplayName = "Should_Log_The_First_Pick_Once_And_Let_Later_Reads_Reuse_It")]
        [MemberData(nameof(SkippingKinds))]
        public async Task Should_log_the_first_pick_once_and_let_later_reads_reuse_it(string kind)
        {
            using var fixture = Open(kind);
            var clock = DateTime.UtcNow;
            fixture.Source.UtcNow = () => clock;
            fixture.Source.ScanInterval = TimeSpan.FromSeconds(10);
            var certificate = fixture.Make(-60, 600, false, Both);
            fixture.Add(certificate);

            X509Certificate2? seeded = null;
            await ExpectLinesAsync(LogLevel.InfoLevel, "changed from none to " + certificate.Thumbprint, 1, async log =>
            {
                seeded = await WaitAsync(fixture, log: log); // the startup check takes this path
            });

            await ExpectLinesAsync(LogLevel.InfoLevel, "certificate changed", 0, log =>
            {
                fixture.Source.GetCertificateWithoutLogging(TlsCertificateRole.Server).Should().BeSameAs(seeded);
                Get(fixture, log: log).Should().BeSameAs(seeded);
                return Task.CompletedTask;
            });

            fixture.Source.ScanCount.Should().Be(1, "the first pick seeded the 10 second reuse");
        }

        // G: the common name is read the way X509Certificate2.MatchesHostname reads it

        public static TheoryData<string, bool> CommonNameShapes => new TheoryData<string, bool>
        {
            { "printable-string", true },
            { "bmp-string", true },
            { "long-form-length", true },
            { "trailing-nul", true },
            { "teletex-string-holding-utf8", true },
            { "multi-valued-rdn", false },
            { "two-common-names", false }
        };

        [Theory(DisplayName = "Should_Match_The_Store_Common_Name_The_Way_The_Hostname_Matcher_Reads_It")]
        [MemberData(nameof(CommonNameShapes))]
        public async Task Should_match_the_store_common_name_the_way_the_hostname_matcher_reads_it(string shape, bool matches)
        {
            using var fixture = Open("store");
            var name = shape == "teletex-string-holding-utf8" ? "café-" + Guid.NewGuid().ToString("N") : fixture.CommonName;
            var source = TlsCertificateSource.FromStore(name, "My", StoreLocation.CurrentUser);
            var ascii = Encoding.ASCII.GetBytes(name);
            var utf8 = Encoding.UTF8.GetBytes(name);
            var subject = shape switch
            {
                "printable-string" => Name(Rdn(Attribute(AttributeOid.CommonName, 0x13, ascii))),
                "bmp-string" => Name(Rdn(Attribute(AttributeOid.CommonName, 0x1E, Encoding.BigEndianUnicode.GetBytes(name)))),
                // More than 127 bytes in the subject, so its length is in the long form.
                "long-form-length" => Name(Rdn(Attribute(AttributeOid.Organization, 0x0C, Encoding.UTF8.GetBytes(new string('x', 150)))),
                    Rdn(Attribute(AttributeOid.CommonName, 0x0C, utf8))),
                "trailing-nul" => Name(Rdn(Attribute(AttributeOid.CommonName, 0x0C, utf8.Concat(new byte[] { 0 }).ToArray()))),
                "teletex-string-holding-utf8" => Name(Rdn(Attribute(AttributeOid.CommonName, 0x14, utf8))),
                "multi-valued-rdn" => Name(Rdn(Attribute(AttributeOid.CommonName, 0x0C, utf8),
                    Attribute(AttributeOid.OrganizationalUnit, 0x0C, Encoding.UTF8.GetBytes("unit")))),
                _ => Name(Rdn(Attribute(AttributeOid.CommonName, 0x0C, utf8)),
                    Rdn(Attribute(AttributeOid.CommonName, 0x0C, Encoding.UTF8.GetBytes("another-name"))))
            };
            var certificate = fixture.Make(subject, false, Both);
            fixture.Add(certificate);
            await source.RefreshAsync();

            if (matches)
            {
                source.GetCertificate(TlsCertificateRole.Server, Sys.Log).Thumbprint.Should().Be(certificate.Thumbprint);
            }
            else
            {
                Action act = () => source.GetCertificate(TlsCertificateRole.Server, Sys.Log);
                act.Should().Throw<InvalidOperationException>().WithMessage("*No certificate in store*");
            }
        }

        // H: more subjects the common name rule must handle, whatever the platform decodes of them

        public static TheoryData<string, bool> CommonNameEdgeShapes => new TheoryData<string, bool>
        {
            { "multi-valued-rdn-without-common-name", true },
            { "two-common-names-in-one-rdn", false },
            { "universal-string-value", false },
            { "undecodable-value-length", false },
            { "empty-relative-name", false }
        };

        [Theory(DisplayName = "Should_Match_The_Store_Common_Name_Only_When_It_Is_The_One_Text_Common_Name_Of_A_Subject_That_Decodes")]
        [MemberData(nameof(CommonNameEdgeShapes))]
        public async Task Should_match_the_store_common_name_only_when_it_is_the_one_text_common_name_of_a_subject_that_decodes(
            string shape, bool matches)
        {
            using var fixture = Open("store");
            var utf8 = Encoding.UTF8.GetBytes(fixture.CommonName);
            var commonName = Rdn(Attribute(AttributeOid.CommonName, 0x0C, utf8));
            var subject = shape switch
            {
                // A multi-valued relative name that holds no common name does not hide the one common name.
                "multi-valued-rdn-without-common-name" => Name(
                    Rdn(Attribute(AttributeOid.Organization, 0x0C, Encoding.UTF8.GetBytes("org")),
                        Attribute(AttributeOid.OrganizationalUnit, 0x0C, Encoding.UTF8.GetBytes("unit"))),
                    commonName),
                "two-common-names-in-one-rdn" => Name(Rdn(Attribute(AttributeOid.CommonName, 0x0C, utf8),
                    Attribute(AttributeOid.CommonName, 0x0C, Encoding.UTF8.GetBytes("another-name")))),
                "universal-string-value" => Name(Rdn(Attribute(AttributeOid.CommonName, 0x1C,
                    Encoding.GetEncoding("utf-32BE").GetBytes(fixture.CommonName)))),
                // The length of the value takes a spare byte, which BER allows and DER does not.
                "undecodable-value-length" => Name(Rdn(Tlv(0x30, Tlv(0x06, AttributeOid.CommonName),
                    new byte[] { 0x0C, 0x81, (byte)utf8.Length }.Concat(utf8).ToArray()))),
                _ => Name(Rdn(), commonName)
            };

            X509Certificate2 certificate;
            try
            {
                certificate = fixture.Make(subject, false, Both);
                fixture.Add(certificate);
            }
            catch (CryptographicException e) when (shape is "universal-string-value" or "undecodable-value-length" or "empty-relative-name")
            {
                Assert.Skip($"This platform does not accept a certificate with such a subject: {e.Message}");
                return;
            }

            if (matches)
            {
                (await PickAsync(fixture)).Should().Be(certificate.Thumbprint);
                return;
            }

            // The look-up goes on past a subject it cannot match, and does not throw out of it.
            await fixture.RefreshAsync();
            Action act = () => Get(fixture);
            act.Should().Throw<InvalidOperationException>().WithMessage("*No certificate in store*");

            var control = fixture.Make(-60, 60, false, Both);
            fixture.Add(control);
            (await PickAsync(fixture)).Should().Be(control.Thumbprint);
        }

        // DER for a name, with the length in the short or the long form as it needs.
        private static byte[] Tlv(byte tag, params byte[][] content)
        {
            var body = content.SelectMany(c => c).ToArray();
            var length = body.Length < 0x80 ? new[] { (byte)body.Length }
                : body.Length < 0x100 ? new byte[] { 0x81, (byte)body.Length }
                : new byte[] { 0x82, (byte)(body.Length >> 8), (byte)body.Length };
            return new[] { tag }.Concat(length).Concat(body).ToArray();
        }

        private static byte[] Attribute(byte[] oid, byte tag, byte[] value) => Tlv(0x30, Tlv(0x06, oid), Tlv(tag, value));

        private static byte[] Rdn(params byte[][] attributes) => Tlv(0x31, attributes);

        private static X500DistinguishedName Name(params byte[][] rdns) => new X500DistinguishedName(Tlv(0x30, rdns));

        // ---- PEM on Windows: the key storage flags ----

        [Fact(DisplayName = "Should_Use_The_Key_Storage_Flags_When_A_Pem_Certificate_Is_Imported_Through_Pkcs12")]
        public async Task Should_use_the_key_storage_flags_when_a_pem_certificate_is_imported_through_pkcs12()
        {
            using var fixture = new PemFolderFixture();
            SkipIfNeeded(fixture);
            var certificate = fixture.Make(-60, 60, true, Both);
            fixture.Add(certificate);

            // Pretend to be Windows, where the paired certificate goes through PKCS#12. Flags that cannot go
            // together make the framework refuse the import, which shows they reach it.
            var invalid = TlsCertificateSource.FromFolder(fixture.Folder, null,
                X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.PersistKeySet);
            invalid.IsWindows = () => true;
            await invalid.RefreshAsync();
            Action act = () => invalid.GetCertificate(TlsCertificateRole.Server, Sys.Log);
            act.Should().Throw<InvalidOperationException>().WithMessage("*Could not load*failed to load*");

            var exportable = TlsCertificateSource.FromFolder(fixture.Folder, null,
                X509KeyStorageFlags.DefaultKeySet | X509KeyStorageFlags.Exportable);
            exportable.IsWindows = () => true;
            await exportable.RefreshAsync();
            var picked = exportable.GetCertificate(TlsCertificateRole.Server, Sys.Log);
            picked.Thumbprint.Should().Be(certificate.Thumbprint);
            Sign(picked);

            // Off Windows there is no import, so the flags do not matter.
            var other = TlsCertificateSource.FromFolder(fixture.Folder, null,
                X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.PersistKeySet);
            other.IsWindows = () => false;
            await other.RefreshAsync();
            other.GetCertificate(TlsCertificateRole.Server, Sys.Log).Thumbprint.Should().Be(certificate.Thumbprint);
        }

        // ---- dates in UTC ----

        [Theory(DisplayName = "Should_Compare_Validity_Dates_In_Utc_Against_The_Source_Clock")]
        [MemberData(nameof(AllKinds))]
        public async Task Should_compare_validity_dates_in_utc_against_the_source_clock(string kind)
        {
            using var fixture = Open(kind);
            var now = Whole(DateTimeOffset.UtcNow);
            var notBefore = now.AddMinutes(-60);
            var notAfter = now.AddMinutes(60);
            var certificate = fixture.Make(notBefore, notAfter, false, Both);
            fixture.Add(certificate);
            var clock = notAfter.UtcDateTime.AddSeconds(-1);
            fixture.Source.UtcNow = () => clock;

            (await PickAsync(fixture)).Should().Be(certificate.Thumbprint); // one second before it expires

            clock = notAfter.UtcDateTime;
            Action expired = () => Get(fixture);
            expired.Should().Throw<InvalidOperationException>(); // it expires at NotAfter

            clock = notBefore.UtcDateTime.AddSeconds(-1);
            Action early = () => Get(fixture);
            early.Should().Throw<InvalidOperationException>(); // one second before it is valid

            clock = notBefore.UtcDateTime;
            (await PickAsync(fixture)).Should().Be(certificate.Thumbprint); // valid from NotBefore, at the next look-up
        }

        // ---- the common name ----

        public static TheoryData<string> ForgedSubjects => new TheoryData<string>
        {
            "real-name-first", "no-name", "organizational-unit",
            // No common name at all, but another attribute holds the wanted text, so the platform's simple name
            // (it falls back to the organizational unit) equals it while the forged line is there too.
            "unit-holds-the-name"
        };

        [Theory(DisplayName = "Should_Not_Match_A_Common_Name_That_Another_Attribute_Forges_With_A_New_Line")]
        [MemberData(nameof(ForgedSubjects))]
        public async Task Should_not_match_a_common_name_that_another_attribute_forges_with_a_new_line(string shape)
        {
            using var fixture = Open("store");
            var forged = "x\nCN=" + fixture.CommonName;
            var subject = shape switch
            {
                "real-name-first" => Subject((AttributeOid.Organization, forged), (AttributeOid.CommonName, "someone-else")),
                "no-name" => Subject((AttributeOid.Organization, forged)),
                "unit-holds-the-name" => Subject((AttributeOid.Organization, forged), (AttributeOid.OrganizationalUnit, fixture.CommonName)),
                _ => Subject((AttributeOid.OrganizationalUnit, forged), (AttributeOid.CommonName, "someone-else"))
            };
            fixture.Add(fixture.Make(subject, false, Both));
            await fixture.RefreshAsync();

            Action act = () => Get(fixture);
            act.Should().Throw<InvalidOperationException>().WithMessage("*No certificate in store*");

            // The control: the same attribute next to the real common name still matches.
            var genuine = fixture.Make(Subject((AttributeOid.Organization, forged), (AttributeOid.CommonName, fixture.CommonName)), false, Both);
            fixture.Add(genuine);
            (await PickAsync(fixture)).Should().Be(genuine.Thumbprint);
        }

        private static class AttributeOid
        {
            public static readonly byte[] CommonName = { 0x55, 0x04, 0x03 };
            public static readonly byte[] Organization = { 0x55, 0x04, 0x0A };
            public static readonly byte[] OrganizationalUnit = { 0x55, 0x04, 0x0B };
        }

        // Builds the subject from DER, one attribute per relative name, so that a value may hold a new line
        // (a subject string would not let the specs write one on every target).
        private static X500DistinguishedName Subject(params (byte[] Oid, string Value)[] attributes)
        {
            static byte[] Tlv(byte tag, params byte[][] content)
            {
                var body = content.SelectMany(c => c).ToArray();
                if (body.Length >= 128)
                    throw new ArgumentException("The spec keeps its subjects short.");
                return new[] { tag, (byte)body.Length }.Concat(body).ToArray();
            }

            var names = attributes.Select(a =>
                Tlv(0x31, Tlv(0x30, Tlv(0x06, a.Oid), Tlv(0x0C, Encoding.UTF8.GetBytes(a.Value)))));
            return new X500DistinguishedName(Tlv(0x30, names.ToArray()));
        }

        // ---- review round 3 ----

        // 1: a read that never returns does not hang startup or a running node

        [Fact(DisplayName = "Should_Skip_A_Named_Pipe_In_The_Folder_Without_Waiting_For_A_Writer")]
        public async Task Should_skip_a_named_pipe_in_the_folder_without_waiting_for_a_writer()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Assert.Skip("A named pipe in a folder needs a Unix file system.");

            using var fixture = new Pkcs12FolderFixture();
            var certificate = fixture.Make(-60, 60, false, Both);
            fixture.Add(certificate);
            MakeFifo(Path.Combine(fixture.Folder, "pipe")).Should().Be(0, "mkfifo must work");

            await EventFilter.Warning(contains: "file 'pipe' failed to load: the file is empty or is not a regular file").ExpectOneAsync(async () =>
            {
                var call = fixture.Source.GetCertificateAsync(TlsCertificateRole.Server, Sys.Log);
                (await Task.WhenAny(call, Task.Delay(TimeSpan.FromSeconds(10)))).Should().BeSameAs(call, "a named pipe must not hold up the look-up");
                (await call).Thumbprint.Should().Be(certificate.Thumbprint);
            });
        }

        [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
        private static extern int MakeFifo(string path, uint mode = 0x1B6); // 0666

        [Fact(DisplayName = "Should_Fail_Within_The_Deadline_When_The_First_Look_Up_Does_Not_Finish")]
        public async Task Should_fail_within_the_deadline_when_the_first_look_up_does_not_finish()
        {
            using var fixture = new PemFolderFixture();
            SkipIfNeeded(fixture);
            var certificate = fixture.Make(-60, 60, true, Both);
            fixture.Add(certificate);
            using var gate = new BlockingGate();
            var source = fixture.Source;
            source.IsWindows = () => gate.Block(false); // the read never returns until the spec says so
            source.RefreshTimeout = TimeSpan.FromMilliseconds(500);

            var call = source.GetCertificateAsync(TlsCertificateRole.Server, Sys.Log); // the startup check
            (await Task.WhenAny(call, Task.Delay(TimeSpan.FromSeconds(10)))).Should().BeSameAs(call, "the caller must not wait forever");
            (await Assert.ThrowsAsync<InvalidOperationException>(() => call)).Message
                .Should().Contain("did not finish in time").And.Contain($"folder '{fixture.Folder}'");

            // A caller that comes after the deadline fails at once, and does not start a second look-up.
            var later = source.GetCertificateAsync(TlsCertificateRole.Server, null);
            (await Task.WhenAny(later, Task.Delay(TimeSpan.FromSeconds(5)))).Should().BeSameAs(later);
            await Assert.ThrowsAsync<InvalidOperationException>(() => later);
            source.ScanCount.Should().Be(1);

            // When the read returns at last, the look-up finishes and the source serves again.
            gate.Release();
            await source.LastRefresh;
            source.GetCertificate(TlsCertificateRole.Server, Sys.Log).Thumbprint.Should().Be(certificate.Thumbprint);
        }

        [Fact(DisplayName = "Should_Keep_Serving_And_Warn_Once_While_A_Look_Up_Does_Not_Finish")]
        public async Task Should_keep_serving_and_warn_once_while_a_look_up_does_not_finish()
        {
            using var fixture = new PemFolderFixture();
            SkipIfNeeded(fixture);
            var first = fixture.Make(-60, 600, true, Both);
            var second = fixture.Make(-30, 600, true, Both);
            fixture.Add(first);
            var source = fixture.Source;
            var clock = DateTime.UtcNow;
            source.UtcNow = () => clock;
            (await PickAsync(fixture)).Should().Be(first.Thumbprint);

            using var gate = new BlockingGate();
            fixture.Add(second);
            source.IsWindows = () => gate.Block(false);
            clock = clock.AddSeconds(11);
            Pick(fixture).Should().Be(first.Thumbprint); // starts the look-up, which blocks
            await gate.EnteredAsync();

            clock = clock.AddSeconds(31); // the look-up has run for more than 30 seconds
            await EventFilter.Warning(contains: "has been running since").ExpectOneAsync(() =>
            {
                Pick(fixture, Server).Should().Be(first.Thumbprint);
                Pick(fixture, Client).Should().Be(first.Thumbprint);
                Pick(fixture, ClientAndServer).Should().Be(first.Thumbprint);
                return Task.CompletedTask;
            });
            source.ScanCount.Should().Be(2, "no second look-up starts while one runs");

            gate.Release();
            await source.LastRefresh;
            Pick(fixture).Should().Be(second.Thumbprint);
        }

        // 2: the work of one look-up is bounded

        [Fact(DisplayName = "Should_Read_Only_The_First_100_Files_Of_The_Folder_By_Name")]
        public async Task Should_read_only_the_first_100_files_of_the_folder_by_name()
        {
            using var fixture = new Pkcs12FolderFixture();
            var first = fixture.Make(-60, 600, false, Both);
            var beyond = fixture.Make(-30, 600, false, Both); // issued later, so it would win if it were read
            File.WriteAllBytes(Path.Combine(fixture.Folder, "000-tls.pfx"), first.Export(X509ContentType.Pkcs12, SourceFixture.Password));
            for (var i = 0; i < 150; i++)
                File.WriteAllBytes(Path.Combine(fixture.Folder, $"junk-{i:000}"), new byte[] { 1, 2, 3, 4 });
            File.WriteAllBytes(Path.Combine(fixture.Folder, "zzz-tls.pfx"), beyond.Export(X509ContentType.Pkcs12, SourceFixture.Password));
            await fixture.RefreshAsync();

            await EventFilter.Warning(contains: "the folder holds 152 files and only the first 100 by name are read, so the 52 files from 'junk-099' on are not read")
                .ExpectOneAsync(() =>
                {
                    Pick(fixture).Should().Be(first.Thumbprint);
                    return Task.CompletedTask;
                });
        }

        // The PEM files of one folder give at most 100 certificates and 100 private keys. The files here sort by name:
        // "000-" first, "100-" next, "900-" last.

        private static void WritePemBlocks(PemFolderFixture fixture, string name, IEnumerable<string> blocks) =>
            File.WriteAllText(Path.Combine(fixture.Folder, name), string.Join("\n", blocks) + "\n");

        // Certificates, each with a key of its own, as (certificate block, key block). They are older than the pick.
        private static List<(string Certificate, string Key)> PairBlocks(int count) =>
            Enumerable.Range(0, count).Select(i =>
            {
                using var certificate = Create($"extra-{Guid.NewGuid():N}", -120, 600, false, Both);
                using var key = certificate.GetECDsaPrivateKey()!;
                return (PemEncoding.WriteString("CERTIFICATE", certificate.RawData),
                    PemEncoding.WriteString("PRIVATE KEY", key.ExportPkcs8PrivateKey()));
            }).ToList();

        private static List<string> KeyBlocks(int count) =>
            Enumerable.Range(0, count).Select(i =>
            {
                using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                return PemEncoding.WriteString("PRIVATE KEY", key.ExportPkcs8PrivateKey());
            }).ToList();

        [Fact(DisplayName = "Should_Read_Only_The_First_100_Pem_Certificates_Of_The_Folder_And_Still_Pick_A_Pair_Within_The_Limit")]
        public async Task Should_read_only_the_first_100_pem_certificates_of_the_folder_and_still_pick_a_pair_within_the_limit()
        {
            using var fixture = new PemFolderFixture();
            var pair = fixture.Make(-60, 600, false, Both);
            fixture.Add(pair, true, "000-");
            // 150 more certificates, each with its key, in 3 files of each. A pairing that succeeds asks IsWindows once,
            // so the calls count the pairings.
            var extras = PairBlocks(150);
            for (var file = 0; file < 3; file++)
            {
                WritePemBlocks(fixture, $"100-certificates-{file}.pem", extras.Skip(file * 50).Take(50).Select(e => e.Certificate));
                WritePemBlocks(fixture, $"200-keys-{file}.pem", extras.Skip(file * 50).Take(50).Select(e => e.Key));
            }

            var pairings = 0;
            fixture.Source.IsWindows = () =>
            {
                Interlocked.Increment(ref pairings);
                return false;
            };

            await fixture.RefreshAsync();
            var log = new RecordingLog();

            Pick(fixture, log: log).Should().Be(pair.Thumbprint);
            pairings.Should().Be(100, "the pair and 99 more certificates and keys are read, and the other 51 are not");
            ExpectLines(log, LogLevel.WarningLevel, "hold more than 100 certificates and more than 100 private keys, and only the first 100 certificates and the first 100 private keys in file name order are read, so the others are not read", 1);
        }

        [Fact(DisplayName = "Should_Read_Only_The_First_100_Pem_Private_Keys_Of_The_Folder_And_Not_Pair_A_Key_Past_The_Limit")]
        public async Task Should_read_only_the_first_100_pem_private_keys_of_the_folder_and_not_pair_a_key_past_the_limit()
        {
            using var fixture = new PemFolderFixture();
            var within = fixture.Make(-90, 600, false, Both);
            fixture.Add(within, true, "000-");
            WritePemBlocks(fixture, "100-keys.pem", KeyBlocks(100));
            var beyond = fixture.Make(-60, 600, false, Both); // issued later, so it would win if its key were read
            fixture.Add(beyond, true, "900-");
            await fixture.RefreshAsync();
            var log = new RecordingLog();

            Pick(fixture, log: log).Should().Be(within.Thumbprint);
            ExpectLines(log, LogLevel.WarningLevel, "the PEM files in the folder hold more than 100 private keys, and only the first 100 certificates and the first 100 private keys in file name order are read, so the others are not read", 1);
            ExpectLines(log, LogLevel.WarningLevel, $"{beyond.Thumbprint}", 1);
            log.Warnings.Single(w => w.Contains(beyond.Thumbprint)).Should().Contain("no private key in the folder");
        }

        [Fact(DisplayName = "Should_Replace_Control_Characters_In_A_File_Name_In_Warnings")]
        public async Task Should_replace_control_characters_in_a_file_name_in_warnings()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Assert.Skip("Windows cannot create a file whose name holds a new line.");

            using var fixture = new Pkcs12FolderFixture();
            var pick = fixture.Make(-60, 600, false, Both);
            fixture.Add(pick);
            var forged = fixture.Make(-30, 600, false, ClientAuth); // newer, but not for a server, so a warning names its file
            File.WriteAllBytes(Path.Combine(fixture.Folder, "forged\nWARN fake line.pfx"), forged.Export(X509ContentType.Pkcs12, SourceFixture.Password));
            File.WriteAllBytes(Path.Combine(fixture.Folder, "broken\r\nfile.pfx"), new byte[] { 1, 2, 3, 4 });
            await fixture.RefreshAsync();
            var log = new RecordingLog();

            Pick(fixture, log: log).Should().Be(pick.Thumbprint);
            ExpectLines(log, LogLevel.WarningLevel, "in file 'forged?WARN fake line.pfx' was skipped", 1);
            ExpectLines(log, LogLevel.WarningLevel, "file 'broken??file.pfx' failed to load", 1);
            log.Warnings.Should().OnlyContain(w => !w.Contains('\n') && !w.Contains('\r'));
        }

        // 3: a certificate whose key usage does not permit digital signature is never the pick, and the logs name subjects

        [Theory(DisplayName = "Should_Accept_A_Certificate_With_No_Key_Usage_Extension")]
        [MemberData(nameof(AllKinds))]
        public async Task Should_accept_a_certificate_with_no_key_usage_extension(string kind)
        {
            using var fixture = Open(kind);
            var certificate = fixture.Make(-60, 60, null, false, Both);
            fixture.Add(certificate);

            (await PickAsync(fixture, ClientAndServer)).Should().Be(certificate.Thumbprint);
        }

        [Theory(DisplayName = "Should_Not_Switch_To_A_Newer_Ca_Certificate_And_Name_Its_Subject_In_The_Warning")]
        [MemberData(nameof(SkippingKinds))]
        public async Task Should_not_switch_to_a_newer_ca_certificate_and_name_its_subject_in_the_warning(string kind)
        {
            using var fixture = Open(kind);
            var node = fixture.Make(-60, 600, false, Both);
            var authority = fixture.Make(-30, 600, X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true);
            fixture.Add(node);
            fixture.Add(authority);
            await fixture.RefreshAsync();

            await EventFilter.Info(contains: $"changed from none to {node.Thumbprint} (subject 'CN={fixture.CommonName}', expires").ExpectOneAsync(async () =>
            {
                await EventFilter.Warning(contains: $"{authority.Thumbprint} (subject 'CN={fixture.CommonName}', expires").ExpectOneAsync(() =>
                {
                    Pick(fixture).Should().Be(node.Thumbprint);
                    return Task.CompletedTask;
                });
            });
        }

        // 4: a pick that its holder disposed does not break the role

        [Theory(DisplayName = "Should_Load_The_Certificate_Again_When_A_Caller_Disposed_The_One_It_Was_Given")]
        [MemberData(nameof(SkippingKinds))]
        public async Task Should_load_the_certificate_again_when_a_caller_disposed_the_one_it_was_given(string kind)
        {
            using var fixture = Open(kind);
            var certificate = fixture.Make(-60, 600, true, Both);
            fixture.Add(certificate);
            (await GetAsync(fixture)).Dispose(); // against the rules, but it must not break the node

            var again = await WaitAsync(fixture);
            again.Thumbprint.Should().Be(certificate.Thumbprint);
            Sign(again);

            // A disposed previous pick does not count as a pick to fall back to, and does not stop a later one.
            fixture.Remove(certificate);
            again.Dispose();
            await fixture.RefreshAsync();
            Action act = () => Get(fixture);
            act.Should().Throw<InvalidOperationException>().WithMessage("No certificate in*");

            var renewed = fixture.Make(-30, 600, true, Both);
            fixture.Add(renewed);
            var picked = await GetAsync(fixture);
            picked.Thumbprint.Should().Be(renewed.Thumbprint);
            Sign(picked);
        }

        // 7: the "no certificate" message stays short

        [Fact(DisplayName = "Should_List_At_Most_Ten_Reasons_In_The_No_Certificate_Message")]
        public async Task Should_list_at_most_ten_reasons_in_the_no_certificate_message()
        {
            using var fixture = new Pkcs12FolderFixture();
            for (var i = 0; i < 15; i++)
                File.WriteAllBytes(Path.Combine(fixture.Folder, $"bad-{i:00}"), new byte[] { 1, 2, 3, 4 });
            await fixture.RefreshAsync();

            Action act = () => Get(fixture);
            var message = act.Should().Throw<InvalidOperationException>().Which.Message;
            Regex.Matches(message, "failed to load").Count.Should().Be(10);
            message.Should().Contain("5 more not listed.");
        }

        // 8: one limiter, keyed by the warning

        [Theory(DisplayName = "Should_Warn_At_Once_When_The_Look_Up_Fails_Again_After_A_Recovery")]
        [MemberData(nameof(AllKinds))]
        public async Task Should_warn_at_once_when_the_look_up_fails_again_after_a_recovery(string kind)
        {
            using var fixture = Open(kind);
            var clock = DateTime.UtcNow;
            fixture.Source.UtcNow = () => clock;
            var certificate = fixture.Make(-60, 600, false, Both);
            fixture.Add(certificate);
            (await PickAsync(fixture)).Should().Be(certificate.Thumbprint);

            async Task Expect(int warnings)
            {
                await ExpectLinesAsync(LogLevel.WarningLevel, "keeps using the previous certificate", warnings, async log =>
                    (await PickAsync(fixture, log: log)).Should().Be(certificate.Thumbprint));
            }

            fixture.Remove(certificate);
            await Expect(1); // the first outage
            fixture.Add(certificate);
            await Expect(0); // recovered
            clock = clock.AddMinutes(1);
            fixture.Remove(certificate);
            await Expect(1); // a new outage, one minute later, warns at once
        }

        [Theory(DisplayName = "Should_Not_Warn_Again_Within_Five_Minutes_About_A_File_That_Fails_Again_After_It_Was_Gone")]
        [MemberData(nameof(FolderKinds))]
        public async Task Should_not_warn_again_within_five_minutes_about_a_file_that_fails_again_after_it_was_gone(string kind)
        {
            using var fixture = (FolderFixture)Open(kind);
            var certificate = fixture.Make(-60, 600, false, Both);
            fixture.Add(certificate);
            var path = Path.Combine(fixture.Folder, "bad-file");

            async Task Expect(int warnings)
            {
                await ExpectLinesAsync(LogLevel.WarningLevel, "TLS certificate skipped: file 'bad-file'", warnings, async log =>
                    (await PickAsync(fixture, log: log)).Should().Be(certificate.Thumbprint));
            }

            File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
            await Expect(1);
            File.Delete(path);
            await Expect(0);
            File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 }); // the same failure comes back
            await Expect(0);
        }

        // ---- review round 4 ----

        // 1: the checks made before a file is opened describe the file a symbolic link leads to

        [Theory(DisplayName = "Should_Load_A_Kubernetes_Secret_Mount_Through_Two_Levels_Of_Symbolic_Links")]
        [MemberData(nameof(FolderKinds))]
        public async Task Should_load_a_kubernetes_secret_mount_through_two_levels_of_symbolic_links(string kind)
        {
            using var fixture = (FolderFixture)Open(kind);
            var first = fixture.Make(-60, 600, false, Both);
            var second = fixture.Make(-30, 600, false, Both);
            PublishSecret(fixture, "..2026_11_13_00_00_00.1", first);

            (await PickAsync(fixture)).Should().Be(first.Thumbprint);

            PublishSecret(fixture, "..2026_11_13_00_05_00.2", second); // the kubelet swaps ..data
            (await PickAsync(fixture)).Should().Be(second.Thumbprint);

            // A target larger than 256 KB, reached through both links, is too large to be a certificate.
            PublishSecret(fixture, "..2026_11_13_00_10_00.3", second, padding: 300 * 1024);
            await EventFilter.Warning(new Regex(@"^TLS certificate skipped: file 'tls\.(crt|pfx)' failed to load: the file is larger than 256 KB"))
                .ExpectOneAsync(async () => (await PickAsync(fixture)).Should().Be(second.Thumbprint));
        }

        [Theory(DisplayName = "Should_Check_The_Target_Of_A_Symbolic_Link_Before_The_File_Is_Opened")]
        [InlineData(0, "the file is empty or is not a regular file")]
        [InlineData(256 * 1024 + 1, "the file is larger than 256 KB, so it is too large to be a certificate")]
        public async Task Should_check_the_target_of_a_symbolic_link_before_the_file_is_opened(int size, string reason)
        {
            using var fixture = new Pkcs12FolderFixture();
            var certificate = fixture.Make(-60, 600, false, Both);
            fixture.Add(certificate);
            var target = Path.Combine(fixture.Targets, "target");
            File.WriteAllBytes(target, Enumerable.Repeat((byte)0x30, size).ToArray());
            CreateLink(Path.Combine(fixture.Folder, "link"), target);
            var open = fixture.Source.OpenFile;
            fixture.Source.OpenFile = path => Path.GetFileName(path) == "link" ? throw new IOException("the link was opened") : open(path);

            // The check made before the file is opened rejects it: the link is never opened.
            await EventFilter.Warning(contains: $"file 'link' failed to load: {reason}").ExpectOneAsync(async () =>
                (await PickAsync(fixture)).Should().Be(certificate.Thumbprint));
        }

        [Fact(DisplayName = "Should_Skip_A_Symbolic_Link_To_A_Named_Pipe_Without_Waiting_For_A_Writer")]
        public async Task Should_skip_a_symbolic_link_to_a_named_pipe_without_waiting_for_a_writer()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Assert.Skip("A named pipe in a folder needs a Unix file system.");

            using var fixture = new Pkcs12FolderFixture();
            var certificate = fixture.Make(-60, 600, false, Both);
            fixture.Add(certificate);
            var pipe = Path.Combine(fixture.Targets, "pipe");
            MakeFifo(pipe).Should().Be(0, "mkfifo must work");
            CreateLink(Path.Combine(fixture.Folder, "pipe-link"), pipe);

            await EventFilter.Warning(contains: "file 'pipe-link' failed to load: the file is empty or is not a regular file").ExpectOneAsync(async () =>
            {
                var refresh = fixture.RefreshAsync();
                (await Task.WhenAny(refresh, Task.Delay(TimeSpan.FromSeconds(10)))).Should().BeSameAs(refresh, "a link to a named pipe must not hold up the look-up");
                Pick(fixture).Should().Be(certificate.Thumbprint);
            });
        }

        private static void CreateLink(string link, string target)
        {
            try
            {
                File.CreateSymbolicLink(link, target);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Assert.Skip($"This platform cannot create symbolic links: {e.Message}");
            }
        }

        private static void PublishSecret(FolderFixture fixture, string stamp, X509Certificate2 certificate, int padding = 0)
        {
            // tls.crt and tls.key for PEM, tls.pfx for PKCS#12. Padding goes after the certificate file's content.
            var files = fixture.FilesOf(certificate)
                .Select((file, i) => ($"tls.{file.Kind}",
                    i == 0 ? file.Bytes.Concat(Enumerable.Repeat((byte)'#', padding)).ToArray() : file.Bytes))
                .ToArray();
            try
            {
                fixture.PublishSecret(stamp, files);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Assert.Skip($"This platform cannot create symbolic links: {e.Message}");
            }
        }

        // 2: a certificate that could not be used is judged again from a fresh instance

        [Fact(DisplayName = "Should_Judge_A_Store_Certificate_Again_When_It_Comes_Back_With_Its_Private_Key")]
        public async Task Should_judge_a_store_certificate_again_when_it_comes_back_with_its_private_key()
        {
            using var fixture = Open("store");
            var certificate = fixture.Make(-60, 600, false, Both);
            fixture.Add(certificate, withKey: false);
            await fixture.RefreshAsync();
            Action act = () => Get(fixture);
            act.Should().Throw<InvalidOperationException>().WithMessage("*no private key this process can open*");

            fixture.Add(certificate); // the same certificate, now with its key
            (await PickAsync(fixture)).Should().Be(certificate.Thumbprint);
        }

        [Theory(DisplayName = "Should_Judge_A_Certificate_Again_From_A_Fresh_Instance_When_Its_Key_Could_Not_Be_Opened")]
        [MemberData(nameof(SkippingKinds))]
        public async Task Should_judge_a_certificate_again_from_a_fresh_instance_when_its_key_could_not_be_opened(string kind)
        {
            using var fixture = Open(kind);
            var certificate = fixture.Make(-60, 600, false, Both);
            fixture.Add(certificate);
            var clock = DateTime.UtcNow;
            fixture.Source.UtcNow = () => clock;
            // The process cannot open the key of the first instance it reads, as before an operator grants access.
            X509Certificate2? refused = null;
            fixture.Source.CanOpenKey = c => !ReferenceEquals(c, refused ??= c);
            await fixture.RefreshAsync();
            Action act = () => Get(fixture);
            act.Should().Throw<InvalidOperationException>().WithMessage("*no private key this process can open*");

            // A store is read again at the next look-up, a file 5 minutes later. The look-up reads the certificate
            // again and checks the key of that instance.
            if (kind != "store")
            {
                await fixture.RefreshAsync();
                act.Should().Throw<InvalidOperationException>().WithMessage("*no private key this process can open*");
                clock = clock.AddMinutes(5);
            }

            var picked = await GetAsync(fixture);
            picked.Thumbprint.Should().Be(certificate.Thumbprint);
            picked.Should().NotBeSameAs(refused);
        }

        // 3: a copy of a certificate without its key does not hide the copy with it

        [Theory(DisplayName = "Should_Pick_The_Copy_With_The_Private_Key_When_A_Copy_Without_It_Has_The_Same_Thumbprint")]
        [MemberData(nameof(KeylessCopyCases))]
        public async Task Should_pick_the_copy_with_the_private_key_when_a_copy_without_it_has_the_same_thumbprint(string kind,
            string copyName)
        {
            using var fixture = (FolderFixture)Open(kind);
            var certificate = fixture.Make(-60, 600, false, Both);
            File.WriteAllBytes(Path.Combine(fixture.Folder, copyName), certificate.RawData); // DER, public only
            fixture.Add(certificate);

            (await PickAsync(fixture)).Should().Be(certificate.Thumbprint);
            Pick(fixture, ClientAndServer).Should().Be(certificate.Thumbprint);
            Sign(Get(fixture));
        }

        public static TheoryData<string, string> KeylessCopyCases
        {
            get
            {
                // The copy's name sorts before the copy with the key, and after it.
                var data = new TheoryData<string, string>();
                foreach (var kind in new[] { "pkcs12-folder", "pem-folder" })
                {
                    data.Add(kind, "aaa-node.cer");
                    data.Add(kind, "zzz-node.cer");
                }

                return data;
            }
        }

        // 4: an RSA server certificate may permit key encipherment instead of digital signature

        public static TheoryData<string, string, string, bool> KeyUsageCases
        {
            get
            {
                // case, role, whether the certificate is accepted
                var cases = new[]
                {
                    ("rsa-key-encipherment", Server, true),
                    ("rsa-key-encipherment", Client, false),
                    ("rsa-key-encipherment", ClientAndServer, false),
                    ("ecdsa-key-encipherment", Server, false),
                    ("rsa-certificate-signing", Server, false),
                    ("rsa-digital-signature", Client, true)
                };
                var data = new TheoryData<string, string, string, bool>();
                foreach (var kind in SourceFixture.AllKindNames)
                {
                    foreach (var (name, role, accepted) in cases)
                        data.Add(kind, name, role, accepted);
                }

                return data;
            }
        }

        [Theory(DisplayName = "Should_Accept_Key_Encipherment_Instead_Of_Digital_Signature_Only_For_An_Rsa_Server_Certificate")]
        [MemberData(nameof(KeyUsageCases))]
        public async Task Should_accept_key_encipherment_instead_of_digital_signature_only_for_an_rsa_server_certificate(
            string kind, string usageCase, string role, bool accepted)
        {
            using var fixture = Open(kind);
            var certificate = usageCase switch
            {
                "rsa-key-encipherment" => fixture.MakeWithKeyUsage(-60, 600, true, X509KeyUsageFlags.KeyEncipherment, Both),
                "ecdsa-key-encipherment" => fixture.MakeWithKeyUsage(-60, 600, false, X509KeyUsageFlags.KeyEncipherment, Both),
                "rsa-certificate-signing" => fixture.MakeWithKeyUsage(-60, 600, true,
                    X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, Both),
                _ => fixture.MakeWithKeyUsage(-60, 600, true, X509KeyUsageFlags.DigitalSignature, Both)
            };
            fixture.Add(certificate);
            await fixture.RefreshAsync();

            if (accepted)
            {
                Pick(fixture, role).Should().Be(certificate.Thumbprint);
            }
            else
            {
                Action act = () => Get(fixture, role);
                act.Should().Throw<InvalidOperationException>()
                    .WithMessage($"*{certificate.Thumbprint}*its key usage does not permit digital signature*");
            }
        }

        // 5: a failing callback never puts back an older pick over a newer one

        [Fact(DisplayName = "Should_Fall_Back_To_The_Newest_Pick_Of_The_Callback_When_Calls_Fail_At_The_Same_Time")]
        public async Task Should_fall_back_to_the_newest_pick_of_the_callback_when_calls_fail_at_the_same_time()
        {
            using var older = Create("callback", -60, 600, false, Both);
            using var newer = Create("callback", -30, 600, false, Both);
            using var failing = new ThreadLocal<bool>();
            var current = new[] { older };
            var source = TlsCertificateSource.FromCallback(() =>
                failing.Value ? throw new InvalidOperationException("the callback failed") : Volatile.Read(ref current[0]));

            // Other threads call all the time, and their calls fail. Each falls back to the pick it reads.
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var failers = Enumerable.Range(0, 3).Select(_ => Task.Factory.StartNew(() =>
            {
                failing.Value = true;
                while (!stop.IsCancellationRequested)
                {
                    try
                    {
                        source.GetCertificateWithoutLogging(TlsCertificateRole.Server);
                    }
                    // slopwatch-ignore: SW003 The test makes the callback fail on purpose; the loop only needs the calls to happen.
                    catch (InvalidOperationException)
                    {
                    }
                }
            }, TaskCreationOptions.LongRunning)).ToArray();

            int rounds = 0, lost = 0;
            while (!stop.IsCancellationRequested)
            {
                Volatile.Write(ref current[0], older);
                source.GetCertificateWithoutLogging(TlsCertificateRole.Server);
                Volatile.Write(ref current[0], newer);
                source.GetCertificateWithoutLogging(TlsCertificateRole.Server); // the newer one is handed out
                failing.Value = true;
                if (!ReferenceEquals(source.GetCertificateWithoutLogging(TlsCertificateRole.Server), newer))
                    lost++;
                failing.Value = false;
                rounds++;
            }

            await Task.WhenAll(failers);
            lost.Should().Be(0, $"a failure after the newer certificate was handed out must fall back to it ({rounds} rounds)");
        }

        // 6: the limiter keeps limiting when a look-up gives more than 100 different warnings

        [Fact(DisplayName = "Should_Keep_Limiting_Warnings_When_A_Look_Up_Gives_More_Than_100_Different_Ones")]
        public async Task Should_keep_limiting_warnings_when_a_look_up_gives_more_than_100_different_ones()
        {
            using var fixture = new Pkcs12FolderFixture();
            for (var i = 0; i < 105; i++)
                File.WriteAllBytes(Path.Combine(fixture.Folder, $"bad-{i:000}"), new byte[] { 1, 2, 3, 4 });
            var source = fixture.Source;
            var clock = DateTime.UtcNow;
            source.UtcNow = () => clock;
            var log = new RecordingLog();

            // 100 files fail and the folder holds too many: 101 different warnings in each look-up.
            async Task<int> WarningsOfALookUp()
            {
                clock = clock.AddSeconds(11);
                await source.RefreshAsync();
                var before = log.Warnings.Count;
                Record.Exception(() => source.GetCertificate(TlsCertificateRole.Server, log)).Should().BeAssignableTo<InvalidOperationException>();
                return log.Warnings.Count - before;
            }

            (await WarningsOfALookUp()).Should().Be(101, "100 warnings, and one line that says the others were not logged");
            log.Warnings.Last().Should().Be("Some TLS certificate warnings were not logged: more than 100 different warnings came within 5 minutes.");
            (await WarningsOfALookUp()).Should().Be(0);
            (await WarningsOfALookUp()).Should().Be(0);

            clock = clock.AddMinutes(5);
            (await WarningsOfALookUp()).Should().Be(101, "5 minutes later the warnings are due again");
        }

        // 7: a caller with nothing to present does not wait for a look-up

        [Fact(DisplayName = "Should_Fail_At_Once_When_A_Caller_Has_Nothing_To_Present_While_A_Look_Up_Runs")]
        public async Task Should_fail_at_once_when_a_caller_has_nothing_to_present_while_a_look_up_runs()
        {
            using var fixture = new PemFolderFixture();
            SkipIfNeeded(fixture);
            var certificate = fixture.Make(-60, 600, true, Both);
            fixture.Add(certificate);
            using var gate = new BlockingGate();
            var source = fixture.Source;
            source.IsWindows = () => gate.Block(false); // the first look-up does not return until the spec says so

            var first = Task.Run(() => Record.Exception(() => source.GetCertificate(TlsCertificateRole.Server, Sys.Log)));
            (await Task.WhenAny(first, Task.Delay(TimeSpan.FromSeconds(10)))).Should().BeSameAs(first, "a caller with nothing to present must not wait");
            (await first).Should().BeAssignableTo<InvalidOperationException>().Which.Message
                .Should().Contain("No TLS certificate is available yet to use as a server").And.Contain($"folder '{fixture.Folder}'");

            // Another caller fails at once too, and does not start a second look-up.
            await gate.EnteredAsync();
            Record.Exception(() => source.GetCertificateWithoutLogging(TlsCertificateRole.ClientAndServer))
                .Should().BeAssignableTo<InvalidOperationException>();
            source.ScanCount.Should().Be(1);

            gate.Release();
            await source.LastRefresh;
            Pick(fixture).Should().Be(certificate.Thumbprint);
        }

        // 9: what a look-up guarantees

        [Fact(DisplayName = "Should_Let_A_Later_Look_Up_Start_When_A_Look_Up_Throws")]
        public async Task Should_let_a_later_look_up_start_when_a_look_up_throws()
        {
            using var fixture = new Pkcs12FolderFixture();
            var certificate = fixture.Make(-60, 600, false, Both);
            fixture.Add(certificate);
            var source = fixture.Source;
            var open = source.OpenFile;
            source.OpenFile = _ => throw new OutOfMemoryException("the look-up broke"); // fatal: it escapes the look-up
            await Assert.ThrowsAsync<OutOfMemoryException>(() => source.RefreshAsync());

            source.OpenFile = open;
            await source.RefreshAsync();
            Pick(fixture).Should().Be(certificate.Thumbprint);
            source.ScanCount.Should().Be(2);
        }

        [Fact(DisplayName = "Should_Count_The_Wait_For_A_Look_Up_From_Its_Start_Not_From_When_The_Caller_Came")]
        public async Task Should_count_the_wait_for_a_look_up_from_its_start_not_from_when_the_caller_came()
        {
            using var fixture = new Pkcs12FolderFixture();
            fixture.Add(fixture.Make(-60, 600, false, Both));
            using var gate = new BlockingGate();
            var source = fixture.Source;
            var open = source.OpenFile;
            source.OpenFile = path => gate.Block(open(path)); // the look-up does not return until the spec says so
            source.RefreshTimeout = TimeSpan.FromMilliseconds(200);

            var first = source.GetCertificateAsync(TlsCertificateRole.Server, Sys.Log); // starts the look-up
            await gate.EnteredAsync();
            (await Assert.ThrowsAsync<InvalidOperationException>(() => first)).Message.Should().Contain("did not finish in time");

            // The look-up has run longer than this deadline too, so a caller that comes now fails at once.
            source.RefreshTimeout = TimeSpan.FromMilliseconds(100);
            var later = source.GetCertificateAsync(TlsCertificateRole.Server, Sys.Log);
            later.IsCompleted.Should().BeTrue("the deadline counts from the start of the look-up");
            (await Assert.ThrowsAsync<InvalidOperationException>(() => later)).Message.Should().Contain("did not finish in time");

            gate.Release();
            await source.LastRefresh;
        }

        [Fact(DisplayName = "Should_Stop_Reading_A_File_At_256_KB_When_It_Holds_More_Than_It_Did_When_Checked")]
        public async Task Should_stop_reading_a_file_at_256_kb_when_it_holds_more_than_it_did_when_checked()
        {
            using var fixture = new Pkcs12FolderFixture();
            var certificate = fixture.Make(-60, 600, false, Both);
            fixture.Add(certificate);
            File.WriteAllBytes(Path.Combine(fixture.Folder, "grows"), new byte[] { 1, 2, 3, 4 });
            File.WriteAllBytes(Path.Combine(fixture.Folder, "limit"), new byte[] { 1, 2, 3, 4 });
            var open = fixture.Source.OpenFile;
            // Each file is 4 bytes when it is checked, and holds more when it is read.
            fixture.Source.OpenFile = path => Path.GetFileName(path) switch
            {
                "grows" => new MemoryStream(Enumerable.Repeat((byte)0x30, 256 * 1024 + 1).ToArray()),
                "limit" => new MemoryStream(Enumerable.Repeat((byte)0x30, 256 * 1024).ToArray()),
                _ => open(path)
            };

            var log = new RecordingLog();
            (await PickAsync(fixture, log: log)).Should().Be(certificate.Thumbprint);
            ExpectLines(log, LogLevel.WarningLevel, "file 'grows' failed to load: the file is larger than 256 KB, so it is too large to be a certificate", 1);
            ExpectLines(log, LogLevel.WarningLevel, "file 'limit' failed to load: the file is larger", 0);
        }

        // A refresh the specs ask for starts after the call.

        [Fact(DisplayName = "Should_Run_A_Look_Up_That_Starts_After_The_Call_When_A_Refresh_Is_Asked_For_While_One_Runs")]
        public async Task Should_run_a_look_up_that_starts_after_the_call_when_a_refresh_is_asked_for_while_one_runs()
        {
            using var fixture = new PemFolderFixture();
            SkipIfNeeded(fixture);
            var first = fixture.Make(-60, 600, true, Both);
            var second = fixture.Make(-30, 600, true, Both);
            var third = fixture.Make(-10, 600, true, Both);
            fixture.Add(first);
            (await PickAsync(fixture)).Should().Be(first.Thumbprint);

            // A look-up starts and blocks while it pairs the second certificate: it has read the folder already.
            using var gate = new BlockingGate();
            fixture.Add(second);
            fixture.Source.IsWindows = () => gate.Block(false);
            var running = fixture.RefreshAsync();
            await gate.EnteredAsync();
            fixture.Source.IsWindows = () => false;

            fixture.Add(third);
            var refresh = fixture.RefreshAsync(); // must see the third certificate
            gate.Release();
            await running;
            await refresh;
            Pick(fixture).Should().Be(third.Thumbprint);
        }

        // ---- review round 5 ----

        // 2: the warning that the node keeps its previous certificate is admitted before the warnings about files

        [Fact(DisplayName = "Should_Log_The_Fallback_Warning_When_File_Warnings_Fill_The_Warning_Limit")]
        public async Task Should_log_the_fallback_warning_when_file_warnings_fill_the_warning_limit()
        {
            using var fixture = new Pkcs12FolderFixture();
            var certificate = fixture.Make(-60, 600, false, Both);
            fixture.Add(certificate);
            var source = fixture.Source;
            var log = new RecordingLog();
            await source.RefreshAsync();
            source.GetCertificate(TlsCertificateRole.Server, log).Thumbprint.Should().Be(certificate.Thumbprint);

            // The certificate is gone and 100 files fail: 100 warnings about files, and the fallback warning.
            fixture.Remove(certificate);
            for (var i = 0; i < 100; i++)
                File.WriteAllBytes(Path.Combine(fixture.Folder, $"bad-{i:000}"), new byte[] { 1, 2, 3, 4 });
            await source.RefreshAsync();
            source.GetCertificate(TlsCertificateRole.Server, log).Thumbprint.Should().Be(certificate.Thumbprint);

            log.Warnings.Should().Contain(w => w.StartsWith(
                "TLS server certificate look-up failed, so the node keeps using the previous certificate " + certificate.Thumbprint));
            log.Warnings.First().Should().Contain("keeps using the previous certificate");
        }

        // 3: an instance whose key cannot be opened is released, and a file with that content is not imported again

        [Theory(DisplayName = "Should_Dispose_An_Unpublished_Instance_Whose_Key_Cannot_Be_Opened_At_The_End_Of_The_Look_Up")]
        [MemberData(nameof(SkippingKinds))]
        public async Task Should_dispose_an_unpublished_instance_whose_key_cannot_be_opened_at_the_end_of_the_look_up(string kind)
        {
            using var fixture = Open(kind);
            var certificate = fixture.Make(-60, 600, false, Both);
            fixture.Add(certificate);
            var source = fixture.Source;
            var clock = DateTime.UtcNow;
            source.UtcNow = () => clock;
            var judged = new System.Collections.Concurrent.ConcurrentQueue<X509Certificate2>();
            source.CanOpenKey = c =>
            {
                judged.Enqueue(c);
                return false;
            };

            for (var i = 0; i < 6; i++)
            {
                clock = clock.AddSeconds(11);
                await source.RefreshAsync();
            }

            Action act = () => Get(fixture);
            act.Should().Throw<InvalidOperationException>().WithMessage("*no private key this process can open*");
            judged.Should().OnlyContain(c => c.Handle == IntPtr.Zero, "an instance that was never published is released");
            // A store is judged afresh each look-up, as its key may become readable. A file is not imported again
            // until 5 minutes have passed or its content changes.
            judged.Should().HaveCount(kind == "store" ? 6 : 1);

            clock = clock.AddMinutes(5);
            await source.RefreshAsync();
            judged.Should().HaveCount(kind == "store" ? 7 : 2);
            judged.Should().OnlyContain(c => c.Handle == IntPtr.Zero);
            act.Should().Throw<InvalidOperationException>().WithMessage("*no private key this process can open*");
        }

        [Theory(DisplayName = "Should_Not_Dispose_A_Published_Certificate_When_Its_Key_Can_No_Longer_Be_Opened")]
        [MemberData(nameof(SkippingKinds))]
        public async Task Should_not_dispose_a_published_certificate_when_its_key_can_no_longer_be_opened(string kind)
        {
            using var fixture = Open(kind);
            var certificate = fixture.Make(-60, 600, false, Both);
            fixture.Add(certificate);
            var source = fixture.Source;
            var picked = await GetAsync(fixture);

            source.CanOpenKey = _ => false; // access to the key is taken away
            await source.RefreshAsync();

            Get(fixture).Should().BeSameAs(picked, "the node keeps its previous pick");
            picked.Handle.Should().NotBe(IntPtr.Zero, "a certificate that was published is never disposed");
            Sign(picked);
        }

        // 4: a caller that has to fail gives the reason of the last look-up, which does not change from one to the next

        [Fact(DisplayName = "Should_Fail_With_The_Reason_Of_The_Last_Look_Up_When_The_Caller_Starts_A_New_One")]
        public async Task Should_fail_with_the_reason_of_the_last_look_up_when_the_caller_starts_a_new_one()
        {
            using var fixture = new Pkcs12FolderFixture();
            File.WriteAllBytes(Path.Combine(fixture.Folder, "bad"), new byte[] { 1, 2, 3, 4 }); // never a certificate
            var source = fixture.Source;
            var clock = DateTime.UtcNow;
            source.UtcNow = () => clock;
            using var gate = new BlockingGate();
            var open = source.OpenFile;
            source.OpenFile = path => gate.Block(open(path));

            // Before the first look-up has finished there is no reason yet, and the text carries no time.
            var none = Record.Exception(() => source.GetCertificateWithoutLogging(TlsCertificateRole.Server));
            none.Should().BeAssignableTo<InvalidOperationException>().Which.Message.Should().Be(
                $"No TLS certificate is available yet to use as a server: the look-up in folder '{fixture.Folder}' has not finished. " +
                "A connection does not wait for it; connections built after it finishes use its pick.");
            await gate.EnteredAsync();
            gate.Release();
            await source.LastRefresh;
            source.OpenFile = open;

            // Each look-up finds nothing, and every caller gives that reason, the one that starts a look-up included.
            var reason = Record.Exception(() => Get(fixture))!.Message;
            reason.Should().StartWith($"No certificate in folder '{fixture.Folder}' can be used as a server. Could not load: file 'bad'");

            clock = clock.AddSeconds(11); // a look-up is due: this caller starts it
            Record.Exception(() => Get(fixture))!.Message.Should().Be(reason);
            await source.LastRefresh;
            source.ScanCount.Should().Be(2);
            Record.Exception(() => Get(fixture))!.Message.Should().Be(reason);
        }

        // 5: the callback never stores a pick over one from a call that started after it

        [Fact(DisplayName = "Should_Not_Store_An_Older_Pick_Of_The_Callback_Over_A_Newer_One_When_A_Call_Stalls")]
        public async Task Should_not_store_an_older_pick_of_the_callback_over_a_newer_one_when_a_call_stalls()
        {
            using var older = Create("callback", -60, 600, false, Both);
            using var newer = Create("callback", -30, 600, false, Both);
            using var gate = new BlockingGate();
            var current = new[] { older };
            var stallNext = 1;
            var failing = 0;
            var source = TlsCertificateSource.FromCallback(() =>
            {
                if (Volatile.Read(ref failing) == 1)
                    throw new InvalidOperationException("the callback failed");
                var pick = Volatile.Read(ref current[0]);
                return Interlocked.Exchange(ref stallNext, 0) == 1 ? gate.Block(pick) : pick;
            });

            // Thread A gets the older certificate from the callback and stalls before the source stores it.
            var stalled = Task.Run(() => source.GetCertificateWithoutLogging(TlsCertificateRole.Server));
            await gate.EnteredAsync();

            // Thread B gets the newer one.
            Volatile.Write(ref current[0], newer);
            source.GetCertificateWithoutLogging(TlsCertificateRole.Server).Should().BeSameAs(newer);

            gate.Release();
            (await stalled).Should().BeSameAs(older);

            // A later failure falls back to the newer pick.
            Volatile.Write(ref failing, 1);
            source.GetCertificateWithoutLogging(TlsCertificateRole.Server).Should().BeSameAs(newer);
        }

        // Small texts: the hint names no named pipe, and the "Could not load" list ends in one full stop.

        [Fact(DisplayName = "Should_End_The_No_Certificate_Message_In_One_Full_Stop_When_A_Failed_File_Ends_The_List")]
        public async Task Should_end_the_no_certificate_message_in_one_full_stop_when_a_failed_file_ends_the_list()
        {
            using var fixture = new Pkcs12FolderFixture();
            File.WriteAllBytes(Path.Combine(fixture.Folder, "empty"), Array.Empty<byte>());
            await fixture.RefreshAsync();

            Record.Exception(() => Get(fixture))!.Message.Should().Be(
                $"No certificate in folder '{fixture.Folder}' can be used as a server. " +
                "Could not load: file 'empty' failed to load: the file is empty or is not a regular file.");
        }

        [Fact(DisplayName = "Should_Name_A_Network_Share_And_No_Named_Pipe_When_A_Look_Up_Does_Not_Finish_In_Time")]
        public async Task Should_name_a_network_share_and_no_named_pipe_when_a_look_up_does_not_finish_in_time()
        {
            using var fixture = new Pkcs12FolderFixture();
            fixture.Add(fixture.Make(-60, 600, false, Both));
            using var gate = new BlockingGate();
            var source = fixture.Source;
            var open = source.OpenFile;
            source.OpenFile = path => gate.Block(open(path));
            source.RefreshTimeout = TimeSpan.FromMilliseconds(100);

            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => source.GetCertificateAsync(TlsCertificateRole.Server, Sys.Log));
            failure.Message.Should().EndWith(
                "did not finish in time: it started at " + Regex.Match(failure.Message, @"\d{4}-\d\d-\d\d \d\d:\d\d:\d\dZ").Value +
                " and has not finished after 0 seconds, so there is no certificate to use as a server. " +
                "A file in the folder that cannot be read, such as a file on a network share that does not answer, can cause this.");

            gate.Release();
            await source.LastRefresh;
        }

        // ---- the hook the transport calls once a minute ----

        [Fact(DisplayName = "Should_Log_The_Change_Of_Pick_When_The_Look_Up_A_Poll_Started_Finishes_Before_The_Poll_Returns")]
        public async Task Should_log_the_change_of_pick_when_the_look_up_a_poll_started_finishes_before_the_poll_returns()
        {
            using var fixture = new Pkcs12FolderFixture();
            var oldLeaf = fixture.Make(-60, 600, false, Both);
            var newLeaf = fixture.Make(-30, 600, false, Both); // issued later
            fixture.Add(oldLeaf);
            File.WriteAllBytes(Path.Combine(fixture.Folder, "bad"), new byte[] { 1, 2, 3, 4 }); // a warning to log
            var source = fixture.Source;
            var clock = DateTime.UtcNow;
            source.UtcNow = () => clock;
            await source.RefreshAsync(); // nobody has logged yet: the poll below logs the warning about 'bad'

            fixture.Add(newLeaf);
            clock = clock.AddSeconds(11); // a look-up is due: the poll starts it

            // The poll holds still while it logs that warning, which it does after it started the look-up, until the
            // look-up has finished. The change is logged all the same, once the poll has returned.
            using var finished = new ManualResetEventSlim();
            var log = new RecordingLog();
            log.OnLine = (level, _) =>
            {
                if (level != LogLevel.WarningLevel || finished.IsSet)
                    return;
                source.LastRefresh.ContinueWith(_ => finished.Set(), TaskScheduler.Default);
                finished.Wait(TimeSpan.FromSeconds(10));
            };
            source.Poll(TlsCertificateRole.ClientAndServer, log);
            finished.IsSet.Should().BeTrue("the look-up finished while the poll was still logging");

            await AwaitConditionAsync(() => log.Infos.Any(i => i.Contains("changed from") && i.Contains("to " + newLeaf.Thumbprint)),
                TimeSpan.FromSeconds(5), "the poll logs the change once its look-up finishes");
            source.ScanCount.Should().Be(2);
        }

        [Fact(DisplayName = "Should_Start_One_Look_Up_And_Not_Throw_When_A_Poll_Finds_Nothing_Usable")]
        public async Task Should_start_one_look_up_and_not_throw_when_a_poll_finds_nothing_usable()
        {
            using var fixture = new Pkcs12FolderFixture(); // an empty folder
            var source = fixture.Source;
            var clock = DateTime.UtcNow;
            source.UtcNow = () => clock;

            source.Poll(TlsCertificateRole.ClientAndServer, Sys.Log);
            await source.LastRefresh;
            source.ScanCount.Should().Be(1);

            source.Poll(TlsCertificateRole.ClientAndServer, Sys.Log); // the look-up is 0 seconds old: none is due
            source.ScanCount.Should().Be(1);

            clock = clock.AddSeconds(11);
            source.Poll(TlsCertificateRole.ClientAndServer, Sys.Log);
            await source.LastRefresh;
            source.ScanCount.Should().Be(2);
        }

        [Fact(DisplayName = "Should_Not_Call_The_Callback_And_Not_Look_Up_When_A_Poll_Runs_On_A_Callback_Source")]
        public void Should_not_call_the_callback_and_not_look_up_when_a_poll_runs_on_a_callback_source()
        {
            var calls = 0;
            var source = TlsCertificateSource.FromCallback(() =>
            {
                Interlocked.Increment(ref calls);
                throw new InvalidOperationException("never asked");
            });

            source.LooksUp.Should().BeFalse();
            source.Poll(TlsCertificateRole.ClientAndServer, Sys.Log);
            calls.Should().Be(0, "a callback is called for connections only");
            source.ScanCount.Should().Be(0);
        }

        [Fact(DisplayName = "Should_Log_An_Error_Once_Per_Limiter_Period_When_A_Poll_Finds_Nothing_To_Present_And_No_Connection_Asks")]
        public async Task Should_log_an_error_once_per_limiter_period_when_a_poll_finds_nothing_to_present_and_no_connection_asks()
        {
            using var fixture = new Pkcs12FolderFixture();
            var leaf = fixture.Make(-60, 10, false, Both);
            fixture.Add(leaf);
            var source = fixture.Source;
            var clock = DateTime.UtcNow;
            source.UtcNow = () => clock;
            await source.RefreshAsync();

            // The folder is emptied: the pick is kept while it is in date, with a warning.
            fixture.Remove(leaf);
            clock = clock.AddSeconds(11);
            var log = new RecordingLog();
            source.Poll(TlsCertificateRole.Server, log);
            await AwaitConditionAsync(() => log.Warnings.Any(w => w.Contains("keeps using the previous certificate")),
                TimeSpan.FromSeconds(5), "the poll warns about the fall-back");
            log.Errors.Should().BeEmpty();

            // The pick has expired: nothing to present, and only polls ask.
            clock = clock.AddMinutes(20);
            source.Poll(TlsCertificateRole.Server, log);
            await AwaitConditionAsync(() => log.Errors.Count == 1, TimeSpan.FromSeconds(5), "the poll logs that there is nothing to present");
            var reason = Record.Exception(() => Get(fixture))!.Message;
            log.Errors[0].Should().Be(reason, "the poll gives the reason a connection gets");

            // Within 5 minutes the same reason is not logged again; after them it is.
            clock = clock.AddMinutes(1);
            source.Poll(TlsCertificateRole.Server, log);
            await source.LastRefresh;
            source.Poll(TlsCertificateRole.Server, log);
            log.Errors.Should().HaveCount(1);
            clock = clock.AddMinutes(5);
            source.Poll(TlsCertificateRole.Server, log);
            await AwaitConditionAsync(() => log.Errors.Count == 2, TimeSpan.FromSeconds(5), "the poll logs it again after the period");
        }

        [Fact(DisplayName = "Should_Attach_One_Follow_Up_To_A_Look_Up_That_Hangs_However_Many_Polls_See_It")]
        public async Task Should_attach_one_follow_up_to_a_look_up_that_hangs_however_many_polls_see_it()
        {
            // The follow-up asks the source once more, which reads the clock once. A spec counts the reads after the
            // look-up is released: the same number for 1 poll and for 6.
            async Task<int> ReadsAfterReleaseAsync(int polls)
            {
                using var fixture = new Pkcs12FolderFixture();
                fixture.Add(fixture.Make(-60, 600, false, Both));
                using var gate = new BlockingGate();
                var source = fixture.Source;
                var open = source.OpenFile;
                source.OpenFile = path => gate.Block(open(path));
                var reads = 0;
                var clock = DateTime.UtcNow;
                source.UtcNow = () =>
                {
                    Interlocked.Increment(ref reads);
                    return clock;
                };

                source.Poll(TlsCertificateRole.Server, Sys.Log);
                await gate.EnteredAsync();
                for (var i = 1; i < polls; i++)
                    source.Poll(TlsCertificateRole.Server, Sys.Log);

                var before = Volatile.Read(ref reads);
                gate.Release();
                await source.LastRefresh;

                // Wait until no more reads come.
                int last;
                var stable = 0;
                do
                {
                    last = Volatile.Read(ref reads);
                    await Task.Delay(50);
                    stable = Volatile.Read(ref reads) == last ? stable + 1 : 0;
                } while (stable < 3);

                return last - before;
            }

            var one = await ReadsAfterReleaseAsync(1);
            var many = await ReadsAfterReleaseAsync(6);
            many.Should().Be(one, "one look-up gets one follow-up, not one for each poll that saw it");
        }

        [Fact(DisplayName = "Should_Not_Log_A_Change_Back_To_The_Older_Certificate_When_A_Stalled_Callback_Call_Returns_After_A_Newer_One")]
        public async Task Should_not_log_a_change_back_to_the_older_certificate_when_a_stalled_callback_call_returns_after_a_newer_one()
        {
            using var older = Create("callback", -60, 600, false, Both);
            using var newer = Create("callback", -30, 600, false, Both);
            using var gate = new BlockingGate();
            var current = new[] { older };
            var stallNext = 1;
            var failing = 0;
            var source = TlsCertificateSource.FromCallback(() =>
            {
                if (Volatile.Read(ref failing) == 1)
                    throw new InvalidOperationException("the callback failed");
                var pick = Volatile.Read(ref current[0]);
                return Interlocked.Exchange(ref stallNext, 0) == 1 ? gate.Block(pick) : pick;
            });
            var log = new RecordingLog();

            var stalled = Task.Run(() => source.GetCertificate(TlsCertificateRole.Server, log));
            await gate.EnteredAsync();
            Volatile.Write(ref current[0], newer);
            source.GetCertificate(TlsCertificateRole.Server, log).Should().BeSameAs(newer);

            gate.Release();
            (await stalled).Should().BeSameAs(older, "the stalled call still gives its caller its own certificate");

            // Only the newer pick was ever announced, and a later failure falls back to it without a word about the older one.
            log.Infos.Should().ContainSingle().Which.Should().Contain("changed from none to " + newer.Thumbprint);
            Volatile.Write(ref failing, 1);
            source.GetCertificate(TlsCertificateRole.Server, log).Should().BeSameAs(newer);
            log.Warnings.Should().ContainSingle().Which.Should().Contain("previous certificate " + newer.Thumbprint);
            log.Infos.Should().ContainSingle();
        }

        private static void SkipIfNeeded(SourceFixture fixture)
        {
            if (fixture.SkipReason is not null)
                Assert.Skip(fixture.SkipReason);
        }
    }
}
