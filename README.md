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

### ✅ 已完成 (时间轴)
- 时间轴页面: 按拍摄日期 (EXIF 优先, 回退修改时间) 按天分组瀑布浏览
- `CalendarView` 快速定位 (只显示有照片的日期区间, 点日期滚动到对应分组)
- 与主页联动: 点缩略图回主页打开所在文件夹并定位该图
- 扫描进度指示 + 并发限制 (SemaphoreSlim 4)
- 复用 `ImageInfoService.GetDateTakenAsync` 轻量读取单属性

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

## 技术路径规划

> 规划先行, 不立即实现。按方向分组, 每个方向: 目标 / 技术路线 / 前置 / 分步 / 风险。

### 1. 时间轴界面 (按拍摄日期浏览)
- **目标**: 将文件夹图片按拍摄日期分组 (EXIF 优先, 无 EXIF 回退文件修改时间), 年/月/日层级, 点选跳转到对应图片
- **前置**: "文件夹图库索引" — 扫描时异步读取每张图的 `DateTaken`
- **技术路线**:
  - 数据: `PhotoItem` 增加 `DateTaken`; `ImageInfoService` 已能读 `System.Photo.DateTaken`, 复用
  - 展示: 新建 `TimelinePage` (Frame 导航), `CollectionViewSource` 按日期分组 + `ItemsWrapGrid` 瀑布
  - 定位: `CalendarView` (内建控件, 10240 可用) 选日期 → 滚动到对应分组
  - 联动: 点击缩略图 → 回 `MainPage` 并定位该图
- **依赖**: 全部 UWP 内建, 无新 NuGet
- **风险**: 大文件夹全量读 EXIF 慢 → 只读单属性 + 后台异步 + 进度条
- **分步**: ① DateTaken 采集 → ② 分组时间轴页 → ③ CalendarView 快速定位 → ④ 与 MainPage 跳转联动

### 2. 编辑界面 (裁剪/调色, 非破坏性)
- **目标**: 当前图片裁剪 + 亮度/对比度/饱和度调节 + 简单滤镜, 保存副本不覆盖原图
- **技术路线**:
  - 方案 A (先做, 零依赖): `WriteableBitmap` + `BitmapTransform` (裁剪/旋转/缩放) + `CopyPixels` 像素级运算 (亮度/对比度/灰度/反色); 保存用 `BitmapEncoder` (Jpeg/Png)
  - 方案 B (后做, GPU 滤镜): Win2D.uwp (`CanvasControl` + `CanvasRenderTarget`) — **注意: Win2D 要求 TargetPlatformMinVersion 14393+, 引入前需验证能否拉高 MinVersion 或换 `Microsoft.Graphics.Win2D`, 不行则保持方案 A**
- **交互**: 裁剪 = 图上拖动矩形 (覆盖层 `Canvas` + `Rectangle`); 调色 = 侧边栏 `Slider` 实时预览
- **保存**: 另存为新文件 (FileSavePicker); 撤销栈 = 编辑快照数组
- **风险**: 大图逐像素运算卡顿 → 调色在降采样预览层做, 导出时用原图全分辨率重算
- **分步**: ① 裁剪拖动 + 渲染 → ② 调色滑块 → ③ 滤镜预设 (灰度/反色/棕褐) → ④ 撤销/重做 → ⑤ 另存为

### 3. 浏览体验小优化
- 缩放百分比指示: `ImageViewer` 暴露 `ZoomFactor`, 状态栏显示 `x 100%`
- 原始尺寸显示: 状态栏加 `WxH` (解码时记录到 `PhotoItem`)
- 幻灯片间隔设置: 新增 `SettingsService` (LocalSettings 存 1/3/5/10s), 幻灯片循环取该值
- 收藏/标记: LocalSettings 存收藏路径列表, 缩略图角标星标, 可过滤
- 文件操作: 删除/复制/重命名 (`StorageFile.DeleteAsync` / `CopyAsync`), `ContentDialog` 确认
- 设为壁纸: `UserProfilePersonalizationSettings` — **14393+, 10240 需条件调用**, 桌面场景可做
- 双图对比: 远期已有, 左右分屏同步缩放

### 4. 性能: 分块加载 / 缓存
- **分块加载** (远期): `VirtualSurfaceImageSource` (UWP 内建, 10240 可用) 按可见区域渲染 256px 瓦片, 瓦片用 `BitmapDecoder` + `BitmapTransform` 裁剪解码; 或 Win2D `CanvasVirtualControl` (见 Win2D MinVersion 问题)
- **相邻预取** (先做, 收益大): 当前图解码完成后后台预解码 ±2 张降采样版
- **LRU 缓存** (先做): 自研 `Dictionary + LinkedList` 缓存最近 N 张解码结果, `MemoryManager.AppMemoryUsageLevel` 超标时清空
- **风险**: VirtualSurfaceImageSource 实现复杂度高 → 建议先预取 + LRU, 见效快风险低

### 5. 工程化: 设置页 / 国际化 / 发布
- **设置页**: 新建 `SettingsPage`, 存 LocalSettings: 幻灯片间隔 / 启动行为 / 缩略图大小 / 背景色 (黑/深灰/白)
- **国际化**: `Strings\zh-CN\Resources.resw` + `en-US`, XAML 用 `x:Uid`, 代码用 `ResourceLoader`; 现有硬编码中文 Label/对话框全量抽取 (工作量在替换, 建议与设置页一起做)
- **单元测试**: 纯逻辑抽到无 UI 依赖类 (排序/EXIF 解析/索引), 用 UWP MSTest 测试工程
- **发布**: 自签证书 + 旁加载脚本; Store 提交需开发者账户
- **SQLite 前置调研**: 10240 下只能用 `Microsoft.Data.Sqlite 1.1.1` 或 `sqlite-net-pcl 1.6.x` (SQLitePCLRaw 1.x); 16299+ 才可用新版 — 若图库索引不需要强查询, 优先用 JSON 文件索引零依赖

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

## 踩坑记录

### 1. `AppBarButton.Icon` 不是任意字形名都能用 (XamlParseException)
- 症状: 运行时 `XamlParseException: Failed to create a 'IconElement' from the text 'XXX'`, 编译期不报错
- 原因: `Icon="FlipVertical"` / `Icon="Info"` 这种字符串写法, 是解析为 **`Symbol` 枚举成员**, 不是 Segoe MDL2 Assets 字形名
- 但 `Symbol` 枚举里**根本没有** `FlipVertical` / `Info` 这些成员 (只有 `Rotate`, `Important`, `ContactInfo` 等)
- 结论: `Icon` 字符串只接受 `Symbol` 枚举成员, 不要凭字形名猜
- 解决: 需要枚举之外的字形时, 用 **`FontIcon` + 字形码** 或 **`PathIcon` + 自绘 Data**
- 验证字形存在: 用 `GlyphTypeface` 检查字形码:
  ```powershell
  Add-Type -AssemblyName PresentationCore
  $tf = New-Object Windows.Media.GlyphTypeface("C:\Windows\Fonts\segmdl2.ttf")
  $tf.CharacterToGlyphMap[0xE8C4]  # 0 = 字形不存在
  ```
- **字形码绝不能靠猜, 必须查官方字符表确认名字**: 官方表 `learn.microsoft.com/en-us/windows/apps/design/style/segoe-ui-symbol-font`
  按 `alt="Screenshot of XXX"` + 码位成对出现, 可用正则提取全表
- 已验证的教训: `E8C3` = Read(已读邮件), `E8C4` = ShowBcc, `E946` = Info, `E7AD` = Rotate,
  `E740`/`E1D9` = FullScreen (同一字形, glyph 143), `E890` = View, `E8B4` = Orientation, `E895` = Sync, `E8AB` = Switch
- **Segoe MDL2 Assets 没有"翻转(Flip)"图标**; 翻转按钮用 `PathIcon` 自绘 (16x16 坐标系, 填充几何):
  ```xml
  <!-- 水平翻转: 左右箭头指向中心竖轴 -->
  <PathIcon Data="M2,5 L6,8 L2,11 Z M14,5 L10,8 L14,11 Z M7.6,2 L8.4,2 L8.4,14 L7.6,14 Z"/>
  <!-- 垂直翻转: 上下箭头指向中心横轴 -->
  <PathIcon Data="M5,2 L8,6 L11,2 Z M5,14 L8,10 L11,14 Z M2,7.6 L2,8.4 L14,8.4 L14,7.6 Z"/>
  ```
- 全屏按钮直接 `Icon="FullScreen"` (Symbol 枚举成员, 已验证字形存在); `Icon="Page"` 是文档图标, 不是全屏

### 2. `ScrollViewer.MinZoomFactor` 下限是 0.1 (XamlParseException)
- 症状: 运行时 `XamlParseException: Failed to assign to property 'ScrollViewer.MinZoomFactor'`, 编译期不报错
- 原因: UWP 规定 `MinZoomFactor` 合法范围 `[0.1, 10]`, 设 0.05 会被拒绝
- 解决: 设为 `0.1`

### 3. UWP 部分属性在旧版 TargetPlatformMinVersion 上不可用 (编译期 WMC0612)
- 症状: `XamlCompiler error WMC0612: Property Not Found ... (TargetPlatformMinVersion)`
- 原因: `DefaultLabelPosition` (14393+), `StackPanel.Spacing` (16299+), `BringIntoViewOptions.VerticalAlignment` 等在 10240 上不存在
- 解决: 查文档确认属性引入版本; 用 `Margin` 替代 `Spacing`, 去掉不必要的新属性

### 4. XAML 编译管线 (XamlPreCompile) 与经典 csproj 通配符的坑
- 症状: `Models\PhotoItem.cs` 明明在编译列表里, 但报 "找不到类型/命名空间 PhotoItem"; `CompileXaml 任务返回 false 但未记录错误`
- 原因: 经典 UWP csproj 用 `**\*.cs` 通配符时, XAML 预处理那遍 csc 偶发不包含通配符展开的文件
- 解决: 不用通配符, 每个文件显式列 `<Compile Include="Models\PhotoItem.cs" />` (README 目录结构已同步)

### 5. `IAsyncOperation<T>.GetAwaiter()` 找不到
- 症状: `IAsyncOperation<StorageFile> 不包含 GetAwaiter 的定义`
- 原因: 扩展方法在 `WindowsRuntimeSystemExtensions` (位于 `System` 命名空间), 需要 `using System;`
- 解决: 确保文件顶部有 `using System;`

### 6. `Symbol` 枚举值 / `VirtualKey` 命名空间
- `Symbol` 枚举没有 `FlipVertical`/`Info` 成员 (见坑 1)
- `VirtualKey` 在 `Windows.System` 命名空间, 记得 using

### 7. `CalendarView.DateSelected` 事件是 1703+ 才有 (编译期 WMC0011)
- 症状: `XamlCompiler error WMC0011: Unknown member 'DateSelected' on element 'CalendarView'`
- 原因: `DateSelected` 事件 15063+ (Creators Update) 引入, 10240 没有
- 解决: 用 `SelectedDatesChanged` (10240 就有), 参数同为 `CalendarViewSelectedDatesChangedEventArgs`

### 8. UWP 10240 没有 `System.ValueTuple` (编译期 CS8137/CS8179)
- 症状: `CS8137: 由于找不到编译器必需的类型 TupleElementNamesAttribute... 预定义类型 ValueTuple<T> 未定义`
- 原因: 元组语法需要 System.ValueTuple, 经典 UWP 10240 默认不引用
- 解决: 改用内部结构体 (`struct OrientedPixels { public byte[] Pixels; ... }`)

### 9. `ExifOrientationMode.ApplyExifOrientation` 是 14393+ 才有
- 症状: `CS0117: ExifOrientationMode 未包含 ApplyExifOrientation 的定义`
- 原因: 14393 (Anniversary) 新增
- 解决: 10240 下用 `IgnoreExifOrientation` + 手动读 `System.Photo.Orientation` (1-8) 对像素数组做旋转/翻转 (见 `ImageEditService.ApplyOrientation`)

### 10. PowerShell `Set-Content` 会破坏 XAML 中文编码 (万恶之源)
- 症状: 整个 XAML 文件中文全变乱码, 甚至 `Text="度? VerticalAlignment=` 直接解析报错
- 原因: PowerShell 5.1 `Get-Content` 默认按 ANSI/GBK 读, `Set-Content -Encoding UTF8` 写出双重编码
- 解决: **绝不**用 PowerShell 文本命令改含中文的 XAML/代码文件; 只用 Edit/Write 工具 (UTF-8 正确); 改完立刻 `git diff` 检查
- 附带教训: UWP `Slider` 的属性名是 `Minimum`/`Maximum` (不是 `Min`/`Max`), `Path.FillRule` 不是 Path 的属性 (要设在 PathGeometry 上)

## 开发顺序 (已执行 + 待执行)

1. ✅ 搭 MVVM 骨架 + 目录 + 自研基类
2. ✅ 打通主流程: 文件/文件夹选择 → 单张显示 → 上一张/下一张 → 缩放拖拽
3. ✅ 快捷键 + 全屏 + 文件激活入口
4. ✅ 缩略图栏、旋转/翻转、幻灯片、最近打开、错误占位、加载进度
5. 🔜 文件关联清单(已加)、EXIF 面板(已加)、GIF 动图(已加)、颜色拾取、多 DPI
6. 🔜 超大图分块解码、扩展格式、设置页
7. 🔜 国际化、打包签名、Store 提交
