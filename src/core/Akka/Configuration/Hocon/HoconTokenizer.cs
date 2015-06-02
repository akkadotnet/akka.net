﻿//-----------------------------------------------------------------------
// <copyright file="HoconTokenizer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Akka.Configuration.Hocon
{
    /// <summary>
    /// This class contains methods used to tokenize a string.
    /// </summary>
    public class Tokenizer
    {
        private readonly string _text;
        private int _index;

        /// <summary>
        /// Initializes a new instance of the <see cref="Tokenizer"/> class.
        /// </summary>
        /// <param name="text">The string that contains the text to tokenize.</param>
        public Tokenizer(string text)
        {
            this._text = text;
        }

        /// <summary>
        /// A value indicating whether the tokenizer has reached the end of the string.
        /// </summary>
        public bool EoF
        {
            get { return _index >= _text.Length; }
        }

        /// <summary>
        /// Determines whether the given pattern matches the value at the current
        /// position of the tokenizer.
        /// </summary>
        /// <param name="pattern">The string that contains the characters to match.</param>
        /// <returns><c>true</c> if the pattern matches, otherwise <c>false</c>.</returns>
        public bool Matches(string pattern)
        {
            if (pattern.Length + _index > _text.Length)
                return false;

            //Aaron: added this to make it easier to set a breakpoint to debug config issues
            string selected = _text.Substring(_index, pattern.Length);
            if (selected == pattern)
                return true;

            return false;
        }

        /// <summary>
        /// Retrieves a string of the given length from the current position of the tokenizer.
        /// </summary>
        /// <param name="length">The length of the string to return.</param>
        /// <returns>
        /// The string of the given length. If the length exceeds where the
        /// current index is located, then null is returned.
        /// </returns>
        public string Take(int length)
        {
            if (_index + length > _text.Length)
                return null;

            string s = _text.Substring(_index, length);
            _index += length;
            return s;
        }

        /// <summary>
        /// Determines whether any of the given patterns match the value at the current
        /// position of the tokenizer.
        /// </summary>
        /// <param name="patterns">The string array that contains the characters to match.</param>
        /// <returns><c>true</c> if any one of the patterns match, otherwise <c>false</c>.</returns>
        public bool Matches(params string[] patterns)
        {
            foreach (string pattern in patterns)
            {
                if (pattern.Length + _index >= _text.Length)
                    continue;

                if (_text.Substring(_index, pattern.Length) == pattern)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Retrieves the next character in the tokenizer without advancing its position.
        /// </summary>
        /// <returns>The character at the tokenizer's current position.</returns>
        public char Peek()
        {
            if (EoF)
                return (char) 0;

            return _text[_index];
        }

        /// <summary>
        /// Retrieves the next character in the tokenizer.
        /// </summary>
        /// <returns>The character at the tokenizer's current position.</returns>
        public char Take()
        {
            if (EoF)
                return (char) 0;

            return _text[_index++];
        }

        /// <summary>
        /// Advances the tokenizer to the next non-whitespace character.
        /// </summary>
        public void PullWhitespace()
        {
            while (!EoF && char.IsWhiteSpace(Peek()))
            {
                Take();
            }
        }
    }


    /// <summary>
    /// This class contains methods used to tokenize HOCON (Human-Optimized Config Object Notation)
    /// configuration strings.
    /// </summary>
    public class HoconTokenizer : Tokenizer
    {
        private const string NotInUnquotedKey = "$\"{}[]:=,#`^?!@*&\\.";
        private const string NotInUnquotedText = "$\"{}[]:=,#`^?!@*&\\";

        /// <summary>
        /// Initializes a new instance of the <see cref="HoconTokenizer"/> class.
        /// </summary>
        /// <param name="text">The string that contains the text to tokenize.</param>
        public HoconTokenizer(string text)
            : base(text)
        {
        }

        /// <summary>
        /// Advances the tokenizer to the next non-whitespace, non-comment token.
        /// </summary>
        public void PullWhitespaceAndComments()
        {
            do
            {
                PullWhitespace();
                while (IsStartOfComment())
                {
                    PullComment();
                }
            } while (IsWhitespace());
        }

        /// <summary>
        /// Retrieves the current line from where the current token
        /// is located in the string.
        /// </summary>
        /// <returns>The current line from where the current token is located.</returns>
        public string PullRestOfLine()
        {
            var sb = new StringBuilder();
            while (!EoF)
            {
                char c = Take();
                if (c == '\n')
                    break;

                //ignore
                if (c == '\r')
                    continue;

                sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        /// <summary>
        /// Retrieves the next token from the string.
        /// </summary>
        /// <returns>The next token contained in the string.</returns>
        /// <exception cref="System.Exception">
        /// This exception is thrown when an unknown token is encountered.
        /// </exception>
        public Token PullNext()
        {
            PullWhitespaceAndComments();
            if (IsDot())
            {
                return PullDot();
            }
            if (IsObjectStart())
            {
                return PullStartOfObject();
            }
            if (IsEndOfObject())
            {
                return PullEndOfObject();
            }
            if (IsAssignment())
            {
                return PullAssignment();
            }
            if (IsStartOfQuotedKey())
            {
                return PullQuotedKey();
            }
            if (IsUnquotedKeyStart())
            {
                return PullUnquotedKey();
            }
            if (IsArrayStart())
            {
                return PullArrayStart();
            }
            if (IsArrayEnd())
            {
                return PullArrayEnd();
            }
            if (EoF)
            {
                return new Token(TokenType.EoF);
            }
            throw new Exception("unknown token");
        }

        private bool IsStartOfQuotedKey()
        {
            return Matches("\"");
        }

        /// <summary>
        /// Retrieves a <see cref="TokenType.ArrayEnd"/> token from the tokenizer's current position.
        /// </summary>
        /// <returns>A <see cref="TokenType.ArrayEnd"/> token from the tokenizer's current position.</returns>
        public Token PullArrayEnd()
        {
            Take();
            return new Token(TokenType.ArrayEnd);
        }

        /// <summary>
        /// Determines whether the current token matches an <see cref="TokenType.ArrayEnd"/> token.
        /// </summary>
        /// <returns><c>true</c> if the token matches; otherwise, <c>false</c>.</returns>
        public bool IsArrayEnd()
        {
            return Matches("]");
        }

        /// <summary>
        /// Determines whether the current token matches an <see cref="TokenType.ArrayStart"/> token.
        /// </summary>
        /// <returns><c>true</c> if the token matches; otherwise, <c>false</c>.</returns>
        public bool IsArrayStart()
        {
            return Matches("[");
        }

        /// <summary>
        /// Retrieves a <see cref="TokenType.ArrayStart"/> token from the tokenizer's current position.
        /// </summary>
        /// <returns>A <see cref="TokenType.ArrayStart"/> token from the tokenizer's current position.</returns>
        public Token PullArrayStart()
        {
            Take();
            return new Token(TokenType.ArrayStart);
        }

        /// <summary>
        /// Retrieves a <see cref="TokenType.Dot"/> token from the tokenizer's current position.
        /// </summary>
        /// <returns>A <see cref="TokenType.Dot"/> token from the tokenizer's current position.</returns>
        public Token PullDot()
        {
            Take();
            return new Token(TokenType.Dot);
        }

        /// <summary>
        /// Retrieves a <see cref="TokenType.Comma"/> token from the tokenizer's current position.
        /// </summary>
        /// <returns>A <see cref="TokenType.Comma"/> token from the tokenizer's current position.</returns>
        public Token PullComma()
        {
            Take();
            return new Token(TokenType.Comma);
        }

        /// <summary>
        /// Retrieves a <see cref="TokenType.ObjectStart"/> token from the tokenizer's current position.
        /// </summary>
        /// <returns>A <see cref="TokenType.ObjectStart"/> token from the tokenizer's current position.</returns>
        public Token PullStartOfObject()
        {
            Take();
            return new Token(TokenType.ObjectStart);
        }

        /// <summary>
        /// Retrieves a <see cref="TokenType.ObjectEnd"/> token from the tokenizer's current position.
        /// </summary>
        /// <returns>A <see cref="TokenType.ObjectEnd"/> token from the tokenizer's current position.</returns>
        public Token PullEndOfObject()
        {
            Take();
            return new Token(TokenType.ObjectEnd);
        }

        /// <summary>
        /// Retrieves a <see cref="TokenType.Assign"/> token from the tokenizer's current position.
        /// </summary>
        /// <returns>A <see cref="TokenType.Assign"/> token from the tokenizer's current position.</returns>
        public Token PullAssignment()
        {
            Take();
            return new Token(TokenType.Assign);
        }

        /// <summary>
        /// Determines whether the current token matches an <see cref="TokenType.Comma"/> token.
        /// </summary>
        /// <returns><c>true</c> if the token matches; otherwise, <c>false</c>.</returns>
        public bool IsComma()
        {
            return Matches(",");
        }

        /// <summary>
        /// Determines whether the current token matches an <see cref="TokenType.Dot"/> token.
        /// </summary>
        /// <returns><c>true</c> if the token matches; otherwise, <c>false</c>.</returns>
        public bool IsDot()
        {
            return Matches(".");
        }

        /// <summary>
        /// Determines whether the current token matches an <see cref="TokenType.ObjectStart"/> token.
        /// </summary>
        /// <returns><c>true</c> if the token matches; otherwise, <c>false</c>.</returns>
        public bool IsObjectStart()
        {
            return Matches("{");
        }

        /// <summary>
        /// Determines whether the current token matches an <see cref="TokenType.ObjectEnd"/> token.
        /// </summary>
        /// <returns><c>true</c> if the token matches; otherwise, <c>false</c>.</returns>
        public bool IsEndOfObject()
        {
            return Matches("}");
        }

        /// <summary>
        /// Determines whether the current token matches an <see cref="TokenType.Assign"/> token.
        /// </summary>
        /// <returns><c>true</c> if the token matches; otherwise, <c>false</c>.</returns>
        public bool IsAssignment()
        {
            return Matches("=", ":");
        }

        /// <summary>
        /// Determines whether the current token matches the start of a quoted string.
        /// </summary>
        /// <returns><c>true</c> if token matches; otherwise, <c>false</c>.</returns>
        public bool IsStartOfQuotedText()
        {
            return Matches("\"");
        }

        /// <summary>
        /// Determines whether the current token matches the start of a triple quoted string.
        /// </summary>
        /// <returns><c>true</c> if token matches; otherwise, <c>false</c>.</returns>
        public bool IsStartOfTripleQuotedText()
        {
            return Matches("\"\"\"");
        }

        /// <summary>
        /// Retrieves a <see cref="TokenType.Comment"/> token from the tokenizer's current position.
        /// </summary>
        /// <returns>A <see cref="TokenType.Comment"/> token from the tokenizer's current position.</returns>
        public Token PullComment()
        {
            PullRestOfLine();
            return new Token(TokenType.Comment);
        }

        /// <summary>
        /// Retrieves an unquoted <see cref="TokenType.Key"/> token from the tokenizer's current position.
        /// </summary>
        /// <returns>A <see cref="TokenType.Key"/> token from the tokenizer's current position.</returns>
        public Token PullUnquotedKey()
        {
            var sb = new StringBuilder();
            while (!EoF && IsUnquotedKey())
            {
                sb.Append(Take());
            }

            return Token.Key((sb.ToString().Trim()));
        }

        /// <summary>
        /// Determines whether the current token is an unquoted key.
        /// </summary>
        /// <returns><c>true</c> if token is an unquoted key; otherwise, <c>false</c>.</returns>
        public bool IsUnquotedKey()
        {
            return (!EoF && !IsStartOfComment() && !NotInUnquotedKey.Contains(Peek()));
        }

        /// <summary>
        /// Determines whether the current token is the start of an unquoted key.
        /// </summary>
        /// <returns><c>true</c> if token is the start of an unquoted key; otherwise, <c>false</c>.</returns>
        public bool IsUnquotedKeyStart()
        {
            return (!EoF && !IsWhitespace() && !IsStartOfComment() && !NotInUnquotedKey.Contains(Peek()));
        }

        private bool IsWhitespace()
        {
            return char.IsWhiteSpace(Peek());
        }

        /// <summary>
        /// Retrieves a triple quoted <see cref="TokenType.LiteralValue"/> token from the tokenizer's current position.
        /// </summary>
        /// <returns>A <see cref="TokenType.LiteralValue"/> token from the tokenizer's current position.</returns>
        public Token PullTripleQuotedText()
        {
            var sb = new StringBuilder();
            Take(3);
            while (!EoF && !Matches("\"\"\""))
            {
                if (Matches("\\"))
                {
                    sb.Append(PullEscapeSequence());
                }
                else
                {
                    sb.Append(Peek());
                    Take();
                }
            }
            Take(3);
            return Token.LiteralValue(sb.ToString());
        }

        /// <summary>
        /// Retrieves a quoted <see cref="TokenType.LiteralValue"/> token from the tokenizer's current position.
        /// </summary>
        /// <returns>A <see cref="TokenType.LiteralValue"/> token from the tokenizer's current position.</returns>
        public Token PullQuotedText()
        {
            var sb = new StringBuilder();
            Take();
            while (!EoF && !Matches("\""))
            {
                if (Matches("\\"))
                {
                    sb.Append(PullEscapeSequence());
                }
                else
                {
                    sb.Append(Peek());
                    Take();
                }
            }
            Take();
            return Token.LiteralValue(sb.ToString());
        }

        /// <summary>
        /// Retrieves a quoted <see cref="TokenType.Key"/> token from the tokenizer's current position.
        /// </summary>
        /// <returns>A <see cref="TokenType.Key"/> token from the tokenizer's current position.</returns>
        public Token PullQuotedKey()
        {
            var sb = new StringBuilder();
            Take();
            while (!EoF && !Matches("\""))
            {
                if (Matches("\\"))
                {
                    sb.Append(PullEscapeSequence());
                }
                else
                {
                    sb.Append(Peek());
                    Take();
                }
            }
            Take();
            return Token.Key(sb.ToString());
        }

        private string PullEscapeSequence()
        {
            Take(); //consume "\"
            char escaped = Take();
            switch (escaped)
            {
                case '"':
                    return ("\"");
                case '\\':
                    return ("\\");
                case '/':
                    return ("/");
                case 'b':
                    return ("\b");
                case 'f':
                    return ("\f");
                case 'n':
                    return ("\n");
                case 'r':
                    return ("\r");
                case 't':
                    return ("\t");
                case 'u':
                    string hex = "0x" + Take(4);
                    int j = Convert.ToInt32(hex, 16);
                    return ((char) j).ToString(CultureInfo.InvariantCulture);
                default:
                    throw new NotSupportedException(string.Format("Unknown escape code: {0}", escaped));
            }
        }

        private bool IsStartOfComment()
        {
            return (Matches("#", "//"));
        }

        /// <summary>
        /// Retrieves a value token from the tokenizer's current position.
        /// </summary>
        /// <returns>A value token from the tokenizer's current position.</returns>
        /// <exception cref="System.Exception">
        /// Expected value: Null literal, Array, Quoted Text, Unquoted Text,
        ///     Triple quoted Text, Object or End of array
        /// </exception>
        public Token PullValue()
        {
            if (IsObjectStart())
            {
                return PullStartOfObject();
            }

            if (IsStartOfTripleQuotedText())
            {
                return PullTripleQuotedText();
            }

            if (IsStartOfQuotedText())
            {
                return PullQuotedText();
            }

            if (IsUnquotedText())
            {
                return PullUnquotedText();
            }
            if (IsArrayStart())
            {
                return PullArrayStart();
            }
            if (IsArrayEnd())
            {
                return PullArrayEnd();
            }
            if (IsSubstitutionStart())
            {
                return PullSubstitution();
            }

            throw new Exception(
                "Expected value: Null literal, Array, Quoted Text, Unquoted Text, Triple quoted Text, Object or End of array");
        }

        /// <summary>
        /// Determines whether the current token is the start of a substitution.
        /// </summary>
        /// <returns><c>true</c> if token is the start of a substitution; otherwise, <c>false</c>.</returns>
        public bool IsSubstitutionStart()
        {
            return Matches("${");
        }

        /// <summary>
        /// Retrieves a <see cref="TokenType.Substitute"/> token from the tokenizer's current position.
        /// </summary>
        /// <returns>A <see cref="TokenType.Substitute"/> token from the tokenizer's current position.</returns>
        public Token PullSubstitution()
        {
            var sb = new StringBuilder();
            Take(2);
            while (!EoF && IsUnquotedText())
            {
                sb.Append(Take());
            }
            Take();
            return Token.Substitution(sb.ToString());
        }

        /// <summary>
        /// Determines whether the current token is a space or a tab.
        /// </summary>
        /// <returns><c>true</c> if token is the start of a space or a tab; otherwise, <c>false</c>.</returns>
        public bool IsSpaceOrTab()
        {
            return Matches(" ", "\t");
        }

        /// <summary>
        /// Determines whether the current token is the start of an unquoted string literal.
        /// </summary>
        /// <returns><c>true</c> if token is the start of an unquoted string literal; otherwise, <c>false</c>.</returns>
        public bool IsStartSimpleValue()
        {
            if (IsSpaceOrTab())
                return true;

            if (IsUnquotedText())
                return true;

            return false;
        }

        /// <summary>
        /// Retrieves the current token, including whitespace and tabs, as a string literal token.
        /// </summary>
        /// <returns>A token that contains the string literal value.</returns>
        public Token PullSpaceOrTab()
        {
            var sb = new StringBuilder();
            while (IsSpaceOrTab())
            {
                sb.Append(Take());
            }
            return Token.LiteralValue(sb.ToString());
        }

        private Token PullUnquotedText()
        {
            var sb = new StringBuilder();
            while (!EoF && IsUnquotedText())
            {
                sb.Append(Take());
            }

            return Token.LiteralValue(sb.ToString());
        }

        private bool IsUnquotedText()
        {
            return (!EoF && !IsWhitespace() && !IsStartOfComment() && !NotInUnquotedText.Contains(Peek()));
        }

        /// <summary>
        /// Retrieves the current token as a string literal token.
        /// </summary>
        /// <returns>A token that contains the string literal value.</returns>
        /// <exception cref="System.Exception">
        /// This exception is thrown when the tokenizer cannot find
        /// a string literal value from the current token.
        /// </exception>
        public Token PullSimpleValue()
        {
            if (IsSpaceOrTab())
                return PullSpaceOrTab();
            if (IsUnquotedText())
                return PullUnquotedText();

            throw new Exception("No simple value found");
        }

        /// <summary>
        /// Determines whether the current token is a value.
        /// </summary>
        /// <returns><c>true</c> if the current token is a value; otherwise, <c>false</c>.</returns>
        internal bool IsValue()
        {
            if (IsArrayStart())
                return true;
            if (IsObjectStart())
                return true;
            if (IsStartOfTripleQuotedText())
                return true;
            if (IsSubstitutionStart())
                return true;
            if (IsStartOfQuotedText())
                return true;
            if (IsUnquotedText())
                return true;

            return false;
        }
    }
}
