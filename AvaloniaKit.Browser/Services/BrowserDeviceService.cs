using AvaloniaKit.Services;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace AvaloniaKit.Browser.Services;

// ══════════════════════════════════════════════════════════════════════════════
//  BrowserDeviceService — 设备反馈（Browser 端）
//  · 目标：Tetris 等游戏的音效/震动反馈三端一致
//  · PlaySound：WebAudio 振荡器合成短促提示音（audio.js deviceBeep）
//  · Vibrate：navigator.vibrate（移动端浏览器有效，桌面浏览器静默无效）
//  · 其余硬件能力浏览器沙箱不可用，服务页有平台门禁不会调到
// ══════════════════════════════════════════════════════════════════════════════
[SupportedOSPlatform("browser")]
public partial class BrowserDeviceService : IDeviceService
{
    [JSImport("deviceBeep", "audio")] private static partial void JsBeep();
    [JSImport("deviceTone", "audio")] private static partial void JsTone(double freq, int ms);
    [JSImport("deviceVibrate", "audio")] private static partial void JsVibrate(int ms);

    public void PlaySound()
    {
        try { JsBeep(); } catch { }
    }

    public void PlayTone(double frequency, int durationMs)
    {
        try { JsTone(frequency, durationMs); } catch { }
    }

    public void Vibrate()
    {
        try { JsVibrate(300); } catch { }   // 时长对齐 Android 端 300ms
    }

    // ── 以下硬件能力浏览器沙箱不可用（服务演示页有移动端门禁，不会执行到）──
    public void OpenCamera() { }
    public void OpenAlbum() { }
    public string GetBluetoothStatus() => "浏览器端不支持";
    public string GetGpsLocation() => "浏览器端不支持";
    public string GetNfcStatus() => "浏览器端不支持";
    public string GetWifiStatus() => "浏览器端不支持";
    public void ToggleFlashlight(bool on) { }
    public void SetBrightness(float level) { }
    public string GetSensorInfo() => "浏览器端不支持";
    public void SendNotification(string title, string message) { }
}
