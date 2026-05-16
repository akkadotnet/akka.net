//-----------------------------------------------------------------------
// <copyright file="ExceptionAsSerializableResolver.cs" company="Akka.NET Project">
//     Copyright (C) 2017 Akka.NET Contrib <https://github.com/AkkaNetContrib/Akka.Serialization.MessagePack>
// </copyright>
//-----------------------------------------------------------------------
#if SERIALIZATION
using System;
using MessagePack;
using MessagePack.Formatters;

namespace Akka.Serialization.MessagePack.Resolvers
{
    public class ExceptionAsSerializableResolver : IFormatterResolver
    {
        public static readonly IFormatterResolver Instance = new ExceptionAsSerializableResolver();
        ExceptionAsSerializableResolver() { }

        public IMessagePackFormatter<T> GetFormatter<T>() => FormatterCache<T>.Formatter;

        static class FormatterCache<T>
        {
            public static readonly IMessagePackFormatter<T> Formatter;
            static FormatterCache() => Formatter = (IMessagePackFormatter<T>)ExceptionAsSerializableFormatterHelper.GetFormatter<T>();
        }
    }

    internal static class ExceptionAsSerializableFormatterHelper
    {
        internal static object GetFormatter<T>()
        {
            return ExceptionAsSerializableFormatterAssignable<T>
                .FormatterOrNull;
        }
    }

    /// <summary>
    /// A Generic-type cache to 
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal static class ExceptionAsSerializableFormatterAssignable<T>
    {
        public static readonly object FormatterOrNull =
            typeof(Exception).IsAssignableFrom(typeof(T))
                ? new SerializableFormatter<T>()
                : null; 
        public static readonly bool IsAssignableFrom =
            FormatterOrNull != null;
    }
}
#endif