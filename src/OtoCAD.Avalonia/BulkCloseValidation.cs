using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using lcdb;
using lcdb.Optic;
namespace OtoCAD.Avalonia.Slim;
public static class BulkCloseValidation {
 public static IReadOnlyList<string> Run(string output) {
  var log=new List<string>();void Check(bool ok,string text){if(!ok)throw new Exception("批量清空: "+text);log.Add("PASS "+text);}
  var w=new MainWindow();w.Show();Dispatcher.UIThread.RunJobs();w.WindowState=WindowState.Normal;w.Width=1280;w.Height=820;
  void Pump(){Dispatcher.UIThread.RunJobs();w.Measure(new Size(1280,820));w.Arrange(new Rect(0,0,1280,820));Dispatcher.UIThread.RunJobs();}
  void Wait(Func<bool> done){var watch=System.Diagnostics.Stopwatch.StartNew();while(!done()&&watch.ElapsedMilliseconds<3000){Pump();Thread.Sleep(10);}Check(done(),"批量操作正常结束");}
  var create=typeof(MainWindow).GetMethod("CreateDocumentTab",BindingFlags.Instance|BindingFlags.NonPublic)!;
  CadCanvas New(string name,bool generated){var c=(CadCanvas)create.Invoke(w,new object[]{name,true})!;c.LoadGeneratedSheet(generated?TemplatePipeline.Build(TemplatePipeline.Preset("ISO"),new OpticalLens{Thickness=4,Diameter=10,R1=50,R2=-60},name,"TEST","test.zmx"):TemplatePipeline.Preset("Blank"));return c;}
  TabItem Tab(CadCanvas c)=>w.SidebarDocumentTabs.Single(t=>ReferenceEquals(t.Content,c));
  var button=w.FindControl<Button>("SidebarClearDrawings")!;
  void Click(Button b){b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();}
  Window Dialog(){Pump();return w.OwnedWindows.Single();}
  void Choose(Window d,string label){Click(d.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Content,label)));}
  try {
   Pump();var template=w.ActiveCanvas;template.MarkDirty();var manual=New("另一个模板",false);
   var first=New("元件1",true);string saved=Path.Combine(output,"批量清空_已保存图纸.otocad");first.SaveCurrentScene(saved);var bytes=File.ReadAllBytes(saved);
   var second=New("元件2",true);second.MarkDirty();var third=New("元件3",true);third.MarkDirty();w.SelectSidebarPage("图纸");Pump();
   Check(button.IsEnabled&&button.Content!.ToString()!.Contains("3"),"按钮显示3张加工图，不把模板计入");
   using(var bitmap=new RenderTargetBitmap(new PixelSize(1280,820))){bitmap.Render(w);bitmap.Save(Path.Combine(output,"V1.8_一键清空加工图.png"));}
   int asked=0;var canceled=w.ClearGeneratedDrawingsAsync((total,dirty)=>{asked++;Check(total==3&&dirty==2,"统一确认提供正确总数和未保存数");return System.Threading.Tasks.Task.FromResult(false);}).GetAwaiter().GetResult();
   Check(canceled==0&&asked==1&&w.SidebarDocumentTabs.Count==5&&ReferenceEquals(w.ActiveCanvas,third),"取消清空时所有图纸和当前视图保留");
   Click(button);var dialog=Dialog();Check(w.OwnedWindows.Count==1,"真实按钮只弹一个统一确认窗口");Choose(dialog,"取消");Wait(()=>button.IsEnabled);
   Check(w.SidebarDocumentTabs.Count==5,"确认窗口取消后不关闭图纸");
   Click(button);dialog=Dialog();Choose(dialog,"清空加工图");Wait(()=>w.SidebarDocumentTabs.Count==2);
   Check(w.OwnedWindows.Count==0&&ReferenceEquals(w.ActiveCanvas,template)&&template.IsDirty,"一次确认清空全批并保留未保存模板");
   Check(File.Exists(saved)&&File.ReadAllBytes(saved).SequenceEqual(bytes),"磁盘已保存图纸文件保持不变");Check(!button.IsEnabled,"没有加工图时按钮禁用");
   var next=New("新镜头元件1",true);next.SaveCurrentScene(Path.Combine(output,"批量清空_新镜头.otocad"));w.SelectSidebarPage("图纸");Pump();Click(button);
   Check(w.SidebarDocumentTabs.Count==2&&w.OwnedWindows.Count==0,"重新生成后可再次一键清空，已保存内容不再询问");
   template.SaveCurrentScene(Path.Combine(output,"批量清空_模板.otocad"));manual.SaveCurrentScene(Path.Combine(output,"批量清空_其他模板.otocad"));
   w.CloseSidebarDocumentAsync(Tab(manual)).GetAwaiter().GetResult();var last=New("只剩加工图",true);last.MarkDirty();w.CloseSidebarDocumentAsync(Tab(template)).GetAwaiter().GetResult();
   Check(w.SidebarDocumentTabs.Count==1,"准备原模板页已关闭场景");
   int count=w.ClearGeneratedDrawingsAsync((_,_)=>System.Threading.Tasks.Task.FromResult(true)).GetAwaiter().GetResult();
   Check(count==1&&w.SidebarDocumentTabs.Count==1&&!TemplatePipeline.IsGeneratedDrawing(w.ActiveCanvas.GetAllEntities()),"模板页已关闭时自动恢复模板，保持可继续导入");
   return log;
  } finally {w.Close();}
 }
}
