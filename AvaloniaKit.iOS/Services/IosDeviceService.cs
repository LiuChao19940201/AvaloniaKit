using AudioToolbox;
using AVFoundation;
using AvaloniaKit.Services;
using CoreLocation;
using CoreMotion;
using Foundation;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UIKit;
using UserNotifications;

namespace AvaloniaKit.iOS.Services;

// ══════════════════════════════════════════════════════════════════════════════
//  IosDeviceService — 设备能力（iOS 端）
//  · PlayTone：AVAudioEngine 合成正弦波（指数衰减包络，与
//    Desktop NAudio / Android AudioTrack / Browser WebAudio 音色对齐）
//  · 震动：AudioToolbox 系统震动；手电筒：AVCaptureDevice Torch；
//    亮度：UIScreen.Brightness；通知：UNUserNotificationCenter
//  · 蓝牙/WiFi 等 iOS 无公开同步查询 API 的能力返回说明性文案
//    （与 Android 端"设备不支持 XX"的降级文案模式一致）
// ══════════════════════════════════════════════════════════════════════════════
public class IosDeviceService : IDeviceService
{
    // UIWindow 经由延迟访问器获取：注册发生在 AppDelegate 构建组合根时（窗口尚未创建），
    // 实际调用均在 UI 就绪之后
    private readonly Func<UIWindow?> _getWindow;

    public IosDeviceService(Func<UIWindow?> windowProvider)
    {
        _getWindow = windowProvider;
    }

    private UIViewController? TopViewController
    {
        get
        {
            var vc = _getWindow()?.RootViewController;
            while (vc?.PresentedViewController != null)
                vc = vc.PresentedViewController;
            return vc;
        }
    }

    public void OpenCamera() => PresentImagePicker(UIImagePickerControllerSourceType.Camera);

    public void OpenAlbum() => PresentImagePicker(UIImagePickerControllerSourceType.PhotoLibrary);

    private void PresentImagePicker(UIImagePickerControllerSourceType sourceType)
    {
        UIApplication.SharedApplication.InvokeOnMainThread(() =>
        {
            try
            {
#pragma warning disable CA1422 // PhotoLibrary 在 iOS14+ 标记过时，功能仍可用（演示场景保持简单）
                if (!UIImagePickerController.IsSourceTypeAvailable(sourceType)) return;
                var picker = new UIImagePickerController { SourceType = sourceType };
#pragma warning restore CA1422
                TopViewController?.PresentViewController(picker, true, null);
            }
            catch { /* 相机/相册不可用（如模拟器）时静默 */ }
        });
    }

    public void Vibrate()
    {
        try { SystemSound.Vibrate.PlaySystemSound(); } catch { }
    }

    public void PlaySound() => PlayTone(1000, 150);

    // ── PlayTone：AVAudioEngine 静态缓冲合成（与其他三端音色对齐）────────────
    private AVAudioEngine? _toneEngine;
    private AVAudioPlayerNode? _toneNode;
    private AVAudioFormat? _toneFormat;
    private readonly object _toneLock = new();

    private bool EnsureToneEngine()
    {
        lock (_toneLock)
        {
            if (_toneEngine != null) return true;
            try
            {
                var engine = new AVAudioEngine();
                var node = new AVAudioPlayerNode();
                var format = new AVAudioFormat(44100, 1);
                engine.AttachNode(node);
                engine.Connect(node, engine.MainMixerNode, format);
                engine.StartAndReturnError(out var error);
                if (error != null) return false;
                node.Play();

                _toneEngine = engine;
                _toneNode = node;
                _toneFormat = format;
                return true;
            }
            catch { return false; }
        }
    }

    public void PlayTone(double frequency, int durationMs)
    {
        try
        {
            if (!EnsureToneEngine() || _toneNode is null || _toneFormat is null) return;

            const int sampleRate = 44100;
            uint samples = (uint)(sampleRate * durationMs / 1000);
            if (samples == 0) return;

            var buffer = new AVAudioPcmBuffer(_toneFormat, samples) { FrameLength = samples };

            // 指数衰减包络，避免爆音（与 Android AudioTrack 实现一致）
            var pcm = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                double t = (double)i / sampleRate;
                double env = Math.Exp(-3.0 * i / samples);
                pcm[i] = (float)(Math.Sin(2 * Math.PI * frequency * t) * env * 0.3);
            }

            // FloatChannelData → float* 通道指针数组，单声道取第 0 个
            IntPtr channelArray = buffer.FloatChannelData;
            IntPtr channel0 = Marshal.ReadIntPtr(channelArray);
            Marshal.Copy(pcm, 0, channel0, pcm.Length);

            _toneNode.ScheduleBuffer(buffer, null);
        }
        catch { /* 合成失败静默（与其他端一致） */ }
    }

    public string GetBluetoothStatus()
        => "iOS 需授权后经 CoreBluetooth 异步查询，暂不支持同步读取";

    public string GetGpsLocation()
    {
        try
        {
            return CLLocationManager.LocationServicesEnabled
                ? "定位服务已开启（具体位置需授权后获取）"
                : "定位服务未开启";
        }
        catch { return "无法获取定位服务"; }
    }

    public string GetNfcStatus()
        => "iOS 需经 CoreNFC 会话读取，暂不支持状态查询";

    public string GetWifiStatus()
        => "iOS 无公开 API 查询 WiFi 状态";

    public void ToggleFlashlight(bool on)
    {
        try
        {
            var device = AVCaptureDevice.GetDefaultDevice(AVMediaTypes.Video);
            if (device is not { HasTorch: true }) return;

            device.LockForConfiguration(out var error);
            if (error != null) return;
            device.TorchMode = on ? AVCaptureTorchMode.On : AVCaptureTorchMode.Off;
            device.UnlockForConfiguration();
        }
        catch { /* 无手电筒（模拟器）静默 */ }
    }

    public void SetBrightness(float level)
    {
        UIApplication.SharedApplication.InvokeOnMainThread(() =>
        {
            UIScreen.MainScreen.Brightness = Math.Clamp(level, 0f, 1f);
        });
    }

    public string GetSensorInfo()
    {
        try
        {
            using var motion = new CMMotionManager();
            var sensors = new List<string>();
            if (motion.AccelerometerAvailable) sensors.Add("加速度计");
            if (motion.GyroAvailable) sensors.Add("陀螺仪");
            if (motion.MagnetometerAvailable) sensors.Add("磁力计");
            if (motion.DeviceMotionAvailable) sensors.Add("设备运动");
            return sensors.Count > 0 ? string.Join(", ", sensors) : "未检测到传感器";
        }
        catch { return "无法获取传感器服务"; }
    }

    public void SendNotification(string title, string message)
    {
        var center = UNUserNotificationCenter.Current;
        center.RequestAuthorization(
            UNAuthorizationOptions.Alert | UNAuthorizationOptions.Sound,
            (granted, _) =>
            {
                if (!granted) return;

                var content = new UNMutableNotificationContent
                {
                    Title = title,
                    Body = message,
                };
                var trigger = UNTimeIntervalNotificationTrigger.CreateTrigger(1, false);
                var request = UNNotificationRequest.FromIdentifier(
                    "avalonia_demo_test", content, trigger);
                center.AddNotificationRequest(request, null);
            });
    }
}
