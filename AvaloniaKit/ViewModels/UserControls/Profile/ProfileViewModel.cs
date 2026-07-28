using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using AvaloniaKit.Messages;
using AvaloniaKit.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.IO;
using System.Threading.Tasks;

namespace AvaloniaKit.ViewModels.UserControls.Profile;

public partial class ProfileViewModel : PageViewModelBase
{
    public override string Title => "我";
    public override bool ShowTitleBar => false;

    private readonly ILocalDataService? _localData;
    private readonly IImagePickerService? _imagePicker;

    [ObservableProperty] private string _nickname = "Hello World";
    [ObservableProperty] private string _wechatId = "LC862739233";
    [ObservableProperty] private int _friendCount = 2;
    [ObservableProperty] private Bitmap? _avatarBitmap;
    [ObservableProperty] private bool _hasAvatar;

    /// <summary>头像缩略图宽度（70dp显示 × 3倍屏 ≈ 200px 足够清晰）</summary>
    private const int AvatarDecodeWidth = 200;

    [ObservableProperty] private bool _isDarkTheme;

    public ProfileViewModel(
        ILocalDataService? localDataService = null,
        IImagePickerService? imagePickerService = null)
    {
        _localData = localDataService;
        _imagePicker = imagePickerService;

        _ = LoadAvatarOnStartupAsync();

        // 初始化时同步当前主题
        var app = Application.Current;
        if (app is not null)
            IsDarkTheme = app.ActualThemeVariant == ThemeVariant.Dark;
    }

    private async Task LoadAvatarOnStartupAsync()
    {
        try
        {
            if (_localData is null) return;

            var bytes = await _localData.LoadAvatarAsync();
            if (bytes is null || bytes.Length == 0) return;

            using var ms = new MemoryStream(bytes);
            // 已保存的是缩略图 PNG，直接解码即可
            AvatarBitmap = new Bitmap(ms);
            HasAvatar = true;
        }
        catch
        {
            // 数据损坏或格式不兼容时静默忽略
        }
    }

    [RelayCommand]
    private async Task PickAvatar()
    {
        if (_imagePicker is null) return;

        var stream = await _imagePicker.PickImageAsync();
        if (stream is null) return;

        try
        {
            using (stream)
            {
                // 只解码为缩略图，而非原始分辨率
                // 4000×3000 原图 → 48MB 像素 → WASM 直接 OOM 卡死
                // DecodeToWidth(200) → ~200×150 → 120KB 像素 → 安全
                AvatarBitmap = Bitmap.DecodeToWidth(stream, AvatarDecodeWidth,
                    BitmapInterpolationMode.MediumQuality);
                HasAvatar = true;
            }

            // 将缩略图编码为 PNG 再持久化（~10-30KB，远小于原始 5MB）
            if (_localData is not null)
            {
                using var saveStream = new MemoryStream();
                // Avalonia 12：旧 Save(Stream, int?) 已过时，改用 BitmapEncoderOptions 重载
                AvatarBitmap.Save(saveStream, PngBitmapEncoderOptions.Default); // 编码为 PNG
                await _localData.SaveAvatarAsync(saveStream.ToArray());
            }
        }
        catch
        {
            // 图片格式不支持等异常
        }
    }

    [RelayCommand]
    private void OpenService()
    {
        WeakReferenceMessenger.Default.Send(new NavigateToServiceMessage());
    }

    [RelayCommand]
    private void OpenFavorites() { }

    [RelayCommand]
    private void OpenMoments() { }

    [RelayCommand]
    private void OpenChannels() { }

    [RelayCommand]
    private void OpenOrders() { }

    [RelayCommand]
    private void OpenEmoji() { }

    [RelayCommand]
    private void OpenSettings() { }

    // 切换主题 + 持久化
    [RelayCommand]
    private async Task OpenQrCode()
    {
        var app = Application.Current;
        if (app is null) return;

        IsDarkTheme = app.ActualThemeVariant != ThemeVariant.Dark;
        app.RequestedThemeVariant = IsDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    [RelayCommand]
    private void AddStatus() { }

    [RelayCommand]
    private void OpenFriends() { }
}