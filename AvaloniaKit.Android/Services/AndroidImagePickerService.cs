using Android.App;
using Android.Content;
using AvaloniaKit.Services;
using System;
using System.IO;
using System.Threading.Tasks;

namespace AvaloniaKit.Android.Services;

public class AndroidImagePickerService : IImagePickerService
{
    public const int RequestCode = 2001;

    private static TaskCompletionSource<Stream?>? _tcs;

    // Activity 延迟访问器：注册发生在 Application.OnCreate（Activity 尚未创建）
    private readonly Func<Activity> _getActivity;

    public AndroidImagePickerService(Func<Activity> activityProvider)
    {
        _getActivity = activityProvider;
    }

    public Task<Stream?> PickImageAsync()
    {
        _tcs = new TaskCompletionSource<Stream?>();

        var intent = new Intent(Intent.ActionPick);
        intent.SetType("image/*");
        _getActivity().StartActivityForResult(intent, RequestCode);

        return _tcs.Task;
    }

    public static void HandleActivityResult(
        int requestCode, Result resultCode, Intent? data, ContentResolver contentResolver)
    {
        if (requestCode != RequestCode || _tcs is null) return;

        if (resultCode == Result.Ok && data?.Data is global::Android.Net.Uri uri)
        {
            try
            {
                using var rawStream = contentResolver.OpenInputStream(uri);
                if (rawStream is null)
                {
                    _tcs.TrySetResult(null);
                    return;
                }

                // ContentResolver 流是 non-seekable 的 Java InputStream
                // Bitmap.DecodeToWidth 需要 seekable 流来：
                //   1) 先读取图片头获取原始尺寸
                //   2) seek 回起点再按缩放比解码
                // 如果流不可 seek → Skia 回退到全分辨率解码 → 存入数 MB PNG → 下次 OOM
                var memoryStream = new MemoryStream();
                rawStream.CopyTo(memoryStream);
                memoryStream.Position = 0;
                _tcs.TrySetResult(memoryStream);
            }
            catch
            {
                _tcs.TrySetResult(null);
            }
        }
        else
        {
            _tcs.TrySetResult(null);
        }

        _tcs = null;
    }
}