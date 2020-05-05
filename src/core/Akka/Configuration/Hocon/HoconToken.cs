//-----------------------------------------------------------------------
// <copyright file="HoconToken.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Configuration.Hocon
{
    /// <summary>
    /// This enumeration defines the different types of tokens found within
    /// a HOCON (Human-Optimized Config Object Notation) configuration string.
    /// </summary>
    public enum TokenType
    {
        /// <summary>
        /// This token type represents a comment.
        /// </summary>
        Comment,

        /// <summary>
        /// This token type represents the key portion of a key-value pair.
        /// </summary>
        Key,

        /// <summary>
        /// This token type represents the value portion of a key-value pair.
        /// </summary>
        LiteralValue,

        /// <summary>
        /// This token type represents the assignment operator, <c>=</c> or <c>:</c> .
        /// </summary>
        Assign,

        /// <summary>
        /// This token type represents the beginning of an object, <c>{</c> .
        /// </summary>
        ObjectStart,

        /// <summary>
        /// This token type represents the end of an object, <c>}</c> .
        /// </summary>
        ObjectEnd,

        /// <summary>
        /// This token type represents a namespace separator, <c>.</c> .
        /// </summary>
        Dot,

        /// <summary>
        /// This token type represents the end of the configuration string.
        /// </summary>
        EoF,

        /// <summary>
        /// This token type represents the beginning of an array, <c>[</c> .
        /// </summary>
        ArrayStart,

        /// <summary>
        /// This token type represents the end of an array, <c>]</c> .
        /// </summary>
        ArrayEnd,

        /// <summary>
        /// This token type represents the separator in an array, <c>,</c> .
        /// </summary>
        Comma,

        /// <summary>
        /// This token type represents a replacement variable, <c>$foo</c> .
        /// </summary>
        Substitute,

        /// <summary>
        /// This token type represents an include statement.
        /// </summary>
        Include
    }

    /// <summary>
    /// This class represents a token within a HOCON (Human-Optimized Config Object Notation)
    /// configuration string.
    /// </summary>
    public class Token
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Token"/> class.
        /// </summary>
        protected Token()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Token"/> class.
        /// </summary>
        /// <param name="type">The type of token to associate with.</param>
        public Token(TokenType type)
        {
            Type = type;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Token"/> class.
        /// </summary>
        /// <param name="value">The string literal value to associate with this token.</param>
        public Token(string value)
        {
            Type = TokenType.LiteralValue;
            Value = value;
        }

        /// <summary>
        /// The value associated with this token. If this token is
        /// a <see cref="TokenType.LiteralValue"/>, then this property
        /// holds the string literal.
        /// </summary>
        public string Value { get; set; }

        /// <summary>
        /// The type that represents this token.
        /// </summary>
        public TokenType Type { get; set; }

        /// <summary>
        /// Creates a key token with a given <paramref name="key"/>.
        /// </summary>
        /// <param name="key">The key to associate with this token.</param>
        /// <returns>A key token with the given key.</returns>
        public static Token Key(string key)
        {
            return new Token
            {
                Type = TokenType.Key,
                Value = key,
            };
        }

        /// <summary>
        /// Creates a substitution token with a given <paramref name="path"/>.
        /// </summary>
        /// <param name="path">The path to associate with this token.</param>
        /// <returns>A substitution token with the given path.</returns>
        public static Token Substitution(string path)
        {
            return new Token
            {
                Type = TokenType.Substitute,
                Value = path,
            };
        }

        /// <summary>
        /// Creates a string literal token with a given <paramref name="value"/>.
        /// </summary>
        /// <param name="value">The value to associate with this token.</param>
        /// <returns>A string literal token with the given value.</returns>
        public static Token LiteralValue(string value)
        {
            return new Token
            {
                Type = TokenType.LiteralValue,
                Value = value,
            };
        }

        internal static Token Include(string path)
        {
            return new Token
            {
                Value = path,
                Type = TokenType.Include
            };
        }
    }
}
