using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Drawing;

namespace SatlootLite {
 static class BrowserActions {
  public static readonly string[] Pages={"chrome://extensions/","chrome://bookmarks/","chrome://history/","chrome://downloads/","chrome://settings/"};
  delegate bool WindowCallback(IntPtr window,IntPtr parameter);
  [DllImport("user32.dll")]static extern bool EnumWindows(WindowCallback callback,IntPtr parameter);
  [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint processId);
  [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
  [DllImport("user32.dll")]static extern bool IsIconic(IntPtr window);
  [DllImport("user32.dll")]static extern bool ShowWindowAsync(IntPtr window,int command);
  [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
  // Called only by an explicit click in the manager, never by polling.
  public static bool ReturnToBrowser(Runtime runtime,Profile profile){
   if(!runtime.Running(profile))return false;IntPtr target=IntPtr.Zero;
   EnumWindows((window,parameter)=>{uint pid;GetWindowThreadProcessId(window,out pid);if(pid==profile.ProcessId&&IsWindowVisible(window)){target=window;return false;}return true;},IntPtr.Zero);
   if(target==IntPtr.Zero||!runtime.Running(profile))return false;
   if(IsIconic(target))ShowWindowAsync(target,9);
   return SetForegroundWindow(target);
  }
  [DllImport("user32.dll")]static extern bool PostMessage(IntPtr window,uint message,IntPtr a,IntPtr b);
  public static bool Close(Runtime runtime,Profile profile){if(!runtime.Running(profile))return true;using(var process=Process.GetProcessById(profile.ProcessId)){bool sent=false;EnumWindows((window,parameter)=>{uint pid;GetWindowThreadProcessId(window,out pid);if(pid==profile.ProcessId&&IsWindowVisible(window)){sent|=PostMessage(window,0x10,IntPtr.Zero,IntPtr.Zero);}return true;},IntPtr.Zero);return sent&&process.WaitForExit(12000);}}
  public static void Navigate(Runtime runtime,Store store,Profile profile,string page){if(!Pages.Contains(page)||profile.Deleting)throw new Exception("当前环境不能打开此入口");if(!runtime.Running(profile)){runtime.Launch(profile,store,page);return;}runtime.Verify();Process.Start(new ProcessStartInfo(runtime.Exe,Policy.Quote("--user-data-dir="+store.Folder(profile))+" "+Policy.Quote(page)){UseShellExecute=false,WorkingDirectory=runtime.Root});}
 }
 sealed class BatchWindow:Form {
  CheckedListBox list;public Profile[] Selected{get{return list.CheckedItems.Cast<ProfileChoice>().Select(x=>x.Profile).ToArray();}}
  sealed class ProfileChoice{public Profile Profile;public override string ToString(){return Profile.Name;}}
  public BatchWindow(Profile[] profiles,bool opening){Text=L.T(opening?"批量打开环境":"批量关闭环境");Font=Theme.Font();BackColor=Theme.Bg;ClientSize=new Size(460,510);MinimumSize=new Size(420,420);StartPosition=FormStartPosition.CenterParent;ShowInTaskbar=false;var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(22)};Controls.Add(layout);layout.RowStyles.Add(new RowStyle(SizeType.Absolute,50));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,58));var title=Theme.Label("勾选需要操作的环境，一次最多 20 个。",10,Theme.Muted);title.Dock=DockStyle.Fill;layout.Controls.Add(title);list=new CheckedListBox{Dock=DockStyle.Fill,CheckOnClick=true,BorderStyle=BorderStyle.None,BackColor=Color.White};foreach(var p in profiles)list.Items.Add(new ProfileChoice{Profile=p});layout.Controls.Add(list);var footer=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(0,12,0,0)};var start=Theme.Button("确定",true);var cancel=Theme.Button("取消");footer.Controls.AddRange(new Control[]{start,cancel});layout.Controls.Add(footer);cancel.DialogResult=DialogResult.Cancel;CancelButton=cancel;start.Click+=(s,e)=>{if(Selected.Length<1||Selected.Length>20){L.Show(this,"请选择 1–20 个环境。","批量操作",MessageBoxButtons.OK,MessageBoxIcon.Information);return;}DialogResult=DialogResult.OK;Close();};L.Apply(this);}
 }
}
