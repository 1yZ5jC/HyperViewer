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

### ✅ 已完成 (核心浏览)
- 打开本地图片 (`FileOpenPicker`) / 打开文件夹自动列出图片
- 上一张 / 下一张 / 首张 / 末张 + 底部缩略图栏 (虚拟化, 异步缩略图, 当前项高亮)
- 缩放 + 平移 (滚轮缩放 / 双指缩放 / 拖动平移), 双击 1x ↔ 2x
- 旋转 (R 顺时针 / Shift+R 逆时针 / 180°)、翻转 (H / V, 不修改磁盘文件)
- 键盘快捷键: ← → ↑ ↓ Space 翻页, Home/End 跳首尾, F/F11 全屏
- 全屏切换 (`ApplicationView.TryEnterFullScreenMode`)
- 文件激活入口 (`App.OnFileActivated`) + GIF 动图 (BitmapImage 原生)
- 底部状态栏 (文件名 + 索引/总数 + 尺寸 + 缩放 + 旋转/翻转状态)
- 主页欢迎面板 (大按钮 + 最近文件夹卡片直达, 窄屏自适应 `AdaptiveTrigger`) → 已升级为图库视图 (三 Tab + 卡片网格)
- 最近打开列表 (`FutureAccessList` 持久化最近 10 个文件夹 + LocalSettings 顺序索引)
- 删除 / 重命名 (确认对话框) + 收藏 (路径持久化) + 加载失败占位 / 加载进度环
- 信息面板 (EXIF: 尺寸/拍摄时间/相机/光圈/快门/ISO/焦距/GPS, 可选中复制)

### ✅ 已完成 (时间轴)
- 时间轴页面: 按拍摄日期 (EXIF 优先, 回退修改时间) 按天分组瀑布浏览
- `CalendarView` 快速定位 (只显示有照片的日期区间, 点日期滚动到对应分组)
- 与主页联动: 点缩略图回主页打开所在文件夹并定位该图
- 扫描进度指示 + 并发限制 (SemaphoreSlim 4)

### ✅ 已完成 (编辑)
- 编辑页: 裁剪 (拖拽矩形 + 移动选区) + 亮度/对比度/饱和度 + 滤镜 (灰度/反色/棕褐)
- 调色在降采样预览层实时预览, 导出时原图全分辨率重算; 保存副本不覆盖原图
- 撤销栈 = 编辑快照数组, 恢复原始图

### ✅ 已完成 (设置与国际化)
- 设置页: 幻灯片间隔 (1/3/5/10s) + 主视图背景色 (黑/深灰/白), LocalSettings 即时生效
- 国际化: `Strings\zh-CN` + `en-US` 两套 resw, XAML 全量 `x:Uid`, 代码走 `Helpers/Loc.cs`
- 开发者选项: 10240 模拟开关 (`DebugSimulate10240`) + 详细调试日志 (`DebugVerboseLog`), 日志始终落盘 `LocalState\debug.log` (见踩坑 23)

### 🔜 下一步 (记自"对照旧版 Windows 图片应用"复盘)
**日常高频**
- [x] 删除进回收站 (`SHFileOperation` P/Invoke, 失败回退永久删除; 见 DeleteRecycleFallbackMessage)
- [x] Ctrl+C 复制当前图片 (DataPackage: 位图懒加载流)
- [ ] "在文件夹中显示" (`Launcher.LaunchFolderAsync`, 缩略图/更多菜单)
- [ ] 拖放打开: 拖图片/文件夹进窗口直接打开
- [ ] 缩放快捷键: Ctrl+0 实际尺寸 / Ctrl+9 适应窗口

**分享与导出**
- [ ] 分享按钮 (`DataTransferManager` 分享当前图片)
- [ ] 打印 (`PrintManager`)
- [ ] 主视图"另存为" (Ctrl+S)
- [ ] EXIF 旋转方向写回 (保存副本时可选)

**体验细节**
- [x] 双击退出全屏 (全屏中双击恢复窗口; 窗口模式双击仍缩放)
- [x] 图片切换过渡动画 (淡入/缩放/平移/闪动, 设置页可选)
- [x] 缩放滑块 (状态栏右侧 10%-800%, 与滚轮/双击双向同步)
- [x] 点击图片左右 1/4 区翻页 (放大时禁用防误触)
- [x] 顶栏半透明悬浮 (不占布局, Z 序最上层)
- [x] 设置: 应用主题 (深/浅/跟随系统) + 切图重置旋转开关
- [ ] 窗口尺寸/位置记忆 + 启动恢复上次浏览文件夹 (FAL 令牌已具备)
- [ ] 确认 GIF 过滤器是否含 `.gif` (BitmapImage 默认支持动图)
- [ ] 幻灯片切图时重置缩放为适应窗口
- [ ] 缩略图栏多选批量操作 (删除/复制/分享)

**大件 (按需)**
- [x] **主页升级为图库视图** (已完成 v1): 三 Tab (集锦/全部照片/文件夹) + 相册卡片网格 (封面+张数+日期范围) + 全部照片缩略图瀑布 + 文件夹列表 + 顶部添加按钮; 数据走 `RecentFolders` (用户授权访问过的文件夹), 无 picturesLibrary capability 依赖; 后续可接 `KnownFolders.PicturesLibrary` 实现系统级扫描
- [ ] 时间轴按月/年分组切换
- [ ] 批注涂鸦 (InkCanvas)
- [ ] 文件关联清单双击默认打开 (manifest 扩展名声明)
- [ ] 超大图分块加载、扩展格式 (RAW/WebP/HEIF)、打包签名/Store 提交

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

### 11. ARM/ARM64 打包: VS 校验拒绝但编译产物可用 (绕过方案)
- 症状: `msbuild /p:Platform=ARM` 报错"要编译 ARM 配置的应用程序, 必须将项目的目标平台版本更新为 Windows 11 版本 22H2 (内部版本 22621)或更低版本"; `Platform=ARM64` 报"必须将项目的最低版本更新为 Windows 10 Fall Creators Update (内部版本 16299)或更高版本"
- 原因: VS2022 17.x 的 `_ValidateConfiguration` (Microsoft.AppXPackage.Targets) 在打包前校验; 本机只有 26100 SDK 完整 (14393/15063/16299/17134 缺 UnionMetadata winmd, 降 TargetPlatformVersion 报 WMC1006)
- 关键: **.NET Native 编译在校验失败之前已完成**, `bin\ARM\Release\HyperViewer.exe` 等产物齐全
- 解决: 手动组装布局 + `makeappx.exe` 打包 (已封装为 `build-arm.ps1`, 含签名)

### 12. MakeAppx 手动打包的坑
- `$targetnametoken$.exe` 未替换 → 校验报 "declared for element ... doesn't exist in the package": 直接用 VS 构建生成的规范化 `bin\x86\Debug(或 Release)\AppxManifest.xml` (exe 名已替换, Dependencies 由 csproj 的 TargetPlatformMin/Version 生成)
- `MaxVersionTested` 不能小于 `MinVersion` (错误 0x80080204 "The max version tested value must not be less than the min version value"); 且该属性是**必需**的 (移除报 schema 错) → 设为目标平台版本 (如 `10.0.26100.0`)
- `ProcessorArchitecture` 枚举必须**小写** (`arm`/`arm64`/`x86`/`neutral`), 大写报 C00CE169
- VS 注入的 `build:Metadata` / `xmlns:build` 可清理 (非必需)
- 文本处理用 .NET `File.ReadAllText/WriteAllText`, 别用 PowerShell 文本命令 (见坑 10)
- `makeappx.exe` 位置: `C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x86\makeappx.exe`

### 13. AppX 签名: 证书 Subject 必须等于 manifest Publisher (0x8007000b)
- 症状: `signtool sign` 对 appx 报 `SignerSign() failed (-2147024885/0x8007000b)`, 但签普通 exe 正常
- 根因: AppX 签名要求证书 Subject 与 manifest `<Identity Publisher>` **完全一致** (如 `CN=Alan`); 与 CNG/CAPI 密钥无关, 与 EKU 也无关 (只要含 Code Signing EKU 且被签名工具认可)
- 解决: `New-SelfSignedCertificate -Type CodeSigningCert -Subject "<Publisher值>" -KeyExportPolicy Exportable` → `Export-PfxCertificate` → `signtool sign /fd SHA256 /f x.pfx /p <pw>`
- 时间戳: 默认加 `/tr http://timestamp.digicert.com /td SHA256`, 离线时降级为无时间戳签名
- 自签证书未装信任根时 `signtool verify` 报 "chain terminated in a root certificate which is not trusted" 是**预期行为**; 部署到设备前需把证书安装到设备的"受信任的根证书颁发机构"

### 14. `AppxBundle=Always` + 多架构 `AppxBundlePlatforms` 会毁掉单平台构建
- 症状: x86 Debug 构建报 MakeAppx `0x80070003` (entrypoint 缺失) / mapping 解析失败 / `0x8007000b`
- 原因: bundle 打包对 `AppxBundlePlatforms` 里每个架构都生成布局, 未构建的架构布局里没有 `HyperViewer.exe`
- 解决: 日常单平台构建显式 `/p:AppxBundlePlatforms=<当前架构>` 覆盖 (build.ps1 已加); 多架构包用 VS"创建应用包"向导或 `build-arm.ps1`

### 15. 后台任务扩展必须用默认 (foundation) 命名空间
- 症状: manifest 中 `<uap:Extension Category="windows.backgroundTasks">` 或 `uap3:` 前缀都被 VS 校验拒绝
- 正确写法 (官方文档同款, 无前缀):
  ```xml
  <Extension Category="windows.backgroundTasks" EntryPoint="HyperViewer.Tasks.TileRotationTask">
    <BackgroundTasks><Task Type="timer" /></BackgroundTasks>
  </Extension>
  ```
- `TimerTrigger(15, false)` 的 15 是系统允许的最短周期 (分钟)

### 16. 设置默认值要语义化: 主题默认 "Dark" 会盖掉"跟随系统"
- 症状: 首次启动 (无配置) 时应用固定深色, 不跟随系统颜色 —— 等于默认值"重写"了跟随系统的预期
- 解决: `SettingsService.AppTheme` 默认值 `"Dark"` → `"Default"` (跟随系统); `ApplyThemeNow` 对 `"Default"` 走 `ElementTheme.Default` 天然兼容
- 注意: 已安装实例的 LocalSettings 不会自动迁移旧值, 仅对全新安装生效

### 17. 14393+ API 运行时崩溃: 编译期查不出 (WinRT API 版本守卫)
- 症状: 10240 上点击胶片/滚轮滚动/缩放报运行时异常崩溃; 同样代码在 14393+ 正常
- 原因: winmd 按 TargetPlatformVersion (26100) 解析, **编译期不校验** API 引入版本; 部分 API 在 10240 上不存在, 调用即崩
- 已发现并修复 (都需 `UwpCompat.HasContractV2` 守卫 = UniversalApiContract v2/14393):
  - `UIElement.StartBringIntoView(BringIntoViewOptions)` (14393+) → 10240 用 `ListViewBase.ScrollIntoView(item)`
  - `ScrollViewer.ChangeView(x, y, z, disableAnimation)` 四参重载 (14393+) → 10240 用三参 (无动画)
- 排查方法: 新功能上线前 grep `BringIntoViewOptions|ChangeView\(.*, true\)|AnimationDesired` 等, 并对照文档确认 API 引入版本
- 守卫类: `Helpers/UwpCompat.cs` (`HasContractV2` 为属性, 支持模拟开关即时切换; 见踩坑 23)

### 18. 15063+/1703+ API 兼容: ContentDialog 关闭按钮与剪贴板 (Contract v4 守卫)
- 症状: 10240 上打开任何对话框报运行时异常; 复制图片偶发异常
- 已发现并修复 (都需 `UwpCompat.HasContractV4` 守卫 = UniversalApiContract v4/1703):
  - `ContentDialog.CloseButtonText` (1703+) → 新建 `Helpers/CompatContentDialog.cs`: 在 v4 以下回退 `SecondaryButtonText` (8.1 原生), 全项目 23 处 `new ContentDialog` → `new CompatContentDialog`、`CloseButtonText` → `CompatCloseButtonText`
  - `Clipboard.SetContentWithOptions` (1703+) → 10240 降级 `Clipboard.SetContent(package)`
  - `InkDrawingAttributes.PencilProperties` (14393+) → `HasContractV2 &&` 短路 (10240 无铅笔类型, 枚举永不命中, 属双保险)
- 核对过安全的 API: DataTransferManager/ShowShareUI、PrintManager/PrintDocument、BitmapDecoder/BitmapEncoder、CoreApplicationViewTitleBar/SetTitleBar、SetPreferredMinSize、TryEnterFullScreenMode 均 10240 原生; App.xaml 高对比度字典的 `SystemColorXxxColor` 为框架级资源, 10240 存在
- 排查方法: 全局 grep `ContentDialog|Clipboard\.SetContentWithOptions|CloseButtonText` 及新控件/新属性, 对照文档 "Requirements > API contract" 栏确认引入版本

### 19. 10240 图片显示时序三连: fit 延迟到布局稳定、淡入延迟到动画结束、兜底定时器
- **症状**: 10240 上打开图片"先放大到 >100% 再缩小"、"淡入过程中缩放乱跳"、"打开显示错误的邻居图"
- **根因**: 10240 上 `ImageOpened` 在布局未稳时提前触发、事件链偶发断链 (`ChangeView` 动画完成后 `ViewChanged(IsIntermediate=false)` 不触发; 同一 BitmapImage 实例重复赋值不再触发 `ImageOpened`); 换图瞬间 `TheImage.ActualWidth` 是**旧图遗留尺寸**
- **解决 (ImageViewer 状态机, 见 `Controls/ImageViewer.xaml.cs`)**:
  - `OnSourceChanged`: 换图即隐藏 (`Opacity=0`)、`_pendingFit=true`, 等布局稳定; 起 120ms `_fitRetryTimer` 兜底
  - `SizeChanged`(布局完成)/`OnFitRetryTick` → `TryFit()` → 成功后 `RequestFadeIn`
  - `ViewChanged(IsIntermediate=false)`(fit 动画结束)执行 `FadeIn`, 300ms `_fadeInTimer` 兜底 —— 图片可见时缩放动画已结束, 不会"淡入中乱跳"
- **10240 三参 `ChangeView(null, null, fit)` 居中**: offset 由系统在 zoom 变化时自行锚定(保持视口中心), 居中正确; **不要**显式传 offset —— `Extent` 异步更新, 手算偏移会基于不一致的内部状态偏右下方
- **曾尝试又回退的方案**: `ZoomToFactor`/手动 `cx/cy`/`_pendingCenter`/`CenterViewport` (commit 2e149a7 前), 全部移除; 保持三参 + 系统锚定

### 20. 高 DPI 下 `BitmapImage.PixelWidth` 是物理像素, 与布局尺寸不一致 (fit 必须用 ActualWidth)
- **症状**: 220% 缩放的设备上图片显示比 10240(100% DPI)小约 DPI 倍数; 模拟 10240 与真实不符
- **根因**: `PixelWidth` 是解码物理像素; 220% 设备上 quick 低清图布局 1024x603 而 PixelWidth 报 2248, `ScrollViewer` 的 Extent 按**布局尺寸**算 → 按像素算的 fit 偏小 2.2 倍
- **解决**: `FitToWindow` 用 `TheImage.ActualWidth/ActualHeight`(与 ScrollViewer 同单位); `PixelWidth > 0` 仅作"解码完成"守卫; 换图瞬间 ActualWidth 是旧图遗留值的问题由坑 19 的"布局稳定后才 fit"时序规避
- **观察技巧**: 日志里 `SizeChanged 1024.0x603.0` 与 `PixelWidth 2248` 并存 = 高 DPI 设备, 此时所有像素单位计算都会出错

### 21. 低清占位图 keep-zoom: 不做二次 fit (IsPlaceholder)
- **症状**: 打开/切换图片"先放大(低清 fit)后缩小(高清 fit)"两次缩放动画
- **解决**: `ImageViewer.IsPlaceholder` (DP, 绑定 `Vm.IsQuickShowing`); 低清邻居占位显示时**不隐藏、不拟合、保持当前 zoom** 直接显示; 高清图到达 (`IsQuickShowing=false`) 才走"隐藏 → fit → 淡入", 全程一次 fit
- 绑定顺序保证: VM 先设 `IsQuickShowing=true` 再赋 `DisplayImage` (x:Bind OneWay 在 PropertyChanged 内同步推送到目标 DP, 无竞态)

### 22. `DispatcherTimer` 防抖必须 Tick 内先 `Stop()`, 否则空转死循环
- **症状**: 日志里每 ~270ms 一条重复 `Fit: ... ChangeView3 (anim)` 直到切图/最小化
- **根因**: `Tick` 里只检查条件不 Stop; 条件在"fit 完成后 `IsAtFitZoom`"上恒成立 → 定时器每 250ms 无限重调 `FitToWindow`(防抖只挡住了 resize 突发, 挡不住空转)
- **解决**: `Tick` 第一行 `_resizeFitTimer.Stop();`, 一次性防抖后归位, 下次 resize 再 `Stop()+Start()`

### 23. 10240 调试设施: 模拟开关 + 文件日志
- **模拟开关** `Helpers/UwpCompat.cs` (全局唯一版本判断入口, 模拟时全部按 10240 分支走):
  - `HasContractV2/V4/V5`(契约版本)、`HasInkToolbar`、`HasXamlRoot` —— 必须是**属性**而非 `static readonly`, 开关切换即时生效
  - `!SettingsService.DebugSimulate10240 && ApiInformation.IsApiContractPresent(...)`; 设置页开发者选项开启
  - 已覆盖分支: `ChangeView` 四参 ×4、`BringIntoViewOptions` ×2、`CompatContentDialog`(23 处)、`Clipboard.SetContentWithOptions`、`PencilProperties`、`InkToolbar`、`XamlRoot` 缩放、`RequestRestartAsync`、解码延迟注入 700ms
- **无法用开关模拟的系统级差异**: 10240 系统字体字形、XAML 控件默认样式/动画时长、ScrollViewer 动画被吞的内部实现、ImageOpened 事件断链
- **文件日志** `Helpers/DebugLog.cs`: 始终写 `%LOCALAPPDATA%\Packages\YoungZhouCorp.HyperViewer_*\LocalState\debug.log`(每次启动清空, 格式 `[ms][tag] 消息`); 所有日志点统一走 `DebugLog.Write` (`[IMG]`/`[VM]`/`[KEY]` 同一时间轴), 不依赖开关

### 24. UWP 的 `Debug` 类没有 `Listeners`/`TextWriterTraceListener`
- 症状: `CS0117: Debug 未包含 Listeners`; `StreamWriter(path, bool, Encoding)` 等重载在 UWP 也缺失
- 解决: 文件日志直接 `new StreamWriter(File.Create(path)) { AutoFlush = true }` 自持落盘; UWP `StreamWriter` 只有 `(Stream)` / `(Stream, Encoding)` 构造

### 25. 打开显示"错误的邻居图片": 邻居缓存按 index 残留错配
- **症状**: 全部照片处打开某图, 显示的是别处的低清图
- **根因**: `_neighborCache`(按 index 缓存 ±2 张 1024px 低清)跨文件夹/会话残留, 新列表的同 index 命中了旧图
- **解决**: `LoadFolderAsync`/`GoHome` 清空缓存; `RaiseImageChangedAsync` 加 `_loadSeq` 序号, 解码完成后过期结果直接丢弃

### 26. 旋转动画必须显式 `From` (10240 隐式基值陷阱)
- **症状**: 旋转 90° → 下一张再旋转, 动画从 0° 转(或先跳回 0°)
- **根因**: `ApplyTransform` 的 `DoubleAnimation` 无 `From`; `_rotationStoryboard.Stop()` 后 `CompositeTransform.Rotation` 回落基值 0, 下一次动画从 0 开始
- **解决**: 动画显式 `From=_currentRotation, To=ImageRotation`, 换图时同步 `_currentRotation = ImageRotation`

### 27. resw 文件禁止用编辑工具直接改 (编码事故)
- **症状**: `edit`/`Write` 工具写 resw 后 BOM 丢失、中文乱码 (zh-CN 与 en-US 各发生过一次, en-US 那次还丢掉了根 `<root>` 闭合标签)
- **解决**: resw 一律 `git checkout` 恢复 + .NET `File.ReadAllText` → `WriteAllText(..., UTF8Encoding($true))` 追加内容; 其他文件 (.cs/.xaml/.md) 编辑工具无问题

## 开发顺序 (已执行 + 待执行)

1. ✅ 搭 MVVM 骨架 + 目录 + 自研基类
2. ✅ 打通主流程: 文件/文件夹选择 → 单张显示 → 上一张/下一张 → 缩放拖拽
3. ✅ 快捷键 + 全屏 + 文件激活入口
4. ✅ 缩略图栏、旋转/翻转、幻灯片、最近打开、错误占位、加载进度
5. ✅ EXIF 信息面板、时间轴、编辑页 (裁剪/调色/滤镜)、设置页、国际化
6. 🔜 主页欢迎面板 (已完成, 带窄屏自适应) 之后的 UI 打磨与复盘清单 (见"下一步")
7. 🔜 文件关联清单双击默认打开、超大图分块解码、扩展格式、Store 提交
8. ✅ ARM/ARM64 Release 打包 + 签名 (build-arm.ps1, 见踩坑 11-13)
