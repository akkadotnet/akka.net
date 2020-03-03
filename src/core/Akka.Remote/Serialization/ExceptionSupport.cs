//-----------------------------------------------------------------------
// <copyright file="ExceptionSupport.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization.Formatters;
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
        private const BindingFlags All = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private HashSet<string> DefaultProperties = new HashSet<string>
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
        private FormatterConverter DefaultFormatterConverter = new FormatterConverter();

        public Proto.Msg.ExceptionData ExceptionToProtoNet(Exception exception)
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

            var serializable = exception as ISerializable;
            var serializationInfo = new SerializationInfo(exceptionType, DefaultFormatterConverter);
            serializable.GetObjectData(serializationInfo, new StreamingContext());

            foreach (var info in serializationInfo)
            {
                if (DefaultProperties.Contains(info.Name)) continue;
                var preparedValue = _wrappedPayloadSupport.PayloadToProto(info.Value);
                message.CustomFields.Add(info.Name, preparedValue);
            }

            return message;
        }

        public Exception ExceptionFromProtoNet(Proto.Msg.ExceptionData proto)
        {
            if (string.IsNullOrEmpty(proto.TypeName))
                return null;

            Type exceptionType = Type.GetType(proto.TypeName);

            var serializationInfo = new SerializationInfo(exceptionType, DefaultFormatterConverter);

            serializationInfo.AddValue("ClassName", proto.TypeName);
            serializationInfo.AddValue("Message", proto.Message);
            serializationInfo.AddValue("StackTraceString", proto.StackTrace);
            serializationInfo.AddValue("Source", proto.Source);
            serializationInfo.AddValue("InnerException", ExceptionFromProto(proto.InnerException));
            serializationInfo.AddValue("HelpURL", string.Empty);
            serializationInfo.AddValue("RemoteStackTraceString", string.Empty);
            serializationInfo.AddValue("RemoteStackIndex", 0);
            serializationInfo.AddValue("ExceptionMethod", string.Empty);
            serializationInfo.AddValue("HResult", int.MinValue);

            foreach (var field in proto.CustomFields)
            {
                serializationInfo.AddValue(field.Key, _wrappedPayloadSupport.PayloadFrom(field.Value));
            }

            Exception obj = null;
            ConstructorInfo constructorInfo = exceptionType.GetConstructor(
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
#else
        private TypeInfo ExceptionTypeInfo = typeof(Exception).GetTypeInfo();

        internal Proto.Msg.ExceptionData ExceptionToProtoNetCore(Exception exception)
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

            // serialize all public properties
            foreach (var property in exceptionType.GetTypeInfo().DeclaredProperties)
            {
                if (DefaultProperties.Contains(property.Name)) continue;
                if (property.SetMethod != null)
                {
                    message.CustomFields.Add(property.Name, _wrappedPayloadSupport.PayloadToProto(property.GetValue(exception)));
                }
            }

            return message;
        }

        internal Exception ExceptionFromProtoNetCore(Proto.Msg.ExceptionData proto)
        {
            if (string.IsNullOrEmpty(proto.TypeName))
                return null;

            Type exceptionType = Type.GetType(proto.TypeName);

            var obj = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(exceptionType);

            if (!string.IsNullOrEmpty(proto.Message))
                ExceptionTypeInfo?.GetField("_message", All)?.SetValue(obj, proto.Message);

            if (!string.IsNullOrEmpty(proto.StackTrace))
                ExceptionTypeInfo?.GetField("_stackTraceString", All)?.SetValue(obj, proto.StackTrace);

            if (!string.IsNullOrEmpty(proto.Source))
                ExceptionTypeInfo?.GetField("_source", All)?.SetValue(obj, proto.Source);

            if (!string.IsNullOrEmpty(proto.InnerException.TypeName))
                ExceptionTypeInfo?.GetField("_innerException", All)?.SetValue(obj, ExceptionFromProto(proto.InnerException));

            // deserialize all public properties with setters
            foreach (var property in proto.CustomFields)
            {
                if (DefaultProperties.Contains(property.Key)) continue;
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
