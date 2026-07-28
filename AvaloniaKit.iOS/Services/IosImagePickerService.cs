using AvaloniaKit.Services;
using Foundation;
using System;
using System.IO;
using System.Threading.Tasks;
using UIKit;

namespace AvaloniaKit.iOS.Services;

// ══════════════════════════════════════════════════════════════════════════════
//  IosImagePickerService — 相册选图（UIImagePickerController）
//  · 选中图片编码为 PNG 复制进 MemoryStream（seekable，供共享层
//    Bitmap.DecodeToWidth 先读头再回退解码，与 Android 端处理一致）
// ══════════════════════════════════════════════════════════════════════════════
public class IosImagePickerService : IImagePickerService
{
    // UIWindow 延迟访问器：注册发生在组合根构建时（窗口尚未创建）
    private readonly Func<UIWindow?> _getWindow;

    public IosImagePickerService(Func<UIWindow?> windowProvider)
    {
        _getWindow = windowProvider;
    }

    public Task<Stream?> PickImageAsync()
    {
        var tcs = new TaskCompletionSource<Stream?>();

        UIApplication.SharedApplication.InvokeOnMainThread(() =>
        {
            try
            {
#pragma warning disable CA1422 // PhotoLibrary 在 iOS14+ 标记过时，功能仍可用（演示场景保持简单）
                var root = _getWindow()?.RootViewController;
                if (root is null ||
                    !UIImagePickerController.IsSourceTypeAvailable(
                        UIImagePickerControllerSourceType.PhotoLibrary))
                {
                    tcs.TrySetResult(null);
                    return;
                }

                var picker = new UIImagePickerController
                {
                    SourceType = UIImagePickerControllerSourceType.PhotoLibrary,
                };
#pragma warning restore CA1422

                picker.FinishedPickingMedia += (_, e) =>
                {
                    try
                    {
                        var image = e.OriginalImage;
                        var data = image?.AsPNG();
                        if (data is null)
                        {
                            tcs.TrySetResult(null);
                        }
                        else
                        {
                            // 复制为 seekable MemoryStream，脱离 NSData 生命周期
                            var ms = new MemoryStream(data.ToArray());
                            tcs.TrySetResult(ms);
                        }
                    }
                    catch
                    {
                        tcs.TrySetResult(null);
                    }
                    finally
                    {
                        picker.DismissViewController(true, null);
                    }
                };

                picker.Canceled += (_, _) =>
                {
                    tcs.TrySetResult(null);
                    picker.DismissViewController(true, null);
                };

                root.PresentViewController(picker, true, null);
            }
            catch
            {
                tcs.TrySetResult(null);
            }
        });

        return tcs.Task;
    }
}
