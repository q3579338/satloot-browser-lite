using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace SatlootLite {
 public sealed class RouteNode {
  public string Id {get;set;} public string Name {get;set;} public string Uri {get;set;} public string Type {get;set;} public string Server {get;set;} public string Key {get;set;}
  public override string ToString(){return Name+" · "+Type.ToUpperInvariant();}
 }
 public sealed class RouteBatch {public List<RouteNode> Nodes=new List<RouteNode>();public List<string> Errors=new List<string>();public int Duplicates;}
 public sealed class RouteData {public string Kind {get;set;} public List<RouteNode> Nodes {get;set;}}
 public sealed class NodeStore {
  readonly string file;readonly bool test;public List<RouteNode> Nodes=new List<RouteNode>();
  static readonly byte[] entropy=Encoding.UTF8.GetBytes("SATLOOT.Lite.routes.v1");
  public NodeStore(string root,bool synthetic){test=synthetic;file=Path.Combine(root,"routes.dat");if(synthetic!=File.Exists(Path.Combine(root,".satloot-lite-test")))throw new Exception("线路库身份不匹配");if(File.Exists(file)){byte[] plain=ProtectedData.Unprotect(File.ReadAllBytes(file),entropy,DataProtectionScope.CurrentUser);try{var saved=Json.Read<RouteData>(Encoding.UTF8.GetString(plain));if(saved.Kind!=(test?"synthetic-test":"local-user")||saved.Nodes==null||saved.Nodes.Count>10000)throw new Exception("线路库来源或格式不正确");foreach(var n in saved.Nodes){var verified=ParseOne(n.Uri);if(verified.Key!=n.Key||string.IsNullOrWhiteSpace(n.Name)||n.Name.Length>100)throw new Exception("线路库记录不正确");}Nodes=saved.Nodes;}finally{Array.Clear(plain,0,plain.Length);}}}
  public static RouteNode ParseOne(string uri){
   var config=Network.Parse(uri);string name=(string)config["server"];
   if(uri.StartsWith("vmess://",StringComparison.OrdinalIgnoreCase)){string encoded=uri.Substring(8).Replace('-','+').Replace('_','/');encoded=encoded.PadRight((encoded.Length+3)/4*4,'=');var vm=Json.Read<Dictionary<string,object>>(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));object ps;if(vm.TryGetValue("ps",out ps)&&ps!=null&&!string.IsNullOrWhiteSpace(ps.ToString()))name=ps.ToString();}
   else{int hash=uri.IndexOf('#');if(hash>=0&&hash+1<uri.Length)name=Uri.UnescapeDataString(uri.Substring(hash+1));}
   name=new string(name.Where(c=>!char.IsControl(c)).Take(100).ToArray());if(string.IsNullOrWhiteSpace(name))name=(string)config["server"];
   string key;using(var sha=SHA256.Create())key=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Json.Write(config)))).Replace("-","");
   return new RouteNode{Id=Guid.NewGuid().ToString(),Name=name,Uri=uri.Trim(),Type=(string)config["type"],Server=(string)config["server"],Key=key};
  }
  public static RouteBatch Parse(string text){
   const int limit=2*1024*1024;if(text==null||Encoding.UTF8.GetByteCount(text)>limit)throw new Exception("每次最多导入 2 MB 的线路文本");text=text.Trim().TrimStart('\uFEFF');
   if(text.StartsWith("<")||text.StartsWith("{")||text.StartsWith("[")||text.Contains("proxies:"))throw new Exception("请选择分享链接或 Base64 文本，暂不支持 Clash YAML、JSON 或网页");
   if(!text.Contains("://")){try{string value=new string(text.Where(c=>!char.IsWhiteSpace(c)).ToArray()).Replace('-','+').Replace('_','/');value=value.PadRight((value.Length+3)/4*4,'=');text=new UTF8Encoding(false,true).GetString(Convert.FromBase64String(value));}catch{throw new Exception("请输入分享链接列表或有效的 Base64 订阅内容");}}
   if(Encoding.UTF8.GetByteCount(text)>limit)throw new Exception("订阅内容超过 2 MB");var lines=text.Split('\n');if(lines.Count(x=>!string.IsNullOrWhiteSpace(x)&&!x.TrimStart().StartsWith("#"))>5000)throw new Exception("每次最多导入 5000 条线路");
   var result=new RouteBatch();var known=new HashSet<string>();for(int i=0;i<lines.Length;i++){string line=lines[i].Trim();if(line==""||line.StartsWith("#"))continue;try{var node=ParseOne(line);if(known.Add(node.Key))result.Nodes.Add(node);else result.Duplicates++;}catch{result.Errors.Add("第 "+(i+1)+" 行：不是支持的有效线路，请核对协议、认证和传输参数。");}}
   return result;
  }
  void Save(List<RouteNode> next){if(next.Count>10000)throw new Exception("线路库最多保存 10000 条线路");byte[] plain=Encoding.UTF8.GetBytes(Json.Write(new RouteData{Kind=test?"synthetic-test":"local-user",Nodes=next})),encrypted;try{encrypted=ProtectedData.Protect(plain,entropy,DataProtectionScope.CurrentUser);}finally{Array.Clear(plain,0,plain.Length);}string temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";using(var s=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){s.Write(encrypted,0,encrypted.Length);s.Flush(true);}if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);Nodes=next;}
  public int Import(IEnumerable<RouteNode> nodes){var next=Nodes.ToList();var known=new HashSet<string>(next.Select(n=>n.Key));int added=0;foreach(var n in nodes){var verified=ParseOne(n.Uri);if(known.Add(verified.Key)){next.Add(verified);added++;}}if(added>0)Save(next);return added;}
  public void Rename(string id,string name){name=name.Trim();if(name.Length<1||name.Length>100||name.Any(char.IsControl))throw new Exception("线路名称请填写 1–100 个可见字符");var next=Nodes.Select(n=>Json.Read<RouteNode>(Json.Write(n))).ToList();next.Single(n=>n.Id==id).Name=name;Save(next);}
  public void Remove(IEnumerable<string> ids){var selected=new HashSet<string>(ids);Save(Nodes.Where(n=>!selected.Contains(n.Id)).ToList());}
  public static string Fetch(string address){
   Uri uri;if(address==null||address.Length>8192||address.Any(char.IsControl)||!Uri.TryCreate(address.Trim(),UriKind.Absolute,out uri)||uri.Scheme!="https"||uri.UserInfo!=""||uri.Fragment!="")throw new Exception("请填写不含账号密码或片段的 HTTPS 订阅链接");
   ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;var request=(HttpWebRequest)WebRequest.Create(uri);request.AllowAutoRedirect=false;request.Timeout=15000;request.ReadWriteTimeout=15000;request.Accept="text/plain, application/octet-stream";request.UserAgent="SATLOOT-Lite/0.3.1";
   using(var timer=new Timer(s=>request.Abort(),null,15000,Timeout.Infinite)){try{using(var response=(HttpWebResponse)request.GetResponse()){if(response.StatusCode!=HttpStatusCode.OK)throw new Exception("订阅地址发生跳转或无法读取，请填写最终 HTTPS 链接");if(response.ContentLength>2*1024*1024)throw new Exception("订阅内容超过 2 MB");using(var stream=response.GetResponseStream())using(var output=new MemoryStream()){byte[] buffer=new byte[8192];int read;while((read=stream.Read(buffer,0,buffer.Length))>0){if(output.Length+read>2*1024*1024)throw new Exception("订阅内容超过 2 MB");output.Write(buffer,0,read);}return new UTF8Encoding(false,true).GetString(output.ToArray());}}}catch(WebException){throw new Exception("无法读取订阅，请核对链接、有效期及本机网络。订阅地址不会记录到日志。");}}
  }
  public static long Probe(string package,RouteNode node){ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;using(var route=Network.Start(package,node.Uri)){var watch=Stopwatch.StartNew();var request=(HttpWebRequest)WebRequest.Create("https://www.gstatic.com/generate_204");request.Proxy=new WebProxy("http://127.0.0.1:"+route.Port,false);request.Timeout=10000;request.ReadWriteTimeout=10000;request.AllowAutoRedirect=false;using(var timer=new Timer(s=>request.Abort(),null,10000,Timeout.Infinite))using(var response=(HttpWebResponse)request.GetResponse()){if((int)response.StatusCode!=204)throw new Exception("测试地址未返回预期结果");return watch.ElapsedMilliseconds;}}}
 }
}
