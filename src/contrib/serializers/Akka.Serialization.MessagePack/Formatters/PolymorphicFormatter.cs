using MessagePack;
using MessagePack.Formatters;

namespace Akka.Serialization.MessagePack.Resolvers
{
    public class PolymorphicFormatter<T> : IMessagePackFormatter<T>
    {
        //private static readonly ForceTypelessFormatter<T> _typelessFormatter =
        //    new ForceTypelessFormatter<T>();

        private readonly IMessagePackFormatter<T> _normalFormatter;

        public PolymorphicFormatter(IMessagePackFormatter<T> normalFormatter)
        {
            _normalFormatter = normalFormatter;
        }
        public void Serialize(ref MessagePackWriter writer, T value,
            MessagePackSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNil();
            }
            else if (value.GetType() == typeof(T))
            {
                _normalFormatter.Serialize(ref writer, value, options);
            }
            else
            {
                TypelessFormatter.Instance.Serialize(ref writer, value,
                    options);
            }
        }

        public T Deserialize(ref MessagePackReader reader,
            MessagePackSerializerOptions options)
        {
            // It's a trap!
            // TryReadExtensionFormatHeader will throw if Code is not Ext!!!
            // So we -must- first peek at NextCode and make sure it is one of
            // The correct types for Ext Data.
            switch (reader.NextCode)
            {
                case MessagePackCode.Ext8:
                case MessagePackCode.Ext16:
                case MessagePackCode.Ext32:
                case MessagePackCode.FixExt1:
                case MessagePackCode.FixExt2:
                case MessagePackCode.FixExt4:
                case MessagePackCode.FixExt8:
                case MessagePackCode.FixExt16:
                {
                    if (reader.CreatePeekReader().TryReadExtensionFormatHeader(out var header) &&
                        header.TypeCode == 100)
                        return (T)TypelessFormatter.Instance.Deserialize(ref reader,
                            options);
                    break;
                }
                case MessagePackCode.Nil:
                {
                    reader.ReadNil();
                    return default!;
                }
            }
            
            return _normalFormatter.Deserialize(ref reader, options);
        }
    }
}

