using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaKit.Services;

// Desktop / Android / iOS 三端共用的纯文件本地存储实现：
//   settings.json —— key-value 设置（Utf8JsonWriter/JsonDocument 手写读写，AOT 安全无反射）
//   avatar.bin    —— 头像原始字节
// 零第三方依赖（仅 BCL），Browser 端另有 BrowserLocalDataService（JS 存储互操作）。
public class FileLocalDataService : ILocalDataService
{
    private readonly string _settingsPath;
    private readonly string _avatarPath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Dictionary<string, string>? _settings;   // 懒加载后常驻内存，写操作直写磁盘

    public FileLocalDataService(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        _settingsPath = Path.Combine(dataDir, "settings.json");
        _avatarPath = Path.Combine(dataDir, "avatar.bin");
    }

    public async Task SaveAvatarAsync(byte[] imageData)
    {
        await _lock.WaitAsync();
        try { await WriteAtomicAsync(_avatarPath, imageData); }
        finally { _lock.Release(); }
    }

    public async Task<byte[]?> LoadAvatarAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (!File.Exists(_avatarPath)) return null;
            return await File.ReadAllBytesAsync(_avatarPath);
        }
        finally { _lock.Release(); }
    }

    // 通用设置
    public async Task SaveSettingAsync(string key, string value)
    {
        await _lock.WaitAsync();
        try
        {
            var map = await LoadSettingsNoLockAsync();
            map[key] = value;
            await WriteAtomicAsync(_settingsPath, SerializeSettings(map));
        }
        finally { _lock.Release(); }
    }

    public async Task<string?> LoadSettingAsync(string key)
    {
        await _lock.WaitAsync();
        try
        {
            var map = await LoadSettingsNoLockAsync();
            return map.TryGetValue(key, out var v) ? v : null;
        }
        finally { _lock.Release(); }
    }

    // 懒初始化：首次操作时读盘建缓存（文件损坏时从空表重建，行为等同旧库建新表）
    private async Task<Dictionary<string, string>> LoadSettingsNoLockAsync()
    {
        if (_settings is not null) return _settings;

        var map = new Dictionary<string, string>();
        if (File.Exists(_settingsPath))
        {
            try
            {
                var bytes = await File.ReadAllBytesAsync(_settingsPath);
                using var doc = JsonDocument.Parse(bytes);
                foreach (var prop in doc.RootElement.EnumerateObject())
                    if (prop.Value.ValueKind == JsonValueKind.String)
                        map[prop.Name] = prop.Value.GetString() ?? "";
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // 损坏/占用时静默降级为空表，下次保存自动覆盖重建
            }
        }
        return _settings = map;
    }

    private static byte[] SerializeSettings(Dictionary<string, string> map)
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            foreach (var (key, value) in map)
                writer.WriteString(key, value);
            writer.WriteEndObject();
        }
        return ms.ToArray();
    }

    // 原子写：先写临时文件再替换，避免写一半崩溃损坏数据
    private static async Task WriteAtomicAsync(string path, byte[] data)
    {
        var tmp = path + ".tmp";
        await File.WriteAllBytesAsync(tmp, data);
        File.Move(tmp, path, overwrite: true);
    }
}
