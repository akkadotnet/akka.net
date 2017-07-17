//-----------------------------------------------------------------------
// <copyright file="WrappedPayloadSupport.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Akka.Actor;
using Akka.Util;
using Akka.Util.Internal;
using Google.Protobuf;
#if SERIALIZATION
using System.Runtime.Serialization;
#endif

namespace Akka.Remote.Serialization
{
    internal class ExceptionSupport
    {
        private readonly WrappedPayloadSupport _wrappedPayloadSupport;

        public ExceptionSupport(ExtendedActorSystem system)
        {
            _wrappedPayloadSupport = new WrappedPayloadSupport(system);
        }

        public byte[] SerializeException(Exception exception)
        {
            return ExceptionToProto(exception).ToByteArray();
        }

        internal Proto.Msg.ExceptionData ExceptionToProto(Exception exception)
        {
#if SERIALIZATION
            return ExceptionToProtoNet(exception);
#else
            return ExceptionToProtoNetCore(exception);
#endif
        }

        public Exception DeserializeException(byte[] bytes)
        {
            var proto = Proto.Msg.ExceptionData.Parser.ParseFrom(bytes);
            return ExceptionFromProto(proto);
        }

        internal Exception ExceptionFromProto(Proto.Msg.ExceptionData proto)
        {
#if SERIALIZATION
            return ExceptionFromProtoNet(proto);
#else
            return ExceptionFromProtoNetCore(proto);
#endif
        }

#if SERIALIZATION
        public Proto.Msg.ExceptionData ExceptionToProtoNet(Exception exception)
        {
            var message = new Proto.Msg.ExceptionData();

            if (exception == null)
                return message;

            message.TypeName = exception.GetType().TypeQualifiedName();

            var serializable = exception as ISerializable;
            var serializationInfo = new SerializationInfo(exception.GetType(), new FormatterConverter());
            serializable.GetObjectData(serializationInfo, new StreamingContext());

            foreach (var info in serializationInfo)
            {
                var preparedValue = _wrappedPayloadSupport.PayloadToProto(info.Value);
                message.Fields.Add(info.Name, preparedValue);
            }

            return message;
        }

        public Exception ExceptionFromProtoNet(Proto.Msg.ExceptionData proto)
        {
            if (proto.Fields.Count == 0)
                return null;

            Type exceptionType = Type.GetType(proto.TypeName);

            var serializationInfo = new SerializationInfo(exceptionType, new FormatterConverter());

            foreach (var field in proto.Fields)
            {
                serializationInfo.AddValue(field.Key, _wrappedPayloadSupport.PayloadFrom(field.Value));
            }

            Exception obj = null;
            ConstructorInfo constructorInfo = exceptionType.GetConstructor(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
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
#else
        private HashSet<string> Exclude = new HashSet<string>
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
            "StackTrace"
        };

        private TypeInfo ExceptionTypeInfo = typeof(Exception).GetTypeInfo();
        private const BindingFlags All = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        internal Proto.Msg.ExceptionData ExceptionToProtoNetCore(Exception exception)
        {
            var message = new Proto.Msg.ExceptionData();

            if (exception == null)
                return message;

            message.TypeName = exception.GetType().TypeQualifiedName();

            message.Fields.Add("Message", _wrappedPayloadSupport.PayloadToProto(exception.Message));
            message.Fields.Add("StackTrace", _wrappedPayloadSupport.PayloadToProto(exception.StackTrace));
            message.Fields.Add("Source", _wrappedPayloadSupport.PayloadToProto(exception.Source));

            if (exception.InnerException != null)
                message.Fields.Add("InnerException", _wrappedPayloadSupport.PayloadToProto(exception.InnerException));

            // serialize all public properties
            foreach (var property in exception.GetType().GetTypeInfo().DeclaredProperties)
            {
                if (Exclude.Contains(property.Name)) continue;
                if (property.SetMethod != null)
                {
                    message.Fields.Add(property.Name, _wrappedPayloadSupport.PayloadToProto(property.GetValue(exception)));
                }
            }

            return message;
        }

        internal Exception ExceptionFromProtoNetCore(Proto.Msg.ExceptionData proto)
        {
            if (proto.Fields.Count == 0)
                return null;

            Type exceptionType = Type.GetType(proto.TypeName);

            var obj = Activator.CreateInstance(exceptionType);

            if (proto.Fields.TryGetValue("Message", out var message))
                ExceptionTypeInfo?.GetField("_message", All)?.SetValue(obj, _wrappedPayloadSupport.PayloadFrom(message));

            if (proto.Fields.TryGetValue("StackTrace", out var stackTrace))
                ExceptionTypeInfo?.GetField("_stackTraceString", All)?.SetValue(obj, _wrappedPayloadSupport.PayloadFrom(stackTrace));

            if (proto.Fields.TryGetValue("Source", out var source))
                ExceptionTypeInfo?.GetField("_source", All)?.SetValue(obj, _wrappedPayloadSupport.PayloadFrom(source));

            if (proto.Fields.TryGetValue("InnerException", out var innerException))
                ExceptionTypeInfo?.GetField("_innerException", All)?.SetValue(obj, _wrappedPayloadSupport.PayloadFrom(innerException));

            // deserialize all public properties with setters
            foreach (var property in proto.Fields)
            {
                if (Exclude.Contains(property.Key)) continue;
                var prop = exceptionType.GetProperty(property.Key, All);
                if (prop.SetMethod != null)
                {
                    prop.SetValue(obj, _wrappedPayloadSupport.PayloadFrom(property.Value));
                }
            }

            return (Exception)obj;
        }
#endif
    }
}
