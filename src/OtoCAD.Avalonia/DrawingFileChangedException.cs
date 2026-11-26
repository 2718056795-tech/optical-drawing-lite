using System.IO;

namespace OtoCAD.Avalonia;

internal sealed class DrawingFileChangedException() : IOException(
    "磁盘上的原图已被修改、移动或删除。当前编辑仍保留，可以另存副本。");
