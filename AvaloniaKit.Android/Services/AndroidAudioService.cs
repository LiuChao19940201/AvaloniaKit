using Android.Media;
using Avalonia.Threading;
using AvaloniaKit.Services;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaKit.Android.Services;

// ══════════════════════════════════════════════════════════════════════════════
//  AndroidAudioService  （修复版）
//  修复点：
//  1. PrepareAsync 的 tcs.Task 不再用 .WaitAsync 阻塞，改为纯异步等待
//  2. ProgressChanged 事件直接在轮询线程触发（ViewModel 层用 Dispatcher.Post 处理）
//  3. Completion / Error 回调同样在 Android 主线程触发，无需额外派发
//  4. ★ 优先流式播放：先用 HttpClient 带 UA/Referer 解析 302 拿到最终 CDN 地址，
//     再让 MediaPlayer 带头直连边下边播（秒开，不用等整首下载）；
//     流式失败时回退到“下载到缓存后播本地文件”的稳妥方案
// ══════════════════════════════════════════════════════════════════════════════
public class AndroidAudioService : IAudioService, IDisposable
{
    private const string UA      = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0.0.0";
    private const string REFERER = "https://music.163.com/";

    private MediaPlayer? _player;
    private CancellationTokenSource? _pollCts;
    private string? _tmpFile;

    // ★ 手动跟 302（AllowAutoRedirect=false），保证每一跳都带 UA/Referer
    private static readonly HttpClient _http = CreateHttp();

    private static HttpClient CreateHttp()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UA);
        http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", REFERER);
        return http;
    }

    // ── 状态 ─────────────────────────────────────────────────────────────────
    public bool   IsPlaying  { get; private set; }
    public long   CurrentMs  => _player?.CurrentPosition ?? 0;
    public long   DurationMs { get; private set; }

    private double _volume = 1.0;
    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);
            _player?.SetVolume((float)_volume, (float)_volume);
        }
    }

    // ── 事件 ─────────────────────────────────────────────────────────────────
    public event EventHandler<AudioProgressEventArgs>? ProgressChanged;
    public event EventHandler? PlaybackEnded;
    public event EventHandler<string>? PlaybackError;

    // ══════════════════════════════════════════════════════════════════════════
    //  PlayAsync — 优先流式（秒开），失败回退全量下载
    public async Task PlayAsync(string url)
    {
        Stop();

        try
        {
            // ★ 先解析 302 链拿到最终 CDN 地址（只读响应头，不下正文），
            //   再让 MediaPlayer 带 UA/Referer 直连流式播放，避免整首下载卡顿
            string finalUrl = await ResolveFinalUrlAsync(url).ConfigureAwait(false);

            try
            {
                await PlayCoreAsync(p =>
                {
                    var headers = new Dictionary<string, string>
                    {
                        ["User-Agent"] = UA,
                        ["Referer"]    = REFERER,
                    };
                    p.SetDataSource(global::Android.App.Application.Context,
                        global::Android.Net.Uri.Parse(finalUrl)!, headers);
                }).ConfigureAwait(false);
                return;
            }
            catch
            {
                // 流式失败（个别 ROM/CDN 不兼容）→ 回退到下载后播本地文件
                ReleasePlayer();
            }

            string localFile = await DownloadToCacheAsync(finalUrl).ConfigureAwait(false);
            _tmpFile = localFile;
            await PlayCoreAsync(p => p.SetDataSource(localFile)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            PlaybackError?.Invoke(this, ex.Message);
        }
    }

    // ── 创建 MediaPlayer、设源、PrepareAsync、Start 的公共流程 ──────────────
    private async Task PlayCoreAsync(Action<MediaPlayer> setSource)
    {
        var player = new MediaPlayer();
        _player = player;
        player.SetAudioAttributes(new AudioAttributes.Builder()
            !.SetUsage(AudioUsageKind.Media)
            !.SetContentType(AudioContentType.Music)
            !.Build()!);

        // 完成回调
        player.Completion += (_, _) =>
        {
            IsPlaying = false;
            _pollCts?.Cancel();
            PlaybackEnded?.Invoke(this, EventArgs.Empty);
        };

        // ★ 修复：使用纯异步 TaskCompletionSource，不用 .WaitAsync 避免死锁
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        player.Prepared += (_, _) => tcs.TrySetResult(true);

        // 错误回调：Prepare 阶段让 tcs 抛出；播放阶段对外通知
        player.Error += (_, e) =>
        {
            if (!tcs.TrySetException(new Exception($"MediaPlayer error {e.What}:{e.Extra}")))
            {
                IsPlaying = false;
                _pollCts?.Cancel();
                PlaybackError?.Invoke(this, $"MediaPlayer error {e.What}:{e.Extra}");
            }
        };

        setSource(player);
        player.PrepareAsync();

        // 15 秒超时
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var timeoutTask = Task.Delay(Timeout.Infinite, timeoutCts.Token);
        var completed   = await Task.WhenAny(tcs.Task, timeoutTask).ConfigureAwait(false);

        if (completed != tcs.Task)
            throw new TimeoutException("Android MediaPlayer PrepareAsync 超时");

        await tcs.Task.ConfigureAwait(false); // 重新 await 以传播异常

        DurationMs = player.Duration;
        player.Start();
        IsPlaying = true;

        StartPolling();
    }

    // ── 解析 302 链（最多 5 跳），只读响应头，返回最终地址 ─────────────────
    private static async Task<string> ResolveFinalUrlAsync(string url)
    {
        string current = url;
        for (int i = 0; i < 5; i++)
        {
            using var resp = await _http.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, current),
                HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

            int code = (int)resp.StatusCode;
            if (code is >= 300 and < 400 && resp.Headers.Location != null)
            {
                var loc = resp.Headers.Location;
                current = loc.IsAbsoluteUri ? loc.ToString() : new Uri(new Uri(current), loc).ToString();
                continue;
            }
            break;
        }
        return current;
    }

    // ── 下载到缓存：手动跟 302（最多 5 跳），每一跳都带 UA/Referer ──────────
    private static async Task<string> DownloadToCacheAsync(string url)
    {
        string current = url;
        HttpResponseMessage resp;

        for (int i = 0; ; i++)
        {
            resp = await _http.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, current),
                HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

            int code = (int)resp.StatusCode;
            if (code is >= 300 and < 400 && resp.Headers.Location != null && i < 5)
            {
                var loc = resp.Headers.Location;
                current = loc.IsAbsoluteUri ? loc.ToString() : new Uri(new Uri(current), loc).ToString();
                resp.Dispose();
                continue;
            }
            break;
        }

        using (resp)
        {
            resp.EnsureSuccessStatusCode();

            string tmpFile = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"netease_{Guid.NewGuid():N}.mp3");

            await using var fs = System.IO.File.Create(tmpFile);
            await resp.Content.CopyToAsync(fs).ConfigureAwait(false);
            return tmpFile;
        }
    }

    public void Pause()
    {
        if (_player == null || !IsPlaying) return;
        _player.Pause();
        IsPlaying = false;
        _pollCts?.Cancel();
    }

    public void Resume()
    {
        if (_player == null || IsPlaying) return;
        _player.Start();
        IsPlaying = true;
        StartPolling();
    }

    public void Stop()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = null;

        ReleasePlayer();
        IsPlaying  = false;
        DurationMs = 0;

        // 清理上一首的缓存文件
        if (_tmpFile != null)
        {
            try { System.IO.File.Delete(_tmpFile); } catch { }
            _tmpFile = null;
        }
    }

    private void ReleasePlayer()
    {
        if (_player != null)
        {
            try { _player.Stop(); } catch { }
            _player.Release();
            _player.Dispose();
            _player = null;
        }
    }

    public void SeekTo(long ms)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            _player?.SeekTo((int)ms, MediaPlayerSeekMode.Closest);
        }
        else
        {
            _player?.SeekTo((int)ms); // 旧版兼容重载
        }
        ProgressChanged?.Invoke(this, new AudioProgressEventArgs
        {
            CurrentMs = ms, DurationMs = DurationMs
        });
    }

    // ── 进度轮询（在后台线程触发，ViewModel 用 Dispatcher.Post 处理）─────────
    private void StartPolling()
    {
        _pollCts?.Cancel();
        _pollCts = new CancellationTokenSource();
        _ = PollAsync(_pollCts.Token);
    }

    private async Task PollAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && IsPlaying)
        {
            await Task.Delay(500, ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested || _player == null) break;

            ProgressChanged?.Invoke(this, new AudioProgressEventArgs
            {
                CurrentMs  = _player.CurrentPosition,
                DurationMs = DurationMs,
            });
        }
    }

    public void Dispose() => Stop();
}
