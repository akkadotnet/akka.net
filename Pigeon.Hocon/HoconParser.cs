using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace Pigeon.Hocon
{
    public static class Parser
    {
        public static Node Parse(string data)
        {
            var nodes = new Stack<Node>();
            var roots = new Stack<Node>();
            var node = new Node();
            Node root = node;
            int i = 0;


            nodes.Push(node);
            roots.Push(node);
            while (i < data.Length)
            {
                Token t = ConsumeNext(data, ref i);
                if (t.Type == TokenType.EoF)
                    break;

                if (t.Type == TokenType.Identifier)
                {
                    Node newNode = node.CreateChild(t.Value);
                    node = newNode;
                    nodes.Push(node);
                }
                else if (t.Type == TokenType.Dot)
                {
                    Token id = ConsumeNext(data, ref i);
                    Node newNode = node.CreateChild(id.Value);
                    node = newNode;
                    nodes.Push(node);
                }
                else if (t.Type == TokenType.Eq)
                {
                    string v = ConsumeLine(data, ref i);
                    node.Value = v;
                    while (nodes.Peek() != roots.Peek())
                    {
                        nodes.Pop();
                    }
                    node = nodes.Peek();
                }
                else if (t.Type == TokenType.LBrace)
                {
                    roots.Push(node);
                }
                else if (t.Type == TokenType.RBrace)
                {
                    node = roots.Pop();
                }
            }

            return root;
        }

        private static string ConsumeLine(string data, ref int i)
        {
            ConsumeWhitespace(data, ref i);
            var sb = new StringBuilder();
            while (i < data.Length)
            {
                char c = data[i];
                if (c == '\n')
                    break;
                if (c != '\r')
                    sb.Append(c);

                i++;
            }
            return sb.ToString().Trim();
        }

        private static Token ConsumeNext(string data, ref int i)
        {
            ConsumeWhitespace(data, ref i);
            switch (data[i])
            {
                case '.':
                    i++;
                    return new Token(TokenType.Dot);
                case '{':
                    i++;
                    return new Token(TokenType.LBrace);
                case '}':
                    i++;
                    return new Token(TokenType.RBrace);
                case '=':
                case ':':
                    i++;
                    return new Token(TokenType.Eq);
                case '#':
                    return ConsumeComment(data, ref i);
                case '/':
                    if (Matches("//",data,ref i))
                        return ConsumeComment(data, ref i);
                    goto default;
                case '"':
                    return ConsumeString(data, ref i);
                default:
                    if (IsIdentifierChar(data[i]))
                        return ConsumeIdentifier(data, ref i);
                    if (i == data.Length - 1)
                        return new Token(TokenType.EoF);
                    throw new Exception("unknown token");
            }
        }

        private static bool Matches(string pattern, string data, ref int i)
        {
            if (pattern.Length + i >= data.Length)
                return false;

            return (data.Substring(i, pattern.Length) == pattern);
        }        

        private static Token ConsumeComment(string data, ref int i)
        {
            ConsumeLine(data, ref i);
            return new Token(TokenType.Comment);
        }

        private static Token ConsumeIdentifier(string data, ref int i)
        {
            int start = i;
            while (IsIdentifierChar(data[i]) && i < data.Length)
            {
                i++;
            }
            return new Token(data.Substring(start, i - start));
        }

        private static bool IsIdentifierChar(char c)
        {
            return (char.IsLetterOrDigit(c) || c == '-' || c == '/');
        }

        private static Token ConsumeString(string data, ref int i)
        {
            var sb = new StringBuilder();
            i++;
            while ((data[i] != '"') && i < data.Length)
            {
                i++;
                if (data[i]=='\\')
                {
                    i++;                    
                }
                sb.Append(data[i]);
            }
            i++;

            return new Token(sb.ToString());
        }

        private static void ConsumeWhitespace(string data, ref int i)
        {
            while (i < data.Length - 1 && char.IsWhiteSpace(data[i]))
            {
                i++;
            }
            if (i < data.Length - 1 && data[i] == '#')
            {
                ConsumeComment(data, ref i);
            }
            if (i < data.Length && data[i] == '#' || char.IsWhiteSpace('#'))
                ConsumeWhitespace(data, ref i);
        }
    }

    public enum TokenType
    {
        Comment = 1,
        Identifier = 2,
        Eq = 61,
        LBrace = 123,
        RBrace = 125,
        Dot = 46,
        EoF,
    }

    public class Token
    {
        public Token(TokenType type)
        {
            Type = type;
        }

        public Token(string value)
        {
            Type = TokenType.Identifier;
            Value = value;
        }

        public string Value { get; set; }
        public TokenType Type { get; set; }
    }

    public class Node
    {
        private readonly Dictionary<string, Node> _children = new Dictionary<string, Node>();

        public Node()
        {
            Id = "";
        }

        public string Id { get; set; }
        public string Value { get; set; }

        public IEnumerable<Node> Children
        {
            get { return _children.Values; }
        }

        public Node CreateChild(string id)
        {
            if (_children.ContainsKey(id))
            {
                return _children[id];
            }
            var child = new Node
            {
                Id = id,
            };
            _children.Add(id, child);
            return child;
        }

        public override string ToString()
        {
            return ToString(0);
        }

        public string ToString(int indent)
        {
            var t = new string(' ', indent * 2);
            var sb = new StringBuilder();

            if (Value != null)
            {
                sb.AppendFormat("{0}{1} = {2}", t, Id, Value);
                return sb.ToString();
            }
            sb.AppendLine(t + Id + " {");
            foreach (Node child in Children)
            {
                sb.AppendLine(child.ToString(indent + 1));
            }
            sb.Append(t + "}");
            return sb.ToString();
        }

        public string ToFlatString()
        {
            var sb = new StringBuilder();

            if (Value != null)
            {
                sb.AppendFormat("{0} = {1}", Id, Value);
                return sb.ToString();
            }
            sb.Append( Id + " {");

            sb.Append(string.Join(",", Children.Select(c => c.ToFlatString())));
            sb.Append( "}");
            return sb.ToString();
        }
    }
}