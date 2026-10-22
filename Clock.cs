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
public class Pref { public int x=80,y=80; public bool top=true,compact=false; public int filter=0; public string[] selected; }
public static class Engine {
 public static Data data;
 public static TimeZoneInfo BJ=TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
 public static DateTime B(DateTime utc){return TimeZoneInfo.ConvertTimeFromUtc(utc,BJ);}
 public static bool Week(DateTime d){return d.DayOfWeek!=DayOfWeek.Saturday && d.DayOfWeek!=DayOfWeek.Sunday;}
 public static bool Holiday(DateTime d){return data.holidays.Contains(d.ToString("yyyy-MM-dd"));}
 public static DateTime U(DateTime d,string z){return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(d,DateTimeKind.Unspecified),TimeZoneInfo.FindSystemTimeZoneById(z));}
 public static List<Span> Intervals(Market m,DateTime utc){
  var list=new List<Span>(); var date=TimeZoneInfo.ConvertTimeFromUtc(utc,TimeZoneInfo.FindSystemTimeZoneById(m.zone)).Date;
  for(int i=-2;i<=18;i++){var d=date.AddDays(i); foreach(var s in m.sessions){
   if(!s.days.Contains(((int)d.DayOfWeek).ToString()))continue;
   if(m.china && Holiday(d))continue;
   if(m.china && s.night){var next=d.AddDays(1);while(!Week(next))next=next.AddDays(1);if(Holiday(next))continue;}
   var begin=TimeSpan.Parse(s.start);var end=TimeSpan.Parse(s.end);
   if(m.id=="mcx")end=TimeSpan.Parse(TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time").IsDaylightSavingTime(DateTime.SpecifyKind(d.AddHours(12),DateTimeKind.Unspecified))?"23:30":"23:55");
   var a=d.Add(begin);var b=d.Add(end);if(end<=begin)b=b.AddDays(1);
   list.Add(new Span{start=U(a,m.zone),end=U(b,m.zone)});
  }}return list.OrderBy(s=>s.start).ToList();
 }
 public static bool Open(Market m,DateTime u){return Intervals(m,u).Any(s=>u>=s.start&&u<s.end);}
 public static string Countdown(Market m,DateTime u){var ss=Intervals(m,u);var active=ss.FirstOrDefault(s=>u>=s.start&&u<s.end);var next=ss.FirstOrDefault(s=>s.start>u);if(active==null&&next==null)return "暂无后续时段";var t=(active!=null?active.end:next.start)-u;return (active!=null?"距本段结束 ":"距下段开始 ")+((int)t.TotalHours).ToString("00")+":"+t.Minutes.ToString("00")+":"+t.Seconds.ToString("00");}
 public static string Today(Market m,DateTime u){var start=U(B(u).Date,"China Standard Time");var end=start.AddDays(1);var ss=Intervals(m,u).Where(s=>s.end>start&&s.start<end);return String.Join("  /  ",ss.Select(s=>(s.start<start?"00:00":B(s.start).ToString("HH:mm"))+"–"+(s.end>=end?"24:00":B(s.end).ToString("HH:mm"))).ToArray());}
 public static string Coverage(Market m,DateTime u){return m.china&&B(u).Year==2026?"已计入2026内地休市日；临时调整及到期日除外":"常规周历；节假日、提前收盘及到期日须核对公告";}
}
public class Surface:Panel { public Surface(){DoubleBuffered=true;ResizeRedraw=true;} }
public class Clock:Form {
 Color bg=Color.FromArgb(12,19,31),panel=Color.FromArgb(20,31,47),muted=Color.FromArgb(141,160,181),gold=Color.FromArgb(240,190,91);
 Surface canvas=new Surface(); FlowLayoutPanel bar=new FlowLayoutPanel(); ComboBox filter=new ComboBox(); Button paging,mode,choose; NotifyIcon tray; Timer timer=new Timer(); Pref pref=new Pref(); int page=0; bool timeline=false; string home=AppDomain.CurrentDomain.BaseDirectory; List<Tuple<Rectangle,Market>> hits=new List<Tuple<Rectangle,Market>>();
 public Clock(){
  Text="全球期货交易时钟 · 中文版"; Font=new Font("Microsoft YaHei UI",9); BackColor=bg;ForeColor=Color.White;AutoScaleMode=AutoScaleMode.None;MinimumSize=new Size(480,570);Size=new Size(1120,780);StartPosition=FormStartPosition.Manual;
  try{pref=new JavaScriptSerializer().Deserialize<Pref>(File.ReadAllText(Path.Combine(home,"settings.json")));}catch{}
  if(pref.selected==null)pref.selected=new string[]{"AU","AG","JM","LC","LH","PX","IM","IC"};
  Location=new Point(pref.x,pref.y);if(!Screen.AllScreens.Any(s=>s.WorkingArea.IntersectsWith(Bounds)))Location=new Point(50,50); TopMost=pref.top;
  bar.Dock=DockStyle.Top;bar.Height=49;bar.Padding=new Padding(12,8,0,0);bar.BackColor=panel;bar.WrapContents=false;
  filter.DropDownStyle=ComboBoxStyle.DropDownList;filter.Width=156;filter.Items.AddRange(new object[]{"1 · 黄金交易时间","2 · 国内重点品种","3 · 自选期货"});filter.SelectedIndex=Math.Max(0,Math.Min(2,pref.filter));filter.SelectedIndexChanged+=(s,e)=>{page=0;RefreshClock();};bar.Controls.Add(filter);
  paging=Btn("下一组",()=>{page++;RefreshClock();});
  Btn("置顶",()=>{TopMost=!TopMost;RefreshClock();});mode=Btn("小挂件",()=>{pref.compact=!pref.compact;ApplySize();});Btn("时间轴",()=>{timeline=!timeline;canvas.Invalidate();});Btn("时段说明",()=>{var m=VisibleMarkets().FirstOrDefault();if(m!=null)ShowDetails(m);});Btn("收至托盘",()=>Hide());choose=Btn("选择品种",()=>ChooseProducts());
  Controls.Add(canvas);Controls.Add(bar);canvas.Dock=DockStyle.Fill;canvas.BackColor=bg;canvas.Paint+=PaintClock;canvas.MouseClick+=(s,e)=>{var hit=hits.FirstOrDefault(h=>h.Item1.Contains(e.Location));if(hit!=null)ShowDetails(hit.Item2);};
  canvas.MouseDown+=(s,e)=>{if(e.Button==MouseButtons.Left&&e.Y<60){ReleaseCapture();SendMessage(Handle,0xA1,(IntPtr)2,IntPtr.Zero);}};
  tray=new NotifyIcon{Icon=SystemIcons.Information,Text="全球期货交易时钟",Visible=true};var menu=new ContextMenuStrip();menu.Items.Add("显示时钟",null,(s,e)=>{Show();WindowState=FormWindowState.Normal;Activate();});menu.Items.Add("退出",null,(s,e)=>Close());tray.ContextMenuStrip=menu;tray.DoubleClick+=(s,e)=>{Show();Activate();};
  timer.Interval=1000;timer.Tick+=(s,e)=>canvas.Invalidate();timer.Start();FormClosing+=(s,e)=>{Save();tray.Dispose();};ResizeEnd+=(s,e)=>Save();ApplySize();
 }
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool ReleaseCapture();
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr h,int m,IntPtr w,IntPtr l);
 Button Btn(string title,Action action){var b=new Button{Text=title,AutoSize=false,Size=new Size(80,28),FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(32,47,65),ForeColor=Color.White,Margin=new Padding(7,0,0,0)};b.FlatAppearance.BorderSize=0;b.Click+=(s,e)=>action();bar.Controls.Add(b);return b;}
 void Save(){pref.x=Left;pref.y=Top;pref.top=TopMost;pref.filter=filter.SelectedIndex;try{File.WriteAllText(Path.Combine(home,"settings.json"),new JavaScriptSerializer().Serialize(pref));}catch{}}
 void ApplySize(){for(int i=4;i<bar.Controls.Count;i++)bar.Controls[i].Visible=!pref.compact;bar.AutoScroll=pref.compact;MinimumSize=pref.compact?new Size(560,650):new Size(1120,780);Size=MinimumSize;mode.Text=pref.compact?"展开":"小挂件";RefreshClock();}
 List<Market> All(){
 if(filter.SelectedIndex==0){var ids=new[]{"shfe-gold","comex-gold","jpx","hkex","mcx","sge"};return ids.Select(id=>Engine.data.markets.First(m=>m.id==id)).ToList();}
 if(filter.SelectedIndex==1){var codes=new[]{"JM","LC","LH","PX","IM","IC","TL"};return codes.Select(c=>Engine.data.catalog.First(m=>m.product==c)).ToList();}
 return Engine.data.catalog.Where(m=>pref.selected.Contains(m.product)).ToList();
 }
 List<Market> VisibleMarkets(){var all=All();int pages=(all.Count+6)/7;page=page%Math.Max(1,pages);var visible=all.Skip(page*7).Take(7).ToList();var palette=new[]{"#F2C66D","#73C1E9","#9EDBC7","#DF8DB9","#B695EE","#E7A35E","#829FF1"};for(int i=0;i<visible.Count;i++)visible[i].color=palette[i];return visible;}
 void RefreshClock(){var ms=VisibleMarkets();paging.Text="下一组 "+(page+1)+"/"+Math.Max(1,(All().Count+6)/7);bar.Controls[2].Text=TopMost?"置顶：开":"置顶：关";if(choose!=null){choose.Visible=filter.SelectedIndex==2;choose.Text="选择品种";}canvas.Invalidate();Save();}
 void TextAt(Graphics g,string t,float size,Color c,float x,float y,bool bold=false){using(var f=new Font("Microsoft YaHei UI",size,bold?FontStyle.Bold:FontStyle.Regular))using(var b=new SolidBrush(c))g.DrawString(t,f,b,x,y);}
 void Center(Graphics g,string t,float size,Color c,float x,float y){using(var f=new Font("Microsoft YaHei UI",size))using(var b=new SolidBrush(c)){var z=g.MeasureString(t,f);g.DrawString(t,f,b,x-z.Width/2,y-z.Height/2);}}
 Color MC(Market m){return ColorTranslator.FromHtml(m.color);}
 void PaintClock(object sender,PaintEventArgs e){
  var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;var now=DateTime.UtcNow;var bj=Engine.B(now);var ms=VisibleMarkets();hits.Clear();bool compact=pref.compact||canvas.Width<900;
  TextAt(g,new[]{"黄金交易时间","国内重点品种","自选期货 · "+All().Count+"个品种"}[filter.SelectedIndex],compact?18:22,Color.White,24,17,true);TextAt(g,"北京时间  UTC+8  ·  "+bj.ToString("yyyy.MM.dd dddd",new CultureInfo("zh-CN")),10,muted,26,54);
  if(!compact){TextAt(g,bj.ToString("HH:mm:ss"),27,gold,canvas.Width-225,15);TextAt(g,"日盘 / 夜盘 · 自动换算夏令时",9,muted,canvas.Width-230,59);}
  float cx=compact?canvas.Width/2f:260,cy=compact?250:330,r=compact?150:215;
  if(!timeline||compact){
  for(int h=0;h<24;h++){double a=(h*15-90)*Math.PI/180;float x=cx+(float)Math.Cos(a)*(r+22),y=cy+(float)Math.Sin(a)*(r+22);Center(g,h==0?"24":h.ToString(),h%6==0?12:9,h%6==0?Color.White:muted,x,y);}
  var day=Engine.U(bj.Date,"China Standard Time");
  for(int i=0;i<ms.Count;i++){var m=ms[i];float rr=r-i*(compact?15:19);using(var p=new Pen(Color.FromArgb(32,47,66),compact?10:13))g.DrawEllipse(p,cx-rr,cy-rr,rr*2,rr*2);
   foreach(var sp in Engine.Intervals(m,now)){double a=Math.Max(0,(sp.start-day).TotalMinutes),b=Math.Min(1440,(sp.end-day).TotalMinutes);if(b<=a)continue;using(var p=new Pen(MC(m),compact?10:13)){g.DrawArc(p,cx-rr,cy-rr,rr*2,rr*2,(float)(a/4-90),(float)((b-a)/4));}}
  }
  double angle=(bj.TimeOfDay.TotalHours*15-90)*Math.PI/180;using(var p=new Pen(Color.FromArgb(255,232,170),2)){g.DrawLine(p,cx,cy,cx+(float)Math.Cos(angle)*(r+8),cy+(float)Math.Sin(angle)*(r+8));}using(var b=new SolidBrush(gold))g.FillEllipse(b,cx-5,cy-5,10,10);
  Center(g,"北京",12,muted,cx,cy-28);Center(g,bj.ToString("HH:mm:ss"),compact?16:22,Color.White,cx,cy+22);if(!compact)Center(g,"24 小时 · 今日时段",8,muted,cx,cy+45);
  }else{DrawTimeline(g,ms,now);}
  if(compact){float y=cy+r+42;for(int i=0;i<ms.Count;i++){int col=i%2,row=i/2;float x=18+col*(canvas.Width/2f);using(var b=new SolidBrush(MC(ms[i])))g.FillEllipse(b,x,y+row*22+6,7,7);TextAt(g,ms[i].name,8,Color.White,x+13,y+row*22);hits.Add(Tuple.Create(new Rectangle((int)x,(int)(y+row*22),canvas.Width/2,22),ms[i]));}TextAt(g,"常规时段参考 · 点击名称查看规则",8,muted,18,canvas.Height-27);return;}
  TextAt(g,timeline?"纵线为当前北京时间 · 所选市场的今日时间轴":"彩环由外向内对应右侧列表 · 指针为当前北京时间",9,muted,35,586);TextAt(g,"彩色 = 今日计划时段   深色 = 休息 / 休市",9,muted,35,610);
  float lx=525,w=canvas.Width-lx-25;
  if(ms.Count==0)TextAt(g,"尚未选择品种，点击上方「选择品种」添加。",12,muted,lx,130);
  for(int i=0;i<ms.Count;i++){var m=ms[i];float y=92+i*72;using(var b=new SolidBrush(panel))g.FillRectangle(b,lx,y,w,65);using(var b=new SolidBrush(MC(m)))g.FillRectangle(b,lx,y,4,65);
   bool on=Engine.Open(m,now);TextAt(g,m.name,11,Color.White,lx+14,y+7,true);TextAt(g,on?"常规时段内":"时段外",9,on?Color.FromArgb(92,216,175):muted,lx+w-100,y+9);
   TextAt(g,m.code+"  ·  "+Engine.Countdown(m,now),9,muted,lx+14,y+29);
   var txt=Engine.Today(m,now);TextAt(g,txt.Length==0?"今日无计划交易时段":txt,8,MC(m),lx+14,y+47);
   hits.Add(Tuple.Create(new Rectangle((int)lx,(int)y,(int)w,65),m));
  }
  TextAt(g,"所有条带时间均为北京时间；点击市场查看当地时间、来源与限制。",9,muted,lx,610);
  using(var b=new SolidBrush(Color.FromArgb(35,31,27)))g.FillRectangle(b,20,canvas.Height-52,canvas.Width-40,35);
  TextAt(g,"日历覆盖：2026内地休市日已录入；境外按常规周历，节假日/提前收盘需核对。非实时报盘。",9,gold,31,canvas.Height-44);
 }
 void DrawTimeline(Graphics g,List<Market> ms,DateTime now){
  float x=36,w=450,top=145;var day=Engine.U(Engine.B(now).Date,"China Standard Time");
  for(int h=0;h<=24;h+=3){float xx=x+w*h/24;using(var p=new Pen(Color.FromArgb(33,48,65)))g.DrawLine(p,xx,top-15,xx,top+ms.Count*56);TextAt(g,h.ToString("00"),8,muted,xx-7,top-37);}
  for(int i=0;i<ms.Count;i++){float y=top+i*56;var m=ms[i];TextAt(g,m.name,9,MC(m),x,y);using(var b=new SolidBrush(panel))g.FillRectangle(b,x,y+25,w,12);
   foreach(var sp in Engine.Intervals(m,now)){double a=Math.Max(0,(sp.start-day).TotalMinutes),b=Math.Min(1440,(sp.end-day).TotalMinutes);if(b>a)using(var brush=new SolidBrush(MC(m)))g.FillRectangle(brush,x+(float)a/1440*w,y+25,(float)(b-a)/1440*w,12);}
  }float current=x+(float)Engine.B(now).TimeOfDay.TotalMinutes/1440*w;using(var p=new Pen(gold,2))g.DrawLine(p,current,top-12,current,top+ms.Count*56);
 }

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
   if(dlg.ShowDialog(this)==DialogResult.OK){pref.selected=Engine.data.catalog.Where(m=>picks.Contains(m.product)).Select(m=>m.product).ToArray();page=0;RefreshClock();}
  }
 }
 void ShowDetails(Market m){var now=DateTime.UtcNow;var local=TimeZoneInfo.ConvertTimeFromUtc(now,TimeZoneInfo.FindSystemTimeZoneById(m.zone));string sessions=String.Join("\r\n",m.sessions.Select(s=>s.start+"—"+s.end+(TimeSpan.Parse(s.end)<=TimeSpan.Parse(s.start)?"（次日）":"")+(s.night?" 夜盘":"")).ToArray());
  MessageBox.Show(this,m.name+"  "+m.code+"\r\n\r\n当地现在："+local.ToString("yyyy-MM-dd HH:mm:ss")+"\r\n当地常规时段：\r\n"+sessions+"\r\n\r\n今日北京时间：\r\n"+Engine.Today(m,now)+"\r\n\r\n"+m.note+"\r\n\r\n"+Engine.Coverage(m,now)+"\r\n核对日期："+Engine.data.checkedDate+"\r\n来源："+m.source,"交易时段说明",MessageBoxButtons.OK,MessageBoxIcon.Information);
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
