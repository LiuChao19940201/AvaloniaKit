using AvaloniaKit.Services;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaKit.Desktop.Services;

// ══════════════════════════════════════════════════════════════════════════════
//  DesktopDeviceService — 设备反馈（Windows Desktop 端）
//  · 目标：Tetris 等游戏的音效/震动反馈三端一致（此前仅 Android 有实现，
//    Desktop/Browser 静默无反馈）
//  · PlaySound：NAudio 合成短促提示音（对齐 Android ToneGenerator PropBeep）
//  · Vibrate：桌面无震动马达，空实现（调用方判空/静默容忍）
//  · 其余硬件能力（相机/蓝牙/NFC 等）桌面不适用，服务页有平台门禁不会调到
// ══════════════════════════════════════════════════════════════════════════════
public class DesktopDeviceService : IDeviceService
{
    public void PlaySound()
    {
        // fire-and-forget：合成 120ms 正弦提示音，任何失败静默（与 Android 端兜底一致）
        Task.Run(() =>
        {
            try
            {
                var beep = new SignalGenerator(44100, 1)
                {
                    Gain = 0.15,
                    Frequency = 880,
                    Type = SignalGeneratorType.Sin,
                }.Take(TimeSpan.FromMilliseconds(120));

                using var waveOut = new WaveOutEvent();
                using var done = new ManualResetEventSlim();
                waveOut.PlaybackStopped += (_, _) => done.Set();
                waveOut.Init(beep);
                waveOut.Play();
                done.Wait(1000);
            }
            catch { }
        });
    }

    public void Vibrate() { }   // 桌面无震动硬件

    // ── 以下硬件能力桌面端不适用（服务演示页有移动端门禁，不会执行到）──
    public void OpenCamera() { }
    public void OpenAlbum() { }
    public string GetBluetoothStatus() => "桌面端不支持";
    public string GetGpsLocation() => "桌面端不支持";
    public string GetNfcStatus() => "桌面端不支持";
    public string GetWifiStatus() => "桌面端不支持";
    public void ToggleFlashlight(bool on) { }
    public void SetBrightness(float level) { }
    public string GetSensorInfo() => "桌面端不支持";
    public void SendNotification(string title, string message) { }
}
