# 第三方版权与许可

发布时，`scripts/collect-notices.ps1` 按本次 `win-x64` 程序的 `.deps.json` 运行依赖清单，从 NuGet 还原缓存提取包名、版本、版权声明、原始 `.nuspec` 与包内 LICENSE、NOTICE、COPYING 等文件，生成 `dependencies.json`。实际 .NET 运行时包版本也从该运行依赖清单读取。

声明采用 SPDX 许可证表达式的包，另附 `license-texts` 中的标准全文。全文来源为 SPDX License List Data 官方仓库 https://github.com/spdx/license-list-data/tree/main/text ，下载归档的内容随源码保存。各软件的实际版权持有人以对应包 `.nuspec` 和原始声明为准。

Inter 字体的 OFL 与字体版权说明单列在 `Inter`。中文界面使用系统已安装字体，包内不附微软中文字体文件。

`vendored` 目录保存直接包含在源码中的 netDxf 及其 Geometric Tools 移植代码的原有版权与许可。SharpCAD / OtoCAD 基础项目的完整版权与 MIT 许可保存在软件根目录 LICENSE，来源范围见 NOTICE.md。

自包含 .NET 运行时的许可证及第三方声明从 NuGet 运行时包复制。发布目录中的 `release-manifest.json` 记录归档文件的 SHA256。软件主许可证及上游来源分别见根目录 LICENSE 和 NOTICE.md。

CDGM 数据目录的再分发授权依据待核对，记录位于 `Config/GlassCatalogs/来源说明.txt`。本清单用于保留已收集的材料，发布者需据实际分发范围完成资源授权核对。
