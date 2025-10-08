using System;
using System.Timers;
using System.Windows.Forms;
using System.IO;

namespace ScreenRecorder
{
    /// <summary>
    /// 自动上传定时器类，负责定时检查和执行自动上传功能
    /// </summary>
    public class AutoUploadTimer
    {
        private System.Timers.Timer? timer;
        private DateTime lastAutoUploadTime;
        private int autoUploadIntervalMinutes = 10; // 自动上传间隔（分钟）
        private FileCompressor fileCompressor;
        private FileUploader fileUploader;
        private Form mainForm;

        public event EventHandler? AutoUploadRequired;

        public int AutoUploadIntervalMinutes
        {
            get { return autoUploadIntervalMinutes; }
            set { autoUploadIntervalMinutes = value; }
        }

        public AutoUploadTimer(Form form, FileCompressor compressor, FileUploader uploader)
        {
            mainForm = form;
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
            mainForm.Invoke((MethodInvoker)delegate
            {
                CheckAutoUpload();
            });
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
        public void PerformAutoUpload(string videoOutputPath, string keylogPath)
        {
            if (string.IsNullOrEmpty(videoOutputPath) || string.IsNullOrEmpty(keylogPath))
                return;

            try
            {
                // 压缩并上传当前的视频文件
                string autoUploadZipFilePath = fileCompressor.CompressFilesForAutoUpload(videoOutputPath, keylogPath);
                if (!string.IsNullOrEmpty(autoUploadZipFilePath))
                {
                    // 因为是在UI线程中，所以需要使用Wait来确保上传完成
                    fileUploader.UploadToRemoteServerAsync(autoUploadZipFilePath).Wait();
                }

                // 通知用户自动上传已完成
                MessageBox.Show("自动上传完成，继续录制中...", "自动上传", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"自动上传过程中出错: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}