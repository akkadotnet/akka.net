// //-----------------------------------------------------------------------
// // <copyright file="ChannelExecutorConfigurationSpec.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.TestKit;
using Xunit;
using FluentAssertions;

namespace Akka.Tests.Dispatch
{
    public class ChannelExecutorConfigurationSpec : AkkaSpec
    {
        [Fact]
        public void ChannelExecutor_config_should_be_injected_when_it_doesnt_exist()
        {
            var config = ConfigurationFactory.ParseString(@"executor = channel-executor");
            var configurator = new ChannelExecutorConfigurator(config, Sys.Dispatchers.Prerequisites);
            configurator.Priority.Should().Be(TaskSchedulerPriority.Normal);
        }

        [Fact]
        public void ChannelExecutor_default_should_be_overriden_by_config()
        {
            var config = ConfigurationFactory.ParseString(@"
executor = channel-executor
channel-executor.priority = high");
            var configurator = new ChannelExecutorConfigurator(config, Sys.Dispatchers.Prerequisites);
            configurator.Priority.Should().Be(TaskSchedulerPriority.High);
        }

        [Fact]
        public void ChannelExecutorConfigurator_should_use_default_when_config_is_null()
        {
            var configurator = new ChannelExecutorConfigurator(null, Sys.Dispatchers.Prerequisites);
            configurator.Priority.Should().Be(TaskSchedulerPriority.Normal);
        }

        // backward compatibility test
        [Fact]
        public async Task ChannelExecutor_instantiation_should_not_throw_when_config_doesnt_exist()
        {
            var config = ConfigurationFactory.ParseString(@"
akka.actor.default-dispatcher = { 
    executor = channel-executor
}");
            
            // Throws NRE in 1.4.29-32
            var sys = ActorSystem.Create("test", config);
            
            // Check that all settings are correct
            var dispatcher = sys.Dispatchers.Lookup("akka.actor.default-dispatcher");
            dispatcher.Configurator.Config.GetString("executor").Should().Be("channel-executor");
            
            var configurator = new ChannelExecutorConfigurator(dispatcher.Configurator.Config, Sys.Dispatchers.Prerequisites);
            configurator.Priority.Should().Be(TaskSchedulerPriority.Normal);

            await sys.Terminate();
        }
    }
}