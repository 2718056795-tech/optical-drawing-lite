using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Buffers.Binary;
using System.Text;
using SkiaSharp;
namespace OtoCAD.Avalonia.Rendering;

// Keeps original glyph IDs, advances, cmap and compound components; only unused outlines are removed.
internal sealed class PdfFontSubsetScope : IDisposable
{
 [ThreadStatic] static PdfFontSubsetScope? current;
 readonly Dictionary<string,(SKTypeface Font,HashSet<ushort> Glyphs)> used=new();
 readonly Dictionary<string,SKTypeface> subsets=new();
 readonly PdfFontSubsetScope? previous;
 bool ready;
 static string Key(SKTypeface t)=>t.FamilyName+"/"+t.FontStyle.Weight+"/"+t.FontStyle.Width+"/"+t.FontStyle.Slant;
 public PdfFontSubsetScope(){previous=current;current=this;}
 public static SKTypeface Resolve(SKTypeface t,string text){if(current is null)return t;var key=Key(t);if(current.ready)return current.subsets.TryGetValue(key,out var v)?v:t;if(!current.used.TryGetValue(key,out var item)){item=(t,new HashSet<ushort>{0});current.used[key]=item;}foreach(var glyph in t.GetGlyphs(text))item.Glyphs.Add(glyph);return t;}
 public void Prepare(){foreach(var (key,item) in used){try{using var stream=item.Font.OpenStream(out int face);if(stream is null||stream.Length<=0)continue;byte[] bytes=new byte[stream.Length];if(stream.Read(bytes,bytes.Length)!=bytes.Length)continue;var subset=Subset(bytes,face,item.Glyphs);if(subset is null)continue;using var data=SKData.CreateCopy(subset);var tf=SKTypeface.FromData(data);if(tf is not null)subsets[key]=tf;}catch{/* A font without TrueType outlines safely keeps the original. */}}ready=true;}
 public void Dispose(){current=previous;foreach(var font in subsets.Values)font.Dispose();}
 static ushort U16(byte[] b,int p)=>BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(p,2));
 static uint U32(byte[] b,int p)=>BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(p,4));
 static void W16(byte[] b,int p,int n)=>BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(p,2),(ushort)n);
 static void W32(byte[] b,int p,uint n)=>BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(p,4),n);
 static uint Sum(byte[] b){uint n=0;for(int i=0;i<b.Length;i+=4){uint v=0;for(int j=0;j<4;j++)v=(v<<8)|(i+j<b.Length?b[i+j]:(byte)0);n=unchecked(n+v);}return n;}
 static byte[]? Subset(byte[] input,int face,HashSet<ushort> keep){
 int sfnt=Encoding.ASCII.GetString(input,0,4)=="ttcf"?(int)U32(input,12+face*4):0;
 var tables=new SortedDictionary<string,byte[]>(StringComparer.Ordinal);
 int count=U16(input,sfnt+4);
 for(int i=0;i<count;i++){int p=sfnt+12+16*i;string tag=Encoding.ASCII.GetString(input,p,4);int off=(int)U32(input,p+8),len=(int)U32(input,p+12);if(tag!="DSIG")tables[tag]=input.AsSpan(off,len).ToArray();}
 if(!tables.ContainsKey("glyf")||!tables.ContainsKey("loca")||!tables.ContainsKey("head")||!tables.ContainsKey("maxp"))return null;
 var glyf=tables["glyf"];var loca=tables["loca"];var head=tables["head"];int num=U16(tables["maxp"],4);bool longLoca=U16(head,50)==1;
 int[] offsets=Enumerable.Range(0,num+1).Select(i=>longLoca?(int)U32(loca,4*i):U16(loca,2*i)*2).ToArray();
 var queue=new Queue<ushort>(keep);while(queue.Count>0){int g=queue.Dequeue();if(g>=num||offsets[g+1]-offsets[g]<10)continue;int p=offsets[g];if(unchecked((short)U16(glyf,p))>=0)continue;p+=10;ushort flags;do{flags=U16(glyf,p);ushort child=U16(glyf,p+2);if(keep.Add(child))queue.Enqueue(child);p+=4+((flags&1)!=0?4:2);p+=(flags&8)!=0?2:(flags&64)!=0?4:(flags&128)!=0?8:0;}while((flags&32)!=0);}
 using var outGlyf=new MemoryStream();byte[] outLoca=new byte[(num+1)*4];for(int i=0;i<num;i++){W32(outLoca,i*4,(uint)outGlyf.Length);if(keep.Contains((ushort)i)){outGlyf.Write(glyf,offsets[i],offsets[i+1]-offsets[i]);while(outGlyf.Length%4!=0)outGlyf.WriteByte(0);}}W32(outLoca,num*4,(uint)outGlyf.Length);
 tables["glyf"]=outGlyf.ToArray();tables["loca"]=outLoca;W16(head,50,1);W32(head,8,0);
 int n=tables.Count,total=12+n*16+tables.Values.Sum(b=>(b.Length+3)&~3);byte[] result=new byte[total];input.AsSpan(sfnt,4).CopyTo(result);W16(result,4,n);int maxPower=1,selector=0;while(maxPower*2<=n){maxPower*=2;selector++;}W16(result,6,maxPower*16);W16(result,8,selector);W16(result,10,n*16-maxPower*16);
 int index=0,dataOff=12+n*16,headOff=0;foreach(var (tag,bytes) in tables){int p=12+index++*16;Encoding.ASCII.GetBytes(tag).CopyTo(result,p);W32(result,p+4,Sum(bytes));W32(result,p+8,(uint)dataOff);W32(result,p+12,(uint)bytes.Length);bytes.CopyTo(result,dataOff);if(tag=="head")headOff=dataOff;dataOff+=(bytes.Length+3)&~3;}
 W32(result,headOff+8,unchecked(0xB1B0AFBA-Sum(result)));return result;
 }
}
