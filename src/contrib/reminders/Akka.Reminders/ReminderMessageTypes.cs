using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Akka.Reminders;

/// <summary>
/// Registers the message types your reminders carry, so the scheduler can build
/// <see cref="ReminderEnvelope{T}"/> for them without reflection.
/// </summary>
/// <remarks>
/// <para>
/// Under the JIT you never need this: an unregistered type falls back to reflection, as it always has.
/// Under Native AOT, or with the <c>Akka.DynamicTypeLoading</c> feature switch turned off, the fallback
/// is gone and every message type you schedule must be registered here. That includes types that only
/// reach the scheduler from storage after a restart.
/// </para>
/// <para>
/// Most applications call <see cref="LocalReminderConfigurationBuilder.WithReminderMessage{TMessage}"/>
/// or <see cref="ReminderConfigurationBuilder.WithReminderMessage{TMessage}"/> instead, which call
/// <see cref="Register{TMessage}"/> for you.
/// </para>
/// <para>
/// Registration is process-wide and idempotent: the envelope for a type is the same in every
/// <see cref="Akka.Actor.ActorSystem"/>, so registering it twice, or from two systems, is harmless.
/// Register the exact runtime type of the message - a registration for a base type does not cover
/// its subclasses, because the delivered envelope is <c>ReminderEnvelope&lt;RuntimeType&gt;</c>.
/// </para>
/// </remarks>
public static class ReminderMessageTypes
{
    /// <summary>
    /// Registers <typeparamref name="TMessage"/> as a reminder payload type.
    /// </summary>
    /// <typeparam name="TMessage">The exact runtime type of the message you schedule.</typeparam>
    public static void Register<TMessage>() where TMessage : notnull
        => ReminderEnvelopeFactory.Register<TMessage>();

    /// <summary>
    /// Returns <c>true</c> when <paramref name="messageType"/> was registered with <see cref="Register{TMessage}"/>.
    /// </summary>
    /// <remarks>
    /// This reports explicit registrations only. Under the JIT an unregistered type can still be
    /// delivered through the reflection fallback.
    /// </remarks>
    public static bool IsRegistered(Type messageType)
        => ReminderEnvelopeFactory.IsRegistered(messageType);
}

/// <summary>
/// Builds <see cref="ReminderEnvelope{T}"/> for a payload whose type is only known at runtime.
/// </summary>
internal static class ReminderEnvelopeFactory
{
    internal const string DynamicTypeLoadingSwitch = "Akka.DynamicTypeLoading";

    internal delegate ReminderEnvelope EnvelopeFactory(
        ReminderEntity entity,
        ReminderKey key,
        DateTimeOffset dueTimeUtc,
        ReminderDeadline deadline,
        object message);

    private static readonly ConcurrentDictionary<Type, EnvelopeFactory> Registered = new();

    // Factories built by reflection on the JIT, cached so we only pay for MakeGenericMethod once per type.
    private static readonly ConcurrentDictionary<Type, EnvelopeFactory> Reflected = new();

    /// <summary>
    /// Mirrors Akka.NET's own <c>Akka.DynamicTypeLoading</c> switch. On by default; a Native AOT or
    /// trimmed publish turns it off, and the trimmer then replaces this getter with <c>false</c>.
    /// On the JIT it is read on every call, so <see cref="AppContext.SetSwitch"/> takes effect at once.
    /// </summary>
    [FeatureSwitchDefinition(DynamicTypeLoadingSwitch)]
    internal static bool IsDynamicTypeLoadingEnabled =>
        !AppContext.TryGetSwitch(DynamicTypeLoadingSwitch, out var enabled) || enabled;

    /// <summary>
    /// <c>true</c> when an unregistered type can still be handled through reflection.
    /// </summary>
    internal static bool IsReflectionFallbackAvailable =>
        RuntimeFeature.IsDynamicCodeSupported && IsDynamicTypeLoadingEnabled;

    internal static void Register<TMessage>() where TMessage : notnull
    {
        Registered.TryAdd(
            typeof(TMessage),
            static (entity, key, dueTimeUtc, deadline, message) =>
                new ReminderEnvelope<TMessage>(entity, key, dueTimeUtc, deadline, (TMessage)message));
    }

    internal static bool IsRegistered(Type messageType) => Registered.ContainsKey(messageType);

    /// <summary>
    /// <c>true</c> when <see cref="Create"/> will succeed for <paramref name="messageType"/>.
    /// </summary>
    internal static bool CanCreate(Type messageType)
        => Registered.ContainsKey(messageType) || IsReflectionFallbackAvailable;

    /// <summary>
    /// Builds a <see cref="ReminderEnvelope{T}"/> closed over the runtime type of <paramref name="message"/>,
    /// so that <c>Receive&lt;ReminderEnvelope&lt;T&gt;&gt;</c> handlers match.
    /// </summary>
    /// <param name="entity">The entity the reminder belongs to.</param>
    /// <param name="key">The reminder key.</param>
    /// <param name="dueTimeUtc">The occurrence due time.</param>
    /// <param name="deadline">The delivery deadline.</param>
    /// <param name="message">The payload.</param>
    /// <param name="allowReflection">
    /// <c>false</c> forces the Native AOT behaviour on the JIT, so specs can exercise it
    /// without flipping a process-wide switch.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The type is not registered and the reflection fallback is unavailable.
    /// </exception>
    internal static ReminderEnvelope Create(
        ReminderEntity entity,
        ReminderKey key,
        DateTimeOffset dueTimeUtc,
        ReminderDeadline deadline,
        object message,
        bool allowReflection = true)
    {
        var messageType = message.GetType();
        if (Registered.TryGetValue(messageType, out var factory))
            return factory(entity, key, dueTimeUtc, deadline, message);

        // Keep this check inline: the analyzer and ILC only treat the reflection call as guarded
        // when it sits directly behind RuntimeFeature.IsDynamicCodeSupported.
        if (allowReflection && RuntimeFeature.IsDynamicCodeSupported && IsDynamicTypeLoadingEnabled)
        {
            factory = Reflected.GetOrAdd(messageType, CreateReflectionFactory);
            return factory(entity, key, dueTimeUtc, deadline, message);
        }

        throw NotRegistered(messageType);
    }

    internal static string NotRegisteredMessage(Type messageType)
        => $"Reminder message type [{messageType.FullName}] is not registered, and ReminderEnvelope<{messageType.Name}> " +
           "cannot be built by reflection because dynamic code is unavailable (Native AOT) or the " +
           $"[{DynamicTypeLoadingSwitch}] feature switch is off. Register it with " +
           $".WithReminderMessage<{messageType.Name}>() on the reminders builder, or call " +
           $"ReminderMessageTypes.Register<{messageType.Name}>() at startup.";

    private static InvalidOperationException NotRegistered(Type messageType)
        => new(NotRegisteredMessage(messageType));

    [RequiresDynamicCode("Builds ReminderEnvelope<T> for a runtime type. Register the type with WithReminderMessage<T>() for Native AOT.")]
    private static EnvelopeFactory CreateReflectionFactory(Type messageType)
    {
        var method = typeof(ReminderEnvelopeFactory)
            .GetMethod(nameof(CreateTyped), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(messageType);
        return method.CreateDelegate<EnvelopeFactory>();
    }

    private static ReminderEnvelope CreateTyped<TMessage>(
        ReminderEntity entity,
        ReminderKey key,
        DateTimeOffset dueTimeUtc,
        ReminderDeadline deadline,
        object message)
        => new ReminderEnvelope<TMessage>(entity, key, dueTimeUtc, deadline, (TMessage)message);
}
