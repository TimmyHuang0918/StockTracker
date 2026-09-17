using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StockTracker;
using StockTracker.Models;
using StockTracker.Services;
using StockTracker.ViewModels;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

public static class MarketOverviewChecks
{
    private static int passed;
    private static void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
            ParserAndCalculationTests();
            var fixture = Fixture();
            RenderDesktop(fixture, args[0], "local-overview-fixture.png");
            if (args.Contains("--refresh-site"))
            {
                var live = RefreshSiteAsync(args[0]).GetAwaiter().GetResult();
                RenderDesktop(live, args[0], "local-overview-live.png");
            }
            Console.WriteLine("PASS total " + passed);
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    private static void ParserAndCalculationTests()
    {
        var month = new DateTime(2026, 9, 1);
        var twse = JObject.Parse(@"{'stat':'OK','fields':['日期','成交股數','成交金額','成交筆數','發行量加權股價指數','漲跌點數'],'data':[['115/09/01','13,000,849,196','1,187,571,567,117','5,301,801','46,948.72','820.25']]}");
        var tpex = JObject.Parse(@"{'stat':'ok','tables':[{'title':'日成交量值指數','fields':['日期','成交張數','金額（仟元）','筆數','櫃買指數','漲/跌'],'data':[['115/09/16','777,380','183,676,772','812,128',399.21,10.48]]}]}");
        var listed = MarketTradingOverviewService.ParseMonth(twse, "TWSE", month).Single();
        var otc = MarketTradingOverviewService.ParseMonth(tpex, "TPEX", month).Single();
        Check(listed.TradeDate == month && otc.TradeDate == new DateTime(2026,9,16), "ROC report dates");
        Check(listed.VolumeShares == 13000849196m && listed.TurnoverNtd == 1187571567117m, "TWSE shares and NTD unchanged");
        Check(otc.VolumeShares == 777380000m && otc.TurnoverNtd == 183676772000m, "TPEx lots and thousand NTD normalized");
        Check(otc.VolumeText == "77.74 萬張" && otc.AmountText == "1,836.8 億", "display units");
        Check(Math.Abs(otc.IndexChangePercent.Value - 10.48m / 388.73m * 100m) < 0.00001m, "index percent uses previous close");
        Check(MarketTradingOverviewService.ParseMonth(twse, "TWSE", month.AddMonths(-1)).Count == 0, "reject a different returned month");
        twse["fields"][2] = "未知單位";
        Check(MarketTradingOverviewService.ParseMonth(twse, "TWSE", month).Count == 0, "reject unsupported units");
        twse["fields"][2] = "成交金額"; twse["data"][0][1] = "--";
        Check(MarketTradingOverviewService.ParseMonth(twse, "TWSE", month).Count == 0, "reject incomplete numeric rows");
        var records = new List<MarketTradingDay>();
        for (var date = new DateTime(2026, 8, 3); records.Count < 26; date = date.AddDays(1))
            if (date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday)
                records.Add(new MarketTradingDay { TradeDate = date, IndexClose = 101, IndexChange = 1, TurnoverNtd = (records.Count + 1) * 100, VolumeShares = 10000000 });
        var lastDate = records.Last().TradeDate;
        var days = MarketTradingOverviewService.CalculateRecent(records, lastDate.AddHours(22));
        Check(days.Count == 5 && days.First().TradeDate == lastDate, "five actual days newest first");
        Check(days[0].TurnoverRatio20 == 2600m / 1550m, "20-session average excludes today");
        Check(days[0].TurnoverChangePercent == 4m, "prior-session value comparison");
        var monday = days.First(d => d.TradeDate.DayOfWeek == DayOfWeek.Monday);
        Check(monday.TurnoverChangePercent.HasValue, "weekend uses previous actual session");
        var provisional = MarketTradingOverviewService.CalculateRecent(records, lastDate.AddHours(14)).First();
        Check(!provisional.IsComplete && !provisional.TurnoverRatio20.HasValue && !provisional.TurnoverChangePercent.HasValue, "incomplete day no full-day comparison");
        Check(provisional.SessionText.Contains("更新中"), "provisional timestamp label");
        var intraday = MarketTradingOverviewService.CalculateRecent(records, lastDate.AddHours(11)).First();
        Check(intraday.SessionText.Contains("盤中累計"), "intraday cumulative label");
        Check(MarketTradingOverviewService.CalculateRecent(records, lastDate.AddHours(18)).First().IsComplete, "18:00 daily completeness boundary");
        Check(MarketTradingOverviewService.CalculateRecent(records, lastDate.AddDays(1).AddMinutes(1)).First().TradeDate == lastDate, "after midnight retain actual report date");
        Check(!MarketTradingOverviewService.CalculateRecent(records, lastDate.AddHours(22), new HashSet<DateTime> { new DateTime(2026,8,1) }).First().TurnoverRatio20.HasValue, "failed history month suppresses baseline");
        Check(!MarketTradingOverviewService.CalculateRecent(records.Take(20), lastDate.AddHours(22)).First().TurnoverRatio20.HasValue, "insufficient 20 previous sessions");
        Check(MarketTradingOverviewService.CalculateRecent(Enumerable.Empty<MarketTradingDay>(), lastDate).Count == 0, "empty source safe");
        Check(new MarketOverviewDay().ForeignNetText == "—" && new MarketOverviewDay().MarginBalanceText == "—", "missing funding not zero");
        Check(new MarketOverviewDay { InstitutionalAvailable = true }.ForeignNetText == "0.0 億", "published zero retained");
        Check(new MarketOverviewDay { CreditAvailable = true }.ShortBalanceText == "0", "published credit zero retained");
        Check(new MarketOverviewDay { ForeignNet = 100 }.HasInstitutional, "legacy cache availability");
        var f = Fixture();
        var html = MarketOverviewHtmlRenderer.Render(f.MarketOverview, f.MarketBreadth);
        Check(Regex.Matches(html, "data-market-table='").Count == 2, "both market tables rendered");
        Check(html.Contains("（+1.0 億）") && html.Contains("（-100 張）"), "credit parentheses and signed changes retained");
        Check(html.Contains("—") && html.Contains("rise") && html.Contains("fall"), "missing values and sign colors");
        Check(!html.Contains("fetch(") && !html.Contains("setInterval("), "overview static no blocking fetch or polling");
        Check(!new MarketBreadthSnapshot { TotalCount = 2, UnchangedCount = 2 }.AdvanceRatioText.Contains("0.0%"), "all-flat breadth undefined ratio");
        var restored = JsonConvert.DeserializeObject<MarketOverviewSnapshot>(JsonConvert.SerializeObject(f.MarketOverview));
        Check(restored.Trading.Markets[0].Days.Count == 5 && restored.Days.First().HasInstitutional, "cache round trip with new fields");
    }
    public static OverviewFixture Fixture()
    {
        var days = Enumerable.Range(0, 5).Select(i => new MarketTradingDay {
            TradeDate = new DateTime(2026,9,16).AddDays(-i), IndexClose = 46000 - i * 100, IndexChange = i % 2 == 0 ? 100 : -100,
            TurnoverNtd = 900000000000m, VolumeShares = 10000000000m, TurnoverChangePercent = i % 2 == 0 ? 12.5m : -8m,
            TurnoverRatio20 = 1.12m, IsComplete = true, SessionText = "盤後資料" }).ToList();
        return new OverviewFixture {
            MarketOverview = new MarketOverviewSnapshot {
                TradeDate = days[0].TradeDate,
                Trading = new MarketTradingOverview { RetrievedAt = new DateTime(2026,9,16,22,0,0), Markets = new List<MarketTradingSeries> {
                    new MarketTradingSeries { Code="TWSE", Name="上市 · 加權指數", Scope="證交所市場成交資訊", SourceUrl="https://www.twse.com.tw/", Days=days, Status="盤後資料" },
                    new MarketTradingSeries { Code="TPEX", Name="上櫃 · 櫃買指數", Scope="上櫃股票成交資訊", SourceUrl="https://www.tpex.org.tw/", Days=days, Status="盤後資料" } } },
                Days = days.Select((d,i) => new MarketOverviewDay { TradeDate=d.TradeDate, InstitutionalAvailable=i!=2, CreditAvailable=i!=2, ForeignNet=10000000000m, TrustNet=-1000000000m, DealerNet=300000000m, ThreeMajorNet=9300000000m, MarginAmountThousand=500000000, MarginAmountChangeThousand=100000, ShortBalanceLots=210000, ShortBalanceChangeLots=-100, PutCallOpenInterestRatio=i==2?(decimal?)null:142.1m }).ToList()
            },
            MarketBreadth = new MarketBreadthSnapshot { DataDateText="掃描資料 2026/09/16", TotalCount=1000, AdvancingCount=600, DecliningCount=350, UnchangedCount=50, AverageChangePercent=0.74m }
        };
    }
    private static void RenderDesktop(OverviewFixture fixture, string repo, string filename)
    {
        if (Application.Current == null)
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources.Add("ValueSignBrushConverter", new StockTracker.Converters.ValueSignBrushConverter());
        }
        var errors = new StringWriter();
        var listener = new TextWriterTraceListener(errors);
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        var control = new MarketOverviewControl { DataContext = fixture, Margin = new Thickness(16) };
        var root = new Border { Width=920, Background = new SolidColorBrush(Color.FromRgb(26,29,35)), Child=control };
        root.Measure(new Size(920, double.PositiveInfinity));
        root.Arrange(new Rect(new Point(0,0), root.DesiredSize));
        root.UpdateLayout();
        control.Dispatcher.Invoke(DispatcherPriority.DataBind, new Action(() => {}));
        root.UpdateLayout();
        Check(Descendants(root).OfType<DataGrid>().Count() >= 2, "initial desktop quantity table visible");
        var bitmap = new RenderTargetBitmap(920, (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(repo, "artifacts", "market-overview", filename))) encoder.Save(file);
        foreach (var tabs in Descendants(root).OfType<TabControl>())
        {
            tabs.SelectedIndex = 1;
            tabs.UpdateLayout();
            tabs.Dispatcher.Invoke(DispatcherPriority.DataBind, new Action(() => {}));
        }
        listener.Flush(); PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
        Check(errors.ToString().Length == 0, "WPF layout and binding " + filename + ": " + errors);
        Check(Descendants(root).OfType<DataGrid>().Count() >= 2, "desktop quantity and funding grids created");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i=0; i<VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent,i); yield return child;
            foreach(var sub in Descendants(child)) yield return sub;
        }
    }
    private static async Task<OverviewFixture> RefreshSiteAsync(string repo)
    {
        // Read the running application's cache without starting the app, migrations, scans or broker connection.
        MarketOverviewSnapshot overview;
        var db = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StockTracker", "T86_History", "Ranking.db");
        using (var connection = new SQLiteConnection("Data Source=" + db + ";Version=3;Read Only=True;FailIfMissing=True;"))
        {
            connection.Open();
            using (var command = connection.CreateCommand())
            {
                command.CommandText="SELECT Payload FROM LatestMarketOverview WHERE Id=1";
                overview=JsonConvert.DeserializeObject<MarketOverviewSnapshot>((string)command.ExecuteScalar());
            }
        }
        var now = MarketTradingOverviewService.TaipeiNow;
        overview.Trading = await new MarketTradingOverviewService().GetRecentAsync(now).ConfigureAwait(false);
        Check(overview.Trading.Markets.Count == 2 && overview.Trading.Markets.All(m => m.Days.Count == 5), "live official five-day quantity reports");
        Check(overview.Trading.Markets.All(m => m.Days.Where(d => d.IsComplete).All(d => d.TurnoverRatio20.HasValue)), "live 20-session history available");
        var dates = overview.Trading.Markets.SelectMany(m=>m.Days).Select(d=>d.TradeDate).Distinct().OrderByDescending(d=>d).Take(5).ToList();
        var institutionsTask = new MarketInstitutionalAmountService().GetByDatesAsync(dates);
        var creditTask = new TwseMarketCreditService().GetByDatesAsync(dates);
        var putCallTask = new TaifexPutCallRatioService().GetRecentAsync(10);
        await Task.WhenAll(institutionsTask,creditTask,putCallTask).ConfigureAwait(false);
        var institutions = institutionsTask.Result;
        var credits = creditTask.Result;
        var pcrs = putCallTask.Result.ToDictionary(d=>d.TradeDate);
        var old = overview.Days.ToDictionary(d=>d.TradeDate);
        overview.Days = dates.Select(date => {
            MarketOverviewDay day;
            if(!old.TryGetValue(date,out day)) day = new MarketOverviewDay { TradeDate=date };
            if(institutions.ContainsKey(date)) {
                var i=institutions[date]; day.InstitutionalAvailable=true; day.ForeignNet=i.ForeignNet; day.TrustNet=i.TrustNet; day.DealerNet=i.DealerNet; day.ThreeMajorNet=i.ThreeMajorNet;
            }
            if(credits.ContainsKey(date)) {
                var c=credits[date]; day.CreditAvailable=true; day.MarginAmountThousand=c.MarginAmountThousand; day.MarginAmountChangeThousand=c.MarginAmountChangeThousand; day.ShortBalanceLots=c.ShortBalanceLots; day.ShortBalanceChangeLots=c.ShortBalanceChangeLots;
            }
            if(pcrs.ContainsKey(date)) day.PutCallOpenInterestRatio=pcrs[date].OpenInterestRatioPercent;
            return day;
        }).ToList();
        overview.TradeDate = dates.Max();
        var path = Path.Combine(repo,"docs","nightly-ranking","index.html");
        var page = File.ReadAllText(path,Encoding.UTF8);
        var rawMatch=Regex.Match(page,@"const rawData = (\[.*?\]);\r?\n");
        Check(rawMatch.Success, "existing website stock payload found");
        var stocks=JArray.Parse(rawMatch.Groups[1].Value);
        var breadth = new MarketBreadthSnapshot {
            TotalCount=stocks.Count,
            AdvancingCount=stocks.Count(s=>(decimal)s["chg"]>0),
            DecliningCount=stocks.Count(s=>(decimal)s["chg"]<0),
            UnchangedCount=stocks.Count(s=>(decimal)s["chg"]==0),
            AverageChangePercent=stocks.Count>0 ? stocks.Average(s=>(decimal)s["chg"]) : 0m,
            DataDateText=System.Net.WebUtility.HtmlDecode(Regex.Match(page,@"id=""summaryText"">(.*?)</div>").Groups[1].Value)
        };
        var rendered=MarketOverviewHtmlRenderer.Render(overview,breadth);
        int start, end;
        if(page.Contains("id='fullMarketOverview'")) {
            start=page.IndexOf("<section class='panel market-overview'");
            end=page.IndexOf("</script>",start)+"</script>".Length;
        } else {
            start=page.IndexOf("<section class='panel'><h3 style='margin:0 0 10px'>全市場資金總覽");
            end=page.IndexOf("<div class='filter-breadth-layout'>",start);
        }
        Check(start>=0 && end>start,"existing overview replacement boundary");
        page=page.Substring(0,start)+rendered+Environment.NewLine+page.Substring(end);
        var breadthStart=page.IndexOf("<section class='panel breadth-card'");
        if(breadthStart>=0) {
            var breadthEnd=page.IndexOf("</section>",breadthStart)+"</section>".Length;
            page=page.Remove(breadthStart,breadthEnd-breadthStart);
        }
        page=page.Replace("<div class='filter-breadth-layout'>","<div class='filter-breadth-layout' style='display:block'>");
        var groupStart=page.IndexOf("<section class='panel' style='margin:0 0 16px;'>");
        var groupEnd=page.IndexOf("</section>",groupStart)+"</section>".Length;
        Check(groupStart>=0 && groupEnd>groupStart,"sector section retained");
        var groups=page.Substring(groupStart,groupEnd-groupStart);
        page=page.Remove(groupStart,groupEnd-groupStart);
        var filterStart=page.IndexOf("<div class='filter-breadth-layout'");
        page=page.Insert(filterStart,groups+Environment.NewLine);
        Check(Regex.Match(page,@"const rawData = (\[.*?\]);\r?\n").Value == rawMatch.Value,"stock payload preserved byte-for-byte");
        File.WriteAllText(path,page,new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(repo,"artifacts","market-overview","live-overview.json"),JsonConvert.SerializeObject(overview,Formatting.Indented));
        foreach(var m in overview.Trading.Markets)
            Console.WriteLine(m.Name+" "+m.DateText+" / "+m.AmountText+" / "+m.VolumeText+" / prior20 "+m.AmountRatioText);
        return new OverviewFixture { MarketOverview=overview,MarketBreadth=breadth };
    }
}
public sealed class OverviewFixture
{
    public MarketOverviewSnapshot MarketOverview { get; set; }
    public MarketBreadthSnapshot MarketBreadth { get; set; }
    public string MarketSummary => MarketOverviewHtmlRenderer.Summarize(MarketOverview,MarketBreadth);
    public string SelectedMarketGroup => "";
    public ICommand ClearMarketGroupFilterCommand => null;
    public ICommand FilterMarketGroupCommand => null;
    public MarketGroupSnapshot[] TopMarketGroups => new[] { new MarketGroupSnapshot { GroupName="半導體", TotalCount=60, AdvancingCount=45, DecliningCount=15, AverageChangePercent=1.2m }, new MarketGroupSnapshot { GroupName="金融", TotalCount=40, AdvancingCount=15, DecliningCount=25, AverageChangePercent=-0.5m } };
}
