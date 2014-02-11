using System;

namespace SymbolLookup.YahooFinance
{
    public static class StockUriHelper
    {
        public static readonly string YahooFinanceStockQuoteBase = @"http://query.yahooapis.com/v1/public/yql?q=" +
                                        "select%20*%20from%20yahoo.finance.quotes%20where%20symbol%20in%20('{0}')" +
                                        "&env=store://datatables.org/alltableswithkeys&format=json";

        public static readonly string YahooFinanceHeadlinesRssBase = @"http://finance.yahoo.com/rss/headline?s={0}";
        public static Uri CreateHeadlinesRssUri(string tickerSymbol)
        {
            return new Uri(string.Format(YahooFinanceHeadlinesRssBase, tickerSymbol.ToLowerInvariant()));
        }

        public static Uri CreateStockQuoteUri(string tickerSymbol)
        {
            return new Uri(string.Format(YahooFinanceStockQuoteBase, tickerSymbol.ToLowerInvariant()));
        }
    }
}
