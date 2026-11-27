# 光学出图简版 · Optical Drawing Lite

面向单透镜和双胶合透镜的 Windows 光学加工图工具。导入光学设计、套用模板、编辑图纸并批量导出。维护与发布署名：**XSZ**。

## 下载与使用

本源码版本为 **V1.13.2**，面向 Windows 10 / 11 x64。已发布版本以 [GitHub Releases](https://github.com/2718056795-tech/optical-drawing-lite/releases) 为准。完整解压软件包后运行 **光学出图简版.exe**，软件包自带 .NET 运行时。

1. 在「模板」页选择默认模板或打开模板库预览。
2. 在「出图」页导入 Zemax（`.zmx`）、CODE V（`.seq`）或受支持的光学处方 JSON。CODE V 导入仅读取处方。
3. 在「图纸」和「属性」页检查材料、口径、尺寸和技术要求。源处方缺少口径时，示意口径以图中提示为准。
4. 在「图纸」页按本次导入、当前图纸、勾选图纸或全部加工图导出 PDF、DXF、PNG、OTOCAD 和 JSON。勾选范围也适用于批量清空。光路总览单独列出。
5. 从一张加工图复用版式时，在「模板」页选择「当前作为模板」或另存独立模板。下一张图会重新填入自己的曲率、材料和厚度。「插入设计字段」可将这些数据放在自定文字或表格的光标位置。
6. 使用固定制图比例时，图签保留完整倍率。打印使用和 PDF 相同的纸面方向和比例，在 PDF 阅读器或打印驱动中选择“实际大小 / 100%”可按纸面毫米输出。底部“视图 100%”用于屏幕查看。

OTOCAD 和原生图纸 JSON 内嵌 Logo，文件可单独搬移。含图片的 DXF 使用相对路径引用输出目录内的 `Assets` 文件夹，交付时将它与 DXF 一起复制。

Zemax 处方按每个面的通光半径、延伸区（Chip Zone）和机械半径生成轮廓。延伸区沿原面矢高继续，到延伸区外缘后才接平肩。通光直径用于参数表。同一元件的各面指定了不同机械半径时，导入报告会列出这些数值。当前轮廓采用该件的最大机械外径，异径外壁需按原设计复核。要把旧图更新为源文件定义的延伸区，请用新版重新导入原始处方。

支持序列设计中的标准球面、平面及偶次非球面。Zemax 读取 STANDARD、EVENASPH（A2 至 A16），文件须声明单位。非序列系统、尚未拆分的 Zemax 多配置和缺少必要单位的处方，需要先在设计软件中整理后再导入。

同一份序列处方中可以混合不同面型。导入窗口列出可出图元件、跳过的元件和具体源面号。例如元件 2 的胶合面 S4 为 TOROIDAL 时，程序跳过整个元件 2，元件 1 和 3 保留原编号并可继续出图。独立的特殊空气面会单独提示，三胶合及以上组件也会注明原因。展开「跳过项与导入提示」可查看详情和报告位置。

CODE V 按静态处方读取，可选择受支持的 ZOO 配置。装配偏心、倾斜等超出加工图处方范围的命令会在导入提示中说明。导入报告中的材料、单位和示意口径提示应逐项核对，材料未匹配时填写实际目录数据或批次数据后再用于加工。

## 模板与资料迁移

在「模板」页选择模板、编辑图框与表格，保存模板后设为默认，再导入设计生成加工图。可编辑的模板保存在 `%LOCALAPPDATA%\OpticalDrawingLite\Templates`，设置、导入报告和自动备份保存在相邻的 `Data` 目录。备份或换电脑时复制整个 `%LOCALAPPDATA%\OpticalDrawingLite` 文件夹。

首次启动会复制程序旁的旧 `Templates` 和 `Data`，已有目标文件优先。沿用 `V版本号\程序` 目录部署时，也会寻找相邻的最近旧版本。使用其他解压结构时，可以关闭程序后将旧版这两个文件夹复制到新资料目录。程序正常启动后，图纸通过「图纸」页另行保存到自己选择的位置。

需要把资料留在 U 盘或软件文件夹时，在 EXE 旁创建空文件 `portable.flag`，并确保该文件夹可写。高级部署可用环境变量 `OPTICAL_DRAWING_LITE_DATA_DIR` 指定资料根目录，其优先级高于便携标记。

## 从源码构建与打包

在 Windows x64 上安装 .NET SDK 10.0.403 或同一 10.0.4xx 功能带的更新补丁版本，从仓库根目录运行下列命令。`global.json` 固定 SDK 功能带，首次构建需要从 NuGet 下载依赖。

```powershell
.\build.ps1
```

输出位于 `artifacts\win-x64-v1.13.2`，ZIP 和对应 SHA256 文件位于 `artifacts\packages`。可用 `-Dotnet` 指定 SDK 可执行文件，用 `-Output` 和 `-PackageDirectory` 指定产物位置。输出目录须为空，每次打包会检查此条件。

构建脚本使用自包含 `win-x64` 发布，保留配置、图标、材料来源说明和许可证。它按本次 NuGet 还原结果复制依赖的版权、许可全文和 .NET 运行时声明，同时生成 `release-manifest.json`，记录 SDK 版本、文件大小、哈希和 EXE 签名状态。新依赖的许可类型需要补入已审核的许可全文后才能打包。

本次交付按发布者 XSZ 的选择提供未签名程序；XSZ 是程序中的维护与发布署名，不是数字签名。已完成本机自动测试、控件级验证、五种格式导出及解压后的运行包验证。另一台电脑的人工流程和外部模拟鼠标点击不在本次已完成验证范围内，发布者已接受这一验证范围。

## 来源与许可

本项目派生自 [OtoCAD Community](https://github.com/otocad/Otocad-Community)，上游代码可追溯到 SharpCAD。详见 [MIT LICENSE](LICENSE)、[NOTICE](NOTICE.md) 和 [第三方依赖声明](third-party-notices/)。

CDGM 玻璃目录的下载来源、名义值说明见 [项目中的玻璃目录说明](https://github.com/2718056795-tech/optical-drawing-lite/blob/main/src/OtoCAD.Avalonia/Config/GlassCatalogs/来源说明.txt)，软件包内也保存在 `Config/GlassCatalogs/来源说明.txt`。本版本依据项目发布者 XSZ 对随包提供该厂家公开目录的确认进行交付，保留厂家归属、下载来源和校验值。

界面采用系统中文字体及 Avalonia 随附 Inter。Inter 的字体版权与 SIL Open Font License 全文随软件保存在 `third-party-notices/Inter`。问题反馈请到本项目 [Issues](https://github.com/2718056795-tech/optical-drawing-lite/issues)。
