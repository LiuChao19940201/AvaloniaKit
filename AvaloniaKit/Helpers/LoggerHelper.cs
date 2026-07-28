using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace AvaloniaKit.Helpers;

// ══════════════════════════════════════════════════════════════════════════
//  LoggerHelper — 轻量文件日志（三端共用）
//  · 单例 + 状态锁 + 按天分文件（Logs/yyyy-MM-dd.log），自动附带调用方
//    类文件/函数名/行号（CallerInfo 编译期注入，AOT 下同样可用）
//  · 日志根目录：默认程序目录；Android 等程序目录不可写的平台在首次使用前
//    通过 LogRootOverride 指向可写目录（如 FilesDir）
//  · 同步镜像一份到 Console：Desktop 调试输出 / Android logcat(mono-stdout) /
//    Browser 浏览器控制台，Web 端虚拟文件系统不落盘也能看到日志
//  · CompileMode：双重判据识别编译模式——开启 PublishAot 后普通 build 的
//    IsDynamicCodeSupported 也是 false，必须叠加 Assembly.Location 判空
//    （NativeAOT 的 IL 已编入原生镜像，Location 恒为空）
// ══════════════════════════════════════════════════════════════════════════
public class LoggerHelper
{
    private static readonly object _lock = new();
    private static LoggerHelper? _instance;

    private readonly string _logFile;

    public static LoggerHelper Instance
    {
        get
        {
            _instance ??= new LoggerHelper();
            return _instance;
        }
    }

    /// <summary>日志根目录覆盖（须在首次访问 Instance 前设置；null = 程序目录）</summary>
    public static string? LogRootOverride { get; set; }

    public LoggerHelper()
    {
        string root = LogRootOverride ?? AppDomain.CurrentDomain.BaseDirectory;
        string logDir = Path.Combine(root, "Logs");
        if (!Directory.Exists(logDir))
        {
            Directory.CreateDirectory(logDir);
        }

        _logFile = Path.Combine(logDir, DateTime.Now.ToString("yyyy-MM-dd") + ".log");
    }

    /// <summary>当前构建配置（随共享工程编译配置而定）</summary>
    public static string BuildConfig =>
#if DEBUG
        "Debug";
#else
        "Release";
#endif

    /// <summary>
    /// 编译模式描述：
    /// · AOT —— 无 JIT 且程序集已编入原生镜像（NativeAOT 产物）
    /// · JIT —— 存在即时编译（日常 build/F5）
    /// · 解释执行 —— 无 JIT 但 IL 独立存在（Browser WASM Interpreter）
    /// </summary>
    public static string CompileMode
    {
        get
        {
            bool noDynamicCode = !RuntimeFeature.IsDynamicCodeSupported;
            bool ilEmbedded = string.IsNullOrEmpty(typeof(LoggerHelper).Assembly.Location);

            if (noDynamicCode && ilEmbedded) return "AOT（原生编译）";
            if (RuntimeFeature.IsDynamicCodeCompiled) return "JIT（即时编译）";
            return OperatingSystem.IsBrowser()
                ? "解释执行（WASM Interpreter，无 JIT）"
                : "JIT（托管，动态代码受限）";
        }
    }

    /// <summary>
    /// 各平台启动时统一调用：打印 .NET 版本 / 位数 / 构建配置 / JIT 或 AOT 编译模式
    /// </summary>
    public void WriteStartup(string platformName,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        string msg = $"平台 .NET {Environment.Version} 软件 AvaloniaKit.{platformName} " +
                     $"以 {(Environment.Is64BitProcess ? "64" : "32")}位 {BuildConfig} 模式启动， " +
                     $"编译模式 {CompileMode} ！";
        Write(msg, true, memberName, sourceFilePath, sourceLineNumber);
    }

    public void Write(string msg, bool hasTime = true,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        lock (_lock)
        {
            string time = hasTime ? "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "]" : string.Empty;

            string content = string.Empty;
            if (!string.IsNullOrEmpty(sourceFilePath))
            {
                content += $"\r\n在 {sourceFilePath} 类中, ";
            }
            if (!string.IsNullOrEmpty(memberName))
            {
                content += $"\r\n函数名称 => {memberName}, ";
            }
            if (sourceLineNumber > 0)
            {
                content += $"第 {sourceLineNumber} 行, ";
            }

            string writeMsg = $"{time}{content}\r\n内容 => {msg}{Environment.NewLine}{Environment.NewLine}";

            try
            {
                using FileStream fileStream = new(_logFile, FileMode.Append, FileAccess.Write, FileShare.Read);
                using StreamWriter writer = new(fileStream);
                writer.Write(writeMsg);
            }
            catch { /* 文件系统不可写（如部分沙箱环境）时仅保留控制台输出 */ }

            // 镜像到控制台：Desktop 调试输出 / Android logcat / Browser 浏览器控制台
            try { Console.WriteLine(writeMsg); } catch { }
        }
    }
}
