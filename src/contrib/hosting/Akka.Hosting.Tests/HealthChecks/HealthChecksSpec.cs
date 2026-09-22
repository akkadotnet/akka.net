using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Hosting.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Akka.Hosting.Tests.HealthChecks;

public class HealthChecksSpec : TestKit.TestKit
{
    public HealthChecksSpec(ITestOutputHelper output)
        : base(output: output)
    {
    }

    private class FooActor : UntypedActor
    {
        protected override void OnReceive(object message)
        {
        }
    }

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        builder
            .WithActorSystemLivenessCheck(failureStatus: HealthStatus.Degraded, tags: ["custom", "liveness"]) // test custom parameters
            .WithHealthCheck("FooActor alive", async (system, registry, cancellationToken) =>
            {
                /*
                 * N.B. CancellationToken is set by the call to MSFT.EXT.DIAGNOSTICS.HEALTHCHECK,
                 * so that value could be "infinite" by default.
                 *
                 * Therefore, it might be a really, really good idea to guard this with a non-infinite
                 * timeout via a LinkedCancellationToken here.
                 */
                try
                {
                    var fooActor = await registry.GetAsync<FooActor>(cancellationToken);

                    try
                    {
                        var r = await fooActor.Ask<ActorIdentity>(new Identify("foo"),
                            cancellationToken: cancellationToken);
                        if (r.Subject.IsNobody())
                            return HealthCheckResult.Unhealthy("FooActor was alive but is now dead");
                    }
                    catch (Exception e)
                    {
                        return HealthCheckResult.Degraded("FooActor found but non-responsive", e);
                    }
                }
                catch (Exception e2)
                {
                    return HealthCheckResult.Unhealthy("FooActor not found in registry", e2);
                }

                return HealthCheckResult.Healthy("fooActor found and responsive");
            }, failureStatus: HealthStatus.Degraded, tags: ["foo", "actor"], timeout: TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task ShouldHaveDefaultHealthCheckRegistration()
    {
        // arrange
        var configurationBuilder = Host.Services.GetRequiredService<AkkaConfigurationBuilder>();

        // act

        // assert
        Assert.Equal(2, configurationBuilder.HealthChecks.Count); // 1 built-in, 1 custom

        // find the built-in implementation
        var actorSystemHealthCheckRegistration =
            configurationBuilder.HealthChecks.Values.Single(c => c.Factory(Host.Services) is ActorSystemLivenessCheck);

        // Verify the custom parameters we set in ConfigureAkka were applied
        Assert.Equal(HealthStatus.Degraded, actorSystemHealthCheckRegistration.FailureStatus);
        Assert.Contains("custom", actorSystemHealthCheckRegistration.Tags);
        Assert.Contains("liveness", actorSystemHealthCheckRegistration.Tags);

        var akkaHealthCheckContext = new AkkaHealthCheckContext(Sys)
            { Registration = actorSystemHealthCheckRegistration.ToHealthCheckRegistration() };

        // invoke the actorSystem liveness check
        var healthCheck = actorSystemHealthCheckRegistration.Factory(Host.Services);
        var healthCheckResult = await healthCheck.CheckHealthAsync(akkaHealthCheckContext, CancellationToken.None);

        // assert - system is alive, health check should be healthy
        Assert.Equal(HealthStatus.Healthy, healthCheckResult.Status);
    }

    [Fact]
    public async Task ShouldReturnAppropriateResults()
    {
        // arrange
        var configurationBuilder = Host.Services.GetRequiredService<AkkaConfigurationBuilder>();

        // act
        var customActorHealthCheck =
            configurationBuilder.HealthChecks.Values.Single(c => c.Factory(Host.Services) is DelegateHealthCheck);

        // Verify custom parameters for the delegate health check
        Assert.Equal(HealthStatus.Degraded, customActorHealthCheck.FailureStatus);
        Assert.Contains("foo", customActorHealthCheck.Tags);
        Assert.Contains("actor", customActorHealthCheck.Tags);
        Assert.Equal(TimeSpan.FromSeconds(30), customActorHealthCheck.Timeout);

        var akkaHealthCheckContext = new AkkaHealthCheckContext(Sys)
            { Registration = customActorHealthCheck.ToHealthCheckRegistration() };

        // should fail - target actor is not alive
        await InvokeHealthCheck(HealthStatus.Unhealthy);

        // start the actor and register it
        var fooActor = Sys.ActorOf(Props.Create(() => new FooActor()), "foo");
        ActorRegistry.Register<FooActor>(fooActor);

        // should succeed - target actor is around
        await InvokeHealthCheck(HealthStatus.Healthy, 3000);

        // kill the target actor
        await WatchAsync(fooActor);
        fooActor.Tell(PoisonPill.Instance);
        await ExpectTerminatedAsync(fooActor);

        // found in the registry, but dead
        await InvokeHealthCheck(HealthStatus.Unhealthy, 3000);
        return;

        async Task InvokeHealthCheck(HealthStatus expectedStatus, int waitMilliseconds = 1)
        {
            using var fastCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(waitMilliseconds));
            var healthCheck = customActorHealthCheck.Factory(Host.Services);
            var healthCheckResult = await healthCheck.CheckHealthAsync(akkaHealthCheckContext, fastCts.Token);
            if (healthCheckResult.Description != null)
                Output?.WriteLine(healthCheckResult.Description);
            Assert.Equal(expectedStatus, healthCheckResult.Status);
        }
    }

    [Fact]
    public async Task ShouldResolveDiHealthCheckFromContainer()
    {
        // arrange
        var configurationBuilder = Host.Services.GetRequiredService<AkkaConfigurationBuilder>();

        // act - add a DI-resolved health check to the existing configuration
        configurationBuilder.WithHealthCheck<TestDiHealthCheck>("test-di-health");

        // assert
        Assert.Equal(3, configurationBuilder.HealthChecks.Count); // 2 from base configuration + 1 DI-resolved

        // find the DI-resolved health check
        var diHealthCheckRegistration =
            configurationBuilder.HealthChecks.Values.Single(c => c.Name == "test-di-health");
        var akkaHealthCheckContext = new AkkaHealthCheckContext(Sys)
        {
            Registration = diHealthCheckRegistration.ToHealthCheckRegistration()
        };

        // invoke the DI-resolved health check - TestDiHealthCheck is registered in ConfigureServices
        var healthCheck = diHealthCheckRegistration.Factory(Host.Services);
        var healthCheckResult = await healthCheck.CheckHealthAsync(akkaHealthCheckContext, CancellationToken.None);

        // assert - health check should be healthy
        Assert.Equal(HealthStatus.Healthy, healthCheckResult.Status);
        Assert.Equal("Test DI health check is working with DI", healthCheckResult.Description);
    }

    [Fact]
    public async Task ShouldResolveDiHealthCheckWithRegistrationTemplate()
    {
        // arrange
        var configurationBuilder = Host.Services.GetRequiredService<AkkaConfigurationBuilder>();

        // act - add a DI-resolved health check using the registration template pattern
        configurationBuilder.WithHealthCheck<TestDiHealthCheck>(
            "template-test",
            HealthStatus.Degraded,
            [
                "custom", "test"
            ]);

        // assert
        Assert.Equal(3, configurationBuilder.HealthChecks.Count); // 2 from base configuration + 1 DI-resolved

        // find the DI-resolved health check
        var diHealthCheckRegistration = configurationBuilder.HealthChecks.Values.Single(c => c.Name == "template-test");
        Assert.Equal(HealthStatus.Degraded, diHealthCheckRegistration.FailureStatus);
        Assert.Contains("custom", diHealthCheckRegistration.Tags);
        Assert.Contains("test", diHealthCheckRegistration.Tags);

        var akkaHealthCheckContext = new AkkaHealthCheckContext(Sys)
            { Registration = diHealthCheckRegistration.ToHealthCheckRegistration() };

        // invoke the health check
        var healthCheck = diHealthCheckRegistration.Factory(Host.Services);
        var healthCheckResult = await healthCheck.CheckHealthAsync(akkaHealthCheckContext, CancellationToken.None);

        // assert
        Assert.Equal(HealthStatus.Healthy, healthCheckResult.Status);
        Assert.Equal("Test DI health check is working with DI", healthCheckResult.Description);
    }

    /// <summary>
    /// Test health check class that requires DI (simulates ILogger dependency)
    /// </summary>
    private class TestDiHealthCheck : IAkkaHealthCheck
    {
        private readonly IServiceProvider? _serviceProvider;

        public TestDiHealthCheck(IServiceProvider serviceProvider)
        {
            // Constructor with DI dependency
            _serviceProvider = serviceProvider;
        }

        public Task<HealthCheckResult> CheckHealthAsync(AkkaHealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            // Verify that dependencies can be resolved (simulates ILogger usage)
            var hasServiceProvider = _serviceProvider != null;

            return Task.FromResult(HealthCheckResult.Healthy("Test DI health check is working" +
                                                             (hasServiceProvider ? " with DI" : "")));
        }
    }
}