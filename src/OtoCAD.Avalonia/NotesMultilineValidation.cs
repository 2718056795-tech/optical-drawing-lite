using System;using System.Collections.Generic;using System.Linq;using System.IO;using System.Reflection;
using Avalonia;using Avalonia.Controls;using Avalonia.LogicalTree;using Avalonia.Media.Imaging;using Avalonia.Threading;
using lcdb;using lcdb.DrawingFrame;using lcdb.Optic;using OtoCAD.Avalonia.PropertyPanel;
namespace OtoCAD.Avalonia.Slim;
public static class NotesMultilineValidation {
 public static IReadOnlyList<string> Run(string output){
  var log=new List<string>();void Check(bool ok,string label){if(!ok)throw new Exception("技术要求多行: "+label);log.Add("PASS "+label);}
  var w=new MainWindow();w.Show();Dispatcher.UIThread.RunJobs();w.WindowState=WindowState.Normal;w.Width=1280;w.Height=820;
  void Layout(){Dispatcher.UIThread.RunJobs();w.Measure(new Size(1280,820));w.Arrange(new Rect(0,0,1280,820));Dispatcher.UIThread.RunJobs();}
  try {
   var template=TemplatePipeline.Preset("ISO");Check(template.OfType<TemplateViewRegion>().Single().ViewScale==0,"保留原有自动比例默认值");
   var lens=new OpticalLens{R1=50,R2=-60,Thickness=4,Diameter=10};lens.ApplyMaterial("N-BK7");
   var sheet=TemplatePipeline.Build(template,lens,"技术要求多行验证","NOTES","test.zmx");var frame=sheet.OfType<IsoLensDrawingFrame>().Single();string scale=frame.DrawScale;double k=sheet.OfType<OpticalLens>().Single().DrawingBinding!.ViewScale;
   w.ActiveCanvas.LoadGeneratedSheet(sheet);Layout();
   var handler=(Action<lcdb.DrawingFrame.DrawingFrame,PropertyCellHit>)typeof(CadCanvas).GetField("PropertyCellSelected",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(w.ActiveCanvas)!;
   handler(frame,new PropertyCellHit("技术要求 2","NoteLine_1",default));Layout();
   var panel=w.FindControl<PropertyPanelControl>("MainPropertyPanel")!;var input=panel.GetLogicalDescendants().OfType<TextBox>().Single(t=>t.Name=="FrameCellValueInput");
   Check(input.AcceptsReturn&&input.TextWrapping==global::Avalonia.Media.TextWrapping.Wrap&&input.MinHeight>=150,"点击任一技术要求行后显示可回车和自动折行的大输入框");
   Check(input.Text==frame.Notes&&input.Text!.Contains("3."),"输入框显示完整技术要求，保留其余条款");
   input.Text="1. 第一个要求\n2. 第二个要求\n3. 第三个要求\n第四行";
   foreach(char ch in "继续输入"){input.Text+=ch;Layout();}
   Layout();
   Check(frame.Notes==input.Text&&frame.Notes.Split('\n').Length==4,"换行后连续输入不重复插入行、不丢失后续文字");
   input.Text="1. 左右表面均镀增透膜\n2. 膜层反射率 RAVG(@420nm to 680nm, 0° to 25°)<0.5%\n3. 镜片外径及平台面涂墨\n新增的第四行\n新增的第五行\n新增的第六行";
   Layout();
   Check(frame.NotesRowHeight>=8+6*3.2,"新增多行时技术要求区域自动留足高度");
   Check(frame.DrawScale==scale&&sheet.OfType<OpticalLens>().Single().DrawingBinding!.ViewScale==k,"编辑文字不改变图纸比例或镜片尺寸");
   string file=Path.Combine(output,"多行技术要求保存.otocad");w.ActiveCanvas.SaveCurrentScene(file);var db=new Database();db.Open(file);
   Check(db.GetEntities().OfType<IsoLensDrawingFrame>().Single().Notes==input.Text,"保存重开保留全部换行和文字");
   Layout();using var bitmap=new RenderTargetBitmap(new PixelSize(1280,820));bitmap.Render(w);bitmap.Save(Path.Combine(output,"V1.9_技术要求多行输入.png"));
   return log;
  }finally{w.Close();}
 }
}
