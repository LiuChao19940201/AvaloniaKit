using UIKit;

namespace AvaloniaKit.iOS;

public class Application
{
    // 应用主入口：如需替换 AppDelegate，可在此指定其他委托类型
    private static void Main(string[] args)
    {
        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}
