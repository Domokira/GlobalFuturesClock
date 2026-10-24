using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using System.Globalization;

public class Session { public string start; public string end; public string days; public bool night; }
public class Market { public string id; public string product; public string exchange; public string name; public string code; public string zone; public string region; public bool precious; public bool china; public string color; public string note; public string source; public Session[] sessions; }
public class Data { public string checkedDate; public Market[] markets; public Market[] catalog; public string[] holidays; }
public class Span { public DateTime start,end; }
public class Pref { public int x=80,y=80; public bool top=true,compact=false; public int filter=0; public string[] selected; public bool rotate=true,quotes=true,cards=true,notes=true,ntp=true,timeline=false,futures=true; public int zone=0; }
public static class Engine {
 public static Data data;
 static Dictionary<Market,Tuple<DateTime,List<Span>>> cache=new Dictionary<Market,Tuple<DateTime,List<Span>>>();
 public static TimeZoneInfo BJ=TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
 public static DateTime B(DateTime utc){return TimeZoneInfo.ConvertTimeFromUtc(utc,BJ);}
 public static bool Week(DateTime d){return d.DayOfWeek!=DayOfWeek.Saturday && d.DayOfWeek!=DayOfWeek.Sunday;}
 public static bool Holiday(DateTime d){return data.holidays.Contains(d.ToString("yyyy-MM-dd"));}
 public static DateTime U(DateTime d,string z){return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(d,DateTimeKind.Unspecified),TimeZoneInfo.FindSystemTimeZoneById(z));}
 public static List<Span> Intervals(Market m,DateTime utc){
  var list=new List<Span>(); var date=TimeZoneInfo.ConvertTimeFromUtc(utc,TimeZoneInfo.FindSystemTimeZoneById(m.zone)).Date;
  Tuple<DateTime,List<Span>> saved;if(cache.TryGetValue(m,out saved)&&saved.Item1==date)return saved.Item2;
  for(int i=-2;i<=18;i++){var d=date.AddDays(i); foreach(var s in m.sessions){
   if(!s.days.Contains(((int)d.DayOfWeek).ToString()))continue;
   if(m.china && Holiday(d))continue;
   if(m.china && s.night){var next=d.AddDays(1);while(!Week(next))next=next.AddDays(1);if(Holiday(next))continue;}
   var begin=TimeSpan.Parse(s.start);var end=TimeSpan.Parse(s.end);
   if(m.id=="mcx")end=TimeSpan.Parse(TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time").IsDaylightSavingTime(DateTime.SpecifyKind(d.AddHours(12),DateTimeKind.Unspecified))?"23:30":"23:55");
   var a=d.Add(begin);var b=d.Add(end);if(end<=begin)b=b.AddDays(1);
   list.Add(new Span{start=U(a,m.zone),end=U(b,m.zone)});
  }}list=list.OrderBy(s=>s.start).ToList();cache[m]=Tuple.Create(date,list);return list;
 }
 public static bool Open(Market m,DateTime u){return Intervals(m,u).Any(s=>u>=s.start&&u<s.end);}
 public static string Countdown(Market m,DateTime u){var ss=Intervals(m,u);var active=ss.FirstOrDefault(s=>u>=s.start&&u<s.end);var next=ss.FirstOrDefault(s=>s.start>u);if(active==null&&next==null)return "暂无后续时段";var t=(active!=null?active.end:next.start)-u;return (active!=null?"距本段结束 ":"距下段开始 ")+((int)t.TotalHours).ToString("00")+":"+t.Minutes.ToString("00")+":"+t.Seconds.ToString("00");}
 public static string Today(Market m,DateTime u){var start=U(B(u).Date,"China Standard Time");var end=start.AddDays(1);var ss=Intervals(m,u).Where(s=>s.end>start&&s.start<end);return String.Join("  /  ",ss.Select(s=>(s.start<start?"00:00":B(s.start).ToString("HH:mm"))+"–"+(s.end>=end?"24:00":B(s.end).ToString("HH:mm"))).ToArray());}
 public static string Coverage(Market m,DateTime u){return m.china&&B(u).Year==2026?"已计入2026内地休市日；临时调整及到期日除外":"常规周历；节假日、提前收盘及到期日须核对公告";}
}
public class Surface:Panel { public Surface(){DoubleBuffered=true;ResizeRedraw=true;} }
public partial class Clock:Form {
 Color bg=Color.FromArgb(12,19,31),panel=Color.FromArgb(20,31,47),muted=Color.FromArgb(141,160,181),gold=Color.FromArgb(240,190,91);
 Surface canvas=new Surface(); FlowLayoutPanel bar=new FlowLayoutPanel(); ComboBox filter=new ComboBox(); Button paging,mode,choose; NotifyIcon tray; Timer timer=new Timer(); Pref pref=new Pref(); int page=0; bool timeline=false; string home=AppDomain.CurrentDomain.BaseDirectory; List<Tuple<Rectangle,Market>> hits=new List<Tuple<Rectangle,Market>>();
 public Clock(){
  Text="全球期货交易时钟 · v1.4"; Font=new Font("Microsoft YaHei UI",9); BackColor=bg;ForeColor=Color.White;AutoScaleMode=AutoScaleMode.None;MinimumSize=new Size(480,570);Size=new Size(1120,780);StartPosition=FormStartPosition.Manual;
  try{pref=new JavaScriptSerializer().Deserialize<Pref>(File.ReadAllText(Path.Combine(home,"settings.json")));}catch{}
  if(pref==null)pref=new Pref(); timeline=pref.timeline; if(pref.selected==null)pref.selected=new string[]{"AU","AG","JM","LC","LH","PX","IM","IC"};
  Location=new Point(pref.x,pref.y);if(!Screen.AllScreens.Any(s=>s.WorkingArea.IntersectsWith(Bounds)))Location=new Point(50,50); TopMost=pref.top;
  bar.Dock=DockStyle.Top;bar.Height=49;bar.Padding=new Padding(12,8,0,0);bar.BackColor=panel;bar.WrapContents=false;
  filter.DropDownStyle=ComboBoxStyle.DropDownList;filter.Width=156;filter.Items.AddRange(new object[]{"1 · 黄金交易时间","2 · 国内重点品种","3 · 自选期货"});filter.SelectedIndex=Math.Max(0,Math.Min(2,pref.filter));filter.SelectedIndexChanged+=(s,e)=>{page=0;nextQuotes=DateTime.MinValue;RefreshClock();};bar.Controls.Add(filter);
  paging=Btn("下一组",()=>{page++;RefreshClock();});
  Btn("置顶",()=>{TopMost=!TopMost;RefreshClock();});mode=Btn("小挂件",()=>{pref.compact=!pref.compact;ApplySize();});choose=Btn("选择品种",()=>ChooseProducts());SetupToolbar();
  Controls.Add(canvas);Controls.Add(bar);canvas.Dock=DockStyle.Fill;canvas.BackColor=bg;canvas.Paint+=PaintClock;canvas.MouseClick+=(s,e)=>{var hit=hits.FirstOrDefault(h=>h.Item1.Contains(CanvasPoint(e.Location)));if(hit!=null)ShowDetails(hit.Item2);};
  canvas.MouseDown+=(s,e)=>{if(e.Button==MouseButtons.Left&&e.Y<60){ReleaseCapture();SendMessage(Handle,0xA1,(IntPtr)2,IntPtr.Zero);}};
  tray=new NotifyIcon{Icon=SystemIcons.Information,Text="全球期货交易时钟",Visible=true};var menu=new ContextMenuStrip();menu.Items.Add("显示时钟",null,(s,e)=>{Show();WindowState=FormWindowState.Normal;Activate();});menu.Items.Add("退出",null,(s,e)=>Close());tray.ContextMenuStrip=menu;tray.DoubleClick+=(s,e)=>{Show();Activate();};
  timer.Interval=100;timer.Tick+=(s,e)=>TickDashboard();timer.Start();FormClosing+=(s,e)=>{Save();timer.Stop();tray.Dispose();};ResizeEnd+=(s,e)=>Save();ApplySize();Shown+=(s,e)=>StartServices();
 }
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool ReleaseCapture();
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr h,int m,IntPtr w,IntPtr l);
 Button Btn(string title,Action action){var b=new Button{Text=title,AutoSize=false,Size=new Size(80,28),FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(32,47,65),ForeColor=Color.White,Margin=new Padding(7,0,0,0)};b.FlatAppearance.BorderSize=0;b.Click+=(s,e)=>action();bar.Controls.Add(b);return b;}
 void Save(){pref.x=Left;pref.y=Top;pref.top=TopMost;pref.filter=filter.SelectedIndex;pref.timeline=timeline;try{File.WriteAllText(Path.Combine(home,"settings.json"),new JavaScriptSerializer().Serialize(pref));}catch{}}
 void ApplySize(){bar.AutoScroll=false;bar.WrapContents=true;bar.Height=pref.compact?82:49;MinimumSize=pref.compact?new Size(570,700):new Size(1000,720);var area=Screen.FromControl(this).WorkingArea;Size=pref.compact?new Size(590,Math.Min(820,area.Height)):new Size(Math.Min(1280,area.Width),Math.Min(990,area.Height));Left=Math.Max(area.Left,Math.Min(Left,area.Right-Width));Top=Math.Max(area.Top,Math.Min(Top,area.Bottom-Height));mode.Text=pref.compact?"展开":"小挂件";RefreshClock();}
 List<Market> All(){
 if(filter.SelectedIndex==0){var ids=new[]{"shfe-gold","comex-gold","jpx","hkex","mcx","sge"};return ids.Select(id=>Engine.data.markets.First(m=>m.id==id)).ToList();}
 if(filter.SelectedIndex==1){var codes=new[]{"JM","LC","LH","PX","IM","IC","TL"};return codes.Select(c=>Engine.data.catalog.First(m=>m.product==c)).ToList();}
 return Engine.data.catalog.Where(m=>pref.selected.Contains(m.product)).ToList();
 }
 List<Market> VisibleMarkets(){var all=All();int pages=(all.Count+6)/7;page=page%Math.Max(1,pages);var visible=all.Skip(page*7).Take(7).ToList();var palette=new[]{"#FFAD0D","#16BFFF","#35E89A","#FF5EAA","#AC82FF","#FF703D","#4C8DFF"};for(int i=0;i<visible.Count;i++)visible[i].color=palette[i];return visible;}
 void RefreshClock(){var ms=VisibleMarkets();paging.Text="下一组 "+(page+1)+"/"+Math.Max(1,(All().Count+6)/7);bar.Controls[2].Text=TopMost?"置顶：开":"置顶：关";if(choose!=null){choose.Visible=true;choose.Text="自选品种";}canvas.Invalidate();Save();}
 void TextAt(Graphics g,string t,float size,Color c,float x,float y,bool bold=false){using(var f=new Font("Microsoft YaHei UI",size,bold?FontStyle.Bold:FontStyle.Regular))using(var b=new SolidBrush(c))g.DrawString(t,f,b,x,y);}
 void Center(Graphics g,string t,float size,Color c,float x,float y){using(var f=new Font("Microsoft YaHei UI",size))using(var b=new SolidBrush(c)){var z=g.MeasureString(t,f);g.DrawString(t,f,b,x-z.Width/2,y-z.Height/2);}}
 Color MC(Market m){return ColorTranslator.FromHtml(m.color);}
 void ChooseProducts(){
  using(var dlg=new Form{Text="自选期货 · 国内六家交易所 / "+Engine.data.catalog.Length+"个品种",Size=new Size(760,640),StartPosition=FormStartPosition.CenterParent,Font=Font,BackColor=bg,ForeColor=Color.White,MinimizeBox=false,MaximizeBox=false}){
   var search=new TextBox{Left=18,Top=42,Width=430};var exchange=new ComboBox{Left=465,Top=42,Width=255,DropDownStyle=ComboBoxStyle.DropDownList};
   exchange.Items.Add("全部交易所");exchange.Items.AddRange(new object[]{"上期所","上期能源","大商所","郑商所","广期所","中金所"});exchange.SelectedIndex=0;
   var info=new Label{Left=18,Top=14,Width=700,Text="搜索中文名称 / 代码，勾选后点击保存；可跨交易所选择。"};
   var list=new CheckedListBox{Left=18,Top=82,Width=702,Height=416,CheckOnClick=true,BackColor=panel,ForeColor=Color.White,BorderStyle=BorderStyle.None,IntegralHeight=false};
   var status=new Label{Left=18,Top=510,Width=700,Height=30};
   var picks=new HashSet<string>(pref.selected);var shown=new List<Market>();bool filling=false;
   Action fill=()=>{filling=true;list.Items.Clear();string q=search.Text.Trim().ToUpperInvariant();shown=Engine.data.catalog.Where(m=>(exchange.SelectedIndex==0||m.exchange==(string)exchange.SelectedItem)&&(m.name.Contains(q)||m.product.Contains(q)||m.exchange.Contains(q))).ToList();foreach(var m in shown)list.Items.Add(m.exchange+"  ·  "+m.product+"  "+m.name+"  "+(m.sessions.Any(s=>s.night)?"日盘 + 夜盘":"仅日盘"),picks.Contains(m.product));filling=false;status.Text="当前显示 "+shown.Count+" / "+Engine.data.catalog.Length+" 个品种 · 已选 "+picks.Count+" 个";};
   list.ItemCheck+=(s,e)=>{if(filling)return;var code=shown[e.Index].product;if(e.NewValue==CheckState.Checked)picks.Add(code);else picks.Remove(code);status.Text="当前显示 "+shown.Count+" / "+Engine.data.catalog.Length+" 个品种 · 已选 "+picks.Count+" 个";};
   search.TextChanged+=(s,e)=>fill();exchange.SelectedIndexChanged+=(s,e)=>fill();
   var all=new Button{Text="全选当前列表",Left=18,Top=548,Width=130};all.Click+=(s,e)=>{foreach(var m in shown)picks.Add(m.product);fill();};
   var clear=new Button{Text="清空全部",Left=160,Top=548,Width=105};clear.Click+=(s,e)=>{picks.Clear();fill();};
   var save=new Button{Text="保存选择",Left=490,Top=548,Width=110,DialogResult=DialogResult.OK};
   var cancel=new Button{Text="取消",Left=612,Top=548,Width=108,DialogResult=DialogResult.Cancel};
   foreach(var b in new[]{all,clear,save,cancel}){b.Height=32;b.BackColor=panel;b.ForeColor=Color.White;b.FlatStyle=FlatStyle.Flat;}
   dlg.Controls.AddRange(new Control[]{info,search,exchange,list,status,all,clear,save,cancel});dlg.AcceptButton=save;dlg.CancelButton=cancel;fill();
   if(dlg.ShowDialog(this)==DialogResult.OK){pref.selected=Engine.data.catalog.Where(m=>picks.Contains(m.product)).Select(m=>m.product).ToArray();page=0;nextQuotes=DateTime.MinValue;RefreshClock();}
  }
 }
 void ShowDetails(Market m){var now=NetworkClock.UtcNow;var local=TimeZoneInfo.ConvertTimeFromUtc(now,TimeZoneInfo.FindSystemTimeZoneById(m.zone));string sessions=String.Join("\r\n",m.sessions.Select(s=>s.start+"—"+s.end+(TimeSpan.Parse(s.end)<=TimeSpan.Parse(s.start)?"（次日）":"")+(s.night?" 夜盘":"")).ToArray());
  MessageBox.Show(this,m.name+"  "+m.code+"\r\n\r\n当地现在："+local.ToString("yyyy-MM-dd HH:mm:ss")+"\r\n当地常规时段：\r\n"+sessions+"\r\n\r\n今日"+ZoneLabel(now)+"：\r\n"+MarketView.Today(m,now,DisplayZone)+"\r\n\r\n"+m.note+"\r\n\r\n"+Engine.Coverage(m,now)+"\r\n核对日期："+Engine.data.checkedDate+"\r\n来源："+m.source,"交易时段说明",MessageBoxButtons.OK,MessageBoxIcon.Information);
 }
}
public static class Program {
 static System.Threading.Mutex instance;
 [STAThread] public static void Main(string[] args){try{Engine.data=new JavaScriptSerializer().Deserialize<Data>(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"markets.json")));
 if(args.Contains("--test")){Test();return;}bool first;instance=new System.Threading.Mutex(true,"GlobalFuturesClock_ZH_v1",out first);if(!first){MessageBox.Show("时钟已经在运行，请在系统托盘双击时钟图标显示窗口。","全球期货交易时钟");return;}Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new Clock());GC.KeepAlive(instance);}
 catch(Exception e){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"error.log"),e.ToString());if(args.Contains("--test"))Environment.Exit(1);else MessageBox.Show("启动失败，请查看 error.log：\r\n"+e.Message,"全球期货交易时钟");}}
 static void Test(){var lines=new List<string>();Action<string,string,bool,string> check=(id,date,want,label)=>{var m=Engine.data.markets.First(x=>x.id==id);var got=Engine.Open(m,DateTime.SpecifyKind(DateTime.Parse(date),DateTimeKind.Utc));if(got!=want)throw new Exception(label+" 失败");lines.Add("PASS "+label);};
 check("shfe-gold","2026-10-23 17:00:00",true,"周六凌晨承接周五夜盘");check("shfe-gold","2026-10-24 17:00:00",false,"周日凌晨无夜盘");check("shfe-gold","2026-10-22 02:15:00",false,"10:15小节休息边界");check("shfe-gold","2026-10-22 02:30:00",true,"10:30恢复交易");check("shfe-gold","2026-10-22 06:59:59",true,"15点前交易");check("shfe-gold","2026-10-22 07:00:00",false,"15点收盘边界");check("shfe-gold","2026-09-30 13:00:00",false,"国庆节前停夜盘");check("shfe-gold","2026-10-01 01:00:00",false,"国庆休市");check("shfe-gold","2026-10-08 01:00:00",true,"假后日盘恢复");check("shfe-gold","2026-10-07 13:00:00",false,"假后首日日盘前不补夜盘");check("shfe-gold","2026-10-23 13:00:00",true,"普通周五有夜盘");check("comex-gold","2026-10-22 21:30:00",false,"美国夏令时维护");check("comex-gold","2026-12-01 22:30:00",false,"美国冬令时维护");check("comex-gold","2026-10-25 22:00:00",true,"美国周日晚开盘");check("gfex","2026-10-22 13:00:00",false,"广期所无夜盘");check("mcx","2026-10-22 18:05:00",false,"印度美夏令时收盘后");check("mcx","2026-12-01 18:05:00",true,"印度美冬令时延长");
 foreach(var m in Engine.data.markets.Concat(Engine.data.catalog)){var ss=Engine.Intervals(m,DateTime.UtcNow);if(ss.Any(s=>s.end<=s.start))throw new Exception("时段顺序错误");}lines.Add("PASS 全部市场时区与跨日区间");
 if(Engine.data.catalog.Length!=91||Engine.data.catalog.Select(m=>m.product).Distinct().Count()!=91)throw new Exception("品种目录重复或缺失");
 lines.Add("PASS 国内91品种唯一性与六所目录");
 foreach(var code in new[]{"JM","PX","L-F","V-F","PP-F"}){if(!Engine.Open(Engine.data.catalog.First(m=>m.product==code),DateTime.Parse("2026-10-22T13:00:00Z").ToUniversalTime()))throw new Exception(code+"夜盘错误");}
 foreach(var code in new[]{"LC","LH","IM","IC"}){if(Engine.Open(Engine.data.catalog.First(m=>m.product==code),DateTime.Parse("2026-10-22T13:00:00Z").ToUniversalTime()))throw new Exception(code+"不应有夜盘");}
 lines.Add("PASS 重点品种及月均价夜盘分组");
 var bond=Engine.data.catalog.First(m=>m.product=="TL");var index=Engine.data.catalog.First(m=>m.product=="IM");var instant=DateTime.Parse("2026-10-22T07:05:00Z").ToUniversalTime();if(!Engine.Open(bond,instant)||Engine.Open(index,instant))throw new Exception("国债股指收盘区别错误");lines.Add("PASS 国债15:15与股指15:00收盘");
File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-results.txt"),lines);}
}
