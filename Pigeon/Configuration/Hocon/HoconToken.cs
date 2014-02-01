using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Pigeon.Configuration.Hocon
{
    public enum TokenType
    {
        Comment,
        Key,
        LiteralValue,
        Assign,
        ObjectStart,
        ObjectEnd,
        Dot,
        EoF,
        ArrayStart,
        ArrayEnd,
        Comma,
    }

    public class Token
    {
        protected Token()
        {
        }

        public Token(TokenType type)
        {
            Type = type;
        }

        public Token(object value)
        {
            Type = TokenType.LiteralValue;
            Value = value;
        }

        public object Value { get; set; }
        public TokenType Type { get; set; }

        public static Token Key(string key)
        {
            return new Token
            {
                Type = TokenType.Key,
                Value = key,
            };
        }

        public static Token LiteralValue(object value)
        {
            return new Token
            {
                Type = TokenType.LiteralValue,
                Value = value,
            };
        }
    }
}
