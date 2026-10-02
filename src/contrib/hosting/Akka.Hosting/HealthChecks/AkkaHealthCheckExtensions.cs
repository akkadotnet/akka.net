using System;
using Akka.Actor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Akka.Hosting.HealthChecks;

internal static class AkkaHealthCheckExtensions
{
    public const string AkkaTag = "akka";
    
    /// <summary>
    /// Converts an <see cref="AkkaHealthCheckRegistration"/> to a <see cref="HealthCheckRegistration"/>
    /// </summary>
    /// <param name="registration">the original Akka.NET health check registration.</param>
    public static HealthCheckRegistration ToHealthCheckRegistration(this AkkaHealthCheckRegistration registration)
    {
        // func for lazily instantiating the health check registration
        Func<IServiceProvider, IHealthCheck> adapter = provider =>
            new HealthCheckAdapter(registration.Factory(provider), provider.GetRequiredService<ActorSystem>());

        var tags = registration.Tags;
        tags.Add(AkkaTag);

        return new HealthCheckRegistration(registration.Name, adapter, registration.FailureStatus, tags,
            registration.Timeout);
    }
}