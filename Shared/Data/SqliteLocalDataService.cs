using AvaloniaKit.Services;
using SQLite;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaKit.Data;

// Desktop / Android / iOS 共用的 SQLite 实现（各平台头以链接文件方式编译本文件，
// 不下沉共享项目是为了避免 sqlite-net-pcl 传染 Browser 平台头）

[Table("app_settings")]
internal class AppSetting
{
    [PrimaryKey] public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

public class SqliteLocalDataService : ILocalDataService
{
    private readonly SQLiteAsyncConnection _db;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public SqliteLocalDataService(string dbPath)
    {
        _db = new SQLiteAsyncConnection(dbPath);
    }

    // 懒初始化：首次操作时自动建表
    private async Task EnsureInitializedAsync()
    {
        if (_initialized) return;
        await _initLock.WaitAsync();
        try
        {
            if (!_initialized)
            {
                await _db.CreateTableAsync<AppSetting>();
                _initialized = true;
            }
        }
        finally { _initLock.Release(); }
    }

    public async Task SaveAvatarAsync(byte[] imageData)
    {
        await EnsureInitializedAsync();
        await _db.InsertOrReplaceAsync(new AppSetting
        {
            Key = "avatar",
            Value = Convert.ToBase64String(imageData)
        });
    }

    public async Task<byte[]?> LoadAvatarAsync()
    {
        await EnsureInitializedAsync();
        var row = await _db.Table<AppSetting>()
                           .FirstOrDefaultAsync(s => s.Key == "avatar");
        if (row?.Value is null) return null;
        return Convert.FromBase64String(row.Value);
    }

    // 通用设置
    public async Task SaveSettingAsync(string key, string value)
    {
        await EnsureInitializedAsync();
        await _db.InsertOrReplaceAsync(new AppSetting { Key = key, Value = value });
    }

    public async Task<string?> LoadSettingAsync(string key)
    {
        await EnsureInitializedAsync();
        var row = await _db.Table<AppSetting>()
                           .FirstOrDefaultAsync(s => s.Key == key);
        return row?.Value;
    }
}
