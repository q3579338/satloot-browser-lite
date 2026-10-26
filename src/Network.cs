using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Web;
namespace SatlootLite {
 public static class Network {
  static Dictionary<string,object> Obj(params object[] pairs){var d=new Dictionary<string,object>();for(int i=0;i<pairs.Length;i+=2)d[(string)pairs[i]]=pairs[i+1];return d;}
  static string Clean(string s,int max=2048){if(s==null)return "";if(s.Length>max||s.IndexOfAny(new[]{'\r','\n','\0'})>=0)throw new Exception("节点参数过长或包含非法字符");return s;}
  static string B64(string s){s=s.Replace('-','+').Replace('_','/');s=s.PadRight((s.Length+3)/4*4,'=');return Encoding.UTF8.GetString(Convert.FromBase64String(s));}
  static string Value(Dictionary<string,object> d,string k,string fallback=""){object v;return d.TryGetValue(k,out v)&&v!=null?Convert.ToString(v):fallback;}
  public static Dictionary<string,object> Parse(string raw){
   raw=Clean((raw??"").Trim(),16000);var q=HttpUtility.ParseQueryString("");string type,server,secret,transport,security;int port;var outbound=Obj();
   if(raw.StartsWith("http://",StringComparison.OrdinalIgnoreCase)||raw.StartsWith("https://",StringComparison.OrdinalIgnoreCase)||raw.StartsWith("socks://",StringComparison.OrdinalIgnoreCase)||raw.StartsWith("socks5://",StringComparison.OrdinalIgnoreCase)||raw.StartsWith("ss://",StringComparison.OrdinalIgnoreCase))return ParseProxy(raw);
   if(raw.StartsWith("vmess://",StringComparison.OrdinalIgnoreCase)){
    Dictionary<string,object> v;try{v=Json.Read<Dictionary<string,object>>(B64(raw.Substring(8)));}catch{throw new Exception("VMess 分享链接格式不正确");}
    type="vmess";server=Value(v,"add");secret=Value(v,"id");if(!int.TryParse(Value(v,"port"),out port))throw new Exception("节点端口不正确");transport=Value(v,"net","tcp");security=Value(v,"tls")=="tls"?"tls":"none";
    q["sni"]=Value(v,"sni",Value(v,"host",server));q["host"]=Value(v,"host");q["path"]=Value(v,"path","/");q["serviceName"]=Value(v,"path");q["alpn"]=Value(v,"alpn");int aid;if(!int.TryParse(Value(v,"aid","0"),out aid)||aid<0)throw new Exception("VMess alterId 不正确");outbound["security"]=Value(v,"scy","auto");outbound["alter_id"]=aid;
   }else{
    Uri u;if(!Uri.TryCreate(raw,UriKind.Absolute,out u)||!new[]{"vless","trojan"}.Contains(u.Scheme))throw new Exception("V2 节点支持 VLESS、VMess 和 Trojan 分享链接");
    type=u.Scheme;server=u.Host.Trim('[',']');secret=Uri.UnescapeDataString(u.UserInfo);port=u.Port;q=HttpUtility.ParseQueryString(u.Query);transport=q["type"]??"tcp";security=q["security"]??(type=="trojan"?"tls":"none");
    if(type=="vless"&&q["encryption"]!=null&&q["encryption"]!="none")throw new Exception("暂不支持该 VLESS 加密扩展");if(!string.IsNullOrEmpty(q["flow"]))outbound["flow"]=Clean(q["flow"],60);
   }
   if(string.IsNullOrWhiteSpace(server)||server.Length>253||Uri.CheckHostName(server)==UriHostNameType.Unknown||port<1||port>65535)throw new Exception("节点服务器或端口不正确");
   outbound["type"]=type;outbound["tag"]="selected";outbound["server"]=server;outbound["server_port"]=port;
   if(type=="trojan"){if(secret.Length<1)throw new Exception("Trojan 密码为空");outbound["password"]=Clean(secret);}else{Guid uuid;if(!Guid.TryParseExact(secret,"D",out uuid))throw new Exception("节点 UUID 不正确");outbound["uuid"]=secret;}
   if(q["allowInsecure"]=="1"||q["allowInsecure"]=="true"||q["insecure"]=="1"||q["insecure"]=="true")throw new Exception("节点必须启用证书校验");
   if(security!="none"&&security!=""){
    if(security!="tls"&&security!="reality")throw new Exception("不支持的 TLS 模式");
    var tls=Obj("enabled",true,"server_name",Clean(q["sni"]??q["serverName"]??server,253));if(!string.IsNullOrEmpty(q["alpn"]))tls["alpn"]=Clean(q["alpn"],256).Split(',');
    if(!string.IsNullOrEmpty(q["fp"])||security=="reality")tls["utls"]=Obj("enabled",true,"fingerprint",Clean(q["fp"]??"chrome",40));
    if(security=="reality"){if(string.IsNullOrWhiteSpace(q["pbk"]))throw new Exception("Reality 节点缺少公钥");tls["reality"]=Obj("enabled",true,"public_key",Clean(q["pbk"],100),"short_id",Clean(q["sid"]??"",40));}outbound["tls"]=tls;
   }
   if(transport!="tcp"&&transport!=""){
    Dictionary<string,object> t;if(transport=="ws"){t=Obj("type","ws","path",Clean(q["path"]??"/"));if(!string.IsNullOrEmpty(q["host"]))t["headers"]=Obj("Host",Clean(q["host"],253));}
    else if(transport=="grpc")t=Obj("type","grpc","service_name",Clean(q["serviceName"]??q["path"]??"",256));
    else if(transport=="httpupgrade")t=Obj("type","httpupgrade","path",Clean(q["path"]??"/"),"host",Clean(q["host"]??"",253));
    else throw new Exception("节点传输支持 TCP、WebSocket、gRPC、HTTPUpgrade");outbound["transport"]=t;
   }return outbound;
  }
  static Dictionary<string,object> ParseProxy(string raw){
   if(raw.StartsWith("ss://",StringComparison.OrdinalIgnoreCase)&&!raw.Substring(5).Split('#')[0].Contains("@")){var parts=raw.Substring(5).Split('#');try{raw="ss://"+B64(parts[0])+(parts.Length>1?"#"+parts[1]:"");}catch{throw new Exception("Shadowsocks 分享链接格式不正确");}}
   Uri u;if(!Uri.TryCreate(raw,UriKind.Absolute,out u))throw new Exception("代理分享链接格式不正确");string server=u.Host.Trim('[',']');int port=u.Port;
   if(string.IsNullOrEmpty(server)||server.Length>253||Uri.CheckHostName(server)==UriHostNameType.Unknown||port<1||port>65535)throw new Exception("代理服务器或端口不正确");
   var query=HttpUtility.ParseQueryString(u.Query);if(!string.IsNullOrEmpty(query["plugin"]))throw new Exception("暂不支持需要外置插件的 Shadowsocks 线路");
   var d=Obj("tag","selected","server",server,"server_port",port);string user=u.UserInfo,username="",password="";int split=user.IndexOf(':');
   if(u.Scheme=="ss"){
    if(split<0){try{user=B64(Uri.UnescapeDataString(user));}catch{throw new Exception("Shadowsocks 认证格式不正确");}split=user.IndexOf(':');}
    if(split<1||split==user.Length-1)throw new Exception("Shadowsocks 认证格式不正确");d["type"]="shadowsocks";d["method"]=Clean(Uri.UnescapeDataString(user.Substring(0,split)),80);d["password"]=Clean(Uri.UnescapeDataString(user.Substring(split+1)),1024);
   }else{
    if(split>=0){username=Uri.UnescapeDataString(user.Substring(0,split));password=Uri.UnescapeDataString(user.Substring(split+1));}else username=Uri.UnescapeDataString(user);
    bool socks=u.Scheme=="socks"||u.Scheme=="socks5";d["type"]=socks?"socks":"http";if(socks)d["version"]="5";if(username!="")d["username"]=Clean(username,1024);if(password!="")d["password"]=Clean(password,1024);if(u.Scheme=="https")d["tls"]=Obj("enabled",true,"server_name",server);
    if(query.Count>0||(u.AbsolutePath!=""&&u.AbsolutePath!="/"))throw new Exception("HTTP / SOCKS 代理不支持路径或查询参数");
   }return d;
  }
  public static string Config(string uri,int port){return Json.Write(Obj("log",Obj("disabled",true),"inbounds",new[]{Obj("type","http","tag","browser","listen","127.0.0.1","listen_port",port)},"outbounds",new[]{Parse(uri)},"route",Obj("final","selected","default_domain_resolver","system"),"dns",Obj("servers",new[]{Obj("type","local","tag","system")})));}
  public sealed class Route:IDisposable {public Process Process;public int Port;public void Dispose(){try{if(Process!=null&&!Process.HasExited){Process.Kill();Process.WaitForExit(3000);}}catch{}if(Process!=null)Process.Dispose();}}
  public static string Exe(string package){return Path.Combine(package,"vendor","sing-box","sing-box.exe");}
  public static void Check(string package,string config){
   if(Runtime.Hash(Exe(package))!="5c19bb3f8c12d4e85b3d47fe63123a7624b314b58dd7a8e32f51004f1c4d2faf"||Runtime.Hash(Path.Combine(Path.GetDirectoryName(Exe(package)),"libcronet.dll"))!="3217c6260fbca5f16072e0b79735742f40109a63bb0ff88fd6b96dd6b54a2928")throw new Exception("网络组件校验失败，请重新安装完整程序包");
   var si=new ProcessStartInfo(Exe(package),"check -c stdin"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardError=true,RedirectStandardOutput=true,WorkingDirectory=Path.GetDirectoryName(Exe(package))};
   using(var p=Process.Start(si)){p.BeginErrorReadLine();p.BeginOutputReadLine();p.StandardInput.Write(config);p.StandardInput.Close();if(!p.WaitForExit(15000)){p.Kill();throw new Exception("节点配置校验超时");}if(p.ExitCode!=0)throw new Exception("节点配置未通过本机校验，请检查分享链接和传输参数");}
  }
  public static Route Start(string package,string uri){
   var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();string config=Config(uri,port);Check(package,config);
   var si=new ProcessStartInfo(Exe(package),"run -c stdin"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardError=true,RedirectStandardOutput=true,WorkingDirectory=Path.GetDirectoryName(Exe(package))};
   var route=new Route{Process=Process.Start(si),Port=port};route.Process.BeginErrorReadLine();route.Process.BeginOutputReadLine();route.Process.StandardInput.Write(config);route.Process.StandardInput.Close();
   try{for(int i=0;i<100;i++){if(route.Process.HasExited)throw new Exception("节点进程启动失败，已阻止浏览器直连");try{using(var socket=new TcpClient()){var task=socket.ConnectAsync(IPAddress.Loopback,port);if(task.Wait(60)&&socket.Connected)return route;}}catch(AggregateException){}Thread.Sleep(50);}throw new Exception("节点尚未就绪，未打开浏览器");}catch{route.Dispose();throw;}
  }
  static Process Match(int pid,long start,string path){try{var p=Process.GetProcessById(pid);if(!p.HasExited&&p.StartTime.ToUniversalTime().Ticks==start&&string.Equals(p.MainModule.FileName,path,StringComparison.OrdinalIgnoreCase))return p;p.Dispose();}catch{}return null;}
  public static int Watch(string package,string[] args){
   int browserId=int.Parse(args[1]),routeId=int.Parse(args[3]);long browserStart=long.Parse(args[2]),routeStart=long.Parse(args[4]);
   using(var route=Match(routeId,routeStart,Exe(package))){if(route==null)return 0;using(var browser=Match(browserId,browserStart,Path.Combine(package,"vendor","satloot-chromium","chrome.exe"))){if(browser!=null)browser.WaitForExit();}try{if(!route.HasExited)route.Kill();}catch{}}
   return 0;
  }
 }
}
