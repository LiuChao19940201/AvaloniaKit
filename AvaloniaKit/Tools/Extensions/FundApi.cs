using System;

namespace AvaloniaKit.Tools.Extensions;

// ══════════════════════════════════════════════════════════════════════════════
//  FundApi — 按平台返回基金接口地址（与 NeteaseApi 同模式）
//  · Desktop/Android/iOS：直连东方财富/天天基金（带 UA/Referer 即可）
//  · Browser(WASM)：C# HttpClient 底层是浏览器 fetch，受同源策略限制，
//    而东财/天天基金均不返回 CORS 头 → 必须经通用 CORS 代理转发
//    （allorigins.win/raw 原样透传响应正文，GET 文本接口均适用）
//  · 共享工程只编译 net10.0，#if BROWSER 永不生效，必须用运行时判断
// ══════════════════════════════════════════════════════════════════════════════
public static class FundApi
{
    /// <summary>Web 端通用 CORS 代理（原样返回目标响应正文，带 CORS:*）</summary>
    private const string CorsProxy = "https://api.allorigins.win/raw?url=";

    private static bool IsWeb => OperatingSystem.IsBrowser();

    /// <summary>直连 URL → 按平台包装（Web 端套 CORS 代理）</summary>
    private static string Route(string directUrl) =>
        IsWeb ? CorsProxy + Uri.EscapeDataString(directUrl) : directUrl;

    /// <summary>发现榜单：东财基金排行（按近1月涨幅降序）</summary>
    public static string Rank(string ft, DateTime start, DateTime end, int count) => Route(
        $"https://fund.eastmoney.com/data/rankhandler.aspx" +
        $"?op=ph&dt=kf&ft={ft}&rs=&gs=0&sc=yzf&st=desc" +
        $"&sd={start:yyyy-MM-dd}&ed={end:yyyy-MM-dd}" +
        $"&qdii=&tabSubtype=,,,,,&pi=1&pn={count}&dx=1" +
        $"&v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");

    /// <summary>全量基金代码表（搜索用，数 MB）</summary>
    public static string CodeTable() =>
        Route("https://fund.eastmoney.com/js/fundcode_search.js");

    /// <summary>天天基金实时估值 jsonp</summary>
    public static string Estimate(string code) => Route(
        $"https://fundgz.1234567.com.cn/js/{code}.js?rt={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");

    /// <summary>东财行情兜底（昨日净值）</summary>
    public static string Quote(string code) => Route(
        $"https://push2.eastmoney.com/api/qt/slist/get?fltt=2&fields=f2,f3,f12,f14&secid=0.{code}");

    /// <summary>东财 f10 历史净值</summary>
    public static string NavHistory(string code, DateTime start, DateTime end) => Route(
        $"https://api.fund.eastmoney.com/f10/lsjz" +
        $"?fundCode={code}&pageIndex=1&pageSize=200" +
        $"&startDate={start:yyyy-MM-dd}&endDate={end:yyyy-MM-dd}");
}
