// -----------------------------------------------------------------------
// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// -----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Pattern;
using Akka.Serialization;
using Akka.Serialization.V2;
using Akka.Util;
using MessagePack;

namespace Akka.Remote.Serialization;

/// <summary>
/// Reusable MessagePack exception formatter with finite native factories and a guarded JIT
/// fallback for custom serializable exceptions. It does not register a global serializer.
/// </summary>
/// <remarks>
/// The constructor receives the actor system through the generator's existing formatter context.
/// Known exceptions retain serialization-constructor state, including raw messages, stack/source,
/// inner exceptions, custom Akka members and data entries. Unknown native exceptions are rejected;
/// arbitrary custom serialization constructors remain a JIT-only compatibility boundary.
/// This formatter prepares its entire value before writing. An enclosing serializer must likewise
/// prepare or stage the whole message if it promises atomic caller-buffer failure across fields.
/// </remarks>
public sealed class ExceptionMessagePackFormatter : IAkkaMessagePackFormatter<Exception>
{
    private const string Manifest = "exception-fragment-v1";
    private const int MaxDepth = 100;
    private readonly ExtendedActorSystem _system;
    private readonly ExceptionWireCodec _codec;
    private readonly FormatterConverter _converter = new();
    private static readonly HashSet<string> Defaults =
    [
        "ClassName", "Message", "Data", "InnerException", "HelpURL", "StackTraceString",
        "RemoteStackTraceString", "RemoteStackIndex", "ExceptionMethod", "HResult", "Source",
        "TargetSite", "HelpLink", "StackTrace", "WatsonBuckets"
    ];

    /// <summary>Creates an exception formatter using the system's payload serializers and actor-ref provider.</summary>
    public ExceptionMessagePackFormatter(ExtendedActorSystem system)
    {
        _system = system;
        _codec = new ExceptionWireCodec(system);
    }

    /// <inheritdoc />
    public void Write(ref MessagePackWriter writer, Exception value)
    {
        ArgumentNullException.ThrowIfNull(value);
        // All classification, recursive validation, payload encoding and JIT fallback complete
        // before the caller's MessagePack writer receives even the first byte of this value.
        var previous = Akka.Serialization.Serialization.CurrentTransportInformation;
        byte[] bytes;
        try
        {
            if (previous is null)
                Akka.Serialization.Serialization.CurrentTransportInformation = _system.Provider.SerializationInformation;
            var wire = Capture(value, new HashSet<Exception>(ReferenceEqualityComparer.Instance), 0);
            bytes = _codec.ToBinary(wire);
        }
        finally
        {
            Akka.Serialization.Serialization.CurrentTransportInformation = previous;
        }
        writer.WriteRaw(bytes);
    }

    /// <inheritdoc />
    public Exception Read(ref MessagePackReader reader)
    {
        var bytes = reader.ReadRaw();
        var wire = (ExceptionWire)_codec.Deserialize(bytes, Manifest);
        return Restore(wire, 0);
    }

    /// <inheritdoc />
    public int SizeOf(Exception value) => SerializerV2.UnknownSize;

    private ExceptionWire Capture(Exception value, HashSet<Exception> active, int depth)
    {
        CheckDepth(depth);
        if (!active.Add(value))
            throw new SerializationException("Exception state contains a cycle.");
        try
        {
            var isKnown = KnownExceptionFactories.TryFind(value.GetType(), out var factory);
            string? jitTypeName = null;
            string? forwardedFrom = null;
            var type = value.GetType();
            if (!isKnown)
            {
                if (AkkaFeatures.IsDynamicTypeLoadingSupported && RuntimeFeature.IsDynamicCodeSupported)
                    (jitTypeName, forwardedFrom) = CaptureJitIdentity(type);
                else
                    throw new SerializationException($"Exception type [{type}] has no native factory. Dynamic exception loading is disabled.");
            }

            var info = new SerializationInfo(type, _converter);
            ((ISerializable)value).GetObjectData(info, new StreamingContext());
            var fields = new List<ExceptionFieldWire>();
            foreach (var entry in info)
            {
                if (!Defaults.Contains(entry.Name))
                {
                    var declared = DeclaredType(entry.ObjectType, entry.Value);
                    string? declaredName = null;
                    if (declared == -1)
                    {
                        if (AkkaFeatures.IsDynamicTypeLoadingSupported && RuntimeFeature.IsDynamicCodeSupported)
                            declaredName = JitTypeName(entry.ObjectType);
                        else
                            throw new SerializationException($"Unsupported declared native exception field type [{entry.ObjectType}].");
                    }
                    fields.Add(new ExceptionFieldWire(entry.Name, declared, CaptureValue(entry.Value, active, depth + 1), declaredName));
                }
            }
            var data = new List<ExceptionDataEntryWire>();
            if (info.GetValue("Data", typeof(IDictionary)) is IDictionary entries)
            {
                foreach (DictionaryEntry entry in entries)
                    data.Add(new ExceptionDataEntryWire(CaptureValue(entry.Key, active, depth + 1), CaptureValue(entry.Value, active, depth + 1)));
            }
            return new ExceptionWire(isKnown ? factory.Descriptor : 0, info.GetString("Message"), info.GetString("StackTraceString"),
                info.GetString("Source"), value.InnerException is null ? null : Capture(value.InnerException, active, depth + 1),
                fields.OrderBy(field => field.Name, StringComparer.Ordinal).ToArray(), jitTypeName, info.GetString("ClassName"),
                info.GetInt32("HResult"), info.GetString("HelpURL"), info.GetString("RemoteStackTraceString"),
                info.GetInt32("RemoteStackIndex"), info.GetString("ExceptionMethod"), data.ToArray(), forwardedFrom);
        }
        finally
        {
            active.Remove(value);
        }
    }

    private ExceptionValueWire CaptureValue(object? value, HashSet<Exception> active, int depth)
    {
        CheckDepth(depth);
        return value switch
        {
            null => new ExceptionValueWire(0, null, null, null, null),
            Exception exception => new ExceptionValueWire(1, null, Capture(exception, active, depth), null, null),
            IActorRef actor when Equals(actor, ActorRefs.Nobody) => new ExceptionValueWire(5, null, null, null, null),
            IActorRef actor => new ExceptionValueWire(2, null, null, actor, null),
            TimeSpan duration => new ExceptionValueWire(3, null, null, null, duration.Ticks),
            _ => CapturePayload(value)
        };
    }

    private ExceptionValueWire CapturePayload(object value)
    {
        var serializer = _system.Serialization.FindSerializerFor(value);
        if ((!AkkaFeatures.IsDynamicTypeLoadingSupported || !RuntimeFeature.IsDynamicCodeSupported) && serializer is NewtonSoftJsonSerializer)
            throw new SerializationException($"Exception payload type [{value.GetType()}] requires an explicit native payload serializer.");
        return new ExceptionValueWire(4, value, null, null, null);
    }

    private Exception Restore(ExceptionWire wire, int depth)
    {
        CheckDepth(depth);
        Type type;
        if (wire.Descriptor == 0)
        {
            if (AkkaFeatures.IsDynamicTypeLoadingSupported && RuntimeFeature.IsDynamicCodeSupported)
                type = ResolveJitType(wire.JitTypeName ?? throw new SerializationException("Missing custom JIT exception type."), wire.JitForwardedFrom);
            else
                throw new SerializationException("Custom JIT exception descriptors cannot be read with dynamic exception loading disabled.");
        }
        else if (KnownExceptionFactories.TryFind(wire.Descriptor, out var nativeFactory))
            type = nativeFactory.Type;
        else
            throw new SerializationException($"Unknown native exception descriptor [{wire.Descriptor}].");

        var info = new SerializationInfo(type, _converter);
        info.AddValue("ClassName", wire.ClassName);
        info.AddValue("Message", wire.Message);
        info.AddValue("StackTraceString", wire.StackTrace);
        info.AddValue("Source", wire.Source);
        info.AddValue("InnerException", wire.Inner is null ? null : Restore(wire.Inner, depth + 1), typeof(Exception));
        info.AddValue("HelpURL", wire.HelpUrl);
        info.AddValue("RemoteStackTraceString", wire.RemoteStackTrace);
        info.AddValue("RemoteStackIndex", wire.RemoteStackIndex);
        info.AddValue("ExceptionMethod", wire.ExceptionMethod);
        info.AddValue("HResult", wire.HResult);
        IDictionary? data = null;
        if (wire.Data.Length > 0)
        {
            data = new Hashtable();
            foreach (var entry in wire.Data)
                data.Add(RestoreValue(entry.Key, depth + 1) ?? throw new SerializationException("Exception data keys cannot be null."), RestoreValue(entry.Value, depth + 1));
        }
        info.AddValue("Data", data, typeof(IDictionary));
        foreach (var field in wire.Fields)
        {
            var value = RestoreValue(field.Value, depth + 1);
            // OpenCircuit's legacy GetObjectData stores TimeSpan, while its serialization ctor
            // explicitly asks for string. Convert the native ticks value without losing precision.
            if (type == typeof(OpenCircuitException) && field.Name == "RemainingDuration" && value is TimeSpan duration)
                info.AddValue(field.Name, duration.ToString("c", CultureInfo.InvariantCulture), typeof(string));
            else
                info.AddValue(field.Name, value, RestoreDeclaredType(field.DeclaredType, value, field.JitDeclaredType));
        }
        if (KnownExceptionFactories.TryFind(wire.Descriptor, out var factory))
            return factory.Create(info, new StreamingContext());
        if (AkkaFeatures.IsDynamicTypeLoadingSupported && RuntimeFeature.IsDynamicCodeSupported)
            return RestoreJit(type, info);
        throw new SerializationException("Dynamic exception construction is disabled.");
    }

    private object? RestoreValue(ExceptionValueWire wire, int depth)
    {
        CheckDepth(depth);
        return wire.Kind switch
        {
            0 => null,
            1 => Restore(wire.Exception ?? throw new SerializationException("Missing nested exception value."), depth),
            2 => wire.ActorRef,
            3 => TimeSpan.FromTicks(wire.Ticks ?? throw new SerializationException("Missing duration ticks.")),
            4 => wire.Payload,
            5 => ActorRefs.Nobody,
            _ => throw new SerializationException($"Unknown exception field value descriptor [{wire.Kind}].")
        };
    }

    private static int DeclaredType(Type type, object? value)
    {
        if (type == typeof(object)) return 1;
        if (type == typeof(string)) return 2;
        if (type == typeof(int)) return 3;
        if (type == typeof(long)) return 4;
        if (type == typeof(bool)) return 5;
        if (type == typeof(double)) return 6;
        if (type == typeof(float)) return 7;
        if (type == typeof(uint)) return 8;
        if (type == typeof(ulong)) return 9;
        if (type == typeof(byte[])) return 10;
        if (type == typeof(TimeSpan)) return 11;
        if (type == typeof(Exception)) return 12;
        if (type == typeof(IActorRef)) return 13;
        // A user serializer returns the actual runtime value, so no type-name lookup is needed.
        if (value is not null && type == value.GetType()) return 0;
        return -1;
    }

    private static Type RestoreDeclaredType(int descriptor, object? value, string? jitTypeName)
    {
        if (descriptor == -1)
        {
            if (AkkaFeatures.IsDynamicTypeLoadingSupported && RuntimeFeature.IsDynamicCodeSupported)
                return ResolveJitType(jitTypeName ?? throw new SerializationException("Missing declared JIT field type."), null);
            throw new SerializationException("Declared JIT field types cannot be read with dynamic loading disabled.");
        }
        return descriptor switch
        {
            0 => value?.GetType() ?? throw new SerializationException("A runtime-typed exception value is missing."),
            1 => typeof(object), 2 => typeof(string), 3 => typeof(int), 4 => typeof(long),
            5 => typeof(bool), 6 => typeof(double), 7 => typeof(float), 8 => typeof(uint),
            9 => typeof(ulong), 10 => typeof(byte[]), 11 => typeof(TimeSpan), 12 => typeof(Exception), 13 => typeof(IActorRef),
            _ => throw new SerializationException($"Unknown declared exception field type descriptor [{descriptor}].")
        };
    }

    [RequiresUnreferencedCode("Uses the existing custom exception serialization/type-forwarding compatibility path on the JIT.")]
    [RequiresDynamicCode("Arbitrary exception serialization constructors are available only on the JIT.")]
    private static (string Name, string? Forwarded) CaptureJitIdentity(Type type)
    {
        GetJitConstructor(type);
        var forwarded = type.GetCustomAttribute<TypeForwardedFromAttribute>()?.AssemblyFullName;
        return (JitTypeName(type), forwarded is null ? null : forwarded.Split(',')[0]);
    }

    [RequiresUnreferencedCode("Retains the type-name contract of custom JIT-only SerializationInfo fields.")]
    private static string JitTypeName(Type type) => type.FullName + ", " + type.Assembly.GetName().Name;

    [RequiresUnreferencedCode("Resolves arbitrary custom exception and declared-field types only when dynamic loading is enabled.")]
    private static Type ResolveJitType(string name, string? forwardedFrom)
    {
        var type = Type.GetType(name);
        if (type is null && !string.IsNullOrEmpty(forwardedFrom))
        {
            var separator = name.IndexOf(',');
            if (separator >= 0)
                type = Type.GetType(name[..separator] + ", " + forwardedFrom);
        }
        return type ?? throw new SerializationException($"Could not resolve custom JIT type [{name}].");
    }

    [RequiresUnreferencedCode("Finds an arbitrary custom exception's serialization constructor only on the JIT.")]
    [RequiresDynamicCode("Arbitrary exception constructors require JIT code generation.")]
    private static ConstructorInfo GetJitConstructor(Type type)
    {
        if (!typeof(Exception).IsAssignableFrom(type))
            throw new SerializationException($"Custom JIT exception descriptor resolves to non-exception type [{type}].");
        return type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            [typeof(SerializationInfo), typeof(StreamingContext)], null)
            ?? throw new SerializationException($"Custom exception [{type}] has no serialization constructor.");
    }

    [RequiresUnreferencedCode("Uses the existing custom exception serialization/type-forwarding compatibility path on the JIT.")]
    [RequiresDynamicCode("Arbitrary exception serialization constructors are available only on the JIT.")]
    private static Exception RestoreJit(Type type, SerializationInfo info)
        => (Exception)GetJitConstructor(type).Invoke([info, new StreamingContext()]);

    private static void CheckDepth(int depth)
    {
        if (depth > MaxDepth)
            throw new SerializationException("Exception state exceeds the supported nesting depth.");
    }
}
