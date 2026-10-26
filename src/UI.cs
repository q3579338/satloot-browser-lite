using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SatlootLite {
 sealed class BufferedLabel:Label {public BufferedLabel(){SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint,true);}}
 static class Theme {
  public static readonly Color Ink=Color.FromArgb(27,46,49),Muted=Color.FromArgb(99,114,115),Teal=Color.FromArgb(17,113,106),Bg=Color.FromArgb(246,247,242),Line=Color.FromArgb(218,226,221),Pale=Color.FromArgb(229,240,234);
  public static Font Font(float size=10,FontStyle style=FontStyle.Regular){return new Font("Microsoft YaHei UI",size,style);}
  public static Label Label(string text,float size=10,Color? color=null){return new BufferedLabel{Text=text,Font=Font(size),ForeColor=color??Ink,AutoSize=false,BackColor=Color.Transparent,TextAlign=ContentAlignment.MiddleLeft};}
  public static Button Button(string text,bool primary=false){var b=new Button{Text=text,Height=42,Width=132,FlatStyle=FlatStyle.Flat,BackColor=primary?Teal:Color.White,ForeColor=primary?Color.White:Ink,Font=Font(10,FontStyle.Bold),Cursor=Cursors.Hand,UseVisualStyleBackColor=false,Margin=new Padding(0,0,10,0)};b.FlatAppearance.BorderColor=primary?Teal:Line;b.FlatAppearance.MouseOverBackColor=primary?Color.FromArgb(12,92,85):Pale;return b;}
  public static void Style(Control c){c.Font=Font();c.ForeColor=Ink;}
 }
 sealed class InfoRows:Control {
  public InfoRows(){DoubleBuffered=true;Font=Theme.Font(10);ForeColor=Theme.Ink;BackColor=Color.White;}
  protected override void OnTextChanged(EventArgs e){base.OnTextChanged(e);Invalidate();}
  protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);var rows=Text.Split(new[]{'\n'},StringSplitOptions.RemoveEmptyEntries);int gap=Math.Min(46,Math.Max(20,(Height-12)/Math.Max(rows.Length,1)));for(int i=0;i<rows.Length;i++)TextRenderer.DrawText(e.Graphics,rows[i],Font,new Rectangle(0,8+i*gap,Width,gap),ForeColor,TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);}
 }
 sealed class EnvList:ScrollableControl {
  public Func<Profile,bool> IsRunning;public readonly List<Profile> Items=new List<Profile>();public event EventHandler SelectedIndexChanged;int selected=-1,updating;const int ItemHeight=86;
  public int SelectedIndex{get{return selected;}set{int next=value>=0&&value<Items.Count?value:-1;if(next==selected)return;int old=selected;selected=next;if(updating==0){if(old>=0)Invalidate(ItemRectangle(old));if(next>=0)Invalidate(ItemRectangle(next));}if(SelectedIndexChanged!=null)SelectedIndexChanged(this,EventArgs.Empty);}}
  public object SelectedItem{get{return selected>=0&&selected<Items.Count?Items[selected]:null;}}
  public EnvList(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.Opaque|ControlStyles.Selectable|ControlStyles.ResizeRedraw,true);BackColor=Color.White;AutoScroll=true;TabStop=true;}
  public void BeginUpdate(){updating++;}
  public void EndUpdate(){if(updating>0)updating--;if(updating==0){AutoScrollMinSize=new Size(0,Items.Count*ItemHeight);Invalidate();}}
  Rectangle ItemRectangle(int index){return new Rectangle(0,AutoScrollPosition.Y+index*ItemHeight,ClientSize.Width,ItemHeight);}
  public int IndexFromPoint(Point point){if(point.X<0||point.Y<0||point.X>=ClientSize.Width||point.Y>=ClientSize.Height)return -1;int index=(point.Y-AutoScrollPosition.Y)/ItemHeight;return index>=0&&index<Items.Count?index:-1;}
  protected override void OnMouseDown(MouseEventArgs e){int index=IndexFromPoint(e.Location);if(index<0)return;if(e.Button==MouseButtons.Left||e.Button==MouseButtons.Right){Focus();SelectedIndex=index;}base.OnMouseDown(e);}
  protected override bool IsInputKey(Keys keyData){if(new[]{Keys.Up,Keys.Down,Keys.Home,Keys.End}.Contains(keyData&Keys.KeyCode))return true;return base.IsInputKey(keyData);}
  protected override void OnKeyDown(KeyEventArgs e){if(Items.Count>0){int next=selected;if(e.KeyCode==Keys.Down)next=Math.Min(Items.Count-1,selected+1);else if(e.KeyCode==Keys.Up)next=Math.Max(0,selected-1);else if(e.KeyCode==Keys.Home)next=0;else if(e.KeyCode==Keys.End)next=Items.Count-1;else{base.OnKeyDown(e);return;}SelectedIndex=next;var row=ItemRectangle(next);if(row.Top<0)AutoScrollPosition=new Point(0,next*ItemHeight);else if(row.Bottom>ClientSize.Height)AutoScrollPosition=new Point(0,(next+1)*ItemHeight-ClientSize.Height);e.Handled=true;}base.OnKeyDown(e);}
  protected override void OnScroll(ScrollEventArgs e){base.OnScroll(e);Invalidate();}
  protected override void OnPaint(PaintEventArgs e){e.Graphics.Clear(Color.White);for(int i=0;i<Items.Count;i++){var r=ItemRectangle(i);if(!r.IntersectsWith(e.ClipRectangle))continue;var p=Items[i];if(i==selected){using(var brush=new SolidBrush(Theme.Pale))e.Graphics.FillRectangle(brush,r);using(var brush=new SolidBrush(Theme.Teal))e.Graphics.FillRectangle(brush,new Rectangle(r.X,r.Y,4,r.Height));}
   using(var font=Theme.Font(11,FontStyle.Bold))TextRenderer.DrawText(e.Graphics,p.Name,font,new Rectangle(r.X+18,r.Y+12,r.Width-38,29),Theme.Ink,TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter);
   bool live=IsRunning!=null&&IsRunning(p);string subtitle=L.T((live?"● 已打开":"○ 未打开")+"   /   "+(p.Network=="node"?"代理线路":p.Network=="proxy"?"指定代理":p.Network=="direct"?"直接连接":"系统网络"));
   using(var font=Theme.Font(9))TextRenderer.DrawText(e.Graphics,subtitle,font,new Rectangle(r.X+18,r.Y+47,r.Width-30,24),live?Theme.Teal:Theme.Muted,TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);using(var pen=new Pen(Theme.Line))e.Graphics.DrawLine(pen,r.X+18,r.Bottom-1,r.Right-18,r.Bottom-1);
  }}
 }
 sealed class Picker:Button {
  public readonly List<object> Items=new List<object>();int selected=-1;
  public event EventHandler SelectedIndexChanged;
  public object SelectedItem {get{return selected<0?null:Items[selected];}set{SelectedIndex=Items.IndexOf(value);}}
  public int SelectedIndex {get{return selected;}set{selected=value;Text=(selected>=0?Items[selected].ToString():"")+"   ▾";if(SelectedIndexChanged!=null)SelectedIndexChanged(this,EventArgs.Empty);}}
  public Picker(){FlatStyle=FlatStyle.Flat;FlatAppearance.BorderColor=Theme.Line;BackColor=Color.White;TextAlign=ContentAlignment.MiddleLeft;Padding=new Padding(7,0,3,0);Cursor=Cursors.Hand;AutoEllipsis=true;}
  protected override void OnClick(EventArgs e){base.OnClick(e);var menu=new ContextMenuStrip{Font=Font};foreach(var value in Items){var choice=value;var item=new ToolStripMenuItem(L.T(value.ToString())){Checked=Equals(value,SelectedItem)};item.Click+=(s,ev)=>{SelectedItem=choice;L.Apply(this);};menu.Items.Add(item);}menu.Closed+=(s,ev)=>menu.Dispose();menu.Show(this,new Point(0,Height));}
 }
 sealed class Editor:Form {
  readonly Profile original;Profile candidate;
  TextBox name,home,proxy,node;Picker hardware,locale,zone,network;CheckBox canvas,webgl,audio;
  public Profile Result;public bool Preview;
  public Editor(Profile profile,bool fresh,bool preview=false,NodeStore lines=null){
   Preview=preview;original=profile.Copy();candidate=profile.Copy();Text=fresh?"新建环境 · SATLOOT Lite":"编辑环境 · SATLOOT Lite";BackColor=Theme.Bg;Font=Theme.Font();AutoScaleMode=AutoScaleMode.Dpi;
   ClientSize=new Size(650,Math.Min(700,Screen.PrimaryScreen.WorkingArea.Height-100));MinimumSize=new Size(590,560);StartPosition=FormStartPosition.CenterParent;ShowInTaskbar=false;MaximizeBox=false;
   var outer=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(24)};
   outer.RowStyles.Add(new RowStyle(SizeType.Absolute,65));outer.RowStyles.Add(new RowStyle(SizeType.Percent,100));outer.RowStyles.Add(new RowStyle(SizeType.Absolute,56));Controls.Add(outer);
   var header=new Panel{Dock=DockStyle.Fill};var title=Theme.Label(fresh?"一个环境，一份独立空间":"编辑这个环境",19);title.Dock=DockStyle.Top;title.Height=34;header.Controls.Add(title);
   var subtitle=Theme.Label("网站登录独立保存；指纹设置保存后固定。",9,Theme.Muted);subtitle.Dock=DockStyle.Bottom;subtitle.Height=26;header.Controls.Add(subtitle);outer.Controls.Add(header,0,0);
   var scroll=new Panel{Dock=DockStyle.Fill,AutoScroll=true};outer.Controls.Add(scroll,0,1);
   var fields=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=2,Padding=new Padding(0,14,8,14)};fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,100));fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));scroll.Controls.Add(fields);
   name=new TextBox{Text=profile.Name,MaxLength=60};home=new TextBox{Text=profile.Home,MaxLength=2048};proxy=new TextBox{Text=profile.Proxy,MaxLength=256};
   hardware=Combo(Policy.Presets.Cast<object>().ToArray());hardware.SelectedItem=Policy.Hardware(profile);
   locale=Combo(Policy.Locales.Cast<object>().ToArray());locale.SelectedItem=profile.Locale;zone=Combo(Policy.Zones.Cast<object>().ToArray());zone.SelectedItem=profile.Timezone;
   network=Combo(new object[]{"系统网络","直接连接","指定代理","代理线路"});network.SelectedIndex=Array.IndexOf(new[]{"system","direct","proxy","node"},profile.Network);
   Row(fields,"环境名称",name);Row(fields,"浏览器语言",locale);Row(fields,"固定时区",zone);Row(fields,"网络方式",network);Row(fields,"代理地址",proxy);
   var networkHelp=Theme.Label("本机代理可填写 http://127.0.0.1:7890。\n带认证、域名代理及 V2 节点，请从下方线路库选择。",9,Theme.Muted);networkHelp.Height=64;Row(fields,"",networkHelp,76);
   var selectLine=Theme.Button("从线路库选择");selectLine.Width=180;selectLine.Enabled=lines!=null;Row(fields,"线路库",selectLine,52);selectLine.Click+=(s,e)=>{using(var library=new LineLibraryWindow(lines,n=>{node.Text=n.Uri;candidate.NodeName=n.Name;network.SelectedIndex=3;}))library.ShowDialog(this);};
   node=new TextBox{Text=profile.NodeUri??"",MaxLength=16000,UseSystemPasswordChar=true};Row(fields,"线路链接",node);node.TextChanged+=(s,e)=>candidate.NodeName=null;var showNode=new CheckBox{Text="显示链接",AutoSize=true};showNode.CheckedChanged+=(s,e)=>node.UseSystemPasswordChar=!showNode.Checked;Row(fields,"",showNode,34);
   var nodeHelp=Theme.Label("支持 VLESS / VMess / Trojan / Shadowsocks / HTTP / SOCKS。\n线路仅作用于此环境；节点域名可能使用系统 DNS。",9,Theme.Muted);Row(fields,"",nodeHelp,72);
   proxy.Enabled=network.SelectedIndex==2;node.Enabled=network.SelectedIndex==3;network.SelectedIndexChanged+=(s,e)=>{proxy.Enabled=network.SelectedIndex==2;node.Enabled=network.SelectedIndex==3;};
   Row(fields,"启动首页",home);var homeHelp=Theme.Label("可留空。重开时恢复上次标签；指定首页会同时打开。",9,Theme.Muted);Row(fields,"",homeHelp,40);
   Row(fields,"硬件组合",hardware);var random=Theme.Button("重新生成指纹");random.Width=170;random.Click+=(s,e)=>{Policy.Randomize(candidate);hardware.SelectedItem=Policy.Hardware(candidate);};Row(fields,"",random,52);
   var checks=new FlowLayoutPanel{Height=40,WrapContents=false};canvas=new CheckBox{Text="画布",Checked=profile.Canvas,Width=82};webgl=new CheckBox{Text="WebGL",Checked=profile.Webgl,Width=95};audio=new CheckBox{Text="短离线音频",Checked=profile.Audio,Width=130};checks.Controls.AddRange(new Control[]{canvas,webgl,audio});Row(fields,"读出保护",checks,45);
   var note=Theme.Label("固定字体与屏幕保护默认启用。硬件参数是网站可读取的信息，不改变电脑实际性能；不承诺完全防关联。",9,Theme.Muted);Row(fields,"",note,76);
   var footer=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(0,12,0,0)};var save=Theme.Button("保存环境",true);var cancel=Theme.Button("取消");footer.Controls.Add(save);footer.Controls.Add(cancel);outer.Controls.Add(footer,0,2);
   cancel.DialogResult=DialogResult.Cancel;CancelButton=cancel;AcceptButton=save;
   save.Click+=(s,e)=>{try{var next=candidate.Copy();next.Name=name.Text.Trim();next.Home=home.Text.Trim();next.Proxy=proxy.Text.Trim();next.NodeUri=node.Text.Trim();next.Locale=(string)locale.SelectedItem;next.Timezone=(string)zone.SelectedItem;next.Preset=((Preset)hardware.SelectedItem).Id;next.Network=new[]{"system","direct","proxy","node"}[network.SelectedIndex];next.Canvas=canvas.Checked;next.Webgl=webgl.Checked;next.Audio=audio.Checked;Policy.Validate(next);
    if(!fresh&&(next.Seed!=original.Seed||next.Preset!=original.Preset||next.Timezone!=original.Timezone||next.Locale!=original.Locale||next.Canvas!=original.Canvas||next.Webgl!=original.Webgl||next.Audio!=original.Audio)){
     if(L.Show(this,"这会改变网站看到的设备信息。已有网站数据保留，网站可能要求重新登录。\n\n确认保存这些修改？","修改环境指纹",MessageBoxButtons.OKCancel,MessageBoxIcon.Information)!=DialogResult.OK)return;}
    Result=next;DialogResult=DialogResult.OK;Close();
   }catch(Exception ex){L.Show(this,ex.Message,"未保存",MessageBoxButtons.OK,MessageBoxIcon.Warning);}};L.Apply(this);
  }
  public void FocusNetwork(bool v2){if(v2)network.SelectedIndex=3;Shown+=(s,e)=>{if(v2)node.Focus();else network.Focus();};L.Apply(this);}
  static Picker Combo(object[] values){var c=new Picker();c.Items.AddRange(values);c.SelectedIndex=0;return c;}
  static void Row(TableLayoutPanel panel,string label,Control control,int height=48){int row=panel.RowCount++;panel.RowStyles.Add(new RowStyle(SizeType.Absolute,height));var l=Theme.Label(label,9,Theme.Muted);l.Dock=DockStyle.Fill;panel.Controls.Add(l,0,row);control.Dock=DockStyle.Fill;control.Margin=new Padding(0,7,0,7);panel.Controls.Add(control,1,row);}
 }
 sealed class AboutWindow:Form {
  public AboutWindow(){Text="关于与更新 · SATLOOT Lite";Font=Theme.Font();BackColor=Theme.Bg;ClientSize=new Size(540,410);StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;ShowInTaskbar=false;
   var content=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,Padding=new Padding(28)};Controls.Add(content);
   foreach(int height in new[]{58,80,92,76,48})content.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
   var title=Theme.Label("SATLOOT Lite",23,Theme.Teal);title.Dock=DockStyle.Fill;content.Controls.Add(title);
   var version=Theme.Label("开发：SATLOOT\n管理器 0.3.1  ·  Windows x64\n指纹内核：Chromium 156.0.8078.48",10);version.Dock=DockStyle.Fill;content.Controls.Add(version);
   var update=Theme.Label("更新\n当前为本地预览版，尚未接入在线更新。\n这里没有进行联网检查，不代表已经是最新版本。",10,Theme.Muted);update.Dock=DockStyle.Fill;content.Controls.Add(update);
   var service=new LinkLabel{Text="https://browser.satloot.com/",Dock=DockStyle.Fill,Font=Theme.Font(),LinkColor=Theme.Teal};service.LinkClicked+=(s,e)=>Process.Start(new ProcessStartInfo(service.Text){UseShellExecute=true});content.Controls.Add(service);
   var close=Theme.Button("关闭",true);close.Anchor=AnchorStyles.Right;close.Click+=(s,e)=>Close();content.Controls.Add(close);CancelButton=close;L.Apply(this);
  }
 }
 sealed class MainWindow:Form {
  readonly Store store;readonly Runtime runtime;readonly bool preview;readonly NodeStore lines;
  EnvList list;TextBox search;Label count,status,detailTitle,detailSub,empty,foot;InfoRows summary;Button open,edit,clone,delete,createEmpty;Panel body;bool busy,refreshing;string runningSignature="";MenuStrip menu;
  protected override CreateParams CreateParams{get{var cp=base.CreateParams;cp.ExStyle|=0x02000000;return cp;}}
  public MainWindow(Store store,Runtime runtime,bool preview=false){
   this.store=store;this.runtime=runtime;this.preview=preview;lines=new NodeStore(store.Root,store.Test);DoubleBuffered=true;
   Text="SATLOOT Lite · 本地预览版";BackColor=Theme.Bg;Font=Theme.Font();AutoScaleMode=AutoScaleMode.Dpi;ClientSize=new Size(1080,Math.Min(720,Screen.PrimaryScreen.WorkingArea.Height-80));MinimumSize=SizeFromClientSize(new Size(960,640));StartPosition=FormStartPosition.CenterScreen;
   var shell=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Padding=new Padding(0)};shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,292));shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));Controls.Add(shell);
   var sidebar=new TableLayoutPanel{Dock=DockStyle.Fill,BackColor=Color.White,ColumnCount=1,RowCount=5,Padding=new Padding(20,22,20,18)};
   foreach(int rowHeight in new[]{65,48,50,35})sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute,rowHeight));sidebar.RowStyles.Add(new RowStyle(SizeType.Percent,100));shell.Controls.Add(sidebar,0,0);
   var brand=Theme.Label("SATLOOT Lite",19,Theme.Teal);brand.Dock=DockStyle.Fill;sidebar.Controls.Add(brand,0,0);
   var add=Theme.Button("＋  新建环境",true);add.Dock=DockStyle.Fill;add.Margin=new Padding(0,0,0,7);add.Click+=(s,e)=>Create();sidebar.Controls.Add(add,0,1);
   search=new TextBox{Dock=DockStyle.Fill,Margin=new Padding(0,10,0,8)};search.TextChanged+=(s,e)=>RefreshList();sidebar.Controls.Add(search,0,2);
   count=Theme.Label("环境",9,Theme.Muted);count.Dock=DockStyle.Fill;sidebar.Controls.Add(count,0,3);
   list=new EnvList{Dock=DockStyle.Fill,IsRunning=p=>!preview&&runtime.Running(p)};list.SelectedIndexChanged+=(s,e)=>{if(!refreshing)Details();};sidebar.Controls.Add(list,0,4);
   list.MouseDown+=(s,e)=>{if(e.Button==MouseButtons.Right){int index=list.IndexFromPoint(e.Location);if(index>=0)list.SelectedIndex=index;}};
   var context=new ContextMenuStrip{Font=Theme.Font(9)};context.Opening+=(s,e)=>{if(Selected==null||busy){e.Cancel=true;return;}context.Items.Clear();context.Items.Add(Item("打开浏览器",async()=>await Open()));context.Items.Add(Item("关闭当前浏览器",async()=>await CloseBrowser()));context.Items.Add(Item("为当前环境选择线路",ChooseLine));context.Items.Add(Item("编辑环境",Edit));context.Items.Add(Item("重命名",Rename));context.Items.Add(Item("复制配置",Clone));context.Items.Add(new ToolStripSeparator());context.Items.Add(Item("备份当前环境",async()=>await ExportBackup()));context.Items.Add(Item("删除环境",async()=>await Delete()));};list.ContextMenuStrip=context;
   KeyPreview=true;KeyDown+=(s,e)=>{if(e.Control&&e.KeyCode==Keys.N){e.SuppressKeyPress=true;Create();}else if(e.Control&&e.KeyCode==Keys.F){e.SuppressKeyPress=true;search.Focus();}else if(e.KeyCode==Keys.F2&&list.Focused){e.SuppressKeyPress=true;Rename();}};
   body=new Panel{Dock=DockStyle.Fill,Padding=new Padding(32,20,32,16)};shell.Controls.Add(body,1,0);
   var layout=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=5,ColumnCount=1};layout.RowStyles.Add(new RowStyle(SizeType.Absolute,90));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,46));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,66));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,55));body.Controls.Add(layout);
   var hero=new Panel{Dock=DockStyle.Fill};var h=Theme.Label("浏览器环境",23);h.Dock=DockStyle.Top;h.Height=51;hero.Controls.Add(h);var desc=Theme.Label("管理独立环境、指纹和网络。",10,Theme.Muted);desc.Dock=DockStyle.Bottom;desc.Height=30;hero.Controls.Add(desc);layout.Controls.Add(hero,0,0);
   status=Theme.Label(preview?"本地预览版  /  Chromium 156  /  原生指纹":"正在后台校验浏览器组件…",9,Theme.Teal);status.Dock=DockStyle.Fill;layout.Controls.Add(status,0,1);
   var card=new Panel{Dock=DockStyle.Fill,BackColor=Color.White,Padding=new Padding(18)};layout.Controls.Add(card,0,2);
   var content=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=4,ColumnCount=1};content.RowStyles.Add(new RowStyle(SizeType.Absolute,44));content.RowStyles.Add(new RowStyle(SizeType.Absolute,34));content.RowStyles.Add(new RowStyle(SizeType.Percent,100));content.RowStyles.Add(new RowStyle(SizeType.Absolute,55));card.Controls.Add(content);
   detailTitle=Theme.Label("从第一个环境开始",20);detailTitle.Dock=DockStyle.Fill;content.Controls.Add(detailTitle,0,0);
   detailSub=Theme.Label("每个环境拥有独立的网站数据目录",9,Theme.Muted);detailSub.Dock=DockStyle.Fill;content.Controls.Add(detailSub,0,1);
   summary=new InfoRows();summary.Dock=DockStyle.Fill;content.Controls.Add(summary,0,2);
   empty=Theme.Label("点击左侧「新建环境」，设置名称后即可开始。\n环境不会读取或导入你现有的浏览器资料。",10,Theme.Muted);empty.Dock=DockStyle.Fill;content.Controls.Add(empty,0,3);
   var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(0,14,0,0),WrapContents=false};open=Theme.Button("打开浏览器",true);edit=Theme.Button("编辑环境");clone=Theme.Button("复制配置");delete=Theme.Button("删除环境");delete.ForeColor=Color.FromArgb(162,54,49);edit.Width=112;clone.Width=112;delete.Width=112;createEmpty=Theme.Button("＋ 新建环境",true);createEmpty.Width=170;actions.Controls.AddRange(new Control[]{open,edit,clone,delete,createEmpty});layout.Controls.Add(actions,0,3);
   open.Click+=async(s,e)=>await Open();edit.Click+=(s,e)=>Edit();clone.Click+=(s,e)=>Clone();delete.Click+=async(s,e)=>await Delete();createEmpty.Click+=(s,e)=>Create();
   foot=Theme.Label("预览版仅在本机使用。关闭管理器后，已打开的浏览器仍可继续使用。",9,Theme.Muted);foot.Dock=DockStyle.Fill;layout.Controls.Add(foot,0,4);
   menu=new MenuStrip{Dock=DockStyle.Top,Font=Theme.Font(9),BackColor=Color.White,Padding=new Padding(14,5,0,5)};Controls.Add(menu);MainMenuStrip=menu;BuildMenu();RefreshList();L.Apply(this);if(!preview)Shown+=async(s,e)=>await Verify();
   runningSignature=LiveSignature();var timer=new System.Windows.Forms.Timer{Interval=1000};timer.Tick+=(s,e)=>PollRunning();if(!preview){timer.Start();Activated+=(s,e)=>PollRunning();}FormClosed+=(s,e)=>timer.Dispose();FormClosing+=(s,e)=>{if(busy)e.Cancel=true;};
  }
  Profile Selected {get{return list.SelectedItem as Profile;}}
  void ImportLines(bool subscription){if(busy)return;using(var dialog=new LineImportWindow(lines,subscription))dialog.ShowDialog(this);}
  void ChooseLine(){var p=Selected;if(p==null||busy||p.Deleting)return;try{runtime.AssertClosed(p,store.Folder(p));using(var dialog=new LineLibraryWindow(lines,n=>{runtime.AssertClosed(p,store.Folder(p));var next=p.Copy();next.Network="node";next.NodeUri=n.Uri;next.NodeName=n.Name;store.Save(next);RefreshList(next.Id);}))dialog.ShowDialog(this);}catch(Exception ex){Error(ex);}}
  void Rename(){var p=Selected;if(p==null||busy||p.Deleting)return;string name=TextPrompt.Ask(this,"重命名环境",p.Name);if(name==null)return;try{var next=p.Copy();next.Name=name.Trim();store.Save(next);RefreshList(next.Id);}catch(Exception ex){Error(ex);}}
  void CreateBatch(){if(busy)return;string value=TextPrompt.Ask(this,"批量新建：输入数量（1–50）","5");if(value==null)return;int count;if(!int.TryParse(value,out count)||count<1||count>50){Error(new Exception("请填写 1–50 的整数"));return;}try{var next=store.Profiles.ToList();for(int i=0;i<count;i++){var p=Policy.Create();p.Name="环境 "+(next.Count+1).ToString("00");next.Add(p);}store.Commit(next);search.Clear();RefreshList(next.Last().Id);}catch(Exception ex){Error(ex);}}
  async Task Batch(bool opening){if(busy)return;var available=store.Profiles.Where(p=>!p.Deleting&&(opening?!runtime.Running(p):runtime.Running(p))).ToArray();if(available.Length==0){L.Show(this,opening?"没有可打开的环境。":"没有正在运行的环境。","批量操作",MessageBoxButtons.OK,MessageBoxIcon.Information);return;}Profile[] selected;using(var dialog=new BatchWindow(available,opening)){if(dialog.ShowDialog(this)!=DialogResult.OK)return;selected=dialog.Selected;}busy=true;UpdateButtons();int done=0;var failures=new System.Collections.Generic.List<string>();try{foreach(var p in selected){status.Text=L.T(opening?"正在打开：":"正在关闭：")+p.Name;try{if(opening)await Task.Run(()=>runtime.Launch(p,store));else if(!await Task.Run(()=>BrowserActions.Close(runtime,p)))throw new Exception("浏览器仍在等待确认，请在窗口中处理");done++;}catch{failures.Add(p.Name);}}RefreshList();status.Text=L.T("已完成：")+done+" / "+selected.Length;if(failures.Count>0)L.Show(this,"以下环境未完成，请检查浏览器窗口或单独重试：\n"+string.Join("\n",failures),"批量操作",MessageBoxButtons.OK,MessageBoxIcon.Information);}finally{busy=false;UpdateButtons();L.Apply(this);}}
  async Task Navigate(string page){var p=Selected;if(p==null||busy||p.Deleting)return;busy=true;UpdateButtons();try{await Task.Run(()=>BrowserActions.Navigate(runtime,store,p,page));RefreshList(p.Id);}catch(Exception ex){Error(ex);}finally{busy=false;UpdateButtons();L.Apply(this);}}
  async Task CloseBrowser(){var p=Selected;if(p==null||busy||!runtime.Running(p))return;busy=true;UpdateButtons();try{if(!await Task.Run(()=>BrowserActions.Close(runtime,p)))L.Show(this,"浏览器仍在等待确认，请在浏览器窗口中处理下载或关闭提示。","关闭浏览器",MessageBoxButtons.OK,MessageBoxIcon.Information);RefreshList(p.Id);}finally{busy=false;UpdateButtons();L.Apply(this);}}
  async Task ExportBackup(){var p=Selected;if(p==null||busy||p.Deleting)return;try{runtime.AssertClosed(p,store.Folder(p));}catch(Exception ex){Error(ex);return;}using(var dialog=new SaveFileDialog{Filter="本机加密备份 (*.slb)|*.slb",FileName="SATLOOT-"+DateTime.Now.ToString("yyyyMMdd-HHmm")+".slb",Title="本机加密备份（仅同一 Windows 账户可恢复）"}){if(dialog.ShowDialog(this)!=DialogResult.OK)return;busy=true;UpdateButtons();status.Text=L.T("正在备份环境数据…");try{await Task.Run(()=>Backups.Export(store,p,dialog.FileName,runtime.AssertClosed));status.Text=L.T("备份已保存；恢复时需要同一 Windows 账户。");}catch(Exception ex){Error(ex);}finally{busy=false;UpdateButtons();L.Apply(this);}}}
  async Task ImportBackup(){if(busy)return;using(var dialog=new OpenFileDialog{Filter="本机加密备份 (*.slb)|*.slb",Title="恢复本机备份（创建新环境）"}){if(dialog.ShowDialog(this)!=DialogResult.OK)return;busy=true;UpdateButtons();status.Text=L.T("正在恢复环境数据…");try{var p=await Task.Run(()=>Backups.Import(store,dialog.FileName));search.Clear();RefreshList(p.Id);status.Text=L.T("备份已恢复为新环境，已有环境保留。");}catch(Exception ex){Error(ex);}finally{busy=false;UpdateButtons();L.Apply(this);}}}
  ToolStripMenuItem Item(string label,Action click){var item=new ToolStripMenuItem(L.T(label));item.Click+=(s,e)=>click();return item;}
  void BuildMenu(){menu.Items.Clear();var environments=new ToolStripMenuItem(L.T("环境"));environments.DropDownItems.Add(Item("新建环境",Create));environments.DropDownItems.Add(Item("编辑环境",Edit));environments.DropDownItems.Add(Item("复制配置",Clone));environments.DropDownItems.Add(Item("删除环境",async()=>await Delete()));
   environments.DropDownItems.Add(new ToolStripSeparator());environments.DropDownItems.Add(Item("重命名",Rename));environments.DropDownItems.Add(Item("批量新建",CreateBatch));environments.DropDownItems.Add(Item("批量打开",async()=>await Batch(true)));environments.DropDownItems.Add(Item("批量关闭",async()=>await Batch(false)));
   var networkMenu=new ToolStripMenuItem(L.T("网络"));networkMenu.DropDownItems.Add(Item("线路库",()=>{if(busy)return;using(var dialog=new LineLibraryWindow(lines))dialog.ShowDialog(this);}));networkMenu.DropDownItems.Add(Item("导入线路",()=>ImportLines(false)));networkMenu.DropDownItems.Add(Item("订阅导入",()=>ImportLines(true)));networkMenu.DropDownItems.Add(new ToolStripSeparator());networkMenu.DropDownItems.Add(Item("为当前环境选择线路",ChooseLine));networkMenu.DropDownItems.Add(Item("当前环境网络设置",()=>EditNetwork(false)));
   var browserMenu=new ToolStripMenuItem(L.T("浏览器"));string[] names={"扩展管理","书签管理","历史记录","下载记录","浏览器设置"};for(int i=0;i<names.Length;i++){string page=BrowserActions.Pages[i];browserMenu.DropDownItems.Add(Item(names[i],async()=>await Navigate(page)));}browserMenu.DropDownItems.Add(new ToolStripSeparator());browserMenu.DropDownItems.Add(Item("关闭当前浏览器",async()=>await CloseBrowser()));
   var settings=new ToolStripMenuItem(L.T("设置"));var languages=new ToolStripMenuItem(L.T("界面语言"));foreach(var pair in new[]{new[]{"zh-CN","简体中文"},new[]{"zh-TW","繁體中文"},new[]{"en-US","English"}}){string code=pair[0];var item=Item(pair[1],()=>{if(busy)return;L.Save(store.Root,code);BuildMenu();Details();L.Apply(this);list.Invalidate();});item.Checked=L.Language==code;languages.DropDownItems.Add(item);}settings.DropDownItems.Add(languages);settings.DropDownItems.Add(Item("打开数据文件夹",()=>Process.Start(new ProcessStartInfo("explorer.exe",Policy.Quote(store.Root)){UseShellExecute=true})));
   var backups=new ToolStripMenuItem(L.T("备份"));backups.DropDownItems.Add(Item("备份当前环境",async()=>await ExportBackup()));backups.DropDownItems.Add(Item("恢复本机备份",async()=>await ImportBackup()));backups.DropDownItems.Add(Item("备份说明",()=>L.Show(this,"备份包含环境设置与网站数据，不含缓存，最多 256 MB。\n备份使用当前 Windows 账户加密，只能在同一账户解密恢复。\n恢复创建新环境，保留原环境；本版暂不提供跨电脑登录状态迁移。","本机备份",MessageBoxButtons.OK,MessageBoxIcon.Information)));
   menu.Items.AddRange(new ToolStripItem[]{environments,networkMenu,browserMenu,backups,settings,Item("产品主页",()=>Process.Start(new ProcessStartInfo("https://browser.satloot.com/"){UseShellExecute=true})),Item("关于与更新",()=>{using(var dialog=new AboutWindow())dialog.ShowDialog(this);}),Item("推广",()=>Process.Start(new ProcessStartInfo("https://browser.satloot.com/#promotion"){UseShellExecute=true}))});
  }
  void EditNetwork(bool v2){if(busy)return;var p=Selected;if(p==null){L.Show(this,"尚未选择环境，请先新建或选中一个环境。","网络",MessageBoxButtons.OK,MessageBoxIcon.Information);return;}if(p.Deleting)return;if(runtime.Running(p)){L.Show(this,"环境已打开，请先正常关闭浏览器。","网络",MessageBoxButtons.OK,MessageBoxIcon.Information);return;}try{using(var editor=new Editor(p,false,false,lines)){editor.FocusNetwork(v2);if(editor.ShowDialog(this)==DialogResult.OK){store.Save(editor.Result);RefreshList(p.Id);}}}catch(Exception e){Error(e);}}
  void PollRunning(){if(busy||preview)return;string next=LiveSignature();if(next==runningSignature)return;runningSignature=next;list.Invalidate();Details();}
  string LiveSignature(){return string.Join("|",store.Profiles.Select(p=>p.Id+":"+runtime.Running(p)));}
  void RefreshList(string id=null){runningSignature=LiveSignature();id=id??(Selected==null?null:Selected.Id);refreshing=true;list.BeginUpdate();try{list.Items.Clear();foreach(var p in store.Profiles.Where(x=>x.Name.IndexOf(search.Text,StringComparison.CurrentCultureIgnoreCase)>=0))list.Items.Add(p);count.Text="环境  "+store.Profiles.Count+"    ·    输入上方文字筛选";int index=-1;for(int i=0;i<list.Items.Count;i++)if(((Profile)list.Items[i]).Id==id)index=i;if(index<0&&list.Items.Count>0)index=0;list.SelectedIndex=index;}finally{list.EndUpdate();refreshing=false;}Details();}
  void Details(){body.SuspendLayout();try{var p=Selected;detailTitle.Tag=p==null?null:"user";if(p==null){bool noEnvironments=store.Profiles.Count==0;detailTitle.Text=noEnvironments?"还没有环境":"没有匹配的环境";detailSub.Text=noEnvironments?"新建一个环境，开始浏览":"试试其他名称，或清空左侧搜索内容";summary.Text=noEnvironments?"1. 点击「新建环境」\n2. 设置名称、指纹和网络\n3. 保存后打开浏览器":"你已有的环境仍保存在本机。";empty.Text=noEnvironments?"每个环境独立保存网站登录和本地数据。":"也可以点击下方按钮新建一个环境。";}else{
    var h=Policy.Hardware(p);detailTitle.Text=p.Name;detailSub.Text="固定指纹  ·  本机独立保存  ·  "+(runtime.Running(p)?"浏览器已打开":"随时开始浏览");
    summary.Text="显卡       "+h.Label+"\n\n硬件       "+h.Cpu+" 线程   /   "+h.Memory+" GB   /   "+h.Width+" × "+h.Height+"\n\n语言       "+p.Locale+"     时区  "+p.Timezone+"\n\n网络       "+(p.Network=="node"?(string.IsNullOrEmpty(p.NodeName)?"代理线路":p.NodeName):p.Network=="proxy"?p.Proxy:p.Network=="direct"?"直接连接":"跟随 Windows 系统网络");
    var flags=new List<string>();if(p.Canvas)flags.Add("画布");if(p.Webgl)flags.Add("WebGL");if(p.Audio)flags.Add("短音频");empty.Text="读出保护："+(flags.Count==0?"已关闭":string.Join("、",flags))+"\n固定字体与屏幕保护已启用。真实绘图能力保持原生。";
    if(p.Deleting){detailSub.Text="删除未完成 · 请重试删除";empty.Text="部分文件可能已删除，该环境暂不能打开。\n关闭占用文件的程序后，再点「删除环境」。";}
   }UpdateButtons();L.Apply(this);}finally{body.ResumeLayout(false);}}
  void UpdateButtons(){bool has=Selected!=null,live=has&&runtime.Running(Selected),pending=has&&Selected.Deleting;open.Visible=edit.Visible=clone.Visible=delete.Visible=has;createEmpty.Visible=!has;createEmpty.Enabled=!busy;open.Enabled=has&&!busy&&runtime.Verified&&!pending;open.Text=live?"返回浏览器":"打开浏览器";edit.Enabled=has&&!busy&&!live&&!pending;clone.Enabled=has&&!busy&&!pending;delete.Enabled=has&&!busy&&!live;}
  async Task Delete(){var p=Selected;if(busy||p==null)return;
   if(L.Show(this,"永久删除「"+p.Name+"」？\n\n将删除这个环境的设置、Cookie、网站登录和环境目录内的数据。\n不经过回收站，无法撤销。环境目录之外的下载文件不受影响。","删除环境",MessageBoxButtons.OKCancel,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.OK)return;
   busy=true;UpdateButtons();status.Text="正在永久删除这个环境…";
   try{await Task.Run(()=>store.DeletePermanent(p.Id,runtime.AssertClosed));RefreshList();status.Text="已永久删除「"+p.Name+"」，未移入回收站。";}
   catch(Exception e){RefreshList(p.Id);status.Text="删除未完成："+e.Message;Error(e);}finally{busy=false;UpdateButtons();L.Apply(this);}
  }
  async Task Verify(){busy=true;UpdateButtons();try{await Task.Run(()=>runtime.Verify());status.Text="内核校验通过  /  Chromium "+runtime.Version+"  /  原生指纹";}catch(Exception e){status.Text="内核不可用："+e.Message;status.ForeColor=Color.Firebrick;}finally{busy=false;UpdateButtons();L.Apply(this);}}
  void Create(){if(busy)return;try{var p=Policy.Create();p.Name="环境 "+(store.Profiles.Count+1).ToString("00");using(var editor=new Editor(p,true,false,lines)){if(editor.ShowDialog(this)==DialogResult.OK){store.Save(editor.Result);search.Text="";RefreshList(editor.Result.Id);}}}catch(Exception e){Error(e);}}
  void Edit(){var p=Selected;if(busy||p==null||p.Deleting||runtime.Running(p))return;try{using(var editor=new Editor(p,false,false,lines)){if(editor.ShowDialog(this)==DialogResult.OK){store.Save(editor.Result);RefreshList(p.Id);}}}catch(Exception e){Error(e);}}
  void Clone(){var p=Selected;if(busy||p==null||p.Deleting)return;try{var copy=p.Copy();copy.Id=Guid.NewGuid().ToString();copy.Name=(p.Name.Length>52?p.Name.Substring(0,52):p.Name)+" · 副本";copy.ProcessId=0;copy.ProcessStart=0;copy.LastOpened=null;copy.Created=DateTime.UtcNow.ToString("o");Policy.Randomize(copy);using(var editor=new Editor(copy,true,false,lines)){if(editor.ShowDialog(this)==DialogResult.OK){store.Save(editor.Result);search.Text="";RefreshList(editor.Result.Id);status.Text="已复制配置并生成新指纹；网站登录数据未复制。";}}}catch(Exception e){Error(e);}}
  async Task Open(){var p=Selected;if(p==null||busy)return;if(runtime.Running(p)){if(!BrowserActions.ReturnToBrowser(runtime,p))L.Show(this,"浏览器进程仍在运行，但暂时无法返回窗口。请检查任务栏或等待浏览器完成退出。","浏览器状态",MessageBoxButtons.OK,MessageBoxIcon.Information);RefreshList(p.Id);return;}busy=true;UpdateButtons();status.Text="正在检查并打开这个环境…";try{var next=await Task.Run(()=>runtime.Launch(p,store));RefreshList(next.Id);status.Text="已发出启动请求。请在独立的浏览器窗口中继续。";}catch(Exception e){Error(e);status.Text="未完成打开；原环境数据保留。";}finally{busy=false;UpdateButtons();L.Apply(this);}}
  void Error(Exception e){L.Show(this,e.Message,"操作未完成",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
 }
 static class Program {
  [STAThread] static int Main(string[] args){
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
   try{
    string package=AppDomain.CurrentDomain.BaseDirectory;
#if QA
    package=Path.GetFullPath(Path.Combine(package,"..","preview"));
#endif
    if(args.Length>0){
     if(args[0]=="--network-watch"&&args.Length==5)return Network.Watch(package,args);
#if QA
     if(args[0]=="--self-test"&&(args.Length==2||args.Length==3))return Checks.Run(args[1],args.Length==3?Path.GetFullPath(args[2]):package);if(args[0]=="--render-preview"&&(args.Length==2||args.Length==3))return Checks.Render(args[1],args.Length==3?Path.GetFullPath(args[2]):package);
#endif
     throw new Exception("不支持的启动参数");}
    bool owned;using(var mutex=new Mutex(true,"Local\\SATLOOT.Lite.Preview."+System.Security.Principal.WindowsIdentity.GetCurrent().User.Value,out owned)){
     if(!owned){MessageBox.Show("轻量管理器已经打开，请使用现有窗口。","SATLOOT Lite");return 0;}
     var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SATLOOT Browser Lite Preview");
     L.Load(root);var store=new Store(root,false);Application.Run(new MainWindow(store,new Runtime(package)));return 0;
    }
   }catch(Exception ex){if(args.Length>0){if(args.Length>1&&Directory.Exists(args[1]))File.WriteAllText(Path.Combine(args[1],"error.txt"),ex.ToString());return 1;}MessageBox.Show(ex.Message+"\n\n原文件已保留。","SATLOOT Lite 未能启动",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
  }
 }
}
