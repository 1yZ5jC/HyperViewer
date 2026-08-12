using System;
using System.Threading.Tasks;
using HyperViewer.Models;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace HyperViewer.Services
{
    /// <summary>
    /// 读取图片基本信息与 EXIF 元数据，零依赖 (仅用 WinRT)。
    /// </summary>
    public static class ImageInfoService
    {
        public static async Task<ImageInfoModel> LoadAsync(PhotoItem photo)
        {
            if (photo?.File == null) return null;
            var info = new ImageInfoModel
            {
                FileName = photo.Name,
                FilePath = photo.Path,
                FileType = photo.ContentType,
            };

            try
            {
                var basic = await photo.File.GetBasicPropertiesAsync();
                info.SizeBytes = basic.Size;
                info.DateModified = basic.DateModified;
            }
            catch { /* ignore */ }

            try
            {
                var img = await photo.File.Properties.GetImagePropertiesAsync();
                info.Width = img.Width;
                info.Height = img.Height;
                info.CameraManufacturer = img.CameraManufacturer;
                info.CameraModel = img.CameraModel;
                info.DateTaken = img.DateTaken;
                info.Title = img.Title;
                info.Latitude = img.Latitude;
                info.Longitude = img.Longitude;
            }
            catch { /* non-image or no props */ }

            // 进阶 EXIF (光圈/快门/ISO/焦距): 用 BitmapDecoder 读 System.Photo.* 元数据
            try
            {
                await LoadAdvancedExifAsync(photo, info);
            }
            catch { /* 进阶 EXIF 不可读就忽略, 不影响主流程 */ }

            return info;
        }

        private static async Task LoadAdvancedExifAsync(PhotoItem photo, ImageInfoModel info)
        {
            using (var stream = await photo.File.OpenReadAsync())
            {
                var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
                var props = await decoder.BitmapProperties.GetPropertiesAsync(new[]
                {
                    "System.Photo.ExposureTime",
                    "System.Photo.FNumber",
                    "System.Photo.FocalLength",
                    "System.Photo.IsoSpeed",
                });

                if (props.TryGetValue("System.Photo.ExposureTime", out var et) && et.Value != null)
                    info.ExposureSeconds = Convert.ToDouble(et.Value);
                if (props.TryGetValue("System.Photo.FNumber", out var fn) && fn.Value != null)
                    info.FNumber = Convert.ToDouble(fn.Value);
                if (props.TryGetValue("System.Photo.FocalLength", out var fl) && fl.Value != null)
                    info.FocalLength = Convert.ToDouble(fl.Value);
                if (props.TryGetValue("System.Photo.IsoSpeed", out var iso) && iso.Value != null)
                    info.IsoSpeed = Convert.ToUInt16(iso.Value);
            }
        }

        /// <summary>
        /// 只读拍摄日期 (轻量, 供时间轴批量扫描): EXIF DateTaken 优先, 无 EXIF 回退文件修改时间。
        /// </summary>
        public static async Task<DateTimeOffset?> GetDateTakenAsync(Windows.Storage.StorageFile file)
        {
            if (file == null) return null;
            try
            {
                var img = await file.Properties.GetImagePropertiesAsync();
                if (img.DateTaken.Year >= 1901) return img.DateTaken;
            }
            catch { /* 非图片或无属性 */ }

            try
            {
                var basic = await file.GetBasicPropertiesAsync();
                return basic.DateModified;
            }
            catch
            {
                return null;
            }
        }
    }
}
