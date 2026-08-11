# HyperViewer

一个面向 **大图 / 高清图** 浏览体验的 UWP 图片查看器。

## 平台

- UWP (C# + XAML)
- Target: Windows 10.0.26100 / Min: 10.0.10240
- 默认语言: zh-CN
- MVVM: 自研轻量基类 (`Helpers/ObservableObject.cs`, `Helpers/RelayCommand.cs`)
  - 原因: `CommunityToolkit.Mvvm` / `Microsoft.Toolkit.Mvvm` 不直接兼容 UAP 10.0.10240
  - 自研基类已覆盖 `ObservableObject` / `RelayCommand` / `AsyncRelayCommand`

## 功能进度

### ✅ 已完成 (MVP)
- 打开本地图片 (`FileOpenPicker`)
- 打开文件夹并自动列出图片
- 上一张 / 下一张 / 首张 / 末张
- 缩放 + 平移 (鼠标滚轮缩放 / 双指缩放 / 拖动平移)
- 双击切换 1x ↔ 2x
- 键盘快捷键: ← → ↑ ↓ Space 翻页, Home/End 跳首尾, F/F11 全屏
- 全屏切换 (`ApplicationView.TryEnterFullScreenMode`)
- 文件激活入口 (`App.OnFileActivated`)
- 底部状态栏 (文件名 + 索引/总数)
- 空态提示

### 🚧 下一步 (核心体验) — 优先做
- [ ] 缩略图栏 (底部水平 `ListView` + 虚拟化, 异步加载缩略图, 当前项高亮)
- [ ] 旋转 (R 顺时针 / Shift+R 逆时针, 不修改磁盘文件)
- [ ] 翻转 (H 水平翻转 / V 垂直翻转)
- [ ] 幻灯片 (Space-or-Play 自动切换, 可配置间隔 1/3/5/10s)
- [ ] 最近打开列表 (`ApplicationDataContainer.LocalSettings` 持久化最近 10 个文件夹)
- [ ] 错误占位 (加载失败显示 fallback 图标 + 错误信息)
- [ ] 加载进度指示 (`ProgressRing` 在大图解码期间)

### 🔜 中期 (差异化)
- [ ] 文件关联清单 (`Package.appxmanifest` 声明 jpg/png/... 扩展, 双击图片默认打开本应用)
- [ ] `Package.appxmanifest` 完善显示名 / 描述 / 视觉资产 / 能力
- [ ] EXIF 元数据面板 (尺寸/相机/焦距/ISO/光圈/快门/GPS)
- [ ] 颜色拾取 (放大显示像素 + 取色)
- [ ] 滚轮缩放改进 (Ctrl + 滚轮才缩放, 默认滚轮滚动平移 - 与照片应用一致)
- [ ] GIF / 动图帧播放
- [ ] 多 DPI 自适应 (`DisplayInformation.DpiChanged`)

### 🧪 远期 (进阶能力)
- [ ] 超大图分块加载 (单图 > 10000px, `VirtualSurfaceImageSource` / `CanvasVirtualControl`)
- [ ] 扩展格式 RAW / WebP / HEIF / SVG / PSD (评估 `Magick.NET` UWP 兼容性, 不行则 `SkiaSharp`)
- [ ] 设置页 (主题/背景色/快捷键自定义/默认排序)
- [ ] 批量转换 / 导出 (jpg/png/webp 互转 + 压缩质量)
- [ ] 简单滤镜 (灰度/反色/对比度, 评估 `Lanczos` 缩放)
- [ ] 双图对比模式 (左右分屏, 同步缩放)
- [ ] 国际化 (en-US 资源 + 多语言切换)
- [ ] 打包签名 / 旁加载 / Store 提交

## 目录结构

```
HyperViewer/
├── App.xaml(.cs)            入口 / 生命周期 / 文件激活
├── MainPage.xaml(.cs)       主页 (CommandBar + Viewer + 状态栏)
├── Models/
│   └── PhotoItem.cs         单张图片数据载体
├── ViewModels/
│   └── MainViewModel.cs     主页 VM: 列表/当前/翻页命令
├── Services/
│   ├── FilePickerService.cs 文件/文件夹选择与扫描
│   └── ImageLoaderService.cs 异步解码与降采样
├── Controls/
│   └── ImageViewer.xaml(.cs) 缩放平移控件 (ScrollViewer 内嵌)
├── Helpers/
│   ├── ObservableObject.cs  MVVM 基类
│   └── RelayCommand.cs       同步/异步命令
└── Assets/
```

## 技术选型（更新）

- MVVM: **自研轻量基类** (`Helpers/ObservableObject.cs` + `Helpers/RelayCommand.cs`)
  - 替代不兼容的 `CommunityToolkit.Mvvm`
- 缩放手势: **`ScrollViewer.ZoomMode="Enabled"`** (渲染级缩放, 性能最优)
  - 自定义 `PointerWheelChanged` 实现直接滚轮缩放
- 超大图 (远期): `VirtualSurfaceImageSource` 或自写分块解码
- 扩展格式 (远期): 优先评估 `Magick.NET` UWP 兼容性, 不行则 `SkiaSharp`

## 关键技术点

- `ScrollViewer.MinZoomFactor` 必须 ≥ 0.1 (UWP 限制)
- `IAsyncOperation<T>.GetAwaiter()` 需 `using System;` (扩展方法在 `WindowsRuntimeSystemExtensions`)
- `VirtualKey` 在 `Windows.System` 命名空间
- `BitmapImage.DecodePixelWidth` + `DecodePixelType.Logical` 做降采样, 避免一次加载超大图
- 经典 UWP csproj 不要用通配符 `**\*.cs`, 显式列出每个文件 (否则 XAML 编译管线偶发找不到类型)

## 开发顺序 (已执行 + 待执行)

1. ✅ 搭 MVVM 骨架 + 目录 + 自研基类
2. ✅ 打通主流程: 文件/文件夹选择 → 单张显示 → 上一张/下一张 → 缩放拖拽
3. ✅ 快捷键 + 全屏 + 文件激活入口
4. 🔜 缩略图栏、旋转/翻转、幻灯片、最近打开、错误占位、加载进度
5. 🔜 文件关联清单、EXIF、颜色拾取、GIF 动图、多 DPI
6. 🔜 超大图分块解码、扩展格式、设置页
7. 🔜 国际化、打包签名、Store 提交
