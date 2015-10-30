
namespace Akka.Logger.Serilog.SemanticAdapter
{
    internal class FormatPart
    {
        public FormatPart(string text, int index)
        {
            Text = text;
            Index = index;
        }

        public string Text { get;private set; }

        public int Index { get;private set; }
    }
}