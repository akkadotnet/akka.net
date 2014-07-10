using SymbolLookup.YahooFinance;

namespace SymbolLookup.Actors.Messages
{
    public class CompletedStockQuoteDownload
    {
        public string Symbol { get; set; }

        public Quote Quote { get; set; } 
    }
}
