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
        Substitute,
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

        public Token(string value)
        {
            Type = TokenType.LiteralValue;
            Value = value;
        }

        public string Value { get; set; }
        public TokenType Type { get; set; }

        public static Token Key(string key)
        {
            return new Token
            {
                Type = TokenType.Key,
                Value = key,
            };
        }

        public static Token Substitution(string path)
        {
            return new Token
            {
                Type = TokenType.Substitute,
                Value = path,
            };
        }

        public static Token LiteralValue(string value)
        {
            return new Token
            {
                Type = TokenType.LiteralValue,
                Value = value,
            };
        }
    }
}
