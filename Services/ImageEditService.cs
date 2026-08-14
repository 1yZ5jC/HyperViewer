using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Media.Imaging;

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

        internal static byte[] Flip(byte[] src, int w, int h, bool horizontal)
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

        internal static byte[] Rotate180(byte[] src, int w, int h)
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
        internal static byte[] Rotate90(byte[] src, int w, int h, bool clockwise)
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
                    case AdjustFilter.Warm:
                        rd *= 1.10f; bl *= 0.92f;
                        break;
                    case AdjustFilter.Cool:
                        rd *= 0.92f; bl *= 1.10f;
                        break;
                    case AdjustFilter.Film:
                        // 胶片感: 提灰 + 轻微去饱和 + 青蓝阴影
                        float fg = (rd + gr + bl) / 3f;
                        rd = fg * 0.82f + rd * 0.18f;
                        gr = fg * 0.82f + gr * 0.18f;
                        bl = fg * 0.82f + bl * 0.18f + 8f;
                        rd = 255f * (rd / 255f) * 0.92f + 18f;
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

        /// <summary>
        /// 白平衡: 色温 (-100 冷 .. 100 暖) + 色调 (-100 绿 .. 100 品红)。原地修改 BGRA8。
        /// </summary>
        public static void ApplyWhiteBalance(byte[] px, int temperature, int tint)
        {
            if (px == null || px.Length == 0) return;
            float tr = 1f + temperature * 0.0016f;
            float tb = 1f - temperature * 0.0022f;
            float tR = 1f + tint * 0.0012f;
            float tB = 1f + tint * 0.0012f;
            float tG = 1f - tint * 0.0020f;
            for (int i = 0; i < px.Length; i += 4)
            {
                px[i] = Clamp(px[i] * tb * tB);
                px[i + 1] = Clamp(px[i + 1] * tG);
                px[i + 2] = Clamp(px[i + 2] * tr * tR);
            }
        }

        /// <summary>
        /// 盒式模糊 (分离两遍, 滑窗求和), 半径按图幅裁剪。原地修改。
        /// </summary>
        public static void ApplyBoxBlur(byte[] px, int w, int h, int radius)
        {
            if (px == null || px.Length == 0) return;
            radius = Math.Max(1, Math.Min(radius, Math.Min(64, (Math.Min(w, h) - 1) / 2)));
            var tmp = new byte[px.Length];
            BlurPass(px, tmp, w, h, radius, horizontal: true);
            BlurPass(tmp, px, w, h, radius, horizontal: false);
        }

        private static void BlurPass(byte[] src, byte[] dst, int w, int h, int r, bool horizontal)
        {
            int span = r * 2 + 1;
            int inner = horizontal ? w : h;
            int outer = horizontal ? h : w;
            for (int o = 0; o < outer; o++)
            {
                long b = 0, g = 0, rd = 0, a = 0;
                for (int k = -r; k <= r; k++)
                {
                    int p = ClampIdx(0 + k, inner);
                    int idx = horizontal ? (o * w + p) * 4 : (p * w + o) * 4;
                    b += src[idx]; g += src[idx + 1]; rd += src[idx + 2]; a += src[idx + 3];
                }
                for (int i = 0; i < inner; i++)
                {
                    int outIdx = horizontal ? (o * w + i) * 4 : (i * w + o) * 4;
                    dst[outIdx] = (byte)(b / span);
                    dst[outIdx + 1] = (byte)(g / span);
                    dst[outIdx + 2] = (byte)(rd / span);
                    dst[outIdx + 3] = (byte)(a / span);
                    int rm = ClampIdx(i - r, inner);
                    int ap = ClampIdx(i + r + 1, inner);
                    int mIdx = horizontal ? (o * w + rm) * 4 : (rm * w + o) * 4;
                    int pIdx = horizontal ? (o * w + ap) * 4 : (ap * w + o) * 4;
                    b += src[pIdx] - src[mIdx];
                    g += src[pIdx + 1] - src[mIdx + 1];
                    rd += src[pIdx + 2] - src[mIdx + 2];
                    a += src[pIdx + 3] - src[mIdx + 3];
                }
            }
        }

        private static int ClampIdx(int v, int n)
        {
            if (v < 0) return 0;
            if (v >= n) return n - 1;
            return v;
        }

        /// <summary>
        /// 锐化 (反锐化掩膜): out = src + amount * (src - blur)。amount 0..2。原地修改。
        /// </summary>
        public static void ApplySharpen(byte[] px, int w, int h, double amount)
        {
            if (px == null || px.Length == 0 || amount <= 0.01) return;
            var blur = new byte[px.Length];
            Array.Copy(px, blur, px.Length);
            ApplyBoxBlur(blur, w, h, 1);
            for (int i = 0; i < px.Length; i++)
            {
                int d = px[i] - blur[i];
                px[i] = Clamp(px[i] + (float)(d * amount));
            }
        }

        /// <summary>
        /// 暗角滤镜: 径向压暗四角。strength 0..1。
        /// </summary>
        public static void ApplyVignette(byte[] px, int w, int h, float strength)
        {
            if (px == null || px.Length == 0 || strength <= 0f) return;
            double cx = (w - 1) / 2.0, cy = (h - 1) / 2.0;
            double maxD2 = cx * cx + cy * cy;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    double dx = (x - cx) / cx, dy = (y - cy) / cy;
                    double d2 = dx * dx + dy * dy;
                    if (d2 < 0.6) continue;
                    double f = 1.0 - strength * Math.Min(1.0, (d2 - 0.6) / 1.4);
                    int i = (y * w + x) * 4;
                    px[i] = Clamp((float)(px[i] * f));
                    px[i + 1] = Clamp((float)(px[i + 1] * f));
                    px[i + 2] = Clamp((float)(px[i + 2] * f));
                }
            }
        }

        /// <summary>
        /// 沿笔画涂抹马赛克块。pts 为归一化坐标 (0..1), size 为块边长 (占图幅比例)。
        /// 从快照取样, 避免相邻块互相污染。
        /// </summary>
        public static void ApplyMosaicStroke(EditableImage img, IReadOnlyList<Windows.Foundation.Point> pts, double size)
        {
            if (pts == null || pts.Count < 2) return;
            int w = img.Width, h = img.Height;
            var snap = (byte[])img.Pixels.Clone();
            int block = Math.Max(4, (int)Math.Round(Math.Max(w, h) * Math.Min(0.3, Math.Max(0.01, size))));
            double step = Math.Max(1.0, block * 0.5);
            var last = pts[0];
            for (int i = 1; i < pts.Count; i++)
            {
                var cur = pts[i];
                double dist = Math.Sqrt((cur.X - last.X) * (cur.X - last.X) + (cur.Y - last.Y) * (cur.Y - last.Y));
                int segs = Math.Max(1, (int)Math.Ceiling(dist / step));
                for (int s = 0; s <= segs; s++)
                {
                    double t = s / (double)segs;
                    double nx = last.X + (cur.X - last.X) * t;
                    double ny = last.Y + (cur.Y - last.Y) * t;
                    StampMosaicBlock(snap, img.Pixels, w, h, nx, ny, block);
                }
                last = cur;
            }
        }

        private static void StampMosaicBlock(byte[] snap, byte[] dst, int w, int h, double nx, double ny, int block)
        {
            int bx = (int)Math.Round(nx * w);
            int by = (int)Math.Round(ny * h);
            int x0 = Math.Max(0, bx - block / 2);
            int y0 = Math.Max(0, by - block / 2);
            int x1 = Math.Min(w, bx + (block + 1) / 2);
            int y1 = Math.Min(h, by + (block + 1) / 2);
            if (x1 <= x0 || y1 <= y0) return;
            long b = 0, g = 0, r = 0;
            int cnt = 0;
            for (int y = y0; y < y1; y++)
            {
                for (int x = x0; x < x1; x++)
                {
                    int i = (y * w + x) * 4;
                    b += snap[i]; g += snap[i + 1]; r += snap[i + 2]; cnt++;
                }
            }
            if (cnt == 0) return;
            byte B = (byte)(b / cnt), G = (byte)(g / cnt), R = (byte)(r / cnt);
            for (int y = y0; y < y1; y++)
            {
                for (int x = x0; x < x1; x++)
                {
                    int i = (y * w + x) * 4;
                    dst[i] = B; dst[i + 1] = G; dst[i + 2] = R;
                }
            }
        }

        /// <summary>
        /// 从 BGRA8 缓冲生成 256x80 直方图位图 (亮度白线 + RGB 三色曲线)。
        /// </summary>
        public static WriteableBitmap BuildHistogram(byte[] px, int w, int h)
        {
            const int BW = 256, BH = 80;
            var counts = new int[4][];
            counts[0] = new int[256];   // luma
            counts[1] = new int[256];   // r
            counts[2] = new int[256];   // g
            counts[3] = new int[256];   // b
            int maxC = 1;
            if (px != null && w > 0 && h > 0)
            {
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int i = (y * w + x) * 4;
                        int b = px[i], g = px[i + 1], r = px[i + 2];
                        int l = (r * 299 + g * 587 + b * 114) / 1000;
                        if (++counts[0][l] > maxC) maxC = counts[0][l];
                        if (++counts[1][r] > maxC) maxC = counts[1][r];
                        if (++counts[2][g] > maxC) maxC = counts[2][g];
                        if (++counts[3][b] > maxC) maxC = counts[3][b];
                    }
                }
            }
            var wb = new WriteableBitmap(BW, BH);
            var outPx = new byte[BW * BH * 4];
            // 背景暗色
            for (int i = 0; i < outPx.Length; i += 4)
            {
                outPx[i] = 20; outPx[i + 1] = 20; outPx[i + 2] = 24; outPx[i + 3] = 255;
            }
            byte[][] colors =
            {
                new byte[] { 255, 255, 255 },   // luma 白
                new byte[] { 255, 96, 96 },     // r
                new byte[] { 96, 224, 96 },     // g
                new byte[] { 96, 160, 255 },    // b
            };
            for (int ch = 0; ch < 4; ch++)
            {
                for (int x = 0; x < BW; x++)
                {
                    int cnt = counts[ch][x];
                    int height = (int)Math.Round(BH * (double)cnt / maxC);
                    for (int yy = 0; yy < height; yy++)
                    {
                        int y = BH - 1 - yy;
                        int i = (y * BW + x) * 4;
                        outPx[i] = colors[ch][0]; outPx[i + 1] = colors[ch][1]; outPx[i + 2] = colors[ch][2]; outPx[i + 3] = 255;
                    }
                }
            }
            using (var stream = wb.PixelBuffer.AsStream())
            {
                stream.Write(outPx, 0, outPx.Length);
            }
            wb.Invalidate();
            return wb;
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
        Warm = 4,
        Cool = 5,
        Film = 6,
        Vignette = 7,
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

        /// <summary>
        /// 按归一化矩形裁剪 (0..1)。
        /// </summary>
        public EditableImage CropNormalized(Rect rect)
        {
            int px = (int)Math.Round(rect.X * Width);
            int py = (int)Math.Round(rect.Y * Height);
            int pw = (int)Math.Round(rect.Width * Width);
            int ph = (int)Math.Round(rect.Height * Height);
            return Crop(px, py, pw, ph);
        }

        public EditableImage Clone()
        {
            return new EditableImage((byte[])Pixels.Clone(), Width, Height, SourceWidth, SourceHeight);
        }

        /// <summary>
        /// 旋转。degrees 为 90/180/270 (顺时针)。返回新对象。
        /// </summary>
        public EditableImage Rotate(int degrees)
        {
            int d = ((degrees % 360) + 360) % 360;
            byte[] outPx;
            int w, h;
            switch (d)
            {
                case 90:
                    outPx = ImageEditService.Rotate90(Pixels, Width, Height, true);
                    w = Height; h = Width;
                    break;
                case 180:
                    outPx = ImageEditService.Rotate180(Pixels, Width, Height);
                    w = Width; h = Height;
                    break;
                case 270:
                    outPx = ImageEditService.Rotate90(Pixels, Width, Height, false);
                    w = Height; h = Width;
                    break;
                default:
                    return Clone();
            }
            return new EditableImage(outPx, w, h, SourceWidth, SourceHeight);
        }

        /// <summary>
        /// 翻转。horizontal/vertical 至少一个为 true。返回新对象。
        /// </summary>
        public EditableImage Flip(bool horizontal, bool vertical)
        {
            if (!horizontal && !vertical) return Clone();
            if (horizontal && vertical)
                return Rotate(180);
            return new EditableImage(ImageEditService.Flip(Pixels, Width, Height, horizontal), Width, Height, SourceWidth, SourceHeight);
        }

        /// <summary>
        /// 画布扩展: 四周加边框。ratio 为边框宽度占长边的比例 (0..0.5), 用指定颜色填充。返回新对象。
        /// </summary>
        public EditableImage Pad(double ratio, byte b, byte g, byte r)
        {
            int pad = (int)Math.Round(Math.Max(Width, Height) * Math.Min(0.5, Math.Max(0.0, ratio)));
            if (pad == 0) return Clone();
            int nw = Width + pad * 2;
            int nh = Height + pad * 2;
            var outPx = new byte[nw * nh * 4];
            for (int i = 0; i < outPx.Length; i += 4)
            {
                outPx[i] = b; outPx[i + 1] = g; outPx[i + 2] = r; outPx[i + 3] = 255;
            }
            for (int y = 0; y < Height; y++)
            {
                Array.Copy(Pixels, y * Width * 4, outPx, ((y + pad) * nw + pad) * 4, Width * 4);
            }
            return new EditableImage(outPx, nw, nh, SourceWidth, SourceHeight);
        }
    }
}