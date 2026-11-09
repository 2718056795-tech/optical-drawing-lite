using System;
using System.Collections.Generic;
using System.Linq;
using LitMath;

namespace lcdb.Annotation
{
    /// <summary>
    /// 贴面标记的"宿主曲面"约束 (球面/平面): 记住所贴表面的顶点/半径/玻璃侧,
    /// 拖动标记时把光标投影回曲面 → 锚点沿弯月面滑动、朝向随法线, 不脱面。
    /// 顶点为绝对坐标 (随标记平移一起更新)。半径 ∞/极大 = 平面。
    /// </summary>
    public sealed class SurfaceAttachState
    {
        /// <summary>表面顶点 (绝对坐标, 落在光轴上)。</summary>
        public Vector2 Apex { get; set; }
        /// <summary>球面半径 (∞/极大 = 平面)。</summary>
        public double Radius { get; set; } = double.PositiveInfinity;
        /// <summary>玻璃在该面 +X 侧 (外法线朝 -X)。</summary>
        public bool GlassOnPlusX { get; set; }

        /// <summary>标记相对顶点的轴向高度 (吸附点 Y − Apex.Y)。拖动标记沿面滑时更新, 关联重锚据此保持高度。</summary>
        public double Height { get; set; }

        public string OpticalHostKey { get; set; } = "";
        public int OpticalSurfaceIndex { get; set; } = -1;
        public string LastSurfaceGeometry { get; set; } = "";
        public void Configure(Entity host,int surfaceIndex)
        {
            OpticalHostKey=LinearDimension.AnchorKey(host,true);OpticalSurfaceIndex=surfaceIndex;LastSurfaceGeometry="";
        }
        public bool RefreshAttachedMark(Entity mark,IReadOnlyList<Entity> entities)
        {
            if(mark is not ISurfaceAttachable attached||OpticalSurfaceIndex<0||string.IsNullOrEmpty(OpticalHostKey))return false;
            Entity host=null;
            foreach(var e in entities)if(LinearDimension.AnchorKey(e,false)==OpticalHostKey){if(host is not null)return false;host=e;}
            Optic.OpticalSurface surf=null;double ax=0,cy=0,half=0,angle=0;Vector2 origin=default;
            if(host is Optic.OpticalLens l){
                if(OpticalSurfaceIndex>1)return false;
                surf=OpticalSurfaceIndex==0?l.FrontSurface:l.BackSurface;ax=l.Position.X+(OpticalSurfaceIndex==0?-.5:.5)*l.Thickness;cy=l.Position.Y;half=l.Diameter/2;angle=l.Rotation;origin=l.Position;
            }
            if(host is Optic.CementedLens d){
                if(OpticalSurfaceIndex>2)return false;
                surf=OpticalSurfaceIndex==0?d.FrontSurface:OpticalSurfaceIndex==1?d.ContactSurface:d.BackSurface;
                ax=d.Position.X-(d.T1+d.T2)/2+(OpticalSurfaceIndex==0?0:OpticalSurfaceIndex==1?d.T1:d.T1+d.T2);cy=d.Position.Y;half=d.Diameter/2;angle=d.Rotation;origin=d.Position;
            }
            if(surf is null)return false;
            double drawn=Optic.OpticSurfaceGeometry.DrawnSemiAperture(surf,half);
            string fingerprint=FormattableString.Invariant($"{ax:R}|{cy:R}|{drawn:R}|{surf.Radius:R}|{angle:R}");
            if(surf is Optic.AsphericSurface a)fingerprint+=FormattableString.Invariant($"|{a.ConicConstant:R}|{a.QuadraticCoefficient:R}|")+string.Join(",",a.EvenCoefficients.Select(v=>v.ToString("R",System.Globalization.CultureInfo.InvariantCulture)));
            if(fingerprint==LastSurfaceGeometry)return false;
            LastSurfaceGeometry=fingerprint;
            double h=Math.Clamp(Height,-drawn,drawn),step=Math.Min(.05,Math.Max(1e-5,drawn*.0001));
            double low=Math.Max(-drawn,h-step),high=Math.Min(drawn,h+step);
            double slope=(surf.Sag(high)-surf.Sag(low))/Math.Max(1e-9,high-low);
            var point=new Vector2(ax+surf.Sag(h),cy+h);var normal=new Vector2(1,-slope).normalized;if(GlassOnPlusX)normal=-normal;
            if(angle!=0){point=Vector2.RotateInRadian(point,origin,angle*Math.PI/180);normal=Vector2.RotateInRadian(normal,Vector2.Zero,angle*Math.PI/180);}
            Apex=new Vector2(ax,cy);Radius=surf.Radius;attached.AttachToSurface(point,normal);Height=h;return true;
        }

        public SurfaceAttachState() { }
        public SurfaceAttachState(Vector2 apex, double radius, bool glassOnPlusX)
        {
            Apex = apex; Radius = radius; GlassOnPlusX = glassOnPlusX;
        }

        /// <summary>把光标 (按其 Y 取面上同高度点) 投影到曲面: 返回面上吸附点 + 外法线。</summary>
        public (Vector2 point, Vector2 normal) Project(double cursorY)
        {
            double X(double t) => Apex.X + Optic.OpticSurfaceGeometry.ComputeSag(Radius, Math.Abs(t));
            double h = cursorY - Apex.Y;
            var pt = new Vector2(X(h), Apex.Y + h);
            const double d = 0.05;
            double slope = (X(h + d) - X(h - d)) / (2 * d);    // dX/dh (含符号)
            var nPlusX = new Vector2(1, -slope).normalized;
            var outward = GlassOnPlusX ? -nPlusX : nPlusX;
            return (pt, outward);
        }

        public SurfaceAttachState Clone() => new(Apex, Radius, GlassOnPlusX) { Height=Height,OpticalHostKey=OpticalHostKey,OpticalSurfaceIndex=OpticalSurfaceIndex,LastSurfaceGeometry=LastSurfaceGeometry };
    }
}
