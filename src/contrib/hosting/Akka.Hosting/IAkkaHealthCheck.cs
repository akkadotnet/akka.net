using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Akka.Hosting;

/// <summary>
/// The registration record used to track this Akka.HealthCheck inside the <see cref="AkkaConfigurationBuilder"/>
/// </summary>
/// <remarks>
/// These will all get converted into <see cref="HealthCheckRegistration"/>s by the <see cref="AkkaConfigurationBuilder"/>,
/// and a default "akka.net" tag will be added to each of those registrations for filtering purposes.
/// </remarks>
public sealed class AkkaHealthCheckRegistration
{
    private Func<IServiceProvider, IAkkaHealthCheck> _healthCheck;
    private string _name;
    private TimeSpan _timeout;
    
    /// <summary>
    /// Creates a new <see cref="AkkaHealthCheckRegistration"/> for an existing <see cref="IAkkaHealthCheck"/>
    /// </summary>
    /// <param name="name">The healthcheck name.</param>
    /// <param name="instance">The <see cref="IAkkaHealthCheck"/> instance.</param>
    /// <param name="failureStatus">
    /// The <see cref="HealthStatus"/> that should be reported upon failure of the health check. If the provided value
    /// is <c>null</c>, then <see cref="HealthStatus.Unhealthy"/> will be reported.
    /// </param>
    /// <param name="tags">A list of tags that can be used for filtering health checks.</param>
    /// <exception cref="ArgumentNullException">Thrown if <see cref="name"/> or <see cref="instance"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if a negative timeout other than <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> is used.</exception>
    public AkkaHealthCheckRegistration(string name, IAkkaHealthCheck instance, HealthStatus? failureStatus,
        IEnumerable<string>? tags) : this(name, instance, failureStatus, tags, default)
    {
        
    }
    
    /// <summary>
    /// Creates a new <see cref="AkkaHealthCheckRegistration"/> for an existing <see cref="IAkkaHealthCheck"/>
    /// </summary>
    /// <param name="name">The healthcheck name.</param>
    /// <param name="instance">The <see cref="IAkkaHealthCheck"/> instance.</param>
    /// <param name="failureStatus">
    /// The <see cref="HealthStatus"/> that should be reported upon failure of the health check. If the provided value
    /// is <c>null</c>, then <see cref="HealthStatus.Unhealthy"/> will be reported.
    /// </param>
    /// <param name="tags">A list of tags that can be used for filtering health checks.</param>
    /// <param name="timeout">An optional <see cref="TimeSpan"/> representing the timeout of the check.</param>
    /// <exception cref="ArgumentNullException">Thrown if <see cref="name"/> or <see cref="instance"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if a negative timeout other than <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> is used.</exception>
    public AkkaHealthCheckRegistration(string name, IAkkaHealthCheck instance, HealthStatus? failureStatus,
        IEnumerable<string>? tags, TimeSpan? timeout)
    {
        if(name == null)  throw new ArgumentNullException(nameof(name));
        if(instance == null) throw new ArgumentNullException(nameof(instance));

        if (timeout <= TimeSpan.Zero && timeout != System.Threading.Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        _name = name;
        FailureStatus = failureStatus ?? HealthStatus.Unhealthy;
        Tags = new HashSet<string>(tags ?? [], StringComparer.OrdinalIgnoreCase);
        _healthCheck  = _ => instance;
        Timeout = timeout ?? System.Threading.Timeout.InfiniteTimeSpan;
    }

    /// <summary>
    /// Creates a new <see cref="AkkaHealthCheckRegistration"/> template for use with DI-resolved health checks.
    /// This constructor is intended for use with generic health check registration methods.
    /// </summary>
    /// <param name="name">The healthcheck name.</param>
    /// <param name="factory">The DI-enabled factory.</param>
    /// <param name="failureStatus">
    /// The <see cref="HealthStatus"/> that should be reported upon failure of the health check. If the provided value
    /// is <c>null</c>, then <see cref="HealthStatus.Unhealthy"/> will be reported.
    /// </param>
    /// <param name="tags">A list of tags that can be used for filtering health checks.</param>
    /// <param name="timeout">An optional <see cref="TimeSpan"/> representing the timeout of the check.</param>
    /// <exception cref="ArgumentNullException">Thrown if <see cref="name"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if a negative timeout other than <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> is used.</exception>
    internal AkkaHealthCheckRegistration(string name, Func<IServiceProvider, IAkkaHealthCheck> factory, HealthStatus? failureStatus,
        IEnumerable<string>? tags, TimeSpan? timeout)
    {
        if (name == null)
        {
            throw new ArgumentNullException(nameof(name));
        }
        if(factory == null) throw new ArgumentNullException(nameof(factory));

        if (timeout <= TimeSpan.Zero && timeout != System.Threading.Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        _name = name;
        FailureStatus = failureStatus ?? HealthStatus.Unhealthy;
        Tags = new HashSet<string>(tags ?? [], StringComparer.OrdinalIgnoreCase);
        _healthCheck = factory;
        Timeout = timeout ?? System.Threading.Timeout.InfiniteTimeSpan;
    }
    
    /// <summary>
    /// Gets or sets the healthcheck name.
    /// </summary>
    public string Name
    {
        get => _name;
        set => _name = value ?? throw new ArgumentNullException(nameof(value));
    }
    
    /// <summary>
    /// Gets or sets the <see cref="IAkkaHealthCheck"/>
    /// </summary>
    /// <exception cref="ArgumentNullException"></exception>
    public Func<IServiceProvider,IAkkaHealthCheck> Factory
    {
        get => _healthCheck;
        set => _healthCheck = value ?? throw new ArgumentNullException(nameof(value));
    }
        
    /// <summary>
    /// A set of optional tags used for filtering healthchecks by source.
    /// </summary>
    public ISet<string> Tags { get; }
    
    /// <summary>
    /// Gets or sets the <see cref="HealthStatus"/> that should be reported upon failure of the health check.
    /// </summary>
    public HealthStatus FailureStatus { get; set; }
    
     /// <summary>
    /// Gets or sets the timeout used for the test.
    /// </summary>
    public TimeSpan Timeout
    {
        get => _timeout;
        set
        {
            if (value <= TimeSpan.Zero && value != System.Threading.Timeout.InfiniteTimeSpan)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _timeout = value;
        }
    }

}

/// <summary>
/// Akka.NET health check invocation context. Provides access to the current Akka environment
/// along with the settings for this health check, via <see cref="Registration"/>.
/// </summary>
public sealed class AkkaHealthCheckContext
{
    public AkkaHealthCheckContext(ActorSystem actorSystem)
    {
        ActorSystem = actorSystem;
    }

    /// <summary>
    /// The <see cref="ActorSystem"/> that this health check is associated with.
    /// </summary>
    public ActorSystem ActorSystem { get; }
    
    /// <summary>
    /// The <see cref="ActorRegistry"/> belonging to the current <see cref="ActorSystem"/>.
    /// </summary>
    public ActorRegistry ActorRegistry => ActorRegistry.For(ActorSystem);

    /// <summary>
    /// The health check registration data.
    /// </summary>
    /// Expected to be not-null in live environments, but can be null during testing.
    public HealthCheckRegistration Registration { get; set; } = default!;
}

/// <summary>
/// Healthcheck aimed at testing the health of Akka.NET-specific resources.
/// </summary>
public interface IAkkaHealthCheck
{
    /// <summary>
    /// Performs a health-check using information readily available from the <see cref="ActorSystem"/> and <see cref="ActorRegistry"/>.
    /// 
    /// This can include checking the health status of plugins like Akka.Persistence or Akka.Cluster; or even messaging
    /// specific actors and awaiting a response from them.
    /// </summary>
    /// <param name="context">The context associated with the current health-check execution.</param>
    /// <param name="cancellationToken">A cancellation token that will be used to abort the healthcheck operation.</param>
    /// <returns></returns>
    Task<HealthCheckResult> CheckHealthAsync(AkkaHealthCheckContext context, CancellationToken cancellationToken = default);
}