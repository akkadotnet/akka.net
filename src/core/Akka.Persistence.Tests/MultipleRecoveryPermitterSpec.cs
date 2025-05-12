//-----------------------------------------------------------------------
// <copyright file="MultipleRecoveryPermitterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using FluentAssertions;
using Xunit;

namespace Akka.Persistence.Tests;

public class MultipleRecoveryPermitterSpec : PersistenceSpec
{
    private readonly IActorRef _permitter1;
    private readonly IActorRef _permitter2;
    
    public MultipleRecoveryPermitterSpec() : base(ConfigurationFactory.ParseString($$"""
            akka.persistence
            {
                # default global max recovery value
                max-concurrent-recoveries = 3
                
                journal
                {
                    plugin = "akka.persistence.journal.inmem"
                    inmem2 {
                        # max recovery value override
                        max-concurrent-recoveries = 20
                        class = "Akka.Persistence.Journal.MemoryJournal, Akka.Persistence"
                        plugin-dispatcher = "akka.actor.default-dispatcher"
                    }
                }
                
                # snapshot store plugin is NOT defined, things should still work
                snapshot-store.plugin = "akka.persistence.no-snapshot-store"
                snapshot-store.local.dir = "target/snapshots-"{{typeof(RecoveryPermitterSpec).FullName}}"}
            """))
    {
        var extension = Persistence.Instance.Apply(Sys);
        _permitter1 = extension.RecoveryPermitterFor(null);
        _permitter2 = extension.RecoveryPermitterFor("akka.persistence.journal.inmem2");
    }

    [Fact(DisplayName = "Plugin max-concurrent-recoveries HOCON setting should override akka.persistence setting")]
    public async Task HoconOverrideTest()
    {
        _permitter1.Tell(GetMaxPermits.Instance);
        await ExpectMsgAsync(3);
        
        _permitter2.Tell(GetMaxPermits.Instance);
        await ExpectMsgAsync(20);
    }

    [Fact(DisplayName = "Each plugin should have their own recovery permitter")]
    public void MultiRecoveryPermitterActorTest()
    {
        _permitter1.Equals(_permitter2).Should().BeFalse();
    }
}
