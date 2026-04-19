using NUnit.Framework;
using StockTracker.Services;
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace StockTracker.UnitTest
{
    [TestFixture]
    public class TwseT86UnitTest
    {
        [Test]
        public async Task TestParseCsv()
        {
            var csv = "\"證券代號\",\"證券名稱\",\"外陸資買進股數(不含外資自營商)\",\"外陸資賣出股數(不含外資自營商)\",\"外陸資買賣超股數(不含外資自營商)\",\"外資自營商買進股數\",\"外資自營商賣出股數\",\"外資自營商買賣超股數\",\"投信買進股數\",\"投信賣出股數\",\"投信買賣超股數\",\"自營商買賣超股數\",\"自營商買進股數(自行買賣)\",\"自營商賣出股數(自行買賣)\",\"自營商買賣超股數(自行買賣)\",\"自營商買進股數(避險)\",\"自營商賣出股數(避險)\",\"自營商買賣超股數(避險)\",\"三大法人買賣超股數\"\n\"2330\",\"台積電\",\"1,000\",\"800\",\"200\",\"0\",\"0\",\"0\",\"300\",\"100\",\"200\",\"-50\",\"0\",\"0\",\"0\",\"0\",\"0\",\"0\",\"350\"";

            var handler = new StubHttpMessageHandler(csv);
            using (var httpClient = new HttpClient(handler))
            {
                var client = new TwseT86CsvClient(httpClient);
                var tradeDate = new DateTime(2024, 6, 5);

                var result = await client.DownloadAndParseAsync(tradeDate);

                Assert.That(result, Is.Not.Null);
                Assert.That(result.Count, Is.EqualTo(1));
                Assert.That(result[0].TradeDate, Is.EqualTo(tradeDate));
                Assert.That(result[0].Symbol, Is.EqualTo("2330"));
                Assert.That(result[0].Name, Is.EqualTo("台積電"));
                Assert.That(result[0].ForeignNet, Is.EqualTo(200));
                Assert.That(result[0].InvestmentTrustNet, Is.EqualTo(200));
                Assert.That(result[0].DealerNet, Is.EqualTo(-50));
                Assert.That(result[0].ThreeMajorNet, Is.EqualTo(350));
            }
        }

        private sealed class StubHttpMessageHandler : HttpMessageHandler
        {
            private readonly string _responseContent;

            public StubHttpMessageHandler(string responseContent)
            {
                _responseContent = responseContent;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_responseContent)
                };

                return Task.FromResult(response);
            }
        }
    }
}
