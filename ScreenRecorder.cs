using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using SharpAvi;
using SharpAvi.Output;
namespace ScreenRecorder
{
    /// <summary>
    /// 屏幕录制器类，负责屏幕录制相关功能
    /// </summary>
    public class ScreenRecorder
    {
        private AviWriter? aviWriter;
        private IAviVideoStream? videoStream;
        private int frameRate = 10; // 默认帧率
        private int videoQuality = 20; // 视频质量参数，范围0-100
        private Rectangle screenBounds;
        private string? videoOutputPath;

        public int FrameCount { get; private set; } = 0;

        public ScreenRecorder()
        {
            screenBounds = Screen.PrimaryScreen.Bounds;
        }

        public void SetFrameRate(int rate)
        {
            frameRate = rate;
        }

        public void SetVideoQuality(int quality)
        {
            videoQuality = quality;
        }

        /// <summary>
        /// 开始录制屏幕
        /// </summary>
        /// <param name="outputPath">视频输出路径</param>
        public void StartRecording(string outputPath)
        {
            videoOutputPath = outputPath;
            FrameCount = 0;

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
        }

        /// <summary>
        /// 捕获一帧屏幕图像
        /// </summary>
        public void CaptureFrame()
        {
            // 检查录制器和视频流是否有效
            if (aviWriter == null || videoStream == null)
                return;

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

                        // 再次检查视频流是否有效，防止在异步操作中被释放
                        if (videoStream != null && aviWriter != null)
                        {
                            videoStream.WriteFrame(true, jpegBytes, 0, jpegBytes.Length);
                            FrameCount++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 不抛出异常，只记录错误，防止中断录制过程
                Console.WriteLine("捕获屏幕帧时出错: " + ex.Message);
            }
        }

        /// <summary>
        /// 停止录制并保存视频
        /// </summary>
        public void StopRecording()
        {
            // 关闭AVI视频写入器
            if (aviWriter != null)
            {
                aviWriter.Close();
                aviWriter = null;
                videoStream = null;
            }
        }

        /// <summary>
        /// 获取指定图像格式的编码器信息
        /// </summary>
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
    }
}