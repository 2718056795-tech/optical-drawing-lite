using System;
using System.Collections.Generic;
using System.Linq;
using lcdb;
using LitMath;
namespace lcdb.DrawingFrame;

public sealed class TemplateViewRegion : DrawingFrame
{
    public override string className=>"TemplateViewRegion";
    public double X{get=>Origin.X;set{Origin=new Vector2(value,Origin.Y);InvalidateRenderCache();}}
    public double Y{get=>Origin.Y;set{Origin=new Vector2(Origin.X,value);InvalidateRenderCache();}}
    public double ViewScale{get;set;}=0;
    public bool UseFullPaperArea{get;set;}=false;
    public double DimensionExtensionGap{get;set;}=0.6;
    public TemplateViewRegion(){PaperWidth=120;PaperHeight=150;}
    protected override void Generate()
    {
        _markEntities.Clear();_cellHits.Clear();
        AddRect(Origin.X,Origin.Y,Origin.X+PaperWidth,Origin.Y+PaperHeight);
        AddText(Origin.X+3,Origin.Y+PaperHeight-6,"零件图区 · 导入后自动替换",2.6);
        AddLine(Origin.X,Origin.Y,Origin.X+PaperWidth,Origin.Y+PaperHeight);
        AddLine(Origin.X,Origin.Y+PaperHeight,Origin.X+PaperWidth,Origin.Y);
    }
    protected override DBObject CreateInstance()=>new TemplateViewRegion();
    public override object Clone(){var c=(TemplateViewRegion)base.Clone();c.ViewScale=ViewScale;c.UseFullPaperArea=UseFullPaperArea;c.DimensionExtensionGap=DimensionExtensionGap;return c;}
}

public sealed class TemplateTable : DrawingFrame,IPropertyZoneFrame
{
    public override string className=>"TemplateTable";
    public double X{get=>Origin.X;set{Origin=new Vector2(value,Origin.Y);InvalidateRenderCache();}}
    public double Y{get=>Origin.Y;set{Origin=new Vector2(Origin.X,value);InvalidateRenderCache();}}
    public double RowHeight{get;set;}=7;
    public double TextHeight{get;set;}=2.5;
    public List<IsoSpecColumn> Columns{get;set;}=new();
    public List<double> ColumnWidths{get;set;}=new();
    public TemplateTable(){PaperWidth=170;PaperHeight=35;}
    public double ActualHeight=>(Columns.Count==0?2:Columns.Max(c=>c.Rows.Count)+1)*RowHeight;
    public override Bounding bounding=>new(Origin,new Vector2(Origin.X+PaperWidth,Origin.Y+ActualHeight));
    protected override void Generate()
    {
        _markEntities.Clear();_cellHits.Clear();
        double bottom=Origin.Y,top=bottom+ActualHeight,right=Origin.X+PaperWidth;
        AddRect(Origin.X,bottom,right,top);
        int n=Math.Max(1,Columns.Count);double sum=Enumerable.Range(0,n).Sum(i=>i<ColumnWidths.Count?Math.Max(.1,ColumnWidths[i]):1);
        double x=Origin.X;
        AddLine(x,top-RowHeight,right,top-RowHeight);
        for(int i=0;i<Columns.Count;i++)
        {
            double w=PaperWidth*(i<ColumnWidths.Count?Math.Max(.1,ColumnWidths[i]):1)/sum;
            if(i>0)AddLine(x,bottom,x,top);
            AddTextCentered(x,x+w,top-RowHeight*.72,Columns[i].Title,TextHeight);
            for(int r=0;r<Columns[i].Rows.Count;r++)
            {
                double y=top-(r+1)*RowHeight;
                AddText(x+1.5,y-RowHeight*.72,Columns[i].Rows[r],TextHeight);
                _cellHits.Add(new PropertyCellHit(Columns[i].Title,"",i,r,new Rectangle2(new Vector2(x,y-RowHeight),w,RowHeight)));
                if(r<Columns[i].Rows.Count-1)AddLine(x,y-RowHeight,x+w,y-RowHeight);
            }
            _cellHits.Add(new PropertyCellHit(Columns[i].Title,"",i,-1,new Rectangle2(new Vector2(x,top-RowHeight),w,RowHeight)));
            x+=w;
        }
    }
    public PropertyCellHit? HitTestCell(Vector2 p)=>HitTestCellCore(p);
    public string GetCellText(PropertyCellHit c)=>GetColumnsCellText(Columns,c);
    public void SetCellText(PropertyCellHit c,string s)=>SetColumnsCellText(Columns,c,s);
    public int AddRow(int c,string s="")=>AddColumnsRow(Columns,c,s);
    public int InsertRow(int c,int r,string s)=>InsertColumnsRow(Columns,c,r,s);
    public bool RemoveRow(int c,int r)=>RemoveColumnsRow(Columns,c,r);
    public PropertyCellHit? GetCell(int c,int r)=>FindCell(c,r);
    public IReadOnlyList<string> GetZoneRows(int c)=>GetColumnsRows(Columns,c);
    protected override DBObject CreateInstance()=>new TemplateTable();
    public override object Clone()
    {
        var c=(TemplateTable)base.Clone();c.RowHeight=RowHeight;c.TextHeight=TextHeight;c.ColumnWidths=new(ColumnWidths);
        c.Columns=Columns.Select(x=>new IsoSpecColumn{Title=x.Title,Rows=new(x.Rows),RowKeys=new(x.RowKeys)}).ToList();return c;
    }
}
