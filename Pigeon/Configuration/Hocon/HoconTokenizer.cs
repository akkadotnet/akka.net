using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Pigeon.Configuration.Hocon
{
    public class Tokenizer
    {
        private readonly string text;
        private int index;

        public Tokenizer(string text)
        {
            this.text = text;
        }

        public bool EoF
        {
            get { return index >= text.Length; }
        }

        public bool Matches(string pattern)
        {
            if (pattern.Length + index > text.Length)
                return false;

            if (text.Substring(index, pattern.Length) == pattern)
                return true;

            return false;
        }

        public string Take(int length)
        {
            if (index + length > text.Length)
                return null;

            string s = text.Substring(index, length);
            index += length;
            return s;
        }

        public bool Matches(params string[] patterns)
        {
            foreach (string pattern in patterns)
            {
                if (pattern.Length + index >= text.Length)
                    continue;

                if (text.Substring(index, pattern.Length) == pattern)
                    return true;
            }
            return false;
        }

        public char Peek()
        {
            if (EoF)
                return (char) 0;

            return text[index];
        }

        public char Take()
        {
            if (EoF)
                return (char) 0;

            return text[index++];
        }

        public void PullWhitespace()
        {
            while (!EoF && char.IsWhiteSpace(Peek()))
            {
                Take();
            }
        }
    }

    

    public class HoconTokenizer : Tokenizer
    {
        private const string notInUnquotedKey = "$\"{}[]:=,#`^?!@*&\\.";
        private const string notInUnquotedText = "$\"{}[]:=,#`^?!@*&\\";

        public HoconTokenizer(string text)
            : base(text)
        {
        }

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

        public Token PullArrayEnd()
        {
            Take();
            return new Token(TokenType.ArrayEnd);
        }

        public bool IsArrayEnd()
        {
            return Matches("]");
        }

        public bool IsArrayStart()
        {
            return Matches("[");
        }

        public Token PullArrayStart()
        {
            Take();
            return new Token(TokenType.ArrayStart);
        }

        public Token PullDot()
        {
            Take();
            return new Token(TokenType.Dot);
        }

        public Token PullComma()
        {
            Take();
            return new Token(TokenType.Comma);
        }

        public Token PullStartOfObject()
        {
            Take();
            return new Token(TokenType.ObjectStart);
        }

        public Token PullEndOfObject()
        {
            Take();
            return new Token(TokenType.ObjectEnd);
        }

        public Token PullAssignment()
        {
            Take();
            return new Token(TokenType.Assign);
        }

        public bool IsComma()
        {
            return Matches(",");
        }

        public bool IsDot()
        {
            return Matches(".");
        }

        public bool IsObjectStart()
        {
            return Matches("{");
        }

        public bool IsEndOfObject()
        {
            return Matches("}");
        }

        public bool IsAssignment()
        {
            return Matches("=", ":");
        }

        public bool IsStartOfQuotedText()
        {
            return Matches("\"");
        }

        public bool IsStartOfTrippleQuotedText()
        {
            return Matches("\"\"\"");
        }

        public Token PullComment()
        {
            PullRestOfLine();
            return new Token(TokenType.Comment);
        }

        public Token PullUnquotedKey()
        {
            var sb = new StringBuilder();
            while (!EoF && IsUnquotedKey())
            {
                sb.Append(Take());
            }

            return Token.Key((sb.ToString().Trim()));
        }

        public bool IsUnquotedKey()
        {
            return (!EoF && !IsStartOfComment() && !notInUnquotedKey.Contains(Peek()));
        }

        public bool IsUnquotedKeyStart()
        {
            return (!EoF && !IsWhitespace() && !IsStartOfComment() && !notInUnquotedKey.Contains(Peek()));
        }

        private bool IsWhitespace()
        {
            return char.IsWhiteSpace(Peek());
        }

        public Token PullTrippleQuotedText()
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

        public Token PullValue()
        {
            if (IsObjectStart())
            {
                return PullStartOfObject();
            }
  
            if (IsStartOfTrippleQuotedText())
            {
                return PullTrippleQuotedText();
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

            throw new Exception("Expected value: Null literal, Array, Number, Boolean, Quoted Text, Unquoted Text, Tripple quoted Text, Object or End of array");
        }

        public bool IsSubstitutionStart()
        {
            return Matches("${");
        }

        public Token PullSubstitution()
        {
            var sb = new StringBuilder();
            Take(2);
            while(!EoF && IsUnquotedText())
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

        public bool IsSpaceOrTab()
        {
            return Matches(" ", "\t");
        }

        //private bool IsStartNumber()
        //{
        //    return Matches("-", "+") || char.IsDigit(Peek());
       // }

        public bool IsStartSimpleValue()
        {
            if (IsSpaceOrTab())
                return true;

            if (IsUnquotedText())
                return true;

            return false;
        }

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
            return (!EoF && !IsWhitespace() && !IsStartOfComment() && !notInUnquotedText.Contains(Peek()));
        }

        public Token PullSimpleValue()
        {
            if (IsSpaceOrTab())
                return PullSpaceOrTab();
            if (IsUnquotedText())
                return PullUnquotedText();

            throw new Exception("No simple value found");
        }

        internal bool IsValue()
        {
            if (IsArrayStart())
                return true;
            if (IsObjectStart())
                return true;
            if (IsStartOfTrippleQuotedText())
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