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
            get { return index >= text.Length - 1; }
        }

        public bool Matches(string pattern)
        {
            if (pattern.Length + index >= text.Length)
                return false;

            if (text.Substring(index, pattern.Length) == pattern)
                return true;

            return false;
        }

        public string Take(int length)
        {
            if (index + length >= text.Length)
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
        private const string notInUnquotedKey = "$\"{}[]:=,+#`^?!@*&\\.";
        private const string notInUnquotedText = "$\"{}[]:=,+#`^?!@*&\\";

        public HoconTokenizer(string text)
            : base(text)
        {
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

        public string PullToEndOfLine()
        {
            PullWhitespace();
            return PullRestOfLine();
        }

        public Token PullNext()
        {
            PullWhitespace();
            if (IsDot())
            {
                return PullDot();
            }
            if (IsStartOfObject())
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
            if (IsStartOfComment())
            {
                return PullComment();
            }
            if (IsUnquotedKey())
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

        private Token PullArrayEnd()
        {
            Take();
            return new Token(TokenType.ArrayEnd);
        }

        private bool IsArrayEnd()
        {
            return Matches("]");
        }

        private bool IsArrayStart()
        {
            return Matches("[");
        }

        private Token PullArrayStart()
        {
            Take();
            return new Token(TokenType.ArrayStart);
        }

        public Token PullDot()
        {
            Take();
            return new Token(TokenType.Dot);
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

        public bool IsDot()
        {
            return Peek() == '.';
        }

        public bool IsStartOfObject()
        {
            return Peek() == '{';
        }

        public bool IsEndOfObject()
        {
            return Peek() == '}';
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

            return Token.Key((sb.ToString()));
        }

        public bool IsUnquotedKey()
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
            while (!EoF && Peek() != '"')
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
            while (!EoF && Peek() != '"')
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

        public Token PullNextValue()
        {
            PullWhitespace();

            if (IsStartOfObject())
            {
                return PullStartOfObject();
            }
            if (IsStartNumber())
            {
                return PullNumber();
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
            throw new Exception("unknown token");
        }

        private Token PullNumber()
        {
            var sb = new StringBuilder();
            sb.Append(Take());
            bool isDecimal = false;
            while (!EoF && (char.IsDigit(Peek()) || Matches(".", "e", "E")))
            {
                if (Matches(".", "e", "E"))
                    isDecimal = true;

                sb.Append(Take());
            }

            if (isDecimal)
                return Token.LiteralValue(double.Parse(sb.ToString(), NumberFormatInfo.InvariantInfo));
            return Token.LiteralValue(long.Parse(sb.ToString(), NumberFormatInfo.InvariantInfo));
        }

        private bool IsSpaceOrTab()
        {
            return Matches(" ", "\t");
        }

        private bool IsStartNumber()
        {
            return Matches("-", "+") || char.IsDigit(Peek());
        }

        private Token PullSingleLineWhitespace()
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

                if (sb.Length == 4)
                {
                    string text = sb.ToString();
                    
                    if (text == "true")
                        return new Token(true);
                    if (text == "null")
                        return new Token(null);
                }
                else if (sb.Length == 5)
                {
                    string text = sb.ToString();
                    if (text == "false")
                        return new Token(false);
                }
            }

            return Token.LiteralValue(sb.ToString());
        }

        private bool IsUnquotedText()
        {
            return (!EoF && !IsWhitespace() && !IsStartOfComment() && !notInUnquotedText.Contains(Peek()));
        }
    }
}