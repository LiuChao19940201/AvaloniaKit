namespace AvaloniaKit.Resources;

// ══════════════════════════════════════════════════════════════════════════════
//  MapHtml — 地图页面（一份 HTML，四端复用；仿高德地图，实用导航工具）
//  · 地图与路线：高德 JS API v2.0（AMap.Driving/Walking/Riding/Transfer）
//  · 查路线：多策略并行算路去重 → 多条备选路线，点线/卡片切换选中
//  · 起点/终点输入框内置「定位」按钮：一键填入当前位置；右下角「回到我的位置」悬浮键
//  · 导航：真实 GPS 跟随（watchPosition 吸附路线/偏航自动重算/到达判定/实测车速球）；
//    无定位信号时如实提示并持续等待（无演示模式）
//  · 位置点为大三角方向箭头 + 转向卡 + 剩余里程/ETA + 进度条 +
//    按所选语音包 TTS 逐路口精简播报；「退出」回路线选择
//  · ★ 行进方向朝前（heading-up）：原生端（Android/iOS/Desktop，直接加载）导航中
//    地图随行进方向旋转、箭头恒指屏幕上方、视野中心前移；Browser（iframe 内嵌，
//    旋转不渲染）自动降级为正北朝上 + 箭头指行进方向
//  · 定位：H5 精确定位优先，失败回退 IP 城市级定位（提示区分精度）
//  · 语音包：SpeechSynthesis(TTS) 用音色/语速/音调模拟 5 个语音包
//  · JS→宿主消息：app://voice?id=（切语音包持久化）、app://nav?open=1/0（导航态）
//  · 凭证唯一修改点：下方 AmapKey / AmapSecurity 两个常量
// ══════════════════════════════════════════════════════════════════════════════
public static partial class MapHtml
{
    // ★ 高德开放平台「Web 端 (JS API)」凭证（唯一修改点）；域名白名单须留空
    private const string AmapKey = "4b91b6eb0921d53a23de8495a63c1702";
    private const string AmapSecurity = "94f892d941eb8151ef305a24d46a2d82";

    /// <summary>按初始语音包生成页面（voiceId：warm/lively/deep/calm/fast）</summary>
    public static string Build(string voiceId)
        => Template.Replace("__AMAP_KEY__", AmapKey)
                   .Replace("__AMAP_SECURITY__", AmapSecurity)
                   .Replace("__INIT_VOICE__", string.IsNullOrWhiteSpace(voiceId) ? "warm" : voiceId);

    private static string Template =>
        HeadPart + "\n" + StylesPart + "\n" + BodyPart + "\n" + ScriptCorePart + "\n" + ScriptNavPart;

    private const string HeadPart = """
<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no">
""";
}
