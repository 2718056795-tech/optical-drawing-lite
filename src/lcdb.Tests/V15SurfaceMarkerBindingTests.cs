using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LitMath;
using lcdb;
using lcdb.Optic;
using lcdb.Annotation;
namespace lcdb.Tests;
[TestClass] public class V15SurfaceMarkerBindingTests
{
 [TestMethod] public void Marks_roundtrip_keep_custom_height_and_follow_changed_lens()
 {
  var l=new OpticalLens{Diameter=100,Thickness=20,R1=500,R2=-600};
  var coat=new CoatingMark(Vector2.Zero,CoatingType.AR,4){Owner=l,AttachState=new(Vector2.Zero,500,true){Height=-13}};
  var finish=new SurfaceRoughnessMark(Vector2.Zero,3){Owner=l,AttachState=new(Vector2.Zero,500,true){Height=17}};
  coat.AttachState.Configure(l,0);finish.AttachState.Configure(l,0);var all=new Entity[]{l,coat,finish};
  coat.AttachState.RefreshAttachedMark(coat,all);finish.AttachState.RefreshAttachedMark(finish,all);
  var db=new Database();foreach(var e in all)db.AddEntity((Entity)e.Clone());var p=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".otocad");
  try{
   db.SaveAs(p);var read=new Database();read.Open(p);var entities=read.GetEntities().ToList();var host=entities.OfType<OpticalLens>().Single();var c=entities.OfType<CoatingMark>().Single();var f=entities.OfType<SurfaceRoughnessMark>().Single();
   Assert.IsNotNull(c.AttachState);Assert.IsNotNull(f.AttachState);Assert.AreEqual(-13,c.AttachState.Height,1e-9);Assert.AreEqual(17,f.AttachState.Height,1e-9);
   var before=c.Center;Assert.IsFalse(c.AttachState.RefreshAttachedMark(c,entities));Assert.AreEqual(before.X,c.Center.X,1e-9);
   host.Thickness=40;host.Position=new Vector2(300,20);host.R1=750;
   Assert.IsTrue(c.AttachState.RefreshAttachedMark(c,entities));Assert.IsTrue(f.AttachState.RefreshAttachedMark(f,entities));
   Assert.AreEqual(-13,c.AttachState.Height,1e-9);Assert.AreEqual(17,f.AttachState.Height,1e-9);Assert.AreEqual(280,c.AttachState.Apex.X,1e-9);Assert.AreEqual(750,c.AttachState.Radius,1e-9);
   Assert.IsTrue(c.Center.X>200);Assert.IsTrue(f.Center.X>200);Assert.IsFalse(c.AttachState.RefreshAttachedMark(c,entities));
  }finally{if(File.Exists(p))File.Delete(p);}
 }
 [TestMethod] public void Cloning_surface_constraint_is_independent_and_keeps_height()
 {
  var s=new SurfaceAttachState(new Vector2(1,2),50,false){Height=13,OpticalHostKey="host",OpticalSurfaceIndex=2,LastSurfaceGeometry="f"};var c=s.Clone();c.Height=8;Assert.AreEqual(13,s.Height,1e-9);Assert.AreEqual("host",c.OpticalHostKey);Assert.AreEqual(2,c.OpticalSurfaceIndex);
 }
}
