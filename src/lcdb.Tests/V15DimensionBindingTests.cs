using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LitMath;
using lcdb;
using lcdb.Optic;
namespace lcdb.Tests;
[TestClass] public class V15DimensionBindingTests
{
 static LinearDimension Make(Entity host,int a,int b,string label,double scale,double rotation=0)
 {
  var pts=host.GetSnapPoints();var dim=new LinearDimension{firstReferencePoint=pts[a].position,secondReferencePoint=pts[b].position,rotation=rotation,userText=label,FirstAnchor=new(host,a),SecondAnchor=new(host,b),Owner=host};
  dim.ConfigureAutomaticMeasurement(scale);return dim;
 }
 [TestMethod] public void Native_roundtrip_preserves_live_host_and_true_measurement()
 {
  foreach(var ext in new[]{"json","otocad"}){
   var host=new OpticalLens{Diameter=100,Thickness=20,R1=500,R2=-600};var dim=Make(host,1,2,"2.00",10);var db=new Database();db.AddEntity(host);db.AddEntity(dim);
   string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+"."+ext);
   try{db.SaveAs(path);var loaded=new Database();loaded.Open(path);var all=loaded.GetEntities().ToList();var lens=all.OfType<OpticalLens>().Single();var d=all.OfType<LinearDimension>().Single();
    Assert.IsFalse(string.IsNullOrEmpty(d.FirstAnchorKey));lens.Thickness=40;lens.Position=new Vector2(700,20);d.RestorePersistentAnchors(all);d.RefreshFromAnchors();
    Assert.AreSame(lens,d.FirstAnchor.Host);Assert.AreEqual("4.00",d.userText);Assert.AreEqual(680,d.firstReferencePoint.X,1e-9);Assert.AreEqual(720,d.secondReferencePoint.X,1e-9);
   }finally{if(File.Exists(path))File.Delete(path);}
  }
 }
 [TestMethod] public void Manual_caption_is_not_overwritten_by_geometry_change()
 {
  var lens=new OpticalLens{Diameter=100,Thickness=20,R1=500,R2=-600};var d=Make(lens,1,2,"CT 2.00",10);d.userText="客户自定厚度说明";lens.Thickness=30;d.RefreshFromAnchors();Assert.AreEqual("客户自定厚度说明",d.userText);Assert.AreEqual(30,d.measurement,1e-9);
 }
 [TestMethod] public void Doublet_piece_labels_do_not_replace_the_T1_or_T2_number()
 {
  var l=new CementedLens{Diameter=100,T1=40,T2=12.8,R1=500,RContact=-500,R3=-700};var a=Make(l,1,3,"T1 4.00",10);var b=Make(l,3,2,"T2 1.28",10);l.T1=50;l.T2=15;a.RefreshFromAnchors();b.RefreshFromAnchors();Assert.AreEqual("T1 5.00",a.userText);Assert.AreEqual("T2 1.50",b.userText);
 }
 [TestMethod] public void Mechanical_diameter_caption_uses_live_outline_after_rotation()
 {
  var l=new OpticalLens{Diameter=100,MechanicalDiameter=110,Thickness=20,R1=500,R2=-600};var d=Make(l,8,7,"Ø11.00",10,-Math.PI/2);l.MechanicalDiameter=120;l.Rotation=35;d.RefreshFromAnchors();Assert.AreEqual("Ø12.00",d.userText);Assert.AreEqual(-Math.PI/2+35*Math.PI/180,d.rotation,1e-9);
 }
 [TestMethod] public void Radius_after_roundtrip_updates_text_and_surface_point()
 {
  var l=new OpticalLens{Diameter=100,Thickness=20,R1=500,R2=-600};var d=new RadialDimension{Owner=l,userText="R50"};d.ConfigureOpticalReference(l,0,10,.5);var db=new Database();db.AddEntity(l);db.AddEntity(d);string p=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".otocad");
  try{db.SaveAs(p);var db2=new Database();db2.Open(p);var all=db2.GetEntities().ToList();var host=all.OfType<OpticalLens>().Single();var dim=all.OfType<RadialDimension>().Single();host.R1=750;dim.RefreshOpticalReference(all);Assert.AreEqual("R75",dim.userText);Assert.AreEqual(host.Position.X-host.Thickness/2+750,dim.centerPoint.X,1e-9);Assert.AreEqual(25,dim.chordPoint.Y,1e-9);}
  finally{if(File.Exists(p))File.Delete(p);}
 }
 [TestMethod] public void Radius_flat_edit_hides_obsolete_radius_geometry()
 {
  var l=new OpticalLens{Diameter=100,Thickness=20,R1=500,R2=-600};var d=new RadialDimension{Owner=l,userText="R50"};d.ConfigureOpticalReference(l,0,10,.5);l.R1=double.PositiveInfinity;d.RefreshOpticalReference(new Entity[]{l,d});Assert.IsTrue(d.OpticalSurfaceIsFlat);Assert.AreEqual("R∞",d.userText);
 }
}
