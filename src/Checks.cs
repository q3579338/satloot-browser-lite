using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SatlootLite {
 static class Checks {
  [DllImport("shell32.dll",SetLastError=true)]static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)]string cmd,out int count);
  [DllImport("kernel32.dll")]static extern IntPtr LocalFree(IntPtr p);
  static List<string> passed=new List<string>();
  static void Check(bool value,string label){if(!value)throw new Exception("FAILED: "+label);passed.Add(label);}
  static bool Throws(Action fn){try{fn();return false;}catch{return true;}}
  static void Root(string root){if(!Directory.Exists(root)||!File.Exists(Path.Combine(root,".satloot-lite-test")))throw new Exception("Requires a pre-existing isolated test directory and marker");}
  public static int Run(string root,string package){
   Root(root);var store=new Store(root,true);Check(store.Profiles.Count==0,"new test identity starts empty");var a=Policy.Create();a.Name="研发环境 \"A\"";Policy.Validate(a);store.Save(a);var reloaded=new Store(root,true);Check(reloaded.Profiles.Single().Seed==a.Seed,"encrypted persistence retains seed");
   Check(!File.ReadAllText(Path.Combine(root,"environments.dat")).Contains(a.Seed),"seed not stored in plaintext");Check(Throws(()=>new Store(root,false)),"production rejects test identity");
   var b=a.Copy();b.Id=Guid.NewGuid().ToString();Policy.Randomize(b);store.Save(b);Check(a.Seed!=b.Seed&&a.Preset!=b.Preset,"clone obtains fresh seed and preset");Check(store.Folder(a)!=store.Folder(b),"profiles have separate storage paths");
   Check(Policy.Arguments(a,root).SequenceEqual(Policy.Arguments(reloaded.Profiles[0],root)),"reopen retains browser policy");
   var invalid=a.Copy();invalid.Id="../escape";Check(Throws(()=>Policy.Validate(invalid)),"reject path traversal");invalid=a.Copy();invalid.Seed="bad";Check(Throws(()=>Policy.Validate(invalid)),"reject invalid seed");invalid=a.Copy();invalid.Home="javascript:alert(1)";Check(Throws(()=>Policy.Validate(invalid)),"reject active URL schemes");
   foreach(string bad in new[]{"http://host.example:7890","http://user:secret@127.0.0.1:7890","socks5://127.0.0.1:7890/path","http://127.0.0.1:7890/?a=1"})Check(Throws(()=>Policy.ProxyEndpoint(bad)),"reject unsupported proxy "+bad.Split('@').Last());
   a.Network="proxy";a.Proxy="http://127.0.0.1:7890";var args=Policy.Arguments(a,root);Check(args.Contains("--satloot-proxy-dns=1,127.0.0.1")&&args.Contains("--disable-quic"),"proxy enables native DNS guard and disables QUIC");
   Check(args.All(x=>!x.Contains("remote-debugging")&&!x.Contains("no-sandbox")&&!x.Contains("enable-automation")),"manual launch has no debugging or sandbox bypass flags");
   var samples=new[]{"hello","中文 空格","a\"b","C:\\path with space\\","--user-data-dir=C:\\a b\\x", "\\\"", "", "https://example.com/?a=%20&b=2"};
   int count;IntPtr ptr=CommandLineToArgvW("test.exe "+string.Join(" ",samples.Select(Policy.Quote)),out count);
   try{Check(count==samples.Length+1,"Windows argv count");for(int i=0;i<samples.Length;i++)Check(Marshal.PtrToStringUni(Marshal.ReadIntPtr(ptr,(i+1)*IntPtr.Size))==samples[i],"Windows quoting roundtrip "+i);}finally{LocalFree(ptr);}
   var engine=new Runtime(package);engine.Verify();Check(engine.Verified,"full runtime hash and capability verification");Check(Throws(()=>engine.Launch(a,store)),"test identity blocked from manual browser launch");
   a.Network="direct";a.Home="";File.WriteAllText(Path.Combine(root,"launch-plan.json"),Json.Write(new{executable=engine.Exe,arguments=Policy.Arguments(a,root),profile=a,root=root}));
   var remove=Policy.Create();remove.Name="Delete fixture";store.Save(remove);string removeFolder=store.Folder(remove);Directory.CreateDirectory(Path.Combine(removeFolder,"Default"));File.WriteAllText(Path.Combine(removeFolder,".satloot-lite-profile"),"synthetic-test:"+remove.Id);string payload=Path.Combine(removeFolder,"Default","Cookies");File.WriteAllText(payload,"synthetic cookie");
   Check(Throws(()=>store.DeletePermanent(remove.Id,(p,f)=>{throw new Exception("running");})),"running browser blocks deletion");Check(File.Exists(payload)&&store.Profiles.Any(p=>p.Id==remove.Id),"blocked deletion preserves files and registration");
   using(var locked=new FileStream(payload,FileMode.Open,FileAccess.Read,FileShare.None)){Check(Throws(()=>store.DeletePermanent(remove.Id,(p,f)=>{})),"locked file blocks deletion before data removal");}
   File.WriteAllText(Path.Combine(removeFolder,".satloot-lite-profile"),"wrong identity");Check(Throws(()=>store.DeletePermanent(remove.Id,(p,f)=>{})),"unknown directory ownership blocks deletion");File.WriteAllText(Path.Combine(removeFolder,".satloot-lite-profile"),"synthetic-test:"+remove.Id);
   store.DeletePermanent(remove.Id,(p,f)=>{});Check(!Directory.Exists(removeFolder)&&!new Store(root,true).Profiles.Any(p=>p.Id==remove.Id),"permanent deletion removes directory and persisted record");Check(new Store(root,true).Profiles.Any(p=>p.Id==b.Id),"deletion preserves other profiles");
   var unopened=Policy.Create();store.Save(unopened);store.DeletePermanent(unopened.Id,(p,f)=>{});Check(!store.Profiles.Any(p=>p.Id==unopened.Id),"unopened environment can be deleted");
   string uuid="11223344-5566-7788-9900-aabbccddeeff";
   foreach(string link in new[]{"vless://"+uuid+"@127.0.0.1:45501?security=none&type=tcp", "vmess://"+Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("{\"add\":\"127.0.0.1\",\"port\":45501,\"id\":\""+uuid+"\",\"aid\":0,\"net\":\"tcp\"}")),"trojan://test-password@127.0.0.1:45501?security=tls&sni=example.com"}){Network.Check(package,Network.Config(link,45502));Check(true,"node configuration validates: "+link.Split(':')[0]);}
   Check(Throws(()=>Network.Parse("vless://"+uuid+"@127.0.0.1:45501?security=tls&allowInsecure=1")),"node rejects disabled certificate validation");
   using(var route=Network.Start(package,"vless://"+uuid+"@127.0.0.1:45501?security=none&type=tcp")){Check(!route.Process.HasExited&&route.Port>0,"node helper starts with stdin-only configuration");}
   L.Language="en-US";Check(L.T("删除环境")=="Delete profile","English UI dictionary");L.Language="zh-TW";Check(L.T("设置")=="設置","Traditional Chinese UI conversion");L.Language="zh-CN";
   var routes=new NodeStore(root,true);string http="http://qa-user:qa-password@127.0.0.1:45504#Local%20proxy";string ss="ss://"+Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("aes-128-gcm:test-password"))+"@127.0.0.1:45505#SS";
   var parsed=NodeStore.Parse(http+"\n"+http+"\n"+ss+"\ninvalid-line");Check(parsed.Nodes.Count==2&&parsed.Duplicates==1&&parsed.Errors.Count==1,"route import previews valid, invalid and duplicate lines");
   Check(routes.Import(parsed.Nodes)==2&&routes.Import(parsed.Nodes)==0,"route library deduplicates repeated imports");Check(!File.ReadAllText(Path.Combine(root,"routes.dat")).Contains("qa-password"),"route credentials are encrypted at rest");Check(new NodeStore(root,true).Nodes.Count==2,"route library persists independently of profiles");
   var proxy=Network.Parse(http);Check((string)proxy["username"]=="qa-user"&&(string)proxy["password"]=="qa-password","HTTP credentials parsed correctly");
   foreach(string route in new[]{http,ss,"socks5://user:secret@127.0.0.1:45506#SOCKS","https://user:secret@proxy.example.com:443"}){Network.Check(package,Network.Config(route,45507));Check(true,"proxy protocol configuration validates: "+route.Split(':')[0]);}
   Check(NodeStore.Parse(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(http))).Nodes.Count==1,"Base64 route import");Check(Throws(()=>NodeStore.Parse("<html>not a subscription</html>")),"HTML subscription rejected");Check(Throws(()=>NodeStore.Fetch("http://example.com/sub")),"subscription requires HTTPS");
   routes.Rename(routes.Nodes[0].Id,"Renamed route");Check(new NodeStore(root,true).Nodes[0].Name=="Renamed route","route rename persists");routes.Remove(new[]{routes.Nodes[0].Id});Check(routes.Nodes.Count==1,"remove selected route only");
   var backed=Policy.Create();backed.Name="Backup fixture";store.Save(backed);string data=store.Folder(backed);Directory.CreateDirectory(Path.Combine(data,"Default","Cache"));File.WriteAllText(Path.Combine(data,".satloot-lite-profile"),"synthetic-test:"+backed.Id);File.WriteAllText(Path.Combine(data,"Default","Preferences"),"synthetic-site-state");File.WriteAllText(Path.Combine(data,"Default","Cache","ignored"),"cache");string backup=Path.Combine(Path.GetDirectoryName(root),"backup-"+Guid.NewGuid().ToString("N")+".slb");
   Check(Throws(()=>Backups.Export(store,backed,backup,(p,f)=>{throw new Exception("running");})),"backup blocks a running profile");Backups.Export(store,backed,backup,(p,f)=>{});var restored=Backups.Import(store,backup);Check(restored.Id!=backed.Id&&restored.Seed==backed.Seed,"restore creates a new environment with original fingerprint");Check(File.ReadAllText(Path.Combine(store.Folder(restored),"Default","Preferences"))=="synthetic-site-state","backup restores website data");Check(!Directory.Exists(Path.Combine(store.Folder(restored),"Default","Cache")),"backup excludes cache");Check(File.ReadAllText(Path.Combine(store.Folder(restored),".satloot-lite-profile"))=="synthetic-test:"+restored.Id,"restore uses the new profile ownership marker");
   using(var view=new EnvList{Size=new Size(250,500)}){view.Items.Add(a);view.Items.Add(b);view.EndUpdate();view.SelectedIndex=1;int selections=0,invalidations=0;view.SelectedIndexChanged+=(s,e)=>selections++;view.Invalidated+=(s,e)=>invalidations++;var mouse=typeof(EnvList).GetMethod("OnMouseDown",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);for(int i=0;i<100;i++)mouse.Invoke(view,new object[]{new MouseEventArgs(MouseButtons.Left,1,40,400,0)});Check(view.SelectedIndex==1&&selections==0&&invalidations==0,"100 blank-area clicks preserve selection and trigger zero invalidations");}
   bool fixtureLive=false;engine.RunningOverride=p=>p.Id==a.Id&&fixtureLive;
   using(var manager=new MainWindow(store,engine)){
    var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
    var refresh=typeof(MainWindow).GetMethod("RefreshList",flags);var poll=typeof(MainWindow).GetMethod("PollRunning",flags);
    var openButton=(Button)typeof(MainWindow).GetField("open",flags).GetValue(manager);
    var editButton=(Button)typeof(MainWindow).GetField("edit",flags).GetValue(manager);
    var deleteButton=(Button)typeof(MainWindow).GetField("delete",flags).GetValue(manager);
    var environments=(EnvList)typeof(MainWindow).GetField("list",flags).GetValue(manager);
    refresh.Invoke(manager,new object[]{a.Id});fixtureLive=true;refresh.Invoke(manager,new object[]{a.Id});
    Check(openButton.Enabled&&openButton.Text=="返回浏览器"&&!editButton.Enabled&&!deleteButton.Enabled,"running profile offers return while editing and deletion remain blocked");
    // Reproduce opening and closing entirely between two timer ticks.
    fixtureLive=false;poll.Invoke(manager,null);
    Check(openButton.Enabled&&openButton.Text=="打开浏览器"&&editButton.Enabled&&deleteButton.Enabled,"close before first timer tick restores all closed-profile controls");
    fixtureLive=true;poll.Invoke(manager,null);fixtureLive=false;poll.Invoke(manager,null);
    Check(openButton.Text=="打开浏览器"&&editButton.Enabled,"normal browser exit refreshes controls");
    int invalidations=0;environments.Invalidated+=(s,e)=>invalidations++;
    for(int i=0;i<100;i++)poll.Invoke(manager,null);
    Check(invalidations==0&&((Profile)environments.SelectedItem).Id==a.Id,"unchanged status polling preserves selection without repainting list");
   }
   engine.RunningOverride=null;
   File.WriteAllText(Path.Combine(root,"checks.json"),Json.Write(new{passed=true,checks=passed,realProfilesUsed=false,systemInputUsed=false}));return 0;
  }
  static void Handles(Control c){var handle=c.Handle;foreach(Control child in c.Controls)Handles(child);c.PerformLayout();}
  static void Save(Form form,string path){using(form){
   // Create HWNDs while the top-level form stays hidden. Only the managed
   // visibility bit is changed for WM_PRINT; Show/SetWindowPos are never used.
   Handles(form);form.PerformLayout();var setState=typeof(Control).GetMethod("SetState",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
   setState.Invoke(form,new object[]{2,true});
   try{using(var bmp=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bmp,new Rectangle(Point.Empty,form.Size));bmp.Save(path,ImageFormat.Png);}}
   finally{setState.Invoke(form,new object[]{2,false});}
  }}
  public static int Render(string root,string package){
   Root(root);var store=new Store(root,true);store.Profiles.Clear();var a=Policy.Create();a.Name="日常浏览";a.Preset="desktop-a";var b=Policy.Create();b.Name="工作空间";b.Locale="en-US";b.Timezone="America/Los_Angeles";b.Network="proxy";b.Proxy="http://127.0.0.1:7890";store.Profiles.Add(a);store.Profiles.Add(b);
   var runtime=new Runtime(package);runtime.Verified=true;
   Save(new MainWindow(store,runtime,true),Path.Combine(root,"overview.png"));Save(new Editor(a,true,true),Path.Combine(root,"new-environment.png"));
   var small=new MainWindow(store,runtime,true);small.ClientSize=new Size(960,640);Save(small,Path.Combine(root,"overview-small.png"));
   store.Profiles.Clear();Save(new MainWindow(store,runtime,true),Path.Combine(root,"empty.png"));
   store.Profiles.Add(a);L.Language="en-US";Save(new MainWindow(store,runtime,true),Path.Combine(root,"overview-en.png"));Save(new Editor(a,true,true),Path.Combine(root,"editor-en.png"));L.Language="zh-TW";Save(new MainWindow(store,runtime,true),Path.Combine(root,"overview-tw.png"));L.Language="zh-CN";Save(new AboutWindow(),Path.Combine(root,"about.png"));
   var nodes=new NodeStore(root,true);nodes.Import(NodeStore.Parse("http://qa:password@127.0.0.1:7890#工作代理\nsocks5://127.0.0.1:1080#本机代理").Nodes);Save(new LineLibraryWindow(nodes),Path.Combine(root,"route-library.png"));Save(new LineImportWindow(nodes),Path.Combine(root,"route-import.png"));Save(new Editor(a,true,true,nodes),Path.Combine(root,"editor-library.png"));L.Language="en-US";Save(new LineLibraryWindow(nodes),Path.Combine(root,"route-library-en.png"));L.Language="zh-CN";
   File.WriteAllText(Path.Combine(root,"render.json"),Json.Write(new{passed=true,visibleWindowsShown=false,systemInputUsed=false,syntheticData=true}));return 0;
  }
 }
}
