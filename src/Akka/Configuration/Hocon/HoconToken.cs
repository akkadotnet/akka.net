namespace Akka.Configuration.Hocon
{
    /// <summary>
    /// Enum TokenType
    /// </summary>
    public enum TokenType
    {
        /// <summary>
        /// The comment
        /// </summary>
        Comment,
        /// <summary>
        /// The key
        /// </summary>
        Key,
        /// <summary>
        /// The literal value
        /// </summary>
        LiteralValue,
        /// <summary>
        /// The assign
        /// </summary>
        Assign,
        /// <summary>
        /// The object start
        /// </summary>
        ObjectStart,
        /// <summary>
        /// The object end
        /// </summary>
        ObjectEnd,
        /// <summary>
        /// The dot
        /// </summary>
        Dot,
        /// <summary>
        /// The eo f
        /// </summary>
        EoF,
        /// <summary>
        /// The array start
        /// </summary>
        ArrayStart,
        /// <summary>
        /// The array end
        /// </summary>
        ArrayEnd,
        /// <summary>
        /// The comma
        /// </summary>
        Comma,
        /// <summary>
        /// The substitute
        /// </summary>
        Substitute,
    }

    /// <summary>
    /// Class Token.
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
        /// <param name="type">The type.</param>
        public Token(TokenType type)
        {
            Type = type;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Token"/> class.
        /// </summary>
        /// <param name="value">The value.</param>
        public Token(string value)
        {
            Type = TokenType.LiteralValue;
            Value = value;
        }

        /// <summary>
        /// If this instance is a LiteralValue, the Value property holds the string literal.
        /// </summary>
        /// <value>The value.</value>
        public string Value { get; set; }
        /// <summary>
        /// The type of the token.
        /// </summary>
        /// <value>The type.</value>
        public TokenType Type { get; set; }

        /// <summary>
        /// Creates a Key token.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>Token.</returns>
        public static Token Key(string key)
        {
            return new Token
            {
                Type = TokenType.Key,
                Value = key,
            };
        }

        /// <summary>
        /// Creates a Substitution token with a given Path
        /// </summary>
        /// <param name="path">The path.</param>
        /// <returns>Token.</returns>
        public static Token Substitution(string path)
        {
            return new Token
            {
                Type = TokenType.Substitute,
                Value = path,
            };
        }

        /// <summary>
        /// Creates a string Literal token.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>Token.</returns>
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