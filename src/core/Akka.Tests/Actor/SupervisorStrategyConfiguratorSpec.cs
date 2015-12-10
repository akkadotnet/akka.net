using Akka.Actor;
using Xunit;

namespace Akka.Tests.Actor
{
    public class DefaultSupervisorStrategySpec
    {
        [Fact]
        public void DefaultSupervisorStrategy_returns_default_strategy_as_a_guardian_strategy()
        {
            var defaultStrategyConfigurator = new DefaultSupervisorStrategy();

            var result = defaultStrategyConfigurator.Create();

            Assert.Equal(SupervisorStrategy.DefaultStrategy, result);
        }
    }

    public class StoppingSupervisorStrategySpec
    {
        [Fact]
        public void StoppingSupervisorStrategy_returns_stopping_strategy_as_a_guardian_strategy()
        {
            var defaultStrategyConfigurator = new StoppingSupervisorStrategy();

            var result = defaultStrategyConfigurator.Create();

            Assert.Equal(SupervisorStrategy.StoppingStrategy, result);
        }
    }
}