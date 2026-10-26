using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace SatlootLite {
 public sealed class Profile {
  public string Id {get;set;} public string Name {get;set;} public string Seed {get;set;}
  public string Preset {get;set;} public string Locale {get;set;} public string Timezone {get;set;}
  public string Network {get;set;} public string Proxy {get;set;} public string Home {get;set;}
  public bool Canvas {get;set;} public bool Webgl {get;set;} public bool Audio {get;set;}
  public int ProcessId {get;set;} public long ProcessStart {get;set;}
  public string Created {get;set;} public string LastOpened {get;set;}
  public bool Deleting {get;set;}
  public string NodeUri {get;set;}
  public string NodeName {get;set;}
  public Profile Copy(){ return Json.Read<Profile>(Json.Write(this)); }
 }
 public sealed class Preset {
  public string Id, Gpu, Label; public int Cpu, Memory, Width, Height;
  public Preset(string id,string gpu,string label,int cpu,int memory,int width,int height){Id=id;Gpu=gpu;Label=label;Cpu=cpu;Memory=memory;Width=width;Height=height;}
  public override string ToString(){return Label+" · "+Cpu+" 线程 · "+Memory+" GB · "+Width+" × "+Height;}
 }
 public static class Json {
  public static string Write(object value){return new JavaScriptSerializer{MaxJsonLength=8*1024*1024}.Serialize(value);}
  public static T Read<T>(string value){return new JavaScriptSerializer{MaxJsonLength=8*1024*1024}.Deserialize<T>(value);}
 }
 public sealed class StoreData {public int Version {get;set;} public string Kind {get;set;} public List<Profile> Profiles {get;set;}}
 public sealed class Store {
  public readonly string Root; public readonly bool Test; public List<Profile> Profiles=new List<Profile>();
  static readonly byte[] Entropy=Encoding.UTF8.GetBytes("SATLOOT.Browser.Lite.preview.v1");
  public Store(string root,bool test){
   Root=Path.GetFullPath(root);Test=test;
   if(test && !File.Exists(Path.Combine(Root,".satloot-lite-test")))throw new Exception("测试目录缺少独立身份标记");
   if(!test && File.Exists(Path.Combine(Root,".satloot-lite-test")))throw new Exception("正式模式拒绝读取测试数据");
   Directory.CreateDirectory(Root);
   string file=Path.Combine(Root,"environments.dat");
   if(File.Exists(file)){
    byte[] plain=ProtectedData.Unprotect(File.ReadAllBytes(file),Entropy,DataProtectionScope.CurrentUser);
    try {var data=Json.Read<StoreData>(Encoding.UTF8.GetString(plain));
     if(data.Version!=1||data.Kind!=(test?"synthetic-test":"local-user")||data.Profiles==null||data.Profiles.Count>1000)throw new Exception("环境文件的版本或来源不正确");
     foreach(var p in data.Profiles)Policy.Validate(p);
     if(data.Profiles.Select(p=>p.Id).Distinct().Count()!=data.Profiles.Count)throw new Exception("环境编号重复");
     Profiles=data.Profiles;
    } finally {Array.Clear(plain,0,plain.Length);}
   }
  }
  public void Commit(List<Profile> next){
   if(next.Count>1000||next.Select(p=>p.Id).Distinct().Count()!=next.Count)throw new Exception("环境数量超过 1000 或编号重复");
   foreach(var p in next)Policy.Validate(p);
   byte[] plain=Encoding.UTF8.GetBytes(Json.Write(new StoreData{Version=1,Kind=Test?"synthetic-test":"local-user",Profiles=next}));
   byte[] encrypted;
   try{encrypted=ProtectedData.Protect(plain,Entropy,DataProtectionScope.CurrentUser);}finally{Array.Clear(plain,0,plain.Length);}
   string file=Path.Combine(Root,"environments.dat"),temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";
   using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(encrypted,0,encrypted.Length);stream.Flush(true);}
   if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);
   Profiles=next;
  }
  public void Save(Profile p){var next=Profiles.Select(x=>x.Id==p.Id?p:x).ToList();if(!next.Any(x=>x.Id==p.Id))next.Add(p);Commit(next);}
  public string Folder(Profile p){Policy.Validate(p);return Path.Combine(Root,"profiles",p.Id);}
  static void NoLinks(string target){for(string current=Path.GetFullPath(target);!string.IsNullOrEmpty(current);current=Path.GetDirectoryName(current)){if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new Exception("环境目录含链接，已停止删除");}}
  static void InspectTree(string folder,List<string> files,List<string> directories){
   NoLinks(folder);
   foreach(string file in Directory.EnumerateFiles(folder)){NoLinks(file);using(var handle=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.None)){}files.Add(file);}
   foreach(string child in Directory.EnumerateDirectories(folder))InspectTree(child,files,directories);
   directories.Add(folder);
  }
  public void DeletePermanent(string id,Action<Profile,string> assertClosed){
   var p=Profiles.SingleOrDefault(x=>x.Id==id);if(p==null)throw new Exception("环境已不存在");
   string parent=Path.GetFullPath(Path.Combine(Root,"profiles")),folder=Path.GetFullPath(Folder(p));
   if(!string.Equals(Path.GetDirectoryName(folder),parent,StringComparison.OrdinalIgnoreCase)||!folder.StartsWith(Path.GetFullPath(Root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new Exception("删除路径不在独立环境目录中");
   NoLinks(Root);assertClosed(p,folder);
   var files=new List<string>();var directories=new List<string>();
   if(Directory.Exists(folder)){
    string marker=Path.Combine(folder,".satloot-lite-profile");
    NoLinks(folder);
    if(!p.Deleting&&(!File.Exists(marker)||File.ReadAllText(marker)!=(Test?"synthetic-test:":"local-user:")+p.Id))throw new Exception("环境来源无法核实，已停止删除");
    InspectTree(folder,files,directories);
   }
   // Persist the destructive operation state before touching website data.
   // If interrupted, this profile cannot launch; the user can retry deletion.
   var pending=p.Copy();pending.Deleting=true;Save(pending);assertClosed(pending,folder);
   try{
    foreach(string file in files){NoLinks(file);File.SetAttributes(file,File.GetAttributes(file)&~FileAttributes.ReadOnly);File.Delete(file);}
    foreach(string directory in directories){NoLinks(directory);Directory.Delete(directory,false);}
    Commit(Profiles.Where(x=>x.Id!=id).ToList());
   }catch(Exception ex){throw new Exception("删除未完成，请关闭占用该环境的程序后重试。该环境暂不能打开；没有移入回收站。",ex);}
  }
 }
 public static class Policy {
  public static readonly Preset[] Presets={
   new Preset("desktop-a","nvidia-rtx-4070","RTX 4070",8,16,1920,1080),
   new Preset("desktop-b","nvidia-rtx-4070","RTX 4070",12,16,2560,1440),
   new Preset("desktop-c","nvidia-rtx-4080","RTX 4080",16,32,2560,1440),
   new Preset("desktop-d","nvidia-rtx-4080","RTX 4080",24,32,1920,1080),
   new Preset("desktop-e","nvidia-rtx-4070-ti-super","RTX 4070 Ti SUPER",16,16,2560,1440),
   new Preset("desktop-f","nvidia-rtx-4070-ti-super","RTX 4070 Ti SUPER",24,32,1920,1200)};
  public static readonly string[] Zones={"Asia/Shanghai","Asia/Hong_Kong","Asia/Tokyo","Asia/Singapore","Asia/Seoul","Asia/Dubai","Europe/London","Europe/Berlin","Europe/Paris","America/New_York","America/Chicago","America/Los_Angeles","Australia/Sydney","Etc/UTC"};
  public static readonly string[] Locales={"zh-CN","zh-TW","en-US","en-GB","ja-JP","ko-KR","de-DE","fr-FR"};
  public static Profile Create(){var p=new Profile{Id=Guid.NewGuid().ToString(),Name="新环境",Locale="zh-CN",Timezone="Asia/Shanghai",Network="system",Proxy="",Home="",Canvas=true,Webgl=true,Audio=true,Created=DateTime.UtcNow.ToString("o")};Randomize(p);return p;}
  public static void Randomize(Profile p){using(var rng=RandomNumberGenerator.Create()){byte[] b=new byte[32];rng.GetBytes(b);p.Seed=BitConverter.ToString(b).Replace("-","").ToLowerInvariant();int index;do{rng.GetBytes(b);index=b[0];}while(index>=252);string chosen=Presets[index%Presets.Length].Id;if(chosen==p.Preset)chosen=Presets[(index+1)%Presets.Length].Id;p.Preset=chosen;}}
  public static Preset Hardware(Profile p){return Presets.Single(x=>x.Id==p.Preset);}
  public static void Validate(Profile p){
   Guid id;if(p==null||!Guid.TryParseExact(p.Id,"D",out id))throw new Exception("环境编号不正确");
   if(string.IsNullOrWhiteSpace(p.Name)||p.Name.Length>60||p.Name.Any(char.IsControl))throw new Exception("环境名称请填写 1–60 个可见字符");
   if(!Regex.IsMatch(p.Seed??"",@"\A[a-f0-9]{64}\z"))throw new Exception("环境指纹种子不正确");
   if(!Presets.Any(x=>x.Id==p.Preset)||!Zones.Contains(p.Timezone)||!Locales.Contains(p.Locale))throw new Exception("硬件、语言或时区不在本版支持范围");
   if(!new[]{"system","direct","proxy","node"}.Contains(p.Network))throw new Exception("网络方式不正确");
   if(p.Network=="proxy")ProxyEndpoint(p.Proxy);
   if(p.Network=="node")SatlootLite.Network.Parse(p.NodeUri);
   if(!string.IsNullOrWhiteSpace(p.Home)){Uri u;if(!Uri.TryCreate(p.Home,UriKind.Absolute,out u)||!new[]{"http","https"}.Contains(u.Scheme)||u.UserInfo!=""||p.Home.Any(char.IsControl))throw new Exception("首页请填写不含账号密码的 http:// 或 https:// 网址");}
  }
  public static Uri ProxyEndpoint(string value){
   Uri u;IPAddress ip;
   if(!Uri.TryCreate(value,UriKind.Absolute,out u)||!new[]{"http","socks5"}.Contains(u.Scheme)||!IPAddress.TryParse(u.Host.Trim('[',']'),out ip)||u.Port<1||u.Port>65535||u.UserInfo!=""||u.Query!=""||u.Fragment!=""||(u.AbsolutePath!=""&&u.AbsolutePath!="/"))throw new Exception("代理请填写 HTTP / SOCKS5 的 IP 和端口，例如 http://127.0.0.1:7890。本预览版不支持账号密码或订阅链接。");
   return u;
  }
  // Standard Windows argv quoting; no shell is used for browser launch.
  public static string Quote(string value){
   var result=new StringBuilder("\"");int slashes=0;
   foreach(char c in value??""){if(c=='\0')throw new Exception("启动参数含非法字符");if(c=='\\'){slashes++;continue;}if(c=='\"'){result.Append('\\',slashes*2+1);result.Append(c);}else{result.Append('\\',slashes);result.Append(c);}slashes=0;}
   result.Append('\\',slashes*2);result.Append('"');return result.ToString();
  }
  public static List<string> Arguments(Profile p,string root){
   Validate(p);var hw=Hardware(p);var a=new List<string>{"--user-data-dir="+Path.GetFullPath(Path.Combine(root,"profiles",p.Id)),"--no-first-run","--no-default-browser-check","--disable-background-mode","--disable-default-apps","--window-size=1200,820","--lang="+p.Locale,
    "--satloot-virtual-hardware=1,"+hw.Gpu+","+hw.Cpu+","+hw.Memory,
    "--satloot-timezone=1,"+p.Timezone,"--satloot-font-pack=noto-basic-v1","--satloot-screen=1,"+hw.Width+","+hw.Height,"--satloot-canvas-exceptions=1,",
    "--webrtc-ip-handling-policy=disable_non_proxied_udp","--disable-features=OmniboxEverywhere,AimEnabled,WebUIOmniboxAimPopup,Aim3pEntrypoint"};
   if(p.Canvas)a.Add("--satloot-canvas-readout=1,"+p.Seed);if(p.Webgl)a.Add("--satloot-webgl-readout=1,"+p.Seed);if(p.Audio)a.Add("--satloot-audio-readout=1,"+p.Seed);
   if(p.Network=="direct")a.Add("--no-proxy-server");
   if(p.Network=="proxy"){var u=ProxyEndpoint(p.Proxy);a.Add("--proxy-server="+u.Scheme+"://"+u.Authority);a.Add("--proxy-bypass-list=<-loopback>");a.Add("--disable-quic");a.Add("--dns-prefetch-disable");a.Add("--satloot-proxy-dns=1,"+u.Host.Trim('[',']'));}
   a.Add("--restore-last-session");a.Add(string.IsNullOrWhiteSpace(p.Home)?"chrome://newtab/":p.Home);
   return a;
  }
 }
 public sealed class RuntimeFile {public string file {get;set;} public long size {get;set;} public string sha256 {get;set;}}
 public sealed class RuntimeManifest {public string edition {get;set;} public string version {get;set;} public string state {get;set;} public string[] capabilities {get;set;} public List<RuntimeFile> files {get;set;}}
 public sealed class Runtime {
  public readonly string Root,Exe; public bool Verified; public string Version="156.0.8078.48";
  public const string ExpectedManifest="8355c036a74691a14450c4cfc73c8379346ae3f5ac02ef4087a7def848c9e2c3";
  readonly Dictionary<string,string> stamps=new Dictionary<string,string>();
  public Runtime(string package){Root=Path.Combine(package,"vendor","satloot-chromium");Exe=Path.Combine(Root,"chrome.exe");}
  public static string Hash(string file){using(var sha=SHA256.Create())using(var f=File.OpenRead(file))return BitConverter.ToString(sha.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
  static void NoLink(string file){for(string p=Path.GetFullPath(file);!string.IsNullOrEmpty(p);p=Path.GetDirectoryName(p)){if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new Exception("程序或数据目录不能使用符号链接或目录联接");}}
  void SandboxAccess(){
   // Only grant the Chromium sandbox read/execute access to program resources.
   var acl=Directory.GetAccessControl(Root);bool changed=false;
   foreach(string sid in new[]{"S-1-15-3-1024-3424233489-972189580-2057154623-747635277-1604371224-316187997-3786583170-1043257646","S-1-15-3-1024-2302894289-466761758-1166120688-1039016420-2430351297-4240214049-4028510897-3317428798"}){
    var identity=new System.Security.Principal.SecurityIdentifier(sid);bool found=false;
    foreach(System.Security.AccessControl.FileSystemAccessRule rule in acl.GetAccessRules(true,true,typeof(System.Security.Principal.SecurityIdentifier))){if(rule.IdentityReference.Equals(identity)&&rule.AccessControlType==System.Security.AccessControl.AccessControlType.Allow&&(rule.FileSystemRights&System.Security.AccessControl.FileSystemRights.ReadAndExecute)==System.Security.AccessControl.FileSystemRights.ReadAndExecute&&(rule.InheritanceFlags&(System.Security.AccessControl.InheritanceFlags.ContainerInherit|System.Security.AccessControl.InheritanceFlags.ObjectInherit))==(System.Security.AccessControl.InheritanceFlags.ContainerInherit|System.Security.AccessControl.InheritanceFlags.ObjectInherit))found=true;}
    if(!found){acl.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(identity,System.Security.AccessControl.FileSystemRights.ReadAndExecute,System.Security.AccessControl.InheritanceFlags.ContainerInherit|System.Security.AccessControl.InheritanceFlags.ObjectInherit,System.Security.AccessControl.PropagationFlags.None,System.Security.AccessControl.AccessControlType.Allow));changed=true;}
   }
   if(changed)Directory.SetAccessControl(Root,acl);
  }
  public void Verify(){lock(stamps){
   string manifest=Path.Combine(Root,"runtime-manifest.json");NoLink(manifest);
   if(Hash(manifest)!=ExpectedManifest)throw new Exception("内核清单已变化，请使用完整预览包");
   var m=Json.Read<RuntimeManifest>(File.ReadAllText(manifest));
   if(m.edition!="satloot-chromium"||m.state!="compiled-and-runtime-verified"||m.version!=Version||m.files==null||m.files.Count<2)throw new Exception("指纹内核不完整");
   var required=new[]{"virtual-hardware-v1","fixed-timezone-v1","canvas-readout-v1","webgl-readout-v1","audio-readout-v1","font-pack-v1","screen-size-v1","origin-exceptions-v1","proxy-dns-guard-v1"};
   if(required.Any(x=>!m.capabilities.Contains(x)))throw new Exception("内核缺少所需指纹能力");
   foreach(var entry in m.files){
    string file=Path.GetFullPath(Path.Combine(Root,entry.file));
    if(!file.StartsWith(Path.GetFullPath(Root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new Exception("内核文件路径越界");
    var info=new FileInfo(file);if(!info.Exists||info.Length!=entry.size)throw new Exception("浏览器组件缺失或损坏："+entry.file);NoLink(file);
    string stamp=info.Length+":"+info.LastWriteTimeUtc.Ticks+":"+info.CreationTimeUtc.Ticks,old;
    if(!stamps.TryGetValue(file,out old)||old!=stamp){if(Hash(file)!=entry.sha256)throw new Exception("浏览器组件校验失败："+entry.file);stamps[file]=stamp;}
   }
   SandboxAccess();Verified=true;
  }}
#if QA
  public Func<Profile,bool> RunningOverride;
#endif
  public bool Running(Profile p){
#if QA
   if(RunningOverride!=null)return RunningOverride(p);
#endif
   if(p.ProcessId<=0)return false;
   try{using(var proc=Process.GetProcessById(p.ProcessId)){return !proc.HasExited&&proc.StartTime.ToUniversalTime().Ticks==p.ProcessStart&&string.Equals(proc.MainModule.FileName,Exe,StringComparison.OrdinalIgnoreCase);}}catch{return false;}
  }
  public void AssertClosed(Profile p,string folder){
   if(Running(p))throw new Exception("请先正常关闭这个环境的浏览器，再删除环境");
   using(var search=new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name='chrome.exe'"))using(var processes=search.Get()){
    foreach(ManagementObject item in processes){using(item){string command=item["CommandLine"] as string;if(command!=null&&command.IndexOf(folder,StringComparison.OrdinalIgnoreCase)>=0)throw new Exception("这个环境仍有浏览器进程运行，请先正常关闭后再删除");}}
   }
  }
  public Profile Launch(Profile p,Store store,string page=null){
   if(store.Test)throw new Exception("测试身份不能使用手动启动入口");
   if(p.Deleting)throw new Exception("此环境上次删除未完成，请先完成删除");
   Verify();if(Running(p))throw new Exception("此环境已打开，请使用它现有的浏览器窗口");
   string folder=store.Folder(p);Directory.CreateDirectory(folder);NoLink(folder);
   string marker=Path.Combine(folder,".satloot-lite-profile");
   if(!File.Exists(marker)){if(Directory.EnumerateFileSystemEntries(folder).Any())throw new Exception("拒绝接管来源不明的环境目录");File.WriteAllText(marker,"local-user:"+p.Id,Encoding.UTF8);}
   if(File.ReadAllText(marker)!="local-user:"+p.Id||File.Exists(Path.Combine(folder,".satloot-test")))throw new Exception("环境来源验证失败");
   Network.Route route=null;
   try{
    var effective=p.Copy();if(p.Network=="node"){route=Network.Start(AppDomain.CurrentDomain.BaseDirectory,p.NodeUri);effective.Network="proxy";effective.Proxy="http://127.0.0.1:"+route.Port;}
    var arguments=Policy.Arguments(effective,store.Root);if(page!=null){if(!BrowserActions.Pages.Contains(page))throw new Exception("不支持的浏览器入口");arguments[arguments.Count-1]=page;}
    var si=new ProcessStartInfo(Exe,string.Join(" ",arguments.Select(Policy.Quote))){UseShellExecute=false,WorkingDirectory=Root};
    var proc=Process.Start(si);var next=p.Copy();next.ProcessId=proc.Id;next.ProcessStart=proc.StartTime.ToUniversalTime().Ticks;next.LastOpened=DateTime.UtcNow.ToString("o");
    if(route!=null){Process.Start(new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"SATLOOT Lite.exe"),"--network-watch "+proc.Id+" "+next.ProcessStart+" "+route.Process.Id+" "+route.Process.StartTime.ToUniversalTime().Ticks){UseShellExecute=false,CreateNoWindow=true});}
    store.Save(next);return next;
   }catch{if(route!=null)route.Dispose();throw;}
  }
 }
}
