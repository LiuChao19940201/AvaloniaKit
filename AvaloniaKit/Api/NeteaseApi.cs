using System;
using System.Collections.Generic;

namespace AvaloniaKit.Api;

// ══════════════════════════════════════════════════════════════════════════════
//  NeteaseApi — 按平台返回网易云接口地址
//  · Desktop/Android/iOS：直连官方 music.163.com/api/*（带 UA/Referer 即可）
//  · Browser(WASM)：C# HttpClient 底层是浏览器 fetch，受同源策略限制，
//    而 music.163.com 不返回 CORS 头 → 必须换成带 CORS:* 的镜像实例
//    （NeteaseCloudMusicApi，实测国内可达，响应结构与官方一致）
//  · 封面图片 CDN（music.126.net）自带 CORS:*，三端均可直连，无需经此转换
// ══════════════════════════════════════════════════════════════════════════════
public static class NeteaseApi
{
    /// <summary>Web 端镜像基址（NeteaseCloudMusicApi 实例，带 CORS:*）</summary>
    private const string Mirror = "https://163api.qijieya.cn";

    private static bool IsWeb => OperatingSystem.IsBrowser();

    /// <summary>推荐新音乐（未登录态首页）</summary>
    public static string PersonalizedNewSong(int limit) => IsWeb
        ? $"{Mirror}/personalized/newsong?limit={limit}"
        : $"https://music.163.com/api/personalized/newsong?limit={limit}";

    /// <summary>歌单详情（拿实时 trackIds）</summary>
    public static string PlaylistDetail(long listId) => IsWeb
        ? $"{Mirror}/playlist/detail?id={listId}"
        : $"https://music.163.com/api/v6/playlist/detail?id={listId}&n=0";

    /// <summary>老版歌单详情（tracks 兜底，数据可能滞后）</summary>
    public static string PlaylistDetailLegacy(long listId) => IsWeb
        ? $"{Mirror}/playlist/detail?id={listId}"
        : $"https://music.163.com/api/playlist/detail?id={listId}";

    /// <summary>批量歌曲详情（封面/时长）</summary>
    public static string SongDetail(IReadOnlyList<long> ids)
    {
        if (IsWeb)
            return $"{Mirror}/song/detail?ids={string.Join(",", ids)}";

        var sb = new System.Text.StringBuilder("[");
        for (int i = 0; i < ids.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append("{\"id\":").Append(ids[i]).Append('}');
        }
        sb.Append(']');
        return $"https://music.163.com/api/v3/song/detail?c={Uri.EscapeDataString(sb.ToString())}";
    }

    /// <summary>官方搜索（v3 结构，含 privilege 可播性信息）</summary>
    public static string CloudSearch(string keyword, int limit) => IsWeb
        ? $"{Mirror}/cloudsearch?keywords={Uri.EscapeDataString(keyword)}&limit={limit}&offset=0"
        : $"https://music.163.com/api/cloudsearch/pc?s={Uri.EscapeDataString(keyword)}&type=1&limit={limit}&offset=0";

    /// <summary>老版搜索兜底（只取 id 列表）</summary>
    public static string SearchLegacy(string keyword, int limit) => IsWeb
        ? $"{Mirror}/search?keywords={Uri.EscapeDataString(keyword)}&limit={limit}&offset=0"
        : $"https://music.163.com/api/search/get/web?s={Uri.EscapeDataString(keyword)}&type=1&limit={limit}&offset=0";

    /// <summary>歌词</summary>
    public static string Lyric(long songId) => IsWeb
        ? $"{Mirror}/lyric?id={songId}"
        : $"https://music.163.com/api/song/lyric?id={songId}&lv=1&kv=1&tv=-1";

    /// <summary>播放链接镜像（NeteaseCloudMusicApi 格式：data[0].url）</summary>
    public static string SongUrlMirror(long songId)
        => $"{Mirror}/song/url?id={songId}";
}
