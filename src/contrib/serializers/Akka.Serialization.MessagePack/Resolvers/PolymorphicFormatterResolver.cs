using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Akka.Util;
using CommunityToolkit.HighPerformance;
using MessagePack;
using MessagePack.Formatters;

namespace Akka.Serialization.MessagePack.Resolvers
{
    /// <summary>
    /// A 'Wrapping' resolver that,
    /// When provided as the 'base' resolver for MessagePack,
    /// Applies Polymorphic formatting rules to serialization,
    /// When the instanced type does not match runtime type.
    /// If you do not want this behavior, you may explicitly opt out
    /// Via ISurrogate, or have your in-chain Resolver provide a formatter
    /// That has the <see cref="IDoNotUsePolymorphicFormatter"/>
    /// Interface inherited.
    /// </summary>
    public class PolymorphicFormatterResolver : IFormatterResolver
    {
        private readonly IFormatterResolver _normalResolver;

        private readonly IntIndexedMessagePackFormatterDict _formatterDict =
            new IntIndexedMessagePackFormatterDict(256);
        
        public PolymorphicFormatterResolver(IFormatterResolver normalResolver)
        {
            _normalResolver = normalResolver;
        }
        
        public IMessagePackFormatter<T> GetFormatter<T>()
        {
            if (_formatterDict.TryGet(TypeDict<T>.TypeVal,out var formatter))
            {
                //IMPORTANT!
                // This assumes that MakeValue<T> is doing a proper
                // sanity check on the formatter
                // produced by underlying resolver.
                return Unsafe.As<IMessagePackFormatter<T>>(formatter);
            }
            else
            {
                return MakeValue<T>();
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private IMessagePackFormatter<T> MakeValue<T>()
        {
            // IMPORTANT!!!
            // For type safety make sure to read comments!!!
            // If you break below assumptions,
            // GetFormatter may require refactor!
            //
            // The explicit-casting here,
            // is to ensure we do a type check on returned value.
            // This helps guarantee our Unsafe calls are safe in context.
            // Since this is not at all the hot path,
            // easy cost to justify for retaining type safety.
            // 
            // Minor Note:
            // It's possible that we wastefully alloc here,
            // However our 'wrapped' formatter should be caching,
            // and the Polymorphic formatter itself is just a ref,
            // so it's a cheap temporary cost to pay.
            IMessagePackFormatter<T> formatterToUse =
                (IMessagePackFormatter<T>)(object)_normalResolver
                    .GetFormatter<T>();
            if ((formatterToUse is IDoNotUsePolymorphicFormatter) == false &&
                typeof(T).IsClass)
            {
                if ((typeof(T).IsAbstract || !typeof(T).IsSealed) &&
                    typeof(ISurrogated).IsAssignableFrom(typeof(T))==  false)
                {
                    formatterToUse =
                        new PolymorphicFormatter<T>(formatterToUse);
                }
            }

            return (IMessagePackFormatter<T>)_formatterDict.TryAdd(TypeDict<T>.TypeVal,formatterToUse);
        }
    }
}