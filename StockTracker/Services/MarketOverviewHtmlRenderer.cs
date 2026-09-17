using StockTracker.Models;
using StockTracker.ViewModels;
using System;
using System.Linq;
using System.Net;
using System.Text;

namespace StockTracker.Services
{
    /// <summary>Shared, date-aware market summary for the desktop and static website.</summary>
    public static class MarketOverviewHtmlRenderer
    {
        public static string Summarize(MarketOverviewSnapshot overview, MarketBreadthSnapshot breadth)
        {
            var parts = (overview.Trading?.Markets ?? new System.Collections.Generic.List<MarketTradingSeries>())
                .Select(m => m.Latest == null ? m.Name + "資料待更新。" : $"{m.Name}（{m.Latest.TradeDate:MM/dd}）：{m.Reading}").ToList();
            if (breadth.TotalCount > 0)
                parts.Add($"{breadth.DataDateText}，上漲 {breadth.AdvancingCount:N0}／下跌 {breadth.DecliningCount:N0}／平盤 {breadth.UnchangedCount:N0} 檔。");
            var funding = (overview.Days ?? Array.Empty<MarketOverviewDay>()).Where(d => d.HasInstitutional).OrderBy(d => d.TradeDate).ToList();
            if (funding.Count > 0)
                parts.Add($"法人 {funding.First().TradeDate:MM/dd}–{funding.Last().TradeDate:MM/dd}（{funding.Count} 日已公布）合計 {funding.Sum(d => d.ThreeMajorNet) / 100000000m:+0.0;-0.0;0.0} 億。");
            else parts.Add("法人資料待更新。");
            return string.Join("\n", parts);
        }

        public static string Render(MarketOverviewSnapshot overview, MarketBreadthSnapshot breadth)
        {
            var html = new StringBuilder();
            html.AppendLine("<section class='panel market-overview' id='fullMarketOverview' aria-label='全市場總覽'>");
            html.AppendLine(@"<style>
                .market-overview{display:block;grid-template-columns:none;gap:0}.market-overview h3{margin:0 0 10px}.market-overview h4{margin:18px 0 10px}.market-overview .overview-meta{color:var(--muted,#8b949e);font-size:12px;line-height:1.6}
                .market-overview .overview-lead{white-space:pre-line;line-height:1.85;padding:14px 16px;background:rgba(56,139,253,.07);border-left:3px solid #58a6ff;border-radius:6px;margin:12px 0}
                .market-overview .market-stance{margin:12px 0;padding:14px 16px;border:1px solid var(--border,#30363d);border-radius:8px;background:rgba(139,148,158,.04)}.market-overview .market-stance-head{display:flex;gap:12px;align-items:baseline;flex-wrap:wrap}.market-overview .market-stance-title{font-size:21px;font-weight:700}.market-overview .market-stance-signals{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:10px;margin-top:10px}.market-overview .market-stance-signals span{font-size:11px;color:var(--muted,#8b949e)}.market-overview .market-stance-signals b{display:block;font-size:12px;color:inherit;margin-top:3px;line-height:1.45}
                .market-overview .overview-markets{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:12px}
                .market-overview .overview-market{padding:16px;background:rgba(139,148,158,.04);border:1px solid var(--border,#30363d);border-radius:10px;min-width:0}
                .market-overview .overview-index{font-size:24px;font-weight:700;margin:8px 0}.market-overview .overview-index small{font-size:16px;margin-left:10px}
                .market-overview dl{display:grid;grid-template-columns:1fr 1fr;gap:12px;margin:12px 0}.market-overview dt{font-size:12px;color:var(--muted,#8b949e)}.market-overview dd{font-weight:600;margin:4px 0 0}
                .market-overview .overview-breadth{display:flex;gap:18px;flex-wrap:wrap;padding:12px;border:1px solid var(--border,#30363d);border-radius:8px}.market-overview .overview-breadth b{display:block;font-size:19px}
                .market-overview .overview-table{overflow-x:auto}.market-overview table{width:100%;min-width:680px}.market-overview th,.market-overview td{white-space:nowrap;padding:10px;text-align:right}.market-overview th:first-child,.market-overview td:first-child{text-align:left}
                .market-overview .overview-quantity-tabs{display:flex;gap:6px;margin:8px 0}.market-overview .overview-quantity-tabs button{background:transparent;border:1px solid var(--border,#30363d);color:inherit;border-radius:6px;padding:7px 18px;cursor:pointer}.market-overview .overview-quantity-tabs button[aria-pressed=true]{background:#1f6feb;color:white}
                .market-overview .rise{color:var(--rise,#ff453a)}.market-overview .fall{color:var(--fall,#32d74b)}.market-overview .flat{color:var(--muted,#8b949e)}
                .market-overview details{margin-top:12px}.market-overview details p{overflow-wrap:anywhere}.market-overview a{color:#58a6ff}
                @media(max-width:700px){.market-overview .overview-markets,.market-overview .market-stance-signals{grid-template-columns:1fr}.market-overview .overview-index{font-size:22px}}
                </style>");
            html.AppendLine("<h3>全市場總覽</h3><div class='overview-meta'>今日摘要 → 指數與成交、市場廣度 → 近五日量價與資金明細</div>");
            html.AppendLine("<div class='overview-lead'>" + E(Summarize(overview, breadth)) + "</div>");
            var regime = RankingViewModel.CreateMarketRegime(breadth, overview);
            html.AppendLine("<section class='market-stance' aria-label='市場傾向'><div class='market-stance-head'><strong>市場傾向（非下單建議）</strong><span class='market-stance-title " + Color(regime.PositiveSignals - regime.NegativeSignals) + "'>" + E(regime.Title) + "</span><span class='overview-meta'>" + E(regime.SignalCountText) + "</span></div><p class='overview-meta'>" + E(regime.ActionHint) + "</p><div class='market-stance-signals'><span>近五日指數趨勢<b>" + E(regime.IndexSignal) + "</b></span><span>市場廣度<b>" + E(regime.BreadthSignal) + "</b></span><span>三大法人<b>" + E(regime.InstitutionalSignal) + "</b></span><span>臺指 P/C 未平倉<b>" + E(regime.PutCallSignal) + "</b></span></div><p class='overview-meta'>融資： " + E(regime.MarginSignal) + "</p></section>");
            html.AppendLine("<h4>指數與成交</h4><div class='overview-meta'>" + E(overview.Trading?.UpdatedText ?? "成交資料待更新") + "</div><div class='overview-markets'>");
            var markets = overview.Trading?.Markets ?? new System.Collections.Generic.List<MarketTradingSeries>();
            foreach (var m in markets)
            {
                html.AppendLine("<article class='overview-market'><strong>" + E(m.Name) + "</strong><div class='overview-meta'>" + E(m.DateText) + "</div>");
                html.AppendLine("<div class='overview-index'>" + E(m.IndexText) + "<small class='" + Color(m.Latest?.IndexChangePercent) + "'>" + E(m.ChangeText) + "</small></div><dl>");
                Metric(html, "成交金額", m.AmountText);
                Metric(html, "成交量", m.VolumeText);
                Metric(html, "較前一交易日成交金額", m.AmountChangeText, Color(m.Latest?.TurnoverChangePercent));
                Metric(html, "相對前 20 日平均成交金額", m.AmountRatioText);
                html.AppendLine("</dl><div class='overview-meta'>" + E(m.Reading) + "<br>" + E(m.Status) + "</div></article>");
            }
            if (markets.Count == 0) html.AppendLine("<p class='overview-meta'>上市／上櫃成交資料待更新，完成新版掃描後載入。</p>");
            html.AppendLine("</div><h4>市場廣度</h4><div class='overview-meta'>" + E(breadth.DataDateText) + "；本次成功載入 " + breadth.TotalCount.ToString("N0") + " 檔，上漲比例不含平盤。</div>");
            html.AppendLine("<div class='overview-breadth'><span>上漲<b class='rise'>" + breadth.AdvancingCount.ToString("N0") + "</b></span><span>下跌<b class='fall'>" + breadth.DecliningCount.ToString("N0") + "</b></span><span>平盤<b>" + breadth.UnchangedCount.ToString("N0") + "</b></span><span>上漲比例<b>" + (breadth.AdvancingCount + breadth.DecliningCount > 0 ? breadth.AdvanceRatioPercent.ToString("F1") + "%" : "—") + "</b></span><span>平均漲跌<b class='" + Color(breadth.AverageChangePercent) + "'>" + (breadth.TotalCount > 0 ? MarketTradingDay.Percent(breadth.AverageChangePercent) : "—") + "</b></span></div>");
            html.AppendLine("<h4>近五個交易日 · 量價明細</h4><div class='overview-meta'>逐日顯示含最新當日；20 日基準不含該日。當日累計尚未完整時，整日比較顯示 —。</div><div class='overview-quantity-tabs' aria-label='量價市場切換'>");
            for (int i = 0; i < markets.Count; i++)
                html.AppendLine("<button type='button' data-market-tab='" + E(markets[i].Code) + "' aria-pressed='" + (i == 0 ? "true" : "false") + "'>" + E(markets[i].Name) + "</button>");
            html.AppendLine("</div>");
            for (int i = 0; i < markets.Count; i++)
            {
                var m = markets[i];
                html.AppendLine("<div class='overview-table' data-market-table='" + E(m.Code) + "'" + (i > 0 ? " hidden" : "") + "><table><thead><tr><th>日期／狀態</th><th>收盤／最新指數</th><th>漲跌幅</th><th>成交金額</th><th>較前日</th><th>成交量</th><th>前 20 日均值倍數</th></tr></thead><tbody>");
                foreach (var d in m.Days)
                    html.AppendLine("<tr><td>" + E(d.DateText) + "<br><small class='overview-meta'>" + E(d.SessionText) + "</small></td><td>" + E(d.IndexText) + "</td>" + Cell(d.ChangeText, d.IndexChangePercent) + "<td>" + E(d.AmountText) + "</td>" + Cell(d.AmountChangeText, d.TurnoverChangePercent) + "<td>" + E(d.VolumeText) + "</td><td>" + E(d.AmountRatioText) + "</td></tr>");
                if (m.Days.Count == 0) html.AppendLine("<tr><td colspan='7'>資料待更新</td></tr>");
                html.AppendLine("</tbody></table></div>");
            }
            html.AppendLine("<h4>近五個交易日 · 法人、資券與 P/C</h4><div class='overview-meta'>" + E(overview.FundingStatusText) + "</div><div class='overview-table'><table><thead><tr><th>日期</th><th>外資</th><th>投信</th><th>自營商</th><th>法人合計</th><th>融資餘額（億元）</th><th>融券餘額（張）</th><th>P/C 未平倉</th></tr></thead><tbody>");
            foreach (var d in overview.RecentFundingDays)
                html.AppendLine("<tr><td>" + E(d.TradeDate.ToString("yyyy/MM/dd")) + "</td>" + Cell(d.ForeignNetText, d.HasInstitutional ? (decimal?)d.ForeignNet : null) + Cell(d.TrustNetText, d.HasInstitutional ? (decimal?)d.TrustNet : null) + Cell(d.DealerNetText, d.HasInstitutional ? (decimal?)d.DealerNet : null) + Cell(d.ThreeMajorNetText, d.HasInstitutional ? (decimal?)d.ThreeMajorNet : null) + "<td>" + E(d.MarginBalanceText) + " <span class='" + Color(d.MarginAmountChangeThousand) + "'>" + E(d.MarginAmountChangeDisplayText) + "</span></td><td>" + E(d.ShortBalanceText) + " <span class='" + Color(d.ShortBalanceChangeLots) + "'>" + E(d.ShortBalanceChangeDisplayText) + "</span></td><td>" + E(d.PutCallOpenInterestRatioText) + "</td></tr>");
            if (!overview.RecentFundingDays.Any()) html.AppendLine("<tr><td colspan='8'>資料待更新</td></tr>");
            html.AppendLine("</tbody></table></div><details><summary>資料來源與計算方式</summary><p class='overview-meta'>成交金額以億元、成交股數以萬張顯示（1 萬張 = 1,000 萬股）。相對成交金額 = 該日成交金額 ÷ 前 20 個完整交易日平均；不足 20 日顯示 —。成交增加代表交易活躍，不代表淨流入。</p>");
            foreach (var m in markets) html.AppendLine("<p class='overview-meta'><a href='" + E(m.SourceUrl) + "' target='_blank' rel='noopener noreferrer'>" + E(m.Name) + "官方來源</a>：" + E(m.Scope) + "</p>");
            html.AppendLine("</details></section><script>(function(){const host=document.getElementById('fullMarketOverview');if(!host)return;host.querySelectorAll('[data-market-tab]').forEach(button=>button.addEventListener('click',()=>{host.querySelectorAll('[data-market-tab]').forEach(b=>b.setAttribute('aria-pressed',b===button?'true':'false'));host.querySelectorAll('[data-market-table]').forEach(t=>t.hidden=t.dataset.marketTable!==button.dataset.marketTab);}));})();</script>");
            return html.ToString();
        }
        private static void Metric(StringBuilder html, string label, string value, string color = "") => html.Append("<div><dt>" + E(label) + "</dt><dd class='" + color + "'>" + E(value) + "</dd></div>");
        private static string Cell(string value, decimal? number) => "<td class='" + Color(number) + "'>" + E(value) + "</td>";
        private static string Color(decimal? number) => number > 0 ? "rise" : number < 0 ? "fall" : "flat";
        private static string E(string text) => WebUtility.HtmlEncode(text ?? "");
    }
}
