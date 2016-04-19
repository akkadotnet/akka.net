//-----------------------------------------------------------------------
// <copyright file="LoadPluginSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Journal;
using Xunit;

namespace Akka.Persistence.Tests
{
    public class LoadPluginSpec : PersistenceSpec
    {
        public sealed class GetConfig
        {
            public static readonly GetConfig Instance = new GetConfig();
            private GetConfig() { }
        }

        public class JournalWithConfig : MemoryJournal
        {
            private readonly Config _config;

            public JournalWithConfig(Config config)
            {
                _config = config;
            }

            protected override bool ReceivePluginInternal(object message)
            {
                if (message is GetConfig)
                {
                    Sender.Tell(_config);
                    return true;
                }
                return false;
            }
        }

        public LoadPluginSpec() : base(Configuration("LoadPluginSpec", extraConfig:
  @"akka.persistence.journal.inmem.class = ""Akka.Persistence.Tests.LoadPluginSpec+JournalWithConfig, Akka.Persistence.Tests""
  akka.persistence.journal.inmem.extra-property = 17"))
        {
        }

        [Fact]
        public void Plugin_with_config_parameter_should_be_created_with_plugin_config()
        {
            var pluginRef = Persistence.Instance.Apply(Sys).JournalFor("akka.persistence.journal.inmem");
            pluginRef.Tell(GetConfig.Instance);
            ExpectMsg<Config>(c => c.GetInt("extra-property") == 17);
        }
    }
}
