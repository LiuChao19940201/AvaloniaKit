namespace AvaloniaKit.Services;

public static class ServiceLocator
{
    public static IStatusBarService?    StatusBarService    { get; set; }
    public static IDeviceService?       DeviceService       { get; set; }
    public static IImagePickerService?  ImagePickerService  { get; set; }
    public static ILocalDataService?    LocalDataService    { get; set; }
    public static IAudioService?        AudioService        { get; set; }
    public static IDouyinService?       DouyinService       { get; set; }
}
