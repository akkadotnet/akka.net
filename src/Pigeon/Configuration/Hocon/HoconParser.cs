using System;
using System.Collections.Generic;
using System.Linq;

namespace Pigeon.Configuration.Hocon
{
    public class Parser
    {
        public static HoconValue Parse(string text)
        {
            return new Parser().ParseText(text);
        }

        private HoconTokenizer reader;
        private HoconValue root;
        private List<HoconSubstitution> substitutions = new List<HoconSubstitution>();
        private HoconValue ParseText(string text)
        {
            root = new HoconValue();
            reader = new HoconTokenizer(text);
            reader.PullWhitespaceAndComments();
            ParseObject( root,true);

            Config c = new Config(root);
            foreach(var sub in substitutions)
            {
                var res = c.GetValue(sub.Path);
                if (res == null)
                    throw new Exception("Unresolved substitution:" + sub.Path);
                sub.ResolvedValue = res;

            }
            return root;
        }

        private void ParseObject( HoconValue owner,bool root)
        {
            if (owner.IsObject())
            {
                //the value of this KVP is already an object
            }
            else
            {      
                //the value of this KVP is not an object, thus, we should add a new
                owner.NewValue(new HoconObject()); 
            }

            var currentObject = owner.GetObject();

            while (!reader.EoF)
            {
                Token t = reader.PullNext();
                switch (t.Type)
                {
                    case TokenType.EoF:
                        break;
                    case TokenType.Key:
                        var value = currentObject.GetOrCreateKey(t.Value.ToString());
                        ParseKeyContent( value);
                        if (!root)
                            return;
                        break;
                    
                    case TokenType.ObjectEnd:                        
                        return;
                }
            }
        }

        private void ParseKeyContent(HoconValue value)
        {
            while (!reader.EoF)
            {
                Token t = reader.PullNext();
                switch (t.Type)
                {
                    case TokenType.Dot:
                        ParseObject(value,false);
                        return; 
                    case TokenType.Assign:
                        ParseValue(value);
                        return;
                    case TokenType.ObjectStart:
                        ParseObject( value,true);
                        return;
                }
            }            
        }

        public void ParseValue( HoconValue owner)
        {
            if (reader.EoF)
                throw new Exception("End of file reached while trying to read a value");

            bool isObject = owner.IsObject();
            reader.PullWhitespaceAndComments();
            while (reader.IsValue())
            {
                Token t = reader.PullValue();

                switch (t.Type)
                {
                    case TokenType.EoF:
                        break;
                    case TokenType.LiteralValue:
                        if (isObject)
                        {
                            //needed to allow for override objects
                            isObject = false;
                            owner.Clear();
                        }
                        var lit = new HoconLiteral()
                        {
                            Value = t.Value
                        };
                        owner.AppendValue(lit);
                        
                        break;
                    case TokenType.ObjectStart:
                        ParseObject(owner, true);
                        break;
                    case TokenType.ArrayStart:
                        var arr = ParseArray();
                        owner.AppendValue(arr);
                        break;
                    case TokenType.Substitute:
                        var sub = ParseSubstitution((string)t.Value);
                        substitutions.Add(sub);
                        owner.AppendValue(sub);
                        break;
                }
                if (reader.IsSpaceOrTab())
                {
                    ParseTrailingWhitespace(owner);
                }
            }
            
            IgnoreComma();
        }

        private void ParseTrailingWhitespace(HoconValue owner)
        {
            var ws = reader.PullSpaceOrTab();
            //single line ws should be included if string concat
            if (((string)ws.Value).Length > 0)
            {
                var wsLit = new HoconLiteral
                {
                    Value = ws.Value,
                };
                owner.AppendValue(wsLit);
            }
        }

        private static HoconSubstitution ParseSubstitution(string value )
        {
            return new HoconSubstitution(value);
        }

        public HoconArray ParseArray()
        {
            var arr = new HoconArray();
            while (!reader.EoF && !reader.IsArrayEnd())
            {
                var v = new HoconValue();
                ParseValue(v);
                arr.Add(v);
                reader.PullWhitespaceAndComments();
            }
            reader.PullArrayEnd();
            return arr;
        }   

        private void IgnoreComma()
        {
            if (reader.IsComma()) //optional end of value
            {
                reader.PullComma();
            }
        }
    }
}