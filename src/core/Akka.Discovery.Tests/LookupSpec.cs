//-----------------------------------------------------------------------
// <copyright file="LookupSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Discovery.Tests
{
    public class LookupSpec : TestKit.Xunit2.TestKit
    {
        private readonly List<string> srvWithInvalidDomainNames;
        private readonly List<string> srvWithValidDomainNames;

        // No SRV that should result in simple A/AAAA lookups
        private readonly List<string> noSrvLookups = new List<string>
        {
            "portName.protocol.serviceName.local",
            "serviceName.local",
            "_portName.serviceName",
            "_serviceName.local",
            "_serviceName,local",
            "-serviceName.local",
            "serviceName.local-"
        };

        public LookupSpec(ITestOutputHelper output)
            : base("LookupSpec", output)
        {
            const string portNameAndProtocol = "_portName._protocol.";
            const string char10 = "abcdefghij";
            var char63 = string.Concat(Enumerable.Repeat(char10, 6)) + "abc";
            var char64 = char63 + "d";

            srvWithInvalidDomainNames = new List<string>
            {
                portNameAndProtocol + "1" + char10,
                portNameAndProtocol + "." + char10,
                portNameAndProtocol + char10 + ".",
                portNameAndProtocol + "-" + char10,
                portNameAndProtocol + char10 + "_" + char10,
                portNameAndProtocol + char10 + "#" + char10,
                portNameAndProtocol + char10 + "$" + char10,
                portNameAndProtocol + char10 + "-",
                portNameAndProtocol + char10 + "." + char64,
                portNameAndProtocol + char64 + "." + char10
            };

            srvWithValidDomainNames = new List<string>
            {
                portNameAndProtocol + char10 + "." + char10,
                portNameAndProtocol + char10 + "-" + char10,
                portNameAndProtocol + char10 + "." + char63,
                portNameAndProtocol + char63 + "." + char10,
                portNameAndProtocol + char63 + "." + char63 + "." + char63
            };
        }

        [Fact]
        public void Lookup_ParseSrv_should_extract_service_name_from_a_valid_SRV_string()
        {
            const string name = "_portName._protocol.serviceName.local";
            var lookup = Lookup.ParseSrv(name);
            lookup.ServiceName.Should().Be("serviceName.local");
        }

        [Fact]
        public void Lookup_ParseSrv_should_generate_a_SRV_Lookup_from_a_valid_SRV_string()
        {
            srvWithValidDomainNames.ForEach(str =>
            {
                Output.WriteLine($"parsing '{str}'");

                var lookup = Lookup.ParseSrv(str);
                lookup.PortName.Should().Be("portName");
                lookup.Protocol.Should().Be("protocol");
            });
        }

        [Fact]
        public void Lookup_ParseSrv_should_throw_an_ArgumentException_for_any_non_conforming_SRV_string()
        {
            noSrvLookups.ForEach(str =>
            {
                Output.WriteLine($"parsing '{str}'");
                Assert.Throws<ArgumentException>(() => Lookup.ParseSrv(str));
            });
        }

        [Fact]
        public void Lookup_ParseSrv_should_throw_an_ArgumentException_for_any_SRV_with_invalid_domain_names()
        {
            srvWithInvalidDomainNames.ForEach(str =>
            {
                Output.WriteLine($"parsing '{str}'");
                Assert.Throws<ArgumentException>(() => Lookup.ParseSrv(str));
            });
        }

        [Fact]
        public void Lookup_ParseSrv_should_throw_an_ArgumentNullException_when_passing_a_null_SRV_string() =>
            Assert.Throws<ArgumentNullException>(() => Lookup.ParseSrv(null));

        [Fact]
        public void Lookup_ParseSrv_should_throw_an_ArgumentException_when_passing_an_empty_SRV_string() =>
            Assert.Throws<ArgumentException>(() => Lookup.ParseSrv(""));

        [Fact]
        public void Lookup_IsValidSrv_should_return_false_for_any_non_conforming_SRV_string()
        {
            noSrvLookups.ForEach(str =>
            {
                Output.WriteLine($"checking '{str}'");
                Lookup.IsValid(str).Should().BeFalse();
            });
        }

        [Fact]
        public void Lookup_IsValidSrv_should_return_false_if_domain_name_part_in_SRV_string_is_an_invalid_domain_name()
        {
            srvWithInvalidDomainNames.ForEach(str =>
            {
                Output.WriteLine($"checking '{str}'");
                Lookup.IsValid(str).Should().BeFalse();
            });
        }

        [Fact]
        public void Lookup_IsValidSrv_should_return_true_for_any_valid_SRV_string()
        {
            srvWithValidDomainNames.ForEach(str =>
            {
                Output.WriteLine($"checking '{str}'");
                Lookup.IsValid(str).Should().BeTrue();
            });
        }

        [Fact]
        public void Lookup_IsValidSrv_should_return_false_for_empty_SRV_string() => 
            Lookup.IsValid("").Should().BeFalse();

        [Fact]
        public void Lookup_IsValidSrv_should_return_false_for_null_SRV_string() => 
            Lookup.IsValid(null).Should().BeFalse();
    }
}
