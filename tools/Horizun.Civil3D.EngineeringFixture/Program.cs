using System.Text;
using System.Text.Json.Nodes;
using Horizun.Civil3D.Core;

// Generate new synthetic interchange files. Native DWG/RVT creation requires live hosts.
if(args.Length!=1||!Path.IsPathFullyQualified(args[0])) throw new ArgumentException("Pass a new absolute fixture directory.");
var output=Path.GetFullPath(args[0]);
if(Directory.Exists(output)||File.Exists(output)) throw new IOException("Fixture destination already exists; nothing replaced.");
var parent=Path.GetDirectoryName(output)!;
if(!Directory.Exists(parent)) throw new DirectoryNotFoundException(parent);
var stage=Path.Combine(parent,".hz-engineering-fixture-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(stage);
void Json(string file,JsonNode data)=>File.WriteAllText(Path.Combine(stage,file),data.ToJsonString(Hz.Indented),new UTF8Encoding(false));
var tin=new LxSurface("Fixture_Terrain","Synthetic metric plane z=100+0.1x+0.1y",
    new(){(0,0,100),(10,0,101),(10,10,102),(0,10,101)},new(){(0,1,2),(0,2,3)});
var terrain=RevitTerrainPackage.Prepare(tin,"meter",new JsonObject{["drawing"]="synthetic_fixture",["generated"]=true},
    new JsonObject{["code"]=null},Contract.Hash);
RevitTerrainPackage.Write(Path.Combine(stage,"terrain.zip"),terrain);
File.WriteAllBytes(Path.Combine(stage,"terrain.xml"),terrain.LandXml);
File.WriteAllBytes(Path.Combine(stage,"terrain.obj"),terrain.MeshObj!);
Json("manifest.json",terrain.Summary);
var rows=tin.Points.Select((p,i)=>new PointFile.Row((uint)(i+1),p.X,p.Y,p.Z,"Fixture point "+(i+1))).ToArray();
File.WriteAllText(Path.Combine(stage,"points-editable.csv"),PointEditCsv.Write(PointEditCsv.Capture(rows,"meter","synthetic_fixture_only")),new UTF8Encoding(false));
var xy=new JsonArray(new JsonObject{["x"]=0,["y"]=0},new JsonObject{["x"]=5,["y"]=5},new JsonObject{["x"]=10,["y"]=10});
Json("comparison-expected.json",SurfaceComparison.Evaluate(xy,0.05,(x,y)=>new(100+0.1*x+0.1*y),(x,y)=>new(100.02+0.1*x+0.1*y)));
Json("civil-create-terrain.request.json",new JsonObject{
    ["tool"]="horizun_c3d_surface",["arguments"]=new JsonObject{["action"]="create_tin",["new_name"]=tin.Name,
    ["target_document"]="REPLACE_WITH_NEW_FIXTURE_DRAWING",["dry_run"]=true}});
Json("civil-add-terrain.request.json",new JsonObject{
    ["tool"]="horizun_c3d_surface",["arguments"]=new JsonObject{["action"]="add_data",["name"]=tin.Name,
    ["target_document"]="REPLACE_WITH_NEW_FIXTURE_DRAWING",["dry_run"]=true,
    ["vertices"]=new JsonArray(tin.Points.Select(p=>(JsonNode)new JsonObject{["x"]=p.X,["y"]=p.Y,["z"]=p.Z}).ToArray())}});
Json("revit-new-project.request.json",new JsonObject{
    ["tool"]="horizun_document_session",["arguments"]=new JsonObject{["operation"]="new_project",["dry_run"]=true,["save_as_path"]=Path.Combine(output,"fixture-revit.rvt")}});
Json("fixture-status.json",new JsonObject{["status"]="offline_fixture_files_prepared",["contract_hash"]=Contract.Hash,
    ["native_dwg_created"]=false,["native_rvt_created"]=false,["live_verified"]=false,["expected_plan_area_m2"]=100,
    ["expected_rmse_m"]=0.02,["note"]="The editable CSV is an offline parser example, not a fingerprint of a live DWG. Export a fresh CSV from the actual fixture before applying edits."});
Directory.Move(stage,output);
Console.WriteLine(output);
