using System.Collections.Generic;
using System.Linq;

namespace Pigeon.Configuration.Hocon
{
    public class Parser
    {
        public static HoconObject Parse(string text)
        {
            return new Parser().ParseText(text);
        }

        private HoconObject ParseText(string text)
        {
            var root = new HoconObject();
            var reader = new HoconTokenizer(text);
            ParseObject(reader, root);
            return root;
        }

        private void ParseObject(HoconTokenizer reader, HoconObject context)
        {
            HoconObject self = null;

            while (!reader.EoF)
            {
                Token t = reader.PullNext();
                switch (t.Type)
                {
                    case TokenType.EoF:
                        break;
                    case TokenType.Key:
                        self = context.CreateChild(t.Value.ToString());
                        break;
                    case TokenType.Dot:
                        ParseObject(reader, self);
                        return; //always return after parsed dot to revert to root
                    case TokenType.Assign:
                        ParseValue(reader, self);
                        break;
                    case TokenType.ObjectStart:
                        ParseObject(reader, self);
                        break;
                    case TokenType.ObjectEnd:
                        return;
                }
            }
        }

        public void ParseValue(HoconTokenizer reader, HoconObject context)
        {
            while (!reader.EoF)
            {
                Token t = reader.PullNextValue();
                switch (t.Type)
                {
                    case TokenType.EoF:
                        break;
                    case TokenType.LiteralValue:
                        context.Value = t.Value;
                        return;
                    case TokenType.ObjectStart:
                        ParseObject(reader, context);
                        return;
                    case TokenType.ArrayStart:
                        var arr = ParseArray(reader, context);
                        context.Value = arr;
                        return;
                }
            }
        }

        public HoconArray ParseArray(HoconTokenizer reader, HoconObject context)
        {
            var arr = new HoconArray();
            while (!reader.EoF)
            {
                Token t = reader.PullNextValue();
                switch (t.Type)
                {
                    case TokenType.EoF:
                        break;
                    case TokenType.LiteralValue:
                        arr.Add(t.Value);
                        break;
                    case TokenType.ObjectStart:
                        var c = new HoconObject();
                        ParseObject(reader, c);
                        arr.Add(c);
                        break;
                    case TokenType.ArrayStart:
                        var c2 = new HoconObject();
                        var childArr = ParseArray(reader, c2);
                        arr.Add(childArr);
                        break;
                    case TokenType.ArrayEnd:
                        if (!arr.Any() || !arr.All(e => e is HoconArray)) 
                            return arr;

                        //concat arrays in arrays
                        var x = (from a in arr.OfType<HoconArray>()
                            from e in a 
                            select e).ToArray();
                        arr.Clear();
                        arr.AddRange(x);
                        return arr;
                }
            }
            return arr;
        }
    }
}