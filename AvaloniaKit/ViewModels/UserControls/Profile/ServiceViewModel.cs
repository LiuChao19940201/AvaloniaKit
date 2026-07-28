using AvaloniaKit.Messages;
using AvaloniaKit.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System;

namespace AvaloniaKit.ViewModels.UserControls.Profile;

public partial class ServiceViewModel : ObservableObject
{
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isFlashlightOn;

    private static readonly float[] BrightnessLevels = [0.2f, 0.6f, 0.8f];
    private int _brightnessIndex;

    private static bool IsMobilePlatform() =>
        OperatingSystem.IsAndroid() || OperatingSystem.IsIOS();

    private void ExecuteOnMobile(string featureName, Action action)
    {
        if (!IsMobilePlatform())
        {
            StatusMessage = $"⚠️ [{featureName}] 仅支持移动端平台（Android / iOS）";
            return;
        }

        ExecuteCore(featureName, action);
    }

    /// <summary>无平台门禁：音效/震动三端均有实现（Desktop NAudio / Browser WebAudio）</summary>
    private void ExecuteCore(string featureName, Action action)
    {
        if (ServiceLocator.DeviceService is null)
        {
            StatusMessage = $"⚠️ [{featureName}] 设备服务未注册";
            return;
        }

        try
        {
            action();
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ [{featureName}] 执行失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private void GoBack()
    {
        WeakReferenceMessenger.Default.Send(new NavigateBackToProfileMessage());
    }

    /// <summary>
    /// 每次进入服务页时调用，清除上一次残留的状态信息。
    /// </summary>
    public void OnNavigatedTo()
    {
        StatusMessage = string.Empty;
        IsFlashlightOn = false;
        _brightnessIndex = 0;
    }

    [RelayCommand]
    private void OpenCamera()
    {
        ExecuteOnMobile("相机", () =>
        {
            ServiceLocator.DeviceService!.OpenCamera();
            StatusMessage = "✅ 相机已打开";
        });
    }

    [RelayCommand]
    private void Vibrate()
    {
        // 三端可用：Android 马达 / Browser navigator.vibrate / Desktop 无硬件静默
        ExecuteCore("震动", () =>
        {
            ServiceLocator.DeviceService!.Vibrate();
            StatusMessage = "✅ 震动已触发";
        });
    }

    [RelayCommand]
    private void OpenAlbum()
    {
        ExecuteOnMobile("相册", () =>
        {
            ServiceLocator.DeviceService!.OpenAlbum();
            StatusMessage = "✅ 相册已打开";
        });
    }

    [RelayCommand]
    private void PlaySound()
    {
        // 三端可用：Android ToneGenerator / Desktop NAudio / Browser WebAudio
        ExecuteCore("音效", () =>
        {
            ServiceLocator.DeviceService!.PlaySound();
            StatusMessage = "✅ 音效已播放";
        });
    }

    [RelayCommand]
    private void TestBluetooth()
    {
        ExecuteOnMobile("蓝牙", () =>
        {
            var status = ServiceLocator.DeviceService!.GetBluetoothStatus();
            StatusMessage = $"🔵 蓝牙状态：{status}";
        });
    }

    [RelayCommand]
    private void TestGps()
    {
        ExecuteOnMobile("GPS 定位", () =>
        {
            var location = ServiceLocator.DeviceService!.GetGpsLocation();
            StatusMessage = $"📍 GPS 信息：{location}";
        });
    }

    [RelayCommand]
    private void TestNfc()
    {
        ExecuteOnMobile("NFC", () =>
        {
            var status = ServiceLocator.DeviceService!.GetNfcStatus();
            StatusMessage = $"📡 NFC 状态：{status}";
        });
    }

    [RelayCommand]
    private void TestWifi()
    {
        ExecuteOnMobile("WiFi", () =>
        {
            var status = ServiceLocator.DeviceService!.GetWifiStatus();
            StatusMessage = $"📶 WiFi 状态：{status}";
        });
    }

    [RelayCommand]
    private void TestFlashlight()
    {
        ExecuteOnMobile("闪光灯", () =>
        {
            IsFlashlightOn = !IsFlashlightOn;
            ServiceLocator.DeviceService!.ToggleFlashlight(IsFlashlightOn);
            StatusMessage = IsFlashlightOn ? "🔦 闪光灯已开启" : "🔦 闪光灯已关闭";
        });
    }

    [RelayCommand]
    private void TestBrightness()
    {
        ExecuteOnMobile("屏幕亮度", () =>
        {
            var level = BrightnessLevels[_brightnessIndex];
            ServiceLocator.DeviceService!.SetBrightness(level);
            StatusMessage = $"🔆 屏幕亮度已调至 {level * 100:0}%";
            _brightnessIndex = (_brightnessIndex + 1) % BrightnessLevels.Length;
        });
    }

    [RelayCommand]
    private void TestSensor()
    {
        ExecuteOnMobile("传感器", () =>
        {
            var info = ServiceLocator.DeviceService!.GetSensorInfo();
            StatusMessage = $"🔬 传感器信息：{info}";
        });
    }

    [RelayCommand]
    private void TestNotification()
    {
        ExecuteOnMobile("通知推送", () =>
        {
            ServiceLocator.DeviceService!.SendNotification("测试通知", "这是一条来自 AvaloniaKit 的测试通知");
            StatusMessage = "🔔 通知已发送";
        });
    }
}