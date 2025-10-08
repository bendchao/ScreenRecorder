using System;
using System.IO;
using System.Net.Http;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Drawing;

namespace ScreenRecorder
{
    /// <summary>
    /// 文件上传工具类，负责文件上传到远程服务器的功能
    /// </summary>
    public class FileUploader
    {
        private string remoteServerUrl = "http://localhost:5198/api/upload/file"; // 默认远程存储服务器地址

        public string RemoteServerUrl
        {
            get { return remoteServerUrl; }
            set { remoteServerUrl = value; }
        }

        /// <summary>
        /// 上传文件到远程服务器方法
        /// </summary>
        /// <param name="zipFilePath">压缩文件路径</param>
        /// <returns>上传是否成功</returns>
        public async Task<bool> UploadToRemoteServerAsync(string zipFilePath)
        {
            if (string.IsNullOrEmpty(zipFilePath) || !File.Exists(zipFilePath))
            {
                MessageBox.Show("找不到压缩文件，无法上传！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
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

                if (serverUrlForm.ShowDialog() == DialogResult.OK)
                {
                    remoteServerUrl = textBox.Text;
                }
                else
                {
                    return false; // 用户取消上传
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
                        using (var fileStream = File.OpenRead(zipFilePath))
                        {
                            using (var content = new MultipartFormDataContent())
                            {
                                var fileContent = new StreamContent(fileStream);
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

                    // 上传成功后删除压缩文件，添加延迟重试机制
                    try
                    {
                        // 等待一段时间让系统释放文件句柄
                        System.Threading.Thread.Sleep(500);

                        // 尝试多次删除操作
                        int maxRetries = 3;
                        int retryCount = 0;
                        bool deleted = false;

                        while (!deleted && retryCount < maxRetries)
                        {
                            try
                            {
                                if (File.Exists(zipFilePath))
                                {
                                    File.Delete(zipFilePath);
                                    deleted = true;
                                }
                            }
                            catch (IOException)
                            {
                                // 文件仍被占用，等待后重试
                                retryCount++;
                                if (retryCount < maxRetries)
                                {
                                    System.Threading.Thread.Sleep(1000);
                                }
                            }
                        }

                        if (!deleted && File.Exists(zipFilePath))
                        {
                            MessageBox.Show("无法删除压缩文件，文件可能仍被占用。您可以稍后手动删除该文件。", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("删除压缩文件时出错: " + ex.Message, "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"上传文件时出错: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }
    }
}