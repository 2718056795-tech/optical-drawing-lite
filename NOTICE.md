# NOTICE

## 光学出图简版

Optical Drawing Lite 由 XSZ 维护与发布，在 OtoCAD Community 基础上提供模板化光学加工图工作流。GitHub 账号为 2718056795-tech，本派生版本的主页、问题反馈和发布入口为 https://github.com/2718056795-tech/optical-drawing-lite 。上游作者及原有版权声明保留如下。

发布包的 `third-party-notices/dependencies.json` 记录本次构建的 NuGet 依赖，所用许可全文及包内版权声明随附。Inter 字体的 SIL Open Font License 见 `third-party-notices/Inter`。

软件随附 Adobe Source Han Serif SC 2.003R 的 Regular 和 Bold，字体按 SIL Open Font License 1.1 分发。源码许可文件位于 `third-party-notices/SourceHanSerif/OFL.txt`，运行包位于 `Fonts/OFL-Source-Han-Serif.txt`。程序可选用用户电脑上已有的 Anthropic Serif Web 字体。

CDGM 玻璃目录保持厂家归属。本版本依据项目发布者 XSZ 对随包提供该厂家公开目录的确认进行交付，并保留下载来源与文件校验值。详见 `Config/GlassCatalogs/来源说明.txt`，源码中的对应目录为 `src/OtoCAD.Avalonia/Config/GlassCatalogs`。

OtoCAD 是 RayPragma 在 SharpCAD 基础上演化而来的二维光学 CAD 程序。本文件列出
项目集成的第三方代码归属信息 (vendored, 即源码包含在本仓 `src/` 里的第三方项目).

## 上游派生

### SharpCAD (initial codebase)
- **作者**: Hisin Wang
- **协议**: MIT License (2018)
- **范围**: 项目骨架, 包括 `lcdb` / `lcinterface` / `LitMath` 三个子项目的核心结构,
  以及 Database / Entity / Layer / Block / Polyline / Arc 等 ObjectARX 风格基础类.
- **本仓 LICENSE 已合并版权声明** (双 copyright, OtoCAD 衍生开发同 MIT 协议)

OtoCAD 后续重写/扩展了大量代码 (Avalonia + SkiaSharp 重构, ISO 10110 光学标注体系,
GB/T 13323-2009 光学制图合规, Annotation 关联系统等), 但核心命名空间与部分基础类
的设计仍可追溯到 SharpCAD.

## 内嵌的第三方代码 (vendored)

### netDxf
- **协议**: MIT
- **版权**: Copyright (c) 2019-2021 Daniel Carvajal (haplokuon@gmail.com)
- **来源**: https://github.com/jankozik/netDxf  (commit 26e4d11)
- **位置**: `src/netDxf/`
- **用途**: DXF 文件读写
- **完整声明**: 随包 `third-party-notices/vendored/netDxf/LICENSE.txt`，取自保留的源码文件头。

### Geometric Tools 的 C# 移植

`src/netDxf/GTE` 保留 David Eberly、Geometric Tools 的原始归属，Copyright (c) 1998-2022，原代码使用 Boost Software License 1.0。C# 移植部分另保留 Daniel Carvajal 的 MIT 声明。两组声明及 Boost 全文一并位于 `third-party-notices/vendored/netDxf`。

NuGet 引入的标准 OSS 包 (Avalonia / SkiaSharp / NLog 等) 见各 `*.csproj` 的
`PackageReference`, 按各自 NuGet 元数据中的协议授权, 此处不再单独列出.

## 法律声明

本项目以 MIT License 发布 (见 `LICENSE`). 任何分发本软件 (含修改版) 时,
必须保留 `LICENSE` 与本 `NOTICE.md` 文件以及其中的版权声明.
