using System;
using System.Threading.Tasks;

namespace SimpleTest
{
    class SimpleConsole
    {
        [STAThread]
        static async Task Main(string[] args)
        {
            Console.WriteLine("这是一个简单的测试控制台应用");
            Console.WriteLine("按任意键继续...");
            Console.ReadKey(true);
            
            // 测试异步操作
            Console.WriteLine("测试异步操作...");
            await Task.Delay(1000);
            Console.WriteLine("异步操作完成");
            
            // 测试按键输入循环
            Console.WriteLine("按 'Q' 键退出");
            while (true)
            {
                if (Console.KeyAvailable)
                {
                    ConsoleKeyInfo key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.Q)
                    {
                        break;
                    }
                    Console.WriteLine($"你按下了: {key.Key}");
                }
                await Task.Delay(10);
            }
            
            Console.WriteLine("程序已退出");
            Console.WriteLine("按任意键关闭...");
            Console.ReadKey();
        }
    }
}