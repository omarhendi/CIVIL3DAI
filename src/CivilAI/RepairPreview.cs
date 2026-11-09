using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;
namespace CivilAI;
public sealed partial class Plugin
{
 [CommandMethod("CIVILAIREPAIRPREVIEW", CommandFlags.Modal)]
 public void RepairPreview()
 {
  var doc=Application.DocumentManager.MdiActiveDocument; if(doc is null)return; var ed=doc.Editor;
  try {
   var o=new PromptEntityOptions("\nSelect TIN surface: "); o.AddAllowedClass(typeof(TinSurface),true); var pick=ed.GetEntity(o); if(pick.Status!=PromptStatus.OK)return;
   var pt=ed.GetPoint("\nPick near the steep triangle to inspect candidate swaps: "); if(pt.Status!=PromptStatus.OK)return;
   var rows=new List<PropertyValue>(); string name;
   using(doc.LockDocument()) using(var tr=doc.Database.TransactionManager.StartOpenCloseTransaction()) {
    var s=(TinSurface)tr.GetObject(pick.ObjectId,OpenMode.ForRead); name=s.Name;
    if(s.IsReferenceObject&&(!s.IsReferenceValid||s.IsReferenceStale||!s.IsReferencedSourceExisting))throw new InvalidOperationException("Surface reference unavailable.");
    using var t=s.FindTriangleAtXY(pt.Value.X,pt.Value.Y);
    var tv=new[]{t.Vertex1,t.Vertex2,t.Vertex3}; var p=tv.Select(v=>{using(v)return v.Location;}).ToArray();
    TriangleSlope.TryCalculate(new(p[0].X,p[0].Y,p[0].Z),new(p[1].X,p[1].Y,p[1].Z),new(p[2].X,p[2].Y,p[2].Z),out var current,out _);
    rows.Add(new("Selected triangle slope",$"{current:G8} %")); var candidates=0;
    foreach(var edge in new[]{t.Edge1,t.Edge2,t.Edge3}) using(edge) {
     if(edge.IsLocked){rows.Add(new("Candidate","Skipped locked breakline edge."));continue;}
     using var adjacent=edge.Triangle2;
     if(adjacent is null){rows.Add(new("Candidate","Skipped boundary edge."));continue;}
     using var x=edge.Vertex1; using var y=edge.Vertex2; using var a=adjacent.Vertex1; using var b=adjacent.Vertex2; using var c=adjacent.Vertex3;
     var q=new[]{a.Location,b.Location,c.Location}; var u=x.Location;var v=y.Location;
     var opposite=q.FirstOrDefault(z=>z.DistanceTo(u)>1e-8&&z.DistanceTo(v)>1e-8);
     if(opposite==default)continue;
     var other=p.FirstOrDefault(z=>z.DistanceTo(u)>1e-8&&z.DistanceTo(v)>1e-8); if(other==default)continue;
     var old1=Slope(u,v,other); var old2=Slope(u,v,opposite); var new1=Slope(other,opposite,u); var new2=Slope(other,opposite,v);
     var oldMax=Math.Max(old1,old2);var newMax=Math.Max(new1,new2);
     candidates++;
     rows.Add(new($"Edge candidate {candidates}",$"Unlocked interior edge; max adjacent slope {oldMax:G8}% → alternate diagonal {newMax:G8}%. Endpoints X/Y: ({u.X:G8},{u.Y:G8})–({v.X:G8},{v.Y:G8}). Geometry not changed; verify edge is not a design breakline before any future apply."));
    }
    if(candidates==0)rows.Add(new("Repair preview","No unlocked interior edge candidates."));
    rows.Add(new("Status","Preview only. No surface edit or DWG modification was made."));
   }
   EnsurePanel();palette!.Visible=true;panel!.Show(new DrawingSnapshot(doc.Name,new[]{new DrawingItem("Repair preview",name,rows)},0,rows.Count,Array.Empty<string>()));
  } catch(System.Exception ex){ed.WriteMessage($"\nRepair preview unavailable ({ex.GetType().Name}). No surface edits made.\n");}
 }
 private static double Slope(Autodesk.AutoCAD.Geometry.Point3d a,Autodesk.AutoCAD.Geometry.Point3d b,Autodesk.AutoCAD.Geometry.Point3d c)=>TriangleSlope.TryCalculate(new(a.X,a.Y,a.Z),new(b.X,b.Y,b.Z),new(c.X,c.Y,c.Z),out var g,out _)?g:double.PositiveInfinity;
}
