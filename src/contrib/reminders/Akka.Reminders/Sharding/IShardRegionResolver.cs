using Akka.Actor;
using Akka.Cluster.Sharding;

namespace Akka.Reminders.Sharding;

/// <summary>
/// Thin abstraction for resolving the ShardRegion for a given reminder entity.
/// </summary>
/// <remarks>
/// Exists mostly to facilitate testing, so users can test reminder functionality
/// </remarks>
public interface IShardRegionResolver
{
    public IActorRef? TryResolve(ReminderEntity entity);

    /// <summary>
    /// Delivers a reminder message to the target entity.
    /// Implementations handle the delivery mechanism (e.g., wrapping in ShardingEnvelope for cluster sharding).
    /// </summary>
    /// <param name="entity">The target entity for the reminder.</param>
    /// <param name="envelope">The reminder envelope containing the payload and metadata.</param>
    /// <param name="sender">Optional sender actor reference. Defaults to <see cref="ActorRefs.NoSender"/>.</param>
    public void DeliverReminder(ReminderEntity entity, ReminderEnvelope envelope, IActorRef? sender = null);
}

/// <summary>
/// Resolves the ShardRegion for a given reminder entity using Akka.Cluster.Sharding.
/// </summary>
public sealed class DefaultShardRegionResolver : IShardRegionResolver
{
    private readonly ActorSystem _system;
    private ClusterSharding? _sharding;

    public DefaultShardRegionResolver(ActorSystem system)
    {
        _system = system;
    }

    public IActorRef? TryResolve(ReminderEntity entity)
    {
        // Lazy initialization - defer getting ClusterSharding until first use
        // This allows sharding to be fully initialized before we try to resolve regions
        _sharding ??= ClusterSharding.Get(_system);

        if (_sharding.ShardTypeNames.Contains(entity.ShardRegionName))
        {
            return _sharding.ShardRegion(entity.ShardRegionName);
        }

        return null;
    }

    public void DeliverReminder(ReminderEntity entity, ReminderEnvelope envelope, IActorRef? sender = null)
    {
        var shardRegion = TryResolve(entity);
        shardRegion?.Tell(new ShardingEnvelope(entity.EntityId, envelope), sender ?? ActorRefs.NoSender);
    }
}

/// <summary>
/// A test-only implementation of <see cref="IShardRegionResolver"/> that allows you to specify
/// the ShardRegion for a given reminder entity.
/// </summary>
/// <remarks>
/// This resolver is designed for testing scenarios where you want to control which actors
/// receive reminder messages without requiring a full cluster setup. You can register
/// shard regions manually using the constructor or the <see cref="RegisterShardRegion"/> method.
/// </remarks>
public sealed class TestShardRegionResolver : IShardRegionResolver
{
    private readonly Dictionary<string, IActorRef> _shardRegions;

    /// <summary>
    /// Creates a new <see cref="TestShardRegionResolver"/> with no registered shard regions.
    /// </summary>
    public TestShardRegionResolver()
    {
        _shardRegions = new Dictionary<string, IActorRef>();
    }

    /// <summary>
    /// Creates a new <see cref="TestShardRegionResolver"/> with the specified shard regions.
    /// </summary>
    /// <param name="shardRegions">A dictionary mapping shard region names to actor references.</param>
    public TestShardRegionResolver(IReadOnlyDictionary<string, IActorRef> shardRegions)
    {
        _shardRegions = new Dictionary<string, IActorRef>(shardRegions);
    }

    /// <summary>
    /// Registers a shard region with the resolver.
    /// </summary>
    /// <param name="regionName">The name of the shard region.</param>
    /// <param name="region">The actor reference for the shard region.</param>
    public void RegisterShardRegion(string regionName, IActorRef region)
    {
        _shardRegions[regionName] = region;
    }

    /// <summary>
    /// Removes a shard region registration from the resolver.
    /// </summary>
    /// <param name="regionName">The name of the shard region to remove.</param>
    /// <returns>True if the region was removed, false if it wasn't registered.</returns>
    public bool UnregisterShardRegion(string regionName)
    {
        return _shardRegions.Remove(regionName);
    }

    /// <summary>
    /// Gets the registered shard region names.
    /// </summary>
    public IEnumerable<string> RegisteredRegions => _shardRegions.Keys;

    public IActorRef? TryResolve(ReminderEntity entity)
    {
        return _shardRegions.GetValueOrDefault(entity.ShardRegionName);
    }

    public void DeliverReminder(ReminderEntity entity, ReminderEnvelope envelope, IActorRef? sender = null)
    {
        var actor = TryResolve(entity);
        actor?.Tell(envelope, sender ?? ActorRefs.NoSender);
    }
}