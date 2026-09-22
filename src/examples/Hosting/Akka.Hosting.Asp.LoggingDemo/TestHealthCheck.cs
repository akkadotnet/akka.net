using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Akka.Hosting.Asp.LoggingDemo
{
    public class TestHealth : IAkkaHealthCheck
    {
        private readonly ILogger<TestHealth> _logger;

        public TestHealth(ILogger<TestHealth> logger)
        {
            _logger = logger;
        }

        public Task<HealthCheckResult> CheckHealthAsync(AkkaHealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("DI healthcheck is running");
                return Task.FromResult(HealthCheckResult.Healthy("Test is healthy"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health check failed for test");
                return Task.FromResult(HealthCheckResult.Unhealthy($"Test health check failed: {ex.Message}"));
            }
        }
    }
}
