namespace AvaloniaKit.Services;

public interface IDeviceService
{
    void OpenCamera();
    void Vibrate();
    void OpenAlbum();
    void PlaySound();

    /// <summary>
    /// 合成指定频率/时长的提示音（游戏音效用，三端一致）：
    /// Desktop=NAudio 正弦波、Android=AudioTrack PCM 合成、Browser=WebAudio 振荡器。
    /// </summary>
    void PlayTone(double frequency, int durationMs);
    string GetBluetoothStatus();
    string GetGpsLocation();
    string GetNfcStatus();
    string GetWifiStatus();
    void ToggleFlashlight(bool on);
    void SetBrightness(float level);
    string GetSensorInfo();
    void SendNotification(string title, string message);
}