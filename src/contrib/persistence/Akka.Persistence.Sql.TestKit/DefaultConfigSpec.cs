//-----------------------------------------------------------------------
// <copyright file="DefaultConfigSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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

            config.IsNullOrEmpty().Should().BeFalse();
            config.GetString("class", null).Should().Be("Akka.Persistence.Query.Sql.SqlReadJournalProvider, Akka.Persistence.Query.Sql");
            config.GetTimeSpan("refresh-interval", TimeSpan.MinValue).Should().Be(TimeSpan.FromSeconds(3));
            config.GetInt("max-buffer-size", 0).Should().Be(100);
        }
    }
}

