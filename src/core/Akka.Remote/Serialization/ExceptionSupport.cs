//-----------------------------------------------------------------------
// <copyright file="ExceptionSupport.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Remote.Serialization.Proto.Msg;
using Akka.Serialization;
using Akka.Util;
using Akka.Util.Internal;
using Google.Protobuf;

namespace Akka.Remote.Serialization
{
    internal sealed class ExceptionSupport
    {
        private readonly WrappedPayloadSupport _wrappedPayloadSupport;
        private readonly ExtendedActorSystem _system;
        private const BindingFlags All = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private readonly HashSet<string> _defaultProperties = new()
        {
            "ClassName",
            "Message",
            "StackTraceString",
            "Source",
            "InnerException",
            "HelpURL",
            "RemoteStackTraceString",
            "RemoteStackIndex",
            "ExceptionMethod",
            "HResult",
            "Data",
            "TargetSite",
            "HelpLink",
            "StackTrace",
            "WatsonBuckets"
        };

        public ExceptionSupport(ExtendedActorSystem system)
        {
            _system = system;
            _wrappedPayloadSupport = new WrappedPayloadSupport(system);
        }

        public byte[] SerializeException(Exception exception)
        {
            return ExceptionToProto(exception).ToByteArray();
        }

        internal Proto.Msg.ExceptionData ExceptionToProto(Exception? exception)
        {
            return ExceptionToProtoNet(exception);
        }

        public Exception? DeserializeException(byte[] bytes)
        {
            var proto = Proto.Msg.ExceptionData.Parser.ParseFrom(bytes);
            return ExceptionFromProto(proto);
        }

        internal Exception? ExceptionFromProto(Proto.Msg.ExceptionData proto)
        {
            return ExceptionFromProtoNet(proto);
        }

        private readonly FormatterConverter _defaultFormatterConverter = new();

        public Proto.Msg.ExceptionData ExceptionToProtoNet(Exception? exception)
        {
            var message = new Proto.Msg.ExceptionData();

            if (exception == null)
                return message;

            var exceptionType = exception.GetType();

            message.TypeName = exceptionType.TypeQualifiedName();
            message.Message = exception.Message;
            message.StackTrace = exception.StackTrace ?? "";
            message.Source = exception.Source ?? "";
            message.InnerException = ExceptionToProto(exception.InnerException);
            
            var forwardedFrom = exceptionType.GetCustomAttribute<TypeForwardedFromAttribute>();
            message.TypeForwardedFrom = forwardedFrom is not null 
                ? forwardedFrom.AssemblyFullName[..forwardedFrom.AssemblyFullName.IndexOf(',')] 
                : string.Empty;

            var serializable = (ISerializable)exception;
            var serializationInfo = new SerializationInfo(exceptionType, _defaultFormatterConverter);
            serializable.GetObjectData(serializationInfo, new StreamingContext());

            foreach (var info in serializationInfo)
            {
                if (_defaultProperties.Contains(info.Name)) continue;
                if (info.Value is Exception exceptionValue)
                {
                    var exceptionPayload = ExceptionToProto(exceptionValue);
                    var preparedValue = _wrappedPayloadSupport.PayloadToProto(exceptionPayload);
                    message.CustomFields.Add(info.Name, preparedValue);
                } else
                {
                    var preparedValue = _wrappedPayloadSupport.PayloadToProto(info.Value);
                    message.CustomFields.Add(info.Name, preparedValue);
                }
            }

            return message;
        }

        public Exception? ExceptionFromProtoNet(Proto.Msg.ExceptionData proto)
        {
            if (AkkaFeatures.IsDynamicTypeLoadingSupported && RuntimeFeature.IsDynamicCodeSupported)
                return ExceptionFromProtoJit(proto);
            return ExceptionFromProtoNative(proto);
        }

        private Exception? ExceptionFromProtoNative(Proto.Msg.ExceptionData? proto)
        {
            if (proto is null)
                return null;
            if (string.IsNullOrEmpty(proto.TypeName))
            {
                if (proto.CalculateSize() != 0)
                    throw new SerializationException("Legacy exception data has state but no exception type descriptor. " +
                        "It cannot be read with Akka.DynamicTypeLoading disabled.");
                return null;
            }
            if (!KnownExceptionFactories.TryFindLegacy(proto.TypeName, proto.TypeForwardedFrom, out var factory))
                throw new SerializationException($"Legacy exception type [{proto.TypeName}] has no native factory. " +
                    "Custom exception construction requires a JIT host with Akka.DynamicTypeLoading enabled.");

            // Preserve the historical protobuf format's state, including its intentional losses.
            // G8's richer MessagePack exception format has different defaults and is not used here.
            var info = new SerializationInfo(factory.Type, _defaultFormatterConverter);
            info.AddValue("ClassName", proto.TypeName);
            info.AddValue("Message", ValueOrNull(proto.Message));
            info.AddValue("StackTraceString", ValueOrNull(proto.StackTrace));
            info.AddValue("Source", ValueOrNull(proto.Source));
            info.AddValue("InnerException", ExceptionFromProtoNative(proto.InnerException));
            info.AddValue("HelpURL", string.Empty);
            info.AddValue("RemoteStackTraceString", null);
            info.AddValue("RemoteStackIndex", 0);
            info.AddValue("ExceptionMethod", null);
            info.AddValue("HResult", int.MinValue);
            foreach (var field in proto.CustomFields)
            {
                var value = ReadNativeField(field.Key, field.Value);
                if (value is ExceptionData exception)
                    value = ExceptionFromProtoNative(exception);
                info.AddValue(field.Key, value);
            }
            try
            {
                return factory.Create(info, new StreamingContext());
            }
            catch (Exception error) when (error is SerializationException or InvalidCastException or FormatException or ArgumentException)
            {
                throw new SerializationException($"Cannot reconstruct legacy exception [{proto.TypeName}] with its native factory. " +
                    "Registered payload serializers must preserve the legacy exception field values.", error);
            }
        }

        private object? ReadNativeField(string name, Payload payload)
        {
            try
            {
                var reader = _system.Serialization.GetSerializerById(payload.SerializerId);
                if (reader.GetType() == typeof(NewtonSoftJsonSerializer))
                    throw new SerializationException("The legacy JSON payload reader is unavailable when Akka.DynamicTypeLoading is disabled. " +
                        "Use an explicitly registered native payload codec compatible with this historical field.");
                if (reader.GetType() == typeof(ProtobufSerializer) && IsExceptionDataManifest(payload.MessageManifest.ToStringUtf8()))
                    return ExceptionData.Parser.ParseFrom(payload.Message);
                // In particular, an id 2 alias/subclass override remains the application's reader.
                return _wrappedPayloadSupport.PayloadFrom(payload);
            }
            catch (Exception error) when (error is KeyNotFoundException or SerializationException or NotSupportedException or MissingMethodException)
            {
                throw new SerializationException($"Cannot read legacy exception field [{name}] using registered serializer [{payload.SerializerId}] " +
                    "with Akka.DynamicTypeLoading disabled.", error);
            }
        }

        private static bool IsExceptionDataManifest(string manifest) =>
            Akka.Util.TypeExtensions.TrySplitTypeName(manifest, out var name, out var assembly) &&
            name == typeof(ExceptionData).FullName &&
            (assembly is null || string.Equals(assembly, typeof(ExceptionData).Assembly.GetName().Name, StringComparison.OrdinalIgnoreCase));

        [RequiresUnreferencedCode("Resolves and constructs arbitrary legacy exception types only on the JIT.")]
        [RequiresDynamicCode("Arbitrary legacy serialization constructors require JIT code generation.")]
        private Exception? ExceptionFromProtoJit(Proto.Msg.ExceptionData proto)
        {
            if (string.IsNullOrEmpty(proto.TypeName))
                return null;

            var exceptionType = Type.GetType(proto.TypeName);
            
            // If type loading failed and type was forwarded from an older assembly,
            // retry by loading the type from the older assembly name
            if (exceptionType is null && proto.TypeForwardedFrom != string.Empty)
            {
                var typeName = $"{proto.TypeName[..proto.TypeName.IndexOf(',')]}, {proto.TypeForwardedFrom}";
                exceptionType = Type.GetType(typeName);
            }

            // If we still fail, throw.
            if (exceptionType is null)
                throw new SerializationException($"Failed to deserialize ExceptionData. Could not load {proto.TypeName}. {proto}");

            var serializationInfo = new SerializationInfo(exceptionType, _defaultFormatterConverter);

            serializationInfo.AddValue("ClassName", proto.TypeName);
            serializationInfo.AddValue("Message", ValueOrNull(proto.Message));
            serializationInfo.AddValue("StackTraceString", ValueOrNull(proto.StackTrace));
            serializationInfo.AddValue("Source", ValueOrNull(proto.Source));
            serializationInfo.AddValue("InnerException", ExceptionFromProto(proto.InnerException));
            serializationInfo.AddValue("HelpURL", string.Empty);
            serializationInfo.AddValue("RemoteStackTraceString", null);
            serializationInfo.AddValue("RemoteStackIndex", 0);
            serializationInfo.AddValue("ExceptionMethod", null);
            serializationInfo.AddValue("HResult", int.MinValue);

            foreach (var field in proto.CustomFields)
            {
                var payload = _wrappedPayloadSupport.PayloadFrom(field.Value);
                if (payload is ExceptionData exception)
                    payload = ExceptionFromProto(exception);
                serializationInfo.AddValue(field.Key, payload);
            }

            Exception? obj = null;
            ConstructorInfo? constructorInfo = exceptionType.GetConstructor(
                All,
                null,
                new[] { typeof(SerializationInfo), typeof(StreamingContext) },
                null);

            if (constructorInfo != null)
            {
                object[] args = { serializationInfo, new StreamingContext() };
                obj = constructorInfo.Invoke(args).AsInstanceOf<Exception>();
            }

            return obj;
        }

        private static string? ValueOrNull(string value)
            => string.IsNullOrEmpty(value) ? null : value;
    }
}
