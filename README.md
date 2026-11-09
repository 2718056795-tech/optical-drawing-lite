# 光学出图简版 · Optical Drawing Lite

面向单透镜和双胶合透镜的 Windows 光学加工图工具。导入光学设计、套用模板、编辑图纸并批量导出。

## 下载与使用

当前版本 **V1.11.2**。在 [GitHub Releases](https://github.com/2718056795-tech/optical-drawing-lite/releases/tag/v1.11.2) 下载 Windows x64 便携包，完整解压后运行 **光学出图简版.exe**。便携包自带运行时。

1. 在「模板」页选择默认模板或打开模板库预览。
2. 在「出图」页导入 Zemax（`.zmx`）、CODE V（`.seq`）或受支持的光学处方 JSON。CODE V 导入仅读取处方。
3. 在「图纸」和「属性」页检查材料、口径、尺寸和技术要求。源处方缺少口径时，示意口径以图中提示为准。
4. 在「出图」页导出当前图纸或本批加工图，支持 PDF、DXF、PNG、OTOCAD 和 JSON。

## 从源码构建

在 Windows x64 上安装 .NET 10 SDK，从仓库根目录运行：

```powershell
.\build.ps1
```

输出位于 `artifacts\win-x64`，由本机 .NET 10 运行时启动。请保留输出目录内的资源、配置文件及版权说明。

## 来源与许可

本项目派生自 [OtoCAD Community](https://github.com/otocad/Otocad-Community)，上游代码可追溯到 SharpCAD。详见 [MIT LICENSE](LICENSE)、[NOTICE](NOTICE.md) 和 [第三方依赖声明](third-party-notices/)。

CDGM 玻璃目录为厂家公开数据，来源和名义值说明见 [玻璃目录说明](src/OtoCAD.Avalonia/Config/GlassCatalogs/来源说明.txt)。目录数据保留原归属。
