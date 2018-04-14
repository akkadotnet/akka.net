﻿//-----------------------------------------------------------------------
// <copyright file="DefaultConfigSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Configuration;
using Akka.Persistence.Query;
using Akka.Persistence.Query.Sql;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Sql.TestKit
{
    public class DefaultConfigSpec : Akka.TestKit.Xunit2.TestKit
    {
        public DefaultConfigSpec(ITestOutputHelper output) : base(Config.Empty, output: output)
        {
        }

        [Fact]
        public void SqlReadJournal_applies_default_config()
        {
            var readJournal = Sys.ReadJournalFor<SqlReadJournal>(SqlReadJournal.Identifier);
            var config = Sys.Settings.Config.GetConfig("akka.persistence.query.journal.sql");

            config.Should().NotBeNull();
            config.GetString("class").Should().Be("Akka.Persistence.Query.Sql.SqlReadJournalProvider, Akka.Persistence.Query.Sql");
            config.GetTimeSpan("refresh-interval").Should().Be(TimeSpan.FromSeconds(3));
            config.GetInt("max-buffer-size").Should().Be(100);
        }
    }
}

