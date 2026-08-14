using System;
using System.Collections.Generic;
using Windows.Storage;
using Windows.Storage.FileProperties;
using HyperViewer.Helpers;

namespace HyperViewer.Models
{
    /// <summary>
    /// 图片元数据/属性快照，绑定到 UI 信息面板。
    /// </summary>
    public sealed class ImageInfoModel
    {
        // 基本信息
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public string FileType { get; set; }
        public DateTimeOffset DateModified { get; set; }
        public ulong SizeBytes { get; set; }

        public string FormattedSize
        {
            get
            {
                double s = SizeBytes;
                string[] u = { "B", "KB", "MB", "GB" };
                int i = 0;
                while (s >= 1024 && i < u.Length - 1) { s /= 1024; i++; }
                return $"{s:0.##} {u[i]}";
            }
        }

        // 尺寸
        public uint Width { get; set; }
        public uint Height { get; set; }
        public string Dimensions => Width > 0 && Height > 0 ? $"{Width} × {Height}" : "—";

        // 相机拍摄参数 (来自 ImageProperties)
        public string CameraManufacturer { get; set; }
        public string CameraModel { get; set; }
        public DateTimeOffset DateTaken { get; set; }
        public string Title { get; set; }

        // 更深入 EXIF (来自 BitmapPropertiesView, 在 ImageInfoService 中按需补)
        public double? ExposureSeconds { get; set; }
        public double? FNumber { get; set; }
        public double? FocalLength { get; set; }
        public ushort? IsoSpeed { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        public string ExposureText => ExposureSeconds.HasValue ? FormatExposure(ExposureSeconds.Value) : "—";
        public string FNumberText => FNumber.HasValue ? $"f/{FNumber.Value:0.##}" : "—";
        public string FocalLengthText => FocalLength.HasValue ? $"{FocalLength.Value:0.##} mm" : "—";
        public string IsoSpeedText => IsoSpeed.HasValue ? $"ISO {IsoSpeed.Value}" : "—";
        public string GpsText => (Latitude.HasValue && Longitude.HasValue)
            ? $"{Latitude.Value:0.######}°, {Longitude.Value:0.######}°"
            : "—";

        private static string FormatExposure(double seconds)
        {
            if (seconds <= 0) return "—";
            if (seconds >= 1) return $"{seconds:0.##} s";
            int denom = (int)Math.Round(1.0 / seconds);
            return $"1/{denom} s";
        }

        /// <summary>
        /// 构造为 UI 渲染准备的字段列表 (字段名, 值)。
        /// </summary>
        public IReadOnlyList<InfoRow> BuildRows()
        {
            var rows = new List<InfoRow>();
            rows.Add(new InfoRow(Loc.Get("InfoFileName"), FileName ?? "—"));
            rows.Add(new InfoRow(Loc.Get("InfoPath"), FilePath ?? "—"));
            rows.Add(new InfoRow(Loc.Get("InfoType"), FileType ?? "—"));
            rows.Add(new InfoRow(Loc.Get("InfoSize"), FormattedSize));
            rows.Add(new InfoRow(Loc.Get("InfoModified"), DateModified == DateTimeOffset.MinValue ? "—" : DateModified.ToString("yyyy-MM-dd HH:mm")));
            rows.Add(new InfoRow(Loc.Get("InfoDimensions"), Dimensions));
            rows.Add(new InfoRow(Loc.Get("InfoTaken"), DateTaken == DateTimeOffset.MinValue ? "—" : DateTaken.ToString("yyyy-MM-dd HH:mm")));
            rows.Add(new InfoRow(Loc.Get("InfoCamera"), string.IsNullOrEmpty(CameraModel) ? "—" : $"{CameraManufacturer ?? ""} {CameraModel}".Trim()));
            rows.Add(new InfoRow(Loc.Get("InfoAperture"), FNumberText));
            rows.Add(new InfoRow(Loc.Get("InfoShutter"), ExposureText));
            rows.Add(new InfoRow(Loc.Get("InfoIso"), IsoSpeedText));
            rows.Add(new InfoRow(Loc.Get("InfoFocal"), FocalLengthText));
            rows.Add(new InfoRow(Loc.Get("InfoGps"), GpsText));
            return rows;
        }
    }

    /// <summary>
    /// 信息面板一行字段, 便于 ListView 渲染。
    /// </summary>
    public sealed class InfoRow
    {
        public string Key { get; }
        public string Value { get; set; }
        public InfoRow(string key, string value) { Key = key; Value = value; }
    }
}
