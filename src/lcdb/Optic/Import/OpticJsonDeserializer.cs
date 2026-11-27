using System;
using System.Text.Json;
using System.Collections.Generic;

namespace lcdb.Optic.Import
{
    /// <summary>
    /// Optiland <c>Optic.to_dict()</c> JSON 解析器 — 把 <c>surface_group</c> 转成中性
    /// <see cref="ZmxSystemData"/> (与 <see cref="ZmxParser"/> 输出同一模型, 供
    /// PrescriptionToLensBuilder 共用). 不消费 fields/solves/pickups/ray_tracer (2D 制图无关).
    ///
    /// 契约见 docs/开发文档/光学设计导入开发计划.md §8:
    /// - radius/厚度等非有限值线格式统一为 null ⇒ 此处映射为 ∞/0.
    /// - material_post = 该面之后的材料 (Optiland/Zemax 惯例): 牌号玻璃 / IdealMaterial(index) / 空气.
    /// </summary>
    public sealed class OpticJsonDeserializer
    {
        /// <summary>解析 Optic JSON 文本. 失败信息写入返回对象的 Errors/Warnings (不抛).</summary>
        public ZmxSystemData Parse(string json)
        {
            var data = new ZmxSystemData { Mode = "SEQ" };
            if (string.IsNullOrWhiteSpace(json))
            {
                data.AddError("JSON 内容为空");
                return data;
            }

            JsonDocument doc;
            try { doc = JsonDocument.Parse(json); }
            catch (Exception ex) { data.AddError($"JSON 解析失败: {ex.Message}"); return data; }

            using (doc)
            {
                try { ParseRoot(doc.RootElement, data); }
                catch (Exception ex) { data.AddError($"Optic 结构解析失败: {ex.Message}"); }
            }
            return data;
        }

        private void ParseRoot(JsonElement root, ZmxSystemData data)
        {
            if (root.ValueKind != JsonValueKind.Object)
            {
                data.AddError("根节点不是 JSON 对象");
                return;
            }

            if (root.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                data.Title = name.GetString();

            // version 校验 (契约 §8.3): 目前只验证 1.x, 不匹配仅告警
            if (root.TryGetProperty("version", out var ver) && ver.ValueKind == JsonValueKind.Number)
            {
                double v = ver.GetDouble();
                if (v < 1.0 || v >= 2.0)
                    data.AddWarning($"Optic schema 版本 {v} 未经验证, 字段可能不兼容");
            }

            // aperture { type, value }
            if (root.TryGetProperty("aperture", out var ap) && ap.ValueKind == JsonValueKind.Object)
            {
                if (ap.TryGetProperty("type", out var at) && at.ValueKind == JsonValueKind.String)
                    data.ApertureType = at.GetString();
                if (TryGetNumber(ap, "value", out double apv))
                    data.SystemAperture = apv;
            }

            // wavelengths.wavelengths[].value (um)
            if (root.TryGetProperty("wavelengths", out var wg) && wg.ValueKind == JsonValueKind.Object
                && wg.TryGetProperty("wavelengths", out var wl) && wl.ValueKind == JsonValueKind.Array)
            {
                foreach (var w in wl.EnumerateArray())
                    if (w.ValueKind == JsonValueKind.Object && TryGetNumber(w, "value", out double wv))
                        data.Wavelengths.Add(wv);
            }

            // surface_group.surfaces[]
            if (!root.TryGetProperty("surface_group", out var sg) || sg.ValueKind != JsonValueKind.Object
                || !sg.TryGetProperty("surfaces", out var surfs) || surfs.ValueKind != JsonValueKind.Array)
            {
                data.AddError("缺少 surface_group.surfaces");
                return;
            }

            var invalidRadii = new Dictionary<int,string>();
            int idx = 0;
            foreach (var s in surfs.EnumerateArray())
            {
                data.Surfaces.Add(ParseSurface(s, idx, idx==0 || idx==surfs.GetArrayLength()-1, data, invalidRadii));
                idx++;
            }

            // Geometry of an omitted group need not fit the standard surface schema. Keep topology
            // and axial positions, but do not let its radius diagnostics invalidate its neighbours.
            var omitted=data.GetOmittedDrawingSurfaces();
            foreach(var error in invalidRadii)
            {
                if(omitted[error.Key])data.Surfaces[error.Key].AddUnsupportedReason(error.Value);
                else data.AddError(error.Value);
            }

            if (data.Surfaces.Count < 2)
                data.AddError("表面数量不足 (至少需要 OBJ + IMG)");

            if (data.Wavelengths.Count == 0)
            {
                data.Wavelengths.Add(0.55);
                data.AddWarning("未指定波长, 使用默认 0.55um");
            }
        }

        private ZmxSurface ParseSurface(JsonElement s, int index, bool endpoint, ZmxSystemData data, Dictionary<int,string> invalidRadii)
        {
            var surf = new ZmxSurface { SurfaceNumber = index };
            if (s.ValueKind != JsonValueKind.Object)
            {
                data.AddError($"表面 S{index} 不是对象，无法识别介质边界。");
                return surf;
            }
            if (s.TryGetProperty("type",out var wrapper) && wrapper.ValueKind==JsonValueKind.String &&
                wrapper.GetString() is not ("Surface" or "ObjectSurface" or "ImageSurface"))
                surf.AddUnsupportedReason("不支持的 Optiland 表面 "+wrapper.GetString());

            if (s.TryGetProperty("geometry",out var geometry) && geometry.ValueKind==JsonValueKind.Object)
                ReadGeometry(geometry,surf,invalidRadii);
            else if (!endpoint) surf.AddUnsupportedReason("缺少明确的 geometry，无法确定加工轮廓");

            if (s.TryGetProperty("interaction_model",out var model) && model.ValueKind==JsonValueKind.Object)
            {
                if (model.TryGetProperty("type",out var mt) && mt.ValueKind==JsonValueKind.String &&
                    mt.GetString()!="RefractiveReflectiveModel")
                    surf.AddUnsupportedReason("不支持的相互作用模型 "+mt.GetString());
                if (model.TryGetProperty("is_reflective",out var flag) && flag.ValueKind==JsonValueKind.True)
                {
                    surf.ExtraParameters["reflective"]=true;
                    surf.AddUnsupportedReason("不支持反射相互作用");
                }
            }
            if (s.TryGetProperty("is_reflective",out var reflective) && reflective.ValueKind==JsonValueKind.True)
            {
                surf.ExtraParameters["reflective"]=true;
                surf.AddUnsupportedReason("不支持反射表面");
            }

            // thickness (到下一面距离); 物面 null ⇒ 0
            if (s.TryGetProperty("thickness",out var spacing) && spacing.ValueKind!=JsonValueKind.Null)
            {
                if (Finite(spacing,out var thickness)) surf.Thickness=thickness;
                else data.AddError($"表面 S{index} thickness 无效，无法确定后续元件的轴向位置。");
            }

            if (s.TryGetProperty("material_post",out var m) && m.ValueKind!=JsonValueKind.Null)
            {
                if (m.ValueKind==JsonValueKind.Object)ParseMaterial(m,surf);
                else data.AddError($"表面 S{index} material_post 无效，无法确定玻璃组边界。");
            }

            if (s.TryGetProperty("is_stop", out var st) && st.ValueKind == JsonValueKind.True)
                surf.IsStop = true;

            if (s.TryGetProperty("comment", out var cm) && cm.ValueKind == JsonValueKind.String)
                surf.Comment = cm.GetString();

            // 径向 aperture (Optiland RadialAperture, 来自 Zemax CLAP) = 净口径; r_max=净口径半径.
            // 注: Zemax 的机械 Semi-Diameter(DIAM) 不进 Optic to_dict (见 §8.3 缺口 2), 故此处只得净口径.
            if (s.TryGetProperty("aperture", out var sap) && sap.ValueKind == JsonValueKind.Object)
            {
                if (TryGetNumber(sap, "r_max", out double rmax)) surf.ClearSemiDiameter = rmax;
            }

            return surf;
        }

        private static bool Finite(JsonElement field,out double value)
        {
            value=0;
            bool numeric=field.ValueKind==JsonValueKind.Number && field.TryGetDouble(out value)
                || field.ValueKind==JsonValueKind.String && double.TryParse(field.GetString(),System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,out value);
            return numeric && double.IsFinite(value);
        }

        private static void ReadGeometry(JsonElement g,ZmxSurface surface,Dictionary<int,string> invalidRadii)
        {
            string name="StandardGeometry";
            if (g.TryGetProperty("type",out var type))
                name=type.ValueKind==JsonValueKind.String?type.GetString():"（无效 type）";
            surface.Type=name switch
            {
                "Plane" or "StandardGeometry"=>"STANDARD", "EvenAsphere"=>"EVENASPH",
                _=>name??"（空）"
            };
            if (name is not ("Plane" or "StandardGeometry" or "EvenAsphere"))
            {
                surface.AddUnsupportedReason("不支持的 Optiland 面型 "+name);
                return; // A freeform's geometry fields need not have the standard surface schema.
            }
            if (!g.TryGetProperty("radius",out var field))
            {
                if (name!="Plane") surface.AddUnsupportedReason("缺少 geometry.radius，无法确定曲率");
            }
            else if (field.ValueKind!=JsonValueKind.Null)
            {
                if (!Finite(field,out double radius) || radius==0 || !double.IsFinite(1/radius))
                    invalidRadii[surface.SurfaceNumber]=$"表面 S{surface.SurfaceNumber} geometry.radius 无效，须为非零有限半径或用 null 表示平面。";
                else if (name=="Plane") surface.AddUnsupportedReason("Plane 几何与提供的有限半径冲突");
                else {surface.Radius=radius;surface.Curvature=1/radius;}
            }
            if (g.TryGetProperty("conic",out var conic))
            {
                if (Finite(conic,out var k)) surface.Conic=k;
                else surface.AddUnsupportedReason("无法确定 geometry.conic");
            }
            if (!g.TryGetProperty("coefficients",out var terms)) return;
            if (terms.ValueKind!=JsonValueKind.Array)
            {
                surface.AddUnsupportedReason("无法确定 geometry.coefficients");
                return;
            }
            // Optiland EvenAsphere.sag: coefficients[i] multiplies r^(2i+2), i.e. A2 first.
            int i=0;
            foreach(var coefficient in terms.EnumerateArray())
            {
                int power=2*(i+1);
                if (!Finite(coefficient,out var v))
                    surface.AddUnsupportedReason($"无法识别的非球面系数 A{power}");
                else if (name!="EvenAsphere" && v!=0)
                    surface.AddUnsupportedReason($"{name} 包含未定义的非球面系数 A{power}");
                else if (i==0) surface.QuadraticCoefficient=v;
                else if (i<=surface.AsphericCoefficients.Length) surface.AsphericCoefficients[i-1]=v;
                else if (v!=0)surface.AddUnsupportedReason($"不支持的非球面阶次 A{power}");
                i++;
            }
        }

        private static void ParseMaterial(JsonElement m, ZmxSurface surf)
        {
            string mtype = (m.TryGetProperty("type", out var mt) && mt.ValueKind == JsonValueKind.String
                ? mt.GetString() : null) ?? "";

            // 牌号玻璃 (type=="Material")
            if (m.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(nm.GetString()))
            {
                surf.Glass = nm.GetString();
            }

            // 折射率 (IdealMaterial.index; index≈1.0 视为空气, 不记)
            if (TryGetNumber(m, "index", out double n) && n > 1.0 + 1e-6)
                surf.RefractiveIndexPost = n;

            if (TryGetNumber(m, "abbe", out double abbe))
                surf.AbbeNumberPost = abbe;

            if (mtype.Contains("Mirror",StringComparison.OrdinalIgnoreCase))
            {
                surf.ExtraParameters["reflective"]=true;
                surf.AddUnsupportedReason("不支持反射介质 "+mtype);
            }
            if (m.TryGetProperty("propagation_model",out var model) && model.ValueKind==JsonValueKind.Object &&
                model.TryGetProperty("class",out var kind) && kind.ValueKind==JsonValueKind.String &&
                kind.GetString() is string propagation && (propagation.Contains("Gradient",StringComparison.OrdinalIgnoreCase) ||
                    propagation.Contains("GRIN",StringComparison.OrdinalIgnoreCase)))
                surf.AddUnsupportedReason("不支持梯度折射率材料 "+propagation);
        }

        private static bool TryGetNumber(JsonElement obj, string prop, out double value)
        {
            value = 0.0;
            if (obj.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.Number)
            {
                value = p.GetDouble();
                return true;
            }
            return false;
        }

    }
}
