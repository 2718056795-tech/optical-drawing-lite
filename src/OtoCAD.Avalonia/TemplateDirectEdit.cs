using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using lcdb;
using lcdb.Optic;

namespace OtoCAD.Avalonia.Slim;

public sealed record DirectParameter(string Key, string Label, double Value, string Unit, bool AllowInfinity = false);

public static partial class TemplatePipeline
{
    // A sheet represents one prescription, possibly displayed in several differently scaled views.
    // Driving keys therefore identify prescription properties, never positions or display lengths.
    private static Entity[] DirectOwners(IReadOnlyList<Entity> entities)
    {
        var owners = entities.Where(e => e.DrawingBinding is not null && e is OpticalLens or CementedLens).ToArray();
        if (owners.Length == 0) throw new InvalidOperationException("这张图纸没有可编辑的设计参数绑定，请先导入设计文件生成图纸。");
        var first = owners[0];
        var context = first.DrawingBinding!;
        foreach (var owner in owners)
        {
            var binding = owner.DrawingBinding!;
            if (owner.GetType() != first.GetType() || binding.Name != context.Name || binding.Number != context.Number || binding.Source != context.Source)
                throw new InvalidOperationException("当前图纸包含不同零件，无法确定应修改哪一组设计参数。");
            if (!double.IsFinite(binding.ViewScale) || binding.ViewScale <= 0)
                throw new InvalidOperationException("图纸比例无效，不能修改设计参数。");
        }
        return owners;
    }

    private static Entity DirectSource(Entity[] owners)
        => SourcePartFromView(owners.FirstOrDefault(e => GeometrySignature(e) != e.DrawingBinding!.LastGeometrySignature) ?? owners[0]);

    private static OpticalSurface[] DirectSurfaces(Entity source) => source switch
    {
        OpticalLens l => new[] { l.FrontSurface, l.BackSurface },
        CementedLens d => new[] { d.FrontSurface, d.ContactSurface, d.BackSurface },
        _ => Array.Empty<OpticalSurface>()
    };

    private static DirectParameter? DescribeDirectParameter(Entity source, string key)
    {
        var l = source as OpticalLens;
        var d = source as CementedLens;
        if (l is null && d is null) return null;
        double diameter = l?.Diameter ?? d!.Diameter;
        double Aperture(OpticalSurface surface) => surface.SemiAperture > 0 ? surface.SemiAperture * 2 : diameter;
        return key switch
        {
            "Thickness" when l is not null => new(key, "中心厚度", l.Thickness, "mm"),
            "T1" when d is not null => new(key, "第一片中心厚度", d.T1, "mm"),
            "T2" when d is not null => new(key, "第二片中心厚度", d.T2, "mm"),
            "R1" => new(key, "前表面曲率半径（带正负号）", l?.R1 ?? d!.R1, "mm", true),
            "R2" when l is not null => new(key, "后表面曲率半径（带正负号）", l.R2, "mm", true),
            "RContact" when d is not null => new(key, "胶合面曲率半径（带正负号）", d.RContact, "mm", true),
            "R3" when d is not null => new(key, "后表面曲率半径（带正负号）", d.R3, "mm", true),
            "Diameter" when DirectSurfaces(source).All(s => s.SemiAperture <= 0) => new(key, "通光口径", diameter, "mm"),
            "MechanicalDiameter" => new(key, "机械外径", l?.MechanicalDiameter ?? d?.MechanicalDiameter ?? diameter, "mm"),
            "FrontAperture" => new(key, "前表面通光口径", Aperture(l?.FrontSurface ?? d!.FrontSurface), "mm"),
            "BackAperture" => new(key, "后表面通光口径", Aperture(l?.BackSurface ?? d!.BackSurface), "mm"),
            "ContactAperture" when d is not null => new(key, "胶合面通光口径", Aperture(d.ContactSurface), "mm"),
            _ => null
        };
    }

    private static string? DirectKeyForField(Entity source, string field) => field switch
    {
        "前表面半径" => "R1",
        "后表面半径" => source is OpticalLens ? "R2" : "R3",
        "胶合面半径" or "胶合面片1半径" or "胶合面片2半径" => source is CementedLens ? "RContact" : null,
        "中心厚度" => source is OpticalLens ? "Thickness" : null,
        "片1厚度" => source is OpticalLens ? "Thickness" : "T1",
        "片2厚度" => source is CementedLens ? "T2" : null,
        "通光口径" => "Diameter",
        "机械外径" => "MechanicalDiameter",
        "前表面通光口径" => "FrontAperture",
        "后表面通光口径" => "BackAperture",
        "胶合面通光口径" => source is CementedLens ? "ContactAperture" : null,
        _ => null
    };

    /// <summary>Column and row are zero-based; only tracked data cells expose driving values.</summary>
    public static IReadOnlyList<DirectParameter> GetDirectParametersForCell(IReadOnlyList<Entity> entities, Entity container, int col, int row)
    {
        if (col < 0 || row < 0 || !entities.Any(e => ReferenceEquals(e, container)) || container.TemplateTextBindings is null ||
            !container.TemplateTextBindings.TryGetValue($"c:{col}:r:{row}", out var binding)) return Array.Empty<DirectParameter>();
        try
        {
            var source = DirectSource(DirectOwners(entities));
            return Regex.Matches(binding.Expression, "【([^】]+)】").Select(m => DirectKeyForField(source, m.Groups[1].Value))
                .Where(key => key is not null).Distinct().Select(key => DescribeDirectParameter(source, key!)).OfType<DirectParameter>().ToArray();
        }
        catch (InvalidOperationException) { return Array.Empty<DirectParameter>(); }
    }

    public static DirectParameter? GetDirectParameterForAnnotation(IReadOnlyList<Entity> entities, Entity annotation)
    {
        if (!entities.Any(e => ReferenceEquals(e, annotation))) return null;
        var owner = annotation.ResolveDrivingOwner(entities);
        if (owner?.DrawingBinding is null) return null;
        try
        {
            _ = DirectOwners(entities); // Reject ambiguous sheets even when one dimension can resolve its own view.
            return DescribeDirectParameter(SourcePartFromView(owner), annotation.InferDrivingParameterKey(entities));
        }
        catch (InvalidOperationException) { return null; }
    }

    private static void AssignDirectParameter(Entity source, string key, double value)
    {
        var l = source as OpticalLens;
        var d = source as CementedLens;
        double oldDiameter = l?.Diameter ?? d!.Diameter;
        double? oldMechanical = l?.MechanicalDiameter ?? d?.MechanicalDiameter;
        double minimumOuter = Math.Max(oldDiameter, DirectSurfaces(source).Max(s => s.SemiAperture * 2));
        if (key == "MechanicalDiameter" && value < minimumOuter - 1e-8)
            throw new InvalidOperationException("机械外径不能小于整体或任一面的通光口径。");
        switch (key)
        {
            case "Thickness" when l is not null: l.Thickness = value; break;
            case "T1" when d is not null: d.T1 = value; break;
            case "T2" when d is not null: d.T2 = value; break;
            case "R1": (l?.FrontSurface ?? d!.FrontSurface).Radius = value; break;
            case "R2" when l is not null: l.BackSurface.Radius = value; break;
            case "RContact" when d is not null: d.ContactSurface.Radius = value; break;
            case "R3" when d is not null: d.BackSurface.Radius = value; break;
            case "Diameter": if (l is not null) l.Diameter = value; else d!.Diameter = value; break;
            case "MechanicalDiameter": if (l is not null) l.MechanicalDiameter = value; else d!.MechanicalDiameter = value; break;
            case "FrontAperture": (l?.FrontSurface ?? d!.FrontSurface).SemiAperture = value / 2; break;
            case "BackAperture": (l?.BackSurface ?? d!.BackSurface).SemiAperture = value / 2; break;
            case "ContactAperture" when d is not null: d.ContactSurface.SemiAperture = value / 2; break;
            default: throw new InvalidOperationException("这个数值没有唯一的设计参数，不能直接驱动镜片。");
        }
        if (key is "FrontAperture" or "BackAperture" or "ContactAperture")
        {
            // A face without an explicit aperture still uses the previous overall diameter.
            // Do not shrink that other face or silently enlarge an existing mechanical diameter.
            double overall = DirectSurfaces(source).Max(s => s.SemiAperture > 0 ? s.SemiAperture * 2 : oldDiameter);
            if (l is not null) l.Diameter = overall; else d!.Diameter = overall;
        }
        double nextClear = Math.Max(l?.Diameter ?? d!.Diameter, DirectSurfaces(source).Max(s => s.SemiAperture * 2));
        if (key != "MechanicalDiameter" && oldMechanical.HasValue && nextClear > oldMechanical.Value + 1e-8)
            throw new InvalidOperationException("新的通光口径大于现有机械外径，请先明确修改机械外径。");
        source.InvalidateRenderCache();
    }

    private static void RefreshDirectAnnotations(IReadOnlyList<Entity> entities)
    {
        foreach (var entity in entities)
        {
            if (entity is LinearDimension dimension) { dimension.RestorePersistentAnchors(entities); dimension.RefreshFromAnchors(); }
            if (entity is RadialDimension radius) radius.RefreshOpticalReference(entities);
            if (entity is lcdb.Annotation.CoatingMark coating) coating.AttachState?.RefreshAttachedMark(coating, entities);
            if (entity is lcdb.Annotation.SurfaceRoughnessMark finish) finish.AttachState?.RefreshAttachedMark(finish, entities);
        }
    }

    /// <summary>Validate the complete proposed sheet before touching any live entity.</summary>
    public static bool TrySetDirectParameter(IReadOnlyList<Entity> entities, string key, double newValue, out string error)
    {
        error = "";
        Entity[] owners;
        Entity candidate;
        try
        {
            owners = DirectOwners(entities);
            candidate = DirectSource(owners);
            var parameter = DescribeDirectParameter(candidate, key);
            if (parameter is null)
                throw new InvalidOperationException(key == "Diameter"
                    ? "整体通光口径由逐面口径决定，请修改前表面、后表面或胶合面的通光口径。"
                    : "这个数值为派生尺寸或没有设计参数绑定，不能直接修改。");
            if (double.IsNaN(newValue) || (!double.IsFinite(newValue) && !parameter.AllowInfinity))
                throw new InvalidOperationException("请输入有效的有限数值；只有曲率半径允许设为 ∞。");
            if (parameter.AllowInfinity ? newValue == 0 : newValue <= 0)
                throw new InvalidOperationException(parameter.AllowInfinity ? "曲率半径不能为 0；平面的半径请设为 ∞。" : "厚度和口径必须大于 0。");
            AssignDirectParameter(candidate, key, double.IsInfinity(newValue) ? double.PositiveInfinity : newValue);
            ValidatePart(candidate);

            // Clone first: text reconciliation, surface rescaling and anchor resolution must also succeed.
            // Only persistent associations run here and on commit; old callbacks can capture live owners.
            var trial = entities.Select(e => (Entity)e.Clone()).ToList();
            var trialOwners = DirectOwners(trial);
            foreach (var owner in trialOwners)
            {
                CopyScaledGeometry(candidate, owner);
                if (DirectSurfaces(candidate).Zip(DirectSurfaces(owner)).Any(pair => double.IsFinite(pair.First.Radius) && !double.IsFinite(pair.Second.Radius)))
                    throw new InvalidOperationException("曲率半径与图纸比例的乘积超出可用范围，请减小输入值。");
                ValidatePart(SourcePartFromView(owner));
            }
            SynchronizeGeneratedSheet(trial);
            RefreshDirectAnnotations(trial);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or OverflowException or NotSupportedException)
        {
            error = ex.Message;
            return false;
        }

        // Every view keeps its position, rotation, visual style and individual drawing scale.
        // Text reconciliation retains manual requirements/tolerances while updating only bound values.
        foreach (var owner in owners) CopyScaledGeometry(candidate, owner);
        SynchronizeGeneratedSheet(entities);
        RefreshDirectAnnotations(entities);
        return true;
    }
}




