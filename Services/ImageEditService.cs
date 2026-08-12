using System;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace HyperViewer.Services
{
    /// <summary>
    /// 像素级图像处理服务 (方案 A, 零依赖): 解码 → BGRA8 像素数组 → 裁剪/调色 → 保存。
    /// </summary>
    public static class ImageEditService
    {
        // 解码上限: 超出后按比例降采样 (避免巨额内存), 保存分辨率以实际解码尺寸为准
        private const uint MaxDecodeLongEdge = 8192;

        /// <summary>
        /// 解码整图 (手动应用 EXIF 方向, 与主视图一致), 返回像素缓冲。
        /// </summary>
        public static async Task<EditableImage> LoadAsync(StorageFile file)
        {
            using (var stream = await file.OpenReadAsync())
            {
                var decoder = await BitmapDecoder.CreateAsync(stream);
                uint srcW = decoder.PixelWidth;
                uint srcH = decoder.PixelHeight;
                uint decW = srcW;
                uint decH = srcH;

                var transform = new BitmapTransform();
                if (Math.Max(srcW, srcH) > MaxDecodeLongEdge)
                {
                    double scale = (double)MaxDecodeLongEdge / Math.Max(srcW, srcH);
                    decW = (uint)Math.Max(1, Math.Round(srcW * scale));
                    decH = (uint)Math.Max(1, Math.Round(srcH * scale));
                    transform.ScaledWidth = decW;
                    transform.ScaledHeight = decH;
                }

                // 约束: ApplyExifOrientation 是 14393+ 成员, 10240 需手动转向
                var data = await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Ignore,
                    transform,
                    ExifOrientationMode.IgnoreExifOrientation,
                    ColorManagementMode.DoNotColorManage);

                var pixels = data.DetachPixelData();
                int orientation = await ReadOrientationAsync(decoder);
                var oriented = ApplyOrientation(pixels, (int)decW, (int)decH, orientation);

                return new EditableImage(oriented.Pixels, oriented.Width, oriented.Height, srcW, srcH);
            }
        }

        private static async Task<int> ReadOrientationAsync(BitmapDecoder decoder)
        {
            try
            {
                var props = await decoder.BitmapProperties.GetPropertiesAsync(new[] { "System.Photo.Orientation" });
                if (props.TryGetValue("System.Photo.Orientation", out var v) && v.Value != null)
                    return Convert.ToInt32(v.Value);
            }
            catch { /* 无 EXIF 方向 */ }
            return 1;
        }

        /// <summary>
        /// 按 EXIF Orientation (1-8) 对 BGRA8 像素做旋转/翻转。
        /// </summary>
        private static OrientedPixels ApplyOrientation(byte[] src, int w, int h, int orientation)
        {
            switch (orientation)
            {
                case 2: // 水平翻转
                    return new OrientedPixels(Flip(src, w, h, true), w, h);
                case 3: // 旋转 180
                    return new OrientedPixels(Rotate180(src, w, h), w, h);
                case 4: // 垂直翻转
                    return new OrientedPixels(Flip(src, w, h, false), w, h);
                case 5: // 转置 (沿主对角线镜像)
                    return new OrientedPixels(Transpose(src, w, h, false), h, w);
                case 6: // 顺时针 90
                    return new OrientedPixels(Rotate90(src, w, h, true), h, w);
                case 7: // 转置+翻转 (沿副对角线镜像)
                    return new OrientedPixels(Transpose(src, w, h, true), h, w);
                case 8: // 逆时针 90
                    return new OrientedPixels(Rotate90(src, w, h, false), h, w);
                default:
                    return new OrientedPixels(src, w, h);
            }
        }

        private struct OrientedPixels
        {
            public byte[] Pixels;
            public int Width;
            public int Height;

            public OrientedPixels(byte[] pixels, int width, int height)
            {
                Pixels = pixels;
                Width = width;
                Height = height;
            }
        }

        private static byte[] Flip(byte[] src, int w, int h, bool horizontal)
        {
            var dst = new byte[src.Length];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int sx = horizontal ? w - 1 - x : x;
                    int sy = horizontal ? y : h - 1 - y;
                    int si = (sy * w + sx) * 4;
                    int di = (y * w + x) * 4;
                    dst[di] = src[si]; dst[di + 1] = src[si + 1];
                    dst[di + 2] = src[si + 2]; dst[di + 3] = src[si + 3];
                }
            }
            return dst;
        }

        private static byte[] Rotate180(byte[] src, int w, int h)
        {
            var dst = new byte[src.Length];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int si = ((h - 1 - y) * w + (w - 1 - x)) * 4;
                    int di = (y * w + x) * 4;
                    dst[di] = src[si]; dst[di + 1] = src[si + 1];
                    dst[di + 2] = src[si + 2]; dst[di + 3] = src[si + 3];
                }
            }
            return dst;
        }

        /// <summary>
        /// 旋转 90°。clockwise=true 为顺时针。输出尺寸 (h, w)。
        /// </summary>
        private static byte[] Rotate90(byte[] src, int w, int h, bool clockwise)
        {
            var dst = new byte[src.Length];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    // 目标像素 (dx, dy) = 旋转后的位置
                    int dx = clockwise ? h - 1 - y : y;
                    int dy = clockwise ? x : w - 1 - x;
                    int si = (y * w + x) * 4;
                    int di = (dy * h + dx) * 4;
                    dst[di] = src[si]; dst[di + 1] = src[si + 1];
                    dst[di + 2] = src[si + 2]; dst[di + 3] = src[si + 3];
                }
            }
            return dst;
        }

        /// <summary>
        /// 沿对角线镜像。clockwise=false 为主对角线 (转置), true 为副对角线。输出尺寸 (h, w)。
        /// </summary>
        private static byte[] Transpose(byte[] src, int w, int h, bool anti)
        {
            var dst = new byte[src.Length];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int dx = anti ? h - 1 - y : y;
                    int dy = anti ? w - 1 - x : x;
                    int si = (y * w + x) * 4;
                    int di = (dy * h + dx) * 4;
                    dst[di] = src[si]; dst[di + 1] = src[si + 1];
                    dst[di + 2] = src[si + 2]; dst[di + 3] = src[si + 3];
                }
            }
            return dst;
        }

        /// <summary>
        /// 把像素保存为新文件 (PNG 默认; JPEG 需指定编码器)。
        /// </summary>
        public static async Task SaveAsync(StorageFile target, byte[] pixels, int width, int height, Guid encoderId = default(Guid))
        {
            if (encoderId == default(Guid)) encoderId = BitmapEncoder.PngEncoderId;
            using (var stream = await target.OpenAsync(FileAccessMode.ReadWrite))
            {
                var encoder = await BitmapEncoder.CreateAsync(encoderId, stream);
                encoder.SetPixelData(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Ignore,
                    (uint)width,
                    (uint)height,
                    96,
                    96,
                    pixels);
                await encoder.FlushAsync();
            }
        }

        /// <summary>
        /// 就地调整亮度/对比度/饱和度并应用滤镜 (BGRA8, 原地修改)。
        /// </summary>
        public static void ApplyAdjustments(
            byte[] px,
            int brightness,
            int contrast,
            int saturation,
            AdjustFilter filter)
        {
            if (px == null || px.Length == 0) return;
            float b = brightness * 2.55f;                 // -255..255
            float cs = 1f + contrast / 100f;              // 0..2
            float ss = 1f + saturation / 100f;            // 0..2

            int n = px.Length / 4;
            for (int i = 0; i < n; i++)
            {
                int o = i * 4;
                float bl = px[o];
                float gr = px[o + 1];
                float rd = px[o + 2];

                // 对比度 (中心 128) → 亮度 → 饱和度 (luma)
                bl = (bl - 128f) * cs + 128f + b;
                gr = (gr - 128f) * cs + 128f + b;
                rd = (rd - 128f) * cs + 128f + b;

                if (ss != 1f)
                {
                    float luma = 0.299f * rd + 0.587f * gr + 0.114f * bl;
                    rd = luma + (rd - luma) * ss;
                    gr = luma + (gr - luma) * ss;
                    bl = luma + (bl - luma) * ss;
                }

                switch (filter)
                {
                    case AdjustFilter.Grayscale:
                        float g2 = 0.299f * rd + 0.587f * gr + 0.114f * bl;
                        rd = g2; gr = g2; bl = g2;
                        break;
                    case AdjustFilter.Invert:
                        rd = 255f - rd; gr = 255f - gr; bl = 255f - bl;
                        break;
                    case AdjustFilter.Sepia:
                        float r2 = 0.393f * rd + 0.769f * gr + 0.189f * bl;
                        float g3 = 0.349f * rd + 0.686f * gr + 0.168f * bl;
                        float b3 = 0.272f * rd + 0.534f * gr + 0.131f * bl;
                        rd = r2; gr = g3; bl = b3;
                        break;
                }

                px[o] = Clamp(bl);
                px[o + 1] = Clamp(gr);
                px[o + 2] = Clamp(rd);
            }
        }

        /// <summary>
        /// 2x2 盒式降采样直到长边不超过 maxEdge, 用于调色实时预览。
        /// </summary>
        public static EditableImage Downsample(byte[] src, int w, int h, int maxEdge)
        {
            int cw = w;
            int ch = h;
            byte[] cur = src;
            while (Math.Max(cw, ch) > maxEdge && cw > 1 && ch > 1)
            {
                int nw = (cw + 1) / 2;
                int nh = (ch + 1) / 2;
                var nxt = new byte[nw * nh * 4];
                for (int y = 0; y < nh; y++)
                {
                    for (int x = 0; x < nw; x++)
                    {
                        int b = 0, g = 0, r = 0, a = 0, cnt = 0;
                        for (int dy = 0; dy < 2 && y * 2 + dy < ch; dy++)
                        {
                            for (int dx = 0; dx < 2 && x * 2 + dx < cw; dx++)
                            {
                                int si = ((y * 2 + dy) * cw + x * 2 + dx) * 4;
                                b += cur[si];
                                g += cur[si + 1];
                                r += cur[si + 2];
                                a += cur[si + 3];
                                cnt++;
                            }
                        }
                        int di = (y * nw + x) * 4;
                        nxt[di] = (byte)(b / cnt);
                        nxt[di + 1] = (byte)(g / cnt);
                        nxt[di + 2] = (byte)(r / cnt);
                        nxt[di + 3] = (byte)(a / cnt);
                    }
                }
                cur = nxt;
                cw = nw;
                ch = nh;
            }
            return new EditableImage(cur, cw, ch, (uint)w, (uint)h);
        }

        private static byte Clamp(float v)
        {
            if (v < 0f) return 0;
            if (v > 255f) return 255;
            return (byte)v;
        }
    }

    /// <summary>
    /// 滤镜预设。
    /// </summary>
    public enum AdjustFilter
    {
        None = 0,
        Grayscale = 1,
        Invert = 2,
        Sepia = 3,
    }

    /// <summary>
    /// 可编辑像素缓冲: 支持裁剪 (整图拷贝) 与后续就地调色。
    /// </summary>
    public sealed class EditableImage
    {
        public byte[] Pixels { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public uint SourceWidth { get; }
        public uint SourceHeight { get; }

        public EditableImage(byte[] pixels, int width, int height, uint sourceWidth, uint sourceHeight)
        {
            Pixels = pixels;
            Width = width;
            Height = height;
            SourceWidth = sourceWidth;
            SourceHeight = sourceHeight;
        }

        /// <summary>
        /// 按矩形裁剪 (像素坐标, 必须落在范围内), 返回新缓冲。
        /// </summary>
        public EditableImage Crop(int px, int py, int pw, int ph)
        {
            px = Math.Max(0, Math.Min(px, Width - 1));
            py = Math.Max(0, Math.Min(py, Height - 1));
            pw = Math.Max(1, Math.Min(pw, Width - px));
            ph = Math.Max(1, Math.Min(ph, Height - py));

            var outPixels = new byte[pw * ph * 4];
            for (int row = 0; row < ph; row++)
            {
                int srcOff = ((py + row) * Width + px) * 4;
                Array.Copy(Pixels, srcOff, outPixels, row * pw * 4, pw * 4);
            }
            return new EditableImage(outPixels, pw, ph, SourceWidth, SourceHeight);
        }
    }
}