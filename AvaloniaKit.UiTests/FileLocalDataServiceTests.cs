using AvaloniaKit.Services;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace AvaloniaKit.UiTests;

// ══════════════════════════════════════════════════════════════════════════════
//  FileLocalDataService 纯逻辑单元测试（无 UI，用普通 [Fact]）
//  教学示范：每个测试都是标准 AAA 三段式 —— Arrange 准备 / Act 执行 / Assert 断言
//  · 每个测试用独立临时目录，互不干扰、可并行、可无限重跑（测试必须"无状态"）
//  · IDisposable.Dispose 在每个测试结束后自动清理临时目录
// ══════════════════════════════════════════════════════════════════════════════
public class FileLocalDataServiceTests : IDisposable
{
    private readonly string _tempDir;

    public FileLocalDataServiceTests()
    {
        // xunit 对"每一个"测试方法都会 new 一个本类实例 → 天然隔离
        _tempDir = Path.Combine(Path.GetTempPath(), "AvaloniaKitTest_" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* 清理失败不影响测试结果 */ }
    }

    [Fact]
    public async Task SaveSetting_ThenLoad_ReturnsSameValue()
    {
        // Arrange：准备被测对象
        var svc = new FileLocalDataService(_tempDir);

        // Act：执行被测行为
        await svc.SaveSettingAsync("app_theme", "Dark");
        var loaded = await svc.LoadSettingAsync("app_theme");

        // Assert：存进去什么就必须读出来什么
        Assert.Equal("Dark", loaded);
    }

    [Fact]
    public async Task LoadSetting_WhenKeyMissing_ReturnsNull()
    {
        var svc = new FileLocalDataService(_tempDir);

        var loaded = await svc.LoadSettingAsync("不存在的键");

        // 契约：查无此键返回 null（而不是抛异常）——测试把这个约定锁死
        Assert.Null(loaded);
    }

    [Fact]
    public async Task Settings_PersistAcrossInstances()
    {
        // 模拟"关闭应用再重开"：第一个实例写，全新实例读同一目录
        var first = new FileLocalDataService(_tempDir);
        await first.SaveSettingAsync("game_highscore_2048", "3568");

        var second = new FileLocalDataService(_tempDir);
        var loaded = await second.LoadSettingAsync("game_highscore_2048");

        Assert.Equal("3568", loaded);
    }

    [Fact]
    public async Task SaveSetting_SameKeyTwice_KeepsLatestValue()
    {
        var svc = new FileLocalDataService(_tempDir);

        await svc.SaveSettingAsync("app_theme", "Dark");
        await svc.SaveSettingAsync("app_theme", "Light");

        Assert.Equal("Light", await svc.LoadSettingAsync("app_theme"));
    }

    [Fact]
    public async Task SaveAvatar_ThenLoad_RoundtripsExactBytes()
    {
        var svc = new FileLocalDataService(_tempDir);
        var image = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x00, 0xFF, 0x7B };  // 模拟图片字节

        await svc.SaveAvatarAsync(image);
        var loaded = await svc.LoadAvatarAsync();

        Assert.Equal(image, loaded);   // 二进制必须逐字节一致
    }

    [Fact]
    public async Task LoadAvatar_WhenNeverSaved_ReturnsNull()
    {
        var svc = new FileLocalDataService(_tempDir);

        Assert.Null(await svc.LoadAvatarAsync());
    }

    [Fact]
    public async Task LoadSettings_WithCorruptedJsonFile_DegradesToEmptyInsteadOfThrowing()
    {
        // Arrange：手工写坏 settings.json，模拟磁盘文件损坏
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "settings.json"), "{ 这不是合法JSON !!",
            TestContext.Current.CancellationToken);
        var svc = new FileLocalDataService(_tempDir);

        // Act + Assert：契约是静默降级为空表，绝不能抛异常把应用带崩
        Assert.Null(await svc.LoadSettingAsync("app_theme"));

        // 且降级后仍可正常写入自愈
        await svc.SaveSettingAsync("app_theme", "Dark");
        Assert.Equal("Dark", await svc.LoadSettingAsync("app_theme"));
    }
}
