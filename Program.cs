using System;
using System.Threading.Tasks;

namespace ScreenRecorder
{
    internal static class Program
    {
        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main()
        {
            try
            {
                Console.WriteLine("启动控制台模式的屏幕录制工具...");
                ConsoleApp app = new ConsoleApp();
                app.RunAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"程序执行出错: {ex.Message}");
                Console.WriteLine($"堆栈跟踪: {ex.StackTrace}");
                Console.WriteLine("按任意键退出...");
                Console.ReadKey();
            }
        }
    }
}