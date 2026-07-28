using AVFoundation;
using AvaloniaKit.Services;
using CoreMedia;
using Foundation;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaKit.iOS.Services;

// ══════════════════════════════════════════════════════════════════════════════
//  IosAudioService — AVPlayer 实现（对齐 Desktop/Android 端行为）
//  · 播放：AVPlayer 直连流式播放，自动跟随网易 outer/url → CDN 的 302 链；
//    经 AVUrlAsset 注入 UA/Referer 请求头（部分 CDN 校验来源）
//  · 进度：System.Threading.Timer 每 500ms 推送 ProgressChanged
//    （后台线程触发，VM 端统一 Dispatcher.UIThread.Post 回 UI 线程）
//  · 结束/出错：DidPlayToEndTime / FailedToPlayToEndTime 通知
//  · AVAudioSession 设为 Playback 分类：静音拨键下仍可出声，与音乐类 App 一致
// ══════════════════════════════════════════════════════════════════════════════
public class IosAudioService : IAudioService, IDisposable
{
    private readonly object _lock = new();
    private AVPlayer? _player;
    private AVPlayerItem? _item;
    private NSObject? _endObserver;
    private NSObject? _failObserver;
    private Timer? _timer;

    public IosAudioService()
    {
        try
        {
            var session = AVAudioSession.SharedInstance();
            session.SetCategory(AVAudioSessionCategory.Playback);
            session.SetActive(true);
        }
        catch { /* 音频会话配置失败不阻断启动 */ }
    }

    // ── 状态 ─────────────────────────────────────────────────────────────────
    public bool IsPlaying { get; private set; }

    public long CurrentMs
    {
        get
        {
            lock (_lock)
            {
                var t = _player?.CurrentTime;
                return t is { IsInvalid: false, IsIndefinite: false }
                    ? (long)(t.Value.Seconds * 1000) : 0;
            }
        }
    }

    public long DurationMs
    {
        get
        {
            lock (_lock)
            {
                var d = _item?.Duration;
                return d is { IsInvalid: false, IsIndefinite: false }
                    ? (long)(d.Value.Seconds * 1000) : 0;
            }
        }
    }

    private double _volume = 1.0;
    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);
            lock (_lock)
            {
                if (_player != null) _player.Volume = (float)_volume;
            }
        }
    }

    // ── 事件 ─────────────────────────────────────────────────────────────────
    public event EventHandler<AudioProgressEventArgs>? ProgressChanged;
    public event EventHandler? PlaybackEnded;
    public event EventHandler<string>? PlaybackError;

    // ══════════════════════════════════════════════════════════════════════════
    //  PlayAsync
    // ══════════════════════════════════════════════════════════════════════════
    public Task PlayAsync(string url)
    {
        Stop(); // 停止上一首

        try
        {
            var nsUrl = NSUrl.FromString(url);
            if (nsUrl is null)
            {
                PlaybackError?.Invoke(this, "无效的音频地址");
                return Task.CompletedTask;
            }

            // 注入 UA/Referer（与 Desktop/Android 一致，部分 CDN 校验来源）
            var headers = new NSDictionary(
                (NSString)"User-Agent",
                (NSString)"Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15",
                (NSString)"Referer",
                (NSString)"https://music.163.com/");
            var optionsDict = new NSMutableDictionary
            {
                { (NSString)"AVURLAssetHTTPHeaderFieldsKey", headers },
            };
            var asset = AVUrlAsset.Create(nsUrl, new AVUrlAssetOptions(optionsDict));

            var item = AVPlayerItem.FromAsset(asset);
            var player = new AVPlayer(item) { Volume = (float)_volume };

            lock (_lock)
            {
                _item = item;
                _player = player;

                _endObserver = NSNotificationCenter.DefaultCenter.AddObserver(
                    AVPlayerItem.DidPlayToEndTimeNotification, OnPlayedToEnd, item);
                _failObserver = NSNotificationCenter.DefaultCenter.AddObserver(
                    AVPlayerItem.ItemFailedToPlayToEndTimeNotification, OnPlayFailed, item);

                player.Play();
                IsPlaying = true;
            }

            StartTimer();
        }
        catch (Exception ex)
        {
            PlaybackError?.Invoke(this, ex.Message);
        }
        return Task.CompletedTask;
    }

    private void OnPlayedToEnd(NSNotification _)
    {
        IsPlaying = false;
        StopTimer();
        PlaybackEnded?.Invoke(this, EventArgs.Empty);
    }

    private void OnPlayFailed(NSNotification notification)
    {
        IsPlaying = false;
        StopTimer();
        string msg = _item?.Error?.LocalizedDescription ?? "播放失败";
        PlaybackError?.Invoke(this, msg);
    }

    public void Pause()
    {
        lock (_lock)
        {
            if (_player == null || !IsPlaying) return;
            _player.Pause();
            IsPlaying = false;
        }
        StopTimer();
    }

    public void Resume()
    {
        lock (_lock)
        {
            if (_player == null || IsPlaying) return;
            _player.Play();
            IsPlaying = true;
        }
        StartTimer();
    }

    public void Stop()
    {
        StopTimer();
        lock (_lock)
        {
            if (_endObserver != null)
            {
                NSNotificationCenter.DefaultCenter.RemoveObserver(_endObserver);
                _endObserver = null;
            }
            if (_failObserver != null)
            {
                NSNotificationCenter.DefaultCenter.RemoveObserver(_failObserver);
                _failObserver = null;
            }
            try { _player?.Pause(); } catch { }
            _player?.Dispose();
            _player = null;
            _item?.Dispose();
            _item = null;
            IsPlaying = false;
        }
    }

    public void SeekTo(long ms)
    {
        long current, duration;
        lock (_lock)
        {
            if (_player == null) return;
            duration = DurationMsUnsafe();
            current = Math.Clamp(ms, 0, Math.Max(0, duration));
            _player.Seek(CMTime.FromSeconds(current / 1000.0, 1000));
        }
        ProgressChanged?.Invoke(this, new AudioProgressEventArgs
        {
            CurrentMs = current, DurationMs = duration,
        });
    }

    private long DurationMsUnsafe()
    {
        var d = _item?.Duration;
        return d is { IsInvalid: false, IsIndefinite: false }
            ? (long)(d.Value.Seconds * 1000) : 0;
    }

    // ── 进度定时器（500ms，后台线程；VM 端负责调度回 UI 线程）───────────────
    private void StartTimer()
    {
        StopTimer();
        _timer = new Timer(_ =>
        {
            long cur, dur;
            lock (_lock)
            {
                if (_player == null || !IsPlaying) return;
                var t = _player.CurrentTime;
                cur = t is { IsInvalid: false, IsIndefinite: false }
                    ? (long)(t.Seconds * 1000) : 0;
                dur = DurationMsUnsafe();
            }
            ProgressChanged?.Invoke(this, new AudioProgressEventArgs
            {
                CurrentMs = cur, DurationMs = dur,
            });
        }, null, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));
    }

    private void StopTimer()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public void Dispose() => Stop();
}
