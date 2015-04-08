//-----------------------------------------------------------------------
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
    ///     Class Tokenizer.
    /// </summary>
    public class Tokenizer
    {
        /// <summary>
        ///     The text
        /// </summary>
        private readonly string _text;

        /// <summary>
        ///     The index
        /// </summary>
        private int _index;

        /// <summary>
        ///     Initializes a new instance of the <see cref="Tokenizer" /> class.
        /// </summary>
        /// <param name="text">The text.</param>
        public Tokenizer(string text)
        {
            this._text = text;
        }

        /// <summary>
        ///     Gets a value indicating whether [eof].
        /// </summary>
        /// <value><c>true</c> if [eof]; otherwise, <c>false</c>.</value>
        public bool EoF
        {
            get { return _index >= _text.Length; }
        }

        /// <summary>
        ///     Matches the specified pattern.
        /// </summary>
        /// <param name="pattern">The pattern.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
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
        ///     Takes the specified length.
        /// </summary>
        /// <param name="length">The length.</param>
        /// <returns>System.String.</returns>
        public string Take(int length)
        {
            if (_index + length > _text.Length)
                return null;

            string s = _text.Substring(_index, length);
            _index += length;
            return s;
        }

        /// <summary>
        ///     Matches the specified patterns.
        /// </summary>
        /// <param name="patterns">The patterns.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
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
        ///     Peeks this instance.
        /// </summary>
        /// <returns>System.Char.</returns>
        public char Peek()
        {
            if (EoF)
                return (char) 0;

            return _text[_index];
        }

        /// <summary>
        ///     Takes this instance.
        /// </summary>
        /// <returns>System.Char.</returns>
        public char Take()
        {
            if (EoF)
                return (char) 0;

            return _text[_index++];
        }

        /// <summary>
        ///     Pulls the whitespace.
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
    ///     Class HoconTokenizer.
    /// </summary>
    public class HoconTokenizer : Tokenizer
    {
        /// <summary>
        ///     The not in unquoted key
        /// </summary>
        private const string NotInUnquotedKey = "$\"{}[]:=,#`^?!@*&\\.";

        /// <summary>
        ///     The not in unquoted text
        /// </summary>
        private const string NotInUnquotedText = "$\"{}[]:=,#`^?!@*&\\";

        /// <summary>
        ///     Initializes a new instance of the <see cref="HoconTokenizer" /> class.
        /// </summary>
        /// <param name="text">The text.</param>
        public HoconTokenizer(string text)
            : base(text)
        {
        }

        /// <summary>
        ///     Pulls the whitespace and comments.
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
        ///     Pulls the rest of line.
        /// </summary>
        /// <returns>System.String.</returns>
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
        ///     Pulls the next.
        /// </summary>
        /// <returns>Token.</returns>
        /// <exception cref="System.Exception">unknown token</exception>
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

        /// <summary>
        ///     Determines whether [is start of quoted key].
        /// </summary>
        /// <returns><c>true</c> if [is start of quoted key]; otherwise, <c>false</c>.</returns>
        private bool IsStartOfQuotedKey()
        {
            return Matches("\"");
        }

        /// <summary>
        ///     Pulls the array end.
        /// </summary>
        /// <returns>Token.</returns>
        public Token PullArrayEnd()
        {
            Take();
            return new Token(TokenType.ArrayEnd);
        }

        /// <summary>
        ///     Determines whether [is array end].
        /// </summary>
        /// <returns><c>true</c> if [is array end]; otherwise, <c>false</c>.</returns>
        public bool IsArrayEnd()
        {
            return Matches("]");
        }

        /// <summary>
        ///     Determines whether [is array start].
        /// </summary>
        /// <returns><c>true</c> if [is array start]; otherwise, <c>false</c>.</returns>
        public bool IsArrayStart()
        {
            return Matches("[");
        }

        /// <summary>
        ///     Pulls the array start.
        /// </summary>
        /// <returns>Token.</returns>
        public Token PullArrayStart()
        {
            Take();
            return new Token(TokenType.ArrayStart);
        }

        /// <summary>
        ///     Pulls the dot.
        /// </summary>
        /// <returns>Token.</returns>
        public Token PullDot()
        {
            Take();
            return new Token(TokenType.Dot);
        }

        /// <summary>
        ///     Pulls the comma.
        /// </summary>
        /// <returns>Token.</returns>
        public Token PullComma()
        {
            Take();
            return new Token(TokenType.Comma);
        }

        /// <summary>
        ///     Pulls the start of object.
        /// </summary>
        /// <returns>Token.</returns>
        public Token PullStartOfObject()
        {
            Take();
            return new Token(TokenType.ObjectStart);
        }

        /// <summary>
        ///     Pulls the end of object.
        /// </summary>
        /// <returns>Token.</returns>
        public Token PullEndOfObject()
        {
            Take();
            return new Token(TokenType.ObjectEnd);
        }

        /// <summary>
        ///     Pulls the assignment.
        /// </summary>
        /// <returns>Token.</returns>
        public Token PullAssignment()
        {
            Take();
            return new Token(TokenType.Assign);
        }

        /// <summary>
        ///     Determines whether this instance is comma.
        /// </summary>
        /// <returns><c>true</c> if this instance is comma; otherwise, <c>false</c>.</returns>
        public bool IsComma()
        {
            return Matches(",");
        }

        /// <summary>
        ///     Determines whether this instance is dot.
        /// </summary>
        /// <returns><c>true</c> if this instance is dot; otherwise, <c>false</c>.</returns>
        public bool IsDot()
        {
            return Matches(".");
        }

        /// <summary>
        ///     Determines whether [is object start].
        /// </summary>
        /// <returns><c>true</c> if [is object start]; otherwise, <c>false</c>.</returns>
        public bool IsObjectStart()
        {
            return Matches("{");
        }

        /// <summary>
        ///     Determines whether [is end of object].
        /// </summary>
        /// <returns><c>true</c> if [is end of object]; otherwise, <c>false</c>.</returns>
        public bool IsEndOfObject()
        {
            return Matches("}");
        }

        /// <summary>
        ///     Determines whether this instance is assignment.
        /// </summary>
        /// <returns><c>true</c> if this instance is assignment; otherwise, <c>false</c>.</returns>
        public bool IsAssignment()
        {
            return Matches("=", ":");
        }

        /// <summary>
        ///     Determines whether [is start of quoted text].
        /// </summary>
        /// <returns><c>true</c> if [is start of quoted text]; otherwise, <c>false</c>.</returns>
        public bool IsStartOfQuotedText()
        {
            return Matches("\"");
        }

        /// <summary>
        ///     Determines whether [is start of triple quoted text].
        /// </summary>
        /// <returns><c>true</c> if [is start of triple quoted text]; otherwise, <c>false</c>.</returns>
        public bool IsStartOfTripleQuotedText()
        {
            return Matches("\"\"\"");
        }

        /// <summary>
        ///     Pulls the comment.
        /// </summary>
        /// <returns>Token.</returns>
        public Token PullComment()
        {
            PullRestOfLine();
            return new Token(TokenType.Comment);
        }

        /// <summary>
        ///     Pulls the unquoted key.
        /// </summary>
        /// <returns>Token.</returns>
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
        ///     Determines whether [is unquoted key].
        /// </summary>
        /// <returns><c>true</c> if [is unquoted key]; otherwise, <c>false</c>.</returns>
        public bool IsUnquotedKey()
        {
            return (!EoF && !IsStartOfComment() && !NotInUnquotedKey.Contains(Peek()));
        }

        /// <summary>
        ///     Determines whether [is unquoted key start].
        /// </summary>
        /// <returns><c>true</c> if [is unquoted key start]; otherwise, <c>false</c>.</returns>
        public bool IsUnquotedKeyStart()
        {
            return (!EoF && !IsWhitespace() && !IsStartOfComment() && !NotInUnquotedKey.Contains(Peek()));
        }

        /// <summary>
        ///     Determines whether this instance is whitespace.
        /// </summary>
        /// <returns><c>true</c> if this instance is whitespace; otherwise, <c>false</c>.</returns>
        private bool IsWhitespace()
        {
            return char.IsWhiteSpace(Peek());
        }

        /// <summary>
        ///     Pulls the triple quoted text.
        /// </summary>
        /// <returns>Token.</returns>
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
        ///     Pulls the quoted text.
        /// </summary>
        /// <returns>Token.</returns>
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
        ///     Pulls the quoted key.
        /// </summary>
        /// <returns>Token.</returns>
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

        /// <summary>
        ///     Pulls the escape sequence.
        /// </summary>
        /// <returns>System.String.</returns>
        /// <exception cref="System.NotSupportedException"></exception>
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

        /// <summary>
        ///     Determines whether [is start of comment].
        /// </summary>
        /// <returns><c>true</c> if [is start of comment]; otherwise, <c>false</c>.</returns>
        private bool IsStartOfComment()
        {
            return (Matches("#", "//"));
        }

        /// <summary>
        ///     Pulls the value.
        /// </summary>
        /// <returns>Token.</returns>
        /// <exception cref="System.Exception">
        ///     Expected value: Null literal, Array, Number, Boolean, Quoted Text, Unquoted Text,
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
                "Expected value: Null literal, Array, Number, Boolean, Quoted Text, Unquoted Text, Triple quoted Text, Object or End of array");
        }

        /// <summary>
        ///     Determines whether [is substitution start].
        /// </summary>
        /// <returns><c>true</c> if [is substitution start]; otherwise, <c>false</c>.</returns>
        public bool IsSubstitutionStart()
        {
            return Matches("${");
        }

        /// <summary>
        ///     Pulls the substitution.
        /// </summary>
        /// <returns>Token.</returns>
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

        //public Token PullNextTrailingValue()
        //{
        //    PullSpaceOrTab();

        //}

        /// <summary>
        ///     Determines whether [is space or tab].
        /// </summary>
        /// <returns><c>true</c> if [is space or tab]; otherwise, <c>false</c>.</returns>
        public bool IsSpaceOrTab()
        {
            return Matches(" ", "\t");
        }

        //private bool IsStartNumber()
        //{
        //    return Matches("-", "+") || char.IsDigit(Peek());
        // }

        /// <summary>
        ///     Determines whether [is start simple value].
        /// </summary>
        /// <returns><c>true</c> if [is start simple value]; otherwise, <c>false</c>.</returns>
        public bool IsStartSimpleValue()
        {
            if (IsSpaceOrTab())
                return true;

            if (IsUnquotedText())
                return true;

            return false;
        }

        /// <summary>
        ///     Pulls the space or tab.
        /// </summary>
        /// <returns>Token.</returns>
        public Token PullSpaceOrTab()
        {
            var sb = new StringBuilder();
            while (IsSpaceOrTab())
            {
                sb.Append(Take());
            }
            return Token.LiteralValue(sb.ToString());
        }

        /// <summary>
        ///     Pulls the unquoted text.
        /// </summary>
        /// <returns>Token.</returns>
        private Token PullUnquotedText()
        {
            var sb = new StringBuilder();
            while (!EoF && IsUnquotedText())
            {
                sb.Append(Take());
            }

            return Token.LiteralValue(sb.ToString());
        }

        /// <summary>
        ///     Determines whether [is unquoted text].
        /// </summary>
        /// <returns><c>true</c> if [is unquoted text]; otherwise, <c>false</c>.</returns>
        private bool IsUnquotedText()
        {
            return (!EoF && !IsWhitespace() && !IsStartOfComment() && !NotInUnquotedText.Contains(Peek()));
        }

        /// <summary>
        ///     Pulls the simple value.
        /// </summary>
        /// <returns>Token.</returns>
        /// <exception cref="System.Exception">No simple value found</exception>
        public Token PullSimpleValue()
        {
            if (IsSpaceOrTab())
                return PullSpaceOrTab();
            if (IsUnquotedText())
                return PullUnquotedText();

            throw new Exception("No simple value found");
        }

        /// <summary>
        ///     Determines whether this instance is value.
        /// </summary>
        /// <returns><c>true</c> if this instance is value; otherwise, <c>false</c>.</returns>
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

