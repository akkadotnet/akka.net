//-----------------------------------------------------------------------
// <copyright file="SerializableResolver.cs" company="Akka.NET Project">
//     Copyright (C) 2017 Akka.NET Contrib <https://github.com/AkkaNetContrib/Akka.Serialization.MessagePack>
// </copyright>
//-----------------------------------------------------------------------
using System;
using MessagePack;
using MessagePack.Formatters;

namespace Akka.Serialization.MessagePack.Resolvers
{
    public class SerializableResolver : IFormatterResolver
    {
        public static readonly IFormatterResolver Instance = new SerializableResolver();
        SerializableResolver() { }

        public IMessagePackFormatter<T> GetFormatter<T>() => FormatterCache<T>.Formatter;

        static class FormatterCache<T>
        {
            public static readonly IMessagePackFormatter<T> Formatter;
            static FormatterCache() => Formatter = (IMessagePackFormatter<T>)SerializableFormatterHelper.GetFormatter<T>();
        }
    }

    internal static class SerializableFormatterHelper
    {
        internal static object GetFormatter<T>()
        {
            return SerializableFormatterAssignable<T>.IsAssignableFrom
                ? new SerializableFormatter<T>()
                : null;
        }
    }

    internal static class SerializableFormatterAssignable<T>
    {
        public static readonly bool IsAssignableFrom =
            typeof(Exception).IsAssignableFrom(typeof(T));
    }
}