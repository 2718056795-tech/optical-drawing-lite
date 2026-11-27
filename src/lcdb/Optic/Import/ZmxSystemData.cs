using System;
using System.Collections.Generic;
using System.Linq;

namespace lcdb.Optic.Import
{
    /// <summary>
    /// Zemax光学系统数据模型
    /// </summary>
    public class ZmxSystemData
    {
        #region Properties

        /// <summary>
        /// 文件路径
        /// </summary>
        public string FilePath { get; set; }

        /// <summary>
        /// 系统标题
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// Zemax版本
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// 系统模式 (SEQ = 序列, NSC = 非序列)
        /// </summary>
        public string Mode { get; set; } = "SEQ";

        /// <summary>
        /// 波长列表 (um)
        /// </summary>
        public List<double> Wavelengths { get; set; } = new List<double>();

        /// <summary>
        /// 系统孔径值
        /// </summary>
        public double SystemAperture { get; set; }

        /// <summary>
        /// 孔径类型 (EPD, F/#, NAO, etc.)
        /// </summary>
        public string ApertureType { get; set; }

        /// <summary>
        /// 表面列表
        /// </summary>
        public List<ZmxSurface> Surfaces { get; set; } = new List<ZmxSurface>();

        /// <summary>
        /// 解析警告
        /// </summary>
        public List<string> Warnings { get; set; } = new List<string>();

        /// <summary>
        /// 解析错误
        /// </summary>
        public List<string> Errors { get; set; } = new List<string>();

        /// <summary>
        /// 原始未解析的行
        /// </summary>
        public List<string> UnparsedLines { get; set; } = new List<string>();

        /// <summary>
        /// 扩展系统数据
        /// </summary>
        public Dictionary<string, object> ExtraData { get; set; } = new Dictionary<string, object>();

        #endregion

        #region Calculated Properties

        /// <summary>
        /// 解析是否成功 (无错误)
        /// </summary>
        public bool IsValid => Errors.Count == 0;

        /// <summary>
        /// 光学表面数量 (不含OBJ和IMG)
        /// </summary>
        public int OpticalSurfaceCount => Surfaces.Count > 2 ? Surfaces.Count - 2 : 0;

        /// <summary>
        /// 获取所有元件 (连续的材料组)
        /// </summary>
        public List<ZmxElement> Elements => ExtractElements();

        /// <summary>
        /// 系统总长度 (OBJ到IMG的距离)
        /// </summary>
        public double TotalLength => Surfaces.Sum(s => s.Thickness);

        /// <summary>
        /// 光阑位置
        /// </summary>
        public int? StopSurfaceNumber => Surfaces.FirstOrDefault(s => s.IsStop)?.SurfaceNumber;

        #endregion

        #region Methods

        internal readonly record struct MaterialGroup(int Number, int Front, int Back, bool Closed)
        {
            public int GlassCount => Closed ? Back - Front : Back - Front + 1;
        }

        // Include the terminating air interface in each glass group. Independent air
        // surfaces between groups never get attached to the preceding or following lens.
        internal IEnumerable<MaterialGroup> GetMaterialGroups()
        {
            int number = 0;
            for (int front = 1; front < Surfaces.Count - 1; front++)
            {
                if (!Surfaces[front].HasMaterial) continue;
                int end = front + 1;
                while (end < Surfaces.Count && Surfaces[end].HasMaterial) end++;
                bool closed = end < Surfaces.Count;
                int back = closed ? end : Surfaces.Count - 1;
                yield return new MaterialGroup(++number, front, back, closed);
                front = back;
            }
        }

        internal bool[] GetOmittedDrawingSurfaces()
        {
            var omitted = new bool[Surfaces.Count];
            bool reflectedPath = false;
            for (int i = 0; i < Surfaces.Count; i++)
            {
                var surface = Surfaces[i];
                if (surface.IsReflectiveInterface) reflectedPath = !reflectedPath;
                if (reflectedPath && surface.HasMaterial && double.IsFinite(surface.Thickness) && surface.Thickness < 0)
                    surface.AddUnsupportedReason("镜后折返段的玻璃采用负厚度，尚不支持其加工图方向");
                surface.IdentifyUnsupportedGeometry();
                omitted[i] = Surfaces[i].UnsupportedReasons.Count != 0;
            }
            foreach (var group in GetMaterialGroups())
            {
                bool skip = !group.Closed || group.GlassCount > 2;
                for (int i = group.Front; i <= group.Back; i++) skip |= omitted[i];
                if (skip) Array.Fill(omitted, true, group.Front, group.Back - group.Front + 1);
            }
            return omitted;
        }

        private List<ZmxElement> ExtractElements()
        {
            var elements = new List<ZmxElement>();
            foreach (var group in GetMaterialGroups())
            {
                if (!group.Closed) continue;
                var front = Surfaces[group.Front];
                elements.Add(new ZmxElement
                {
                    StartSurfaceIndex = group.Front, EndSurfaceIndex = group.Back,
                    FrontSurface = front.Clone(), BackSurface = Surfaces[group.Back].Clone(),
                    GlassMaterial = front.Glass, CenterThickness = front.Thickness
                });
            }
            return elements;
        }

        /// <summary>
        /// 获取指定序号的表面
        /// </summary>
        public ZmxSurface GetSurface(int surfaceNumber)
        {
            return Surfaces.FirstOrDefault(s => s.SurfaceNumber == surfaceNumber);
        }

        /// <summary>
        /// 添加警告
        /// </summary>
        public void AddWarning(string message)
        {
            Warnings.Add(message);
        }

        /// <summary>
        /// 添加错误
        /// </summary>
        public void AddError(string message)
        {
            Errors.Add(message);
        }

        public override string ToString()
        {
            return $"ZmxSystem: {Title ?? FilePath}, {Surfaces.Count} surfaces, {Elements.Count} elements";
        }

        #endregion
    }

    /// <summary>
    /// Zemax光学元件数据 (前后表面组成的单个元件)
    /// </summary>
    public class ZmxElement
    {
        /// <summary>
        /// 前表面在Surfaces列表中的索引
        /// </summary>
        public int StartSurfaceIndex { get; set; }

        /// <summary>
        /// 后表面在Surfaces列表中的索引
        /// </summary>
        public int EndSurfaceIndex { get; set; }

        /// <summary>
        /// 前表面数据
        /// </summary>
        public ZmxSurface FrontSurface { get; set; }

        /// <summary>
        /// 后表面数据
        /// </summary>
        public ZmxSurface BackSurface { get; set; }

        /// <summary>
        /// 玻璃材料
        /// </summary>
        public string GlassMaterial { get; set; }

        /// <summary>
        /// 中心厚度
        /// </summary>
        public double CenterThickness { get; set; }

        #region Calculated Properties

        /// <summary>
        /// 前表面曲率半径 (R1)
        /// </summary>
        public double R1 => FrontSurface?.Radius ?? double.PositiveInfinity;

        /// <summary>
        /// 后表面曲率半径 (R2)
        /// </summary>
        public double R2 => BackSurface?.Radius ?? double.PositiveInfinity;

        /// <summary>
        /// 元件直径 (取较大的口径)
        /// </summary>
        public double Diameter => Math.Max(
            FrontSurface?.Diameter ?? 0,
            BackSurface?.Diameter ?? 0);

        /// <summary>
        /// 元件半口径
        /// </summary>
        public double SemiDiameter => Diameter / 2;

        /// <summary>
        /// 前表面边缘厚度
        /// </summary>
        public double FrontEdgeSag => FrontSurface?.EdgeSag ?? 0;

        /// <summary>
        /// 后表面边缘厚度
        /// </summary>
        public double BackEdgeSag => BackSurface?.EdgeSag ?? 0;

        /// <summary>
        /// 边缘厚度
        /// </summary>
        public double EdgeThickness => CenterThickness - FrontEdgeSag + BackEdgeSag;

        /// <summary>
        /// 是否为非球面元件
        /// </summary>
        public bool IsAspheric => (FrontSurface?.IsAspheric ?? false) ||
                                   (BackSurface?.IsAspheric ?? false);

        #endregion

        public override string ToString()
        {
            return $"Element: R1={R1:F3}, R2={R2:F3}, T={CenterThickness:F3}, Glass={GlassMaterial}, D={Diameter:F3}";
        }
    }
}
