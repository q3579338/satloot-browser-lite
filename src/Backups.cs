using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace SatlootLite {
 public sealed class BackupInfo {public int Version {get;set;}public string Kind {get;set;}public Profile Profile {get;set;}}
 public static class Backups {
  const long Limit=256L*1024*1024;static readonly byte[] Entropy=Encoding.UTF8.GetBytes("SATLOOT.Lite.local-backup.v1");
  static void Safe(string path){for(string p=Path.GetFullPath(path);!string.IsNullOrEmpty(p);p=Path.GetDirectoryName(p))if(Directory.Exists(p)||File.Exists(p)){if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new Exception("备份目录不能包含链接或目录联接");}}
  static void Collect(string folder,List<string> files,ref long total){Safe(folder);foreach(string file in Directory.GetFiles(folder)){Safe(file);if(Path.GetFileName(file)==".satloot-lite-profile"||Path.GetFileName(file)=="DevToolsActivePort")continue;total+=new FileInfo(file).Length;if(total>Limit)throw new Exception("此环境的备份数据超过 256 MB，请先整理数据");files.Add(file);}foreach(string child in Directory.GetDirectories(folder)){Safe(child);if(new[]{"Cache","Code Cache","GPUCache","DawnCache","GrShaderCache","ShaderCache","Crashpad"}.Contains(Path.GetFileName(child)))continue;Collect(child,files,ref total);}}
  public static void Export(Store store,Profile p,string destination,Action<Profile,string> assertClosed){
   if(p.Deleting)throw new Exception("删除未完成的环境不能备份");string folder=store.Folder(p);assertClosed(p,folder);var files=new List<string>();long size=0;if(Directory.Exists(folder)){Safe(folder);if(File.ReadAllText(Path.Combine(folder,".satloot-lite-profile"))!=(store.Test?"synthetic-test:":"local-user:")+p.Id)throw new Exception("环境目录来源不正确");Collect(folder,files,ref size);}
   byte[] plain;using(var memory=new MemoryStream()){using(var archive=new ZipArchive(memory,ZipArchiveMode.Create,true)){var metadata=archive.CreateEntry("profile.json");using(var output=new StreamWriter(metadata.Open(),Encoding.UTF8))output.Write(Json.Write(new BackupInfo{Version=1,Kind=store.Test?"synthetic-test":"local-user",Profile=p}));foreach(var file in files){var entry=archive.CreateEntry("data/"+file.Substring(folder.Length+1).Replace('\\','/'),CompressionLevel.Optimal);using(var input=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read))using(var output=entry.Open())input.CopyTo(output);}}plain=memory.ToArray();}
   assertClosed(p,folder);byte[] encrypted;try{encrypted=ProtectedData.Protect(plain,Entropy,DataProtectionScope.CurrentUser);}finally{Array.Clear(plain,0,plain.Length);}
   destination=Path.GetFullPath(destination);Safe(Path.GetDirectoryName(destination));if(destination.StartsWith(Path.GetFullPath(store.Root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new Exception("备份文件请保存在环境数据目录之外");string temp=destination+"."+Guid.NewGuid().ToString("N")+".tmp";try{using(var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){output.Write(encrypted,0,encrypted.Length);output.Flush(true);}if(File.Exists(destination))File.Replace(temp,destination,null);else File.Move(temp,destination);}finally{if(File.Exists(temp))File.Delete(temp);}
  }
  public static Profile Import(Store store,string source){
   if(new FileInfo(source).Length>Limit+8*1024*1024)throw new Exception("备份文件过大");byte[] plain;try{plain=ProtectedData.Unprotect(File.ReadAllBytes(source),Entropy,DataProtectionScope.CurrentUser);}catch{throw new Exception("无法解密此备份。请使用创建备份时的同一 Windows 账户；文件也可能已损坏。");}
   string folder=null;bool committed=false,created=false;try{using(var memory=new MemoryStream(plain))using(var archive=new ZipArchive(memory,ZipArchiveMode.Read)){
    if(archive.Entries.Count>20000||archive.Entries.Sum(e=>e.Length)>Limit+1024*1024)throw new Exception("备份内容超过限制");var metas=archive.Entries.Where(e=>e.FullName=="profile.json").ToArray();if(metas.Length!=1||metas[0].Length>65536)throw new Exception("备份清单不正确");BackupInfo info;using(var read=new StreamReader(metas[0].Open(),Encoding.UTF8))info=Json.Read<BackupInfo>(read.ReadToEnd());if(info.Version!=1||info.Kind!=(store.Test?"synthetic-test":"local-user"))throw new Exception("备份来源不匹配");Policy.Validate(info.Profile);
    var profile=info.Profile.Copy();profile.Id=Guid.NewGuid().ToString();profile.Name=(profile.Name.Length>52?profile.Name.Substring(0,52):profile.Name)+" · 恢复";profile.ProcessId=0;profile.ProcessStart=0;profile.Deleting=false;profile.LastOpened=null;profile.Created=DateTime.UtcNow.ToString("o");folder=Path.GetFullPath(store.Folder(profile));Safe(store.Root);Safe(Path.GetDirectoryName(folder));if(Directory.Exists(folder))throw new Exception("恢复目录已存在");
    var targets=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(var entry in archive.Entries){if(entry.FullName=="profile.json")continue;if(!entry.FullName.StartsWith("data/",StringComparison.Ordinal)||entry.FullName.Contains('\\')||entry.FullName.Contains(':')||entry.FullName.Split('/').Any(part=>part==".."||part=="."))throw new Exception("备份文件路径不正确");string relative=entry.FullName.Substring(5);string target=Path.GetFullPath(Path.Combine(folder,relative));if(relative==""||!target.StartsWith(folder+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!targets.Add(target))throw new Exception("备份文件路径冲突或越界");}
    Directory.CreateDirectory(folder);created=true;foreach(var entry in archive.Entries){if(entry.FullName=="profile.json")continue;string target=Path.GetFullPath(Path.Combine(folder,entry.FullName.Substring(5)));if(entry.FullName.EndsWith("/")){Directory.CreateDirectory(target);continue;}Directory.CreateDirectory(Path.GetDirectoryName(target));using(var input=entry.Open())using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None))input.CopyTo(output);}
    File.WriteAllText(Path.Combine(folder,".satloot-lite-profile"),(store.Test?"synthetic-test:":"local-user:")+profile.Id);store.Save(profile);committed=true;return profile;
   }}finally{Array.Clear(plain,0,plain.Length);if(!committed&&created&&folder!=null&&Directory.Exists(folder)){string parent=Path.GetFullPath(Path.Combine(store.Root,"profiles"));if(!string.Equals(Path.GetDirectoryName(folder),parent,StringComparison.OrdinalIgnoreCase))throw new Exception("恢复清理路径越界");Safe(folder);Directory.Delete(folder,true);}}
  }
 }
}
