using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

public partial class Clock {
 readonly string[] zoneIds={"China Standard Time","Tokyo Standard Time","GMT Standard Time","Eastern Standard Time","UTC"};
 readonly string[] zoneNames={"北京","东京","伦敦","纽约","UTC"};
 ComboBox zones; Button syncButton; ContextMenuStrip displayMenu;
 readonly FeedRouter router=new FeedRouter(); int futuresPage; Rectangle futuresNext; List<IndexQuote> quotes=new List<IndexQuote>(); string quoteStatus="等待获取"; bool fetching;
 DateTime nextQuotes=DateTime.MinValue,nextSync=DateTime.MinValue,lastQuoteFetch=DateTime.MinValue;
 float drawScale=1,drawX,drawY;
 public bool OfflinePreview;
 DateTime Now {get{return PreviewTime??NetworkClock.UtcNow;}}
 public DateTime? PreviewTime; TimeZoneInfo DisplayZone { get { return TimeZoneInfo.FindSystemTimeZoneById(zoneIds[pref.zone]); } } DateTime DisplayTime(DateTime utc){return TimeZoneInfo.ConvertTimeFromUtc(utc,DisplayZone);} string ZoneLabel(DateTime utc){return zoneNames[pref.zone]+" UTC"+DisplayZone.GetUtcOffset(utc).TotalHours.ToString("+0.#;-0.#;0",CultureInfo.InvariantCulture);}
 protected override void Dispose(bool disposing){if(disposing){timer.Dispose();if(tray!=null)tray.Dispose();if(displayMenu!=null)displayMenu.Dispose();}base.Dispose(disposing);}
 void SetupToolbar(){
  displayMenu=new ContextMenuStrip();
  AddToggle("旋转表盘 / 固定当前基准线",()=>pref.rotate,v=>pref.rotate=v);
  AddToggle("显示时间轴",()=>timeline,v=>timeline=v);
  AddToggle("显示品种卡片",()=>pref.cards,v=>pref.cards=v);
  AddToggle("显示主要指数（目标1秒刷新）",()=>pref.quotes,v=>{pref.quotes=v;if(v)nextQuotes=DateTime.MinValue;});
  AddToggle("显示自选期货报价",()=>pref.futures,v=>{pref.futures=v;nextQuotes=DateTime.MinValue;});
  canvas.MouseClick+=(s,e)=>{if(futuresNext.Contains(CanvasPoint(e.Location))){futuresPage++;nextQuotes=DateTime.MinValue;canvas.Invalidate();}};
  AddToggle("显示底部说明",()=>pref.notes,v=>pref.notes=v);
  AddToggle("自动 NTP 校时（每30分钟）",()=>pref.ntp,v=>{pref.ntp=v;if(v)nextSync=DateTime.MinValue;});
  Button display=null;display=Btn("显示设置",()=>displayMenu.Show(display,new Point(0,display.Height)));
  zones=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=130,Margin=new Padding(12,0,0,0)};
  zones.Items.AddRange(new object[]{"北京 · UTC+8","东京 · UTC+9","伦敦 · 自动夏令时","纽约 · 自动夏令时","UTC · 世界时"});
  pref.zone=Math.Max(0,Math.Min(4,pref.zone));zones.SelectedIndex=pref.zone;zones.SelectedIndexChanged+=(s,e)=>{pref.zone=zones.SelectedIndex;RefreshClock();};bar.Controls.Add(zones);
  syncButton=Btn("立即校时",()=>SyncTime());Btn("收至托盘",()=>Hide());
 }
 void AddToggle(string name,Func<bool> get,Action<bool> set){var item=new ToolStripMenuItem(name){Checked=get(),CheckOnClick=true};item.Click+=(s,e)=>{set(item.Checked);Save();canvas.Invalidate();};displayMenu.Items.Add(item);}
 void StartServices(){if(OfflinePreview)return;if(pref.ntp)SyncTime();if((pref.quotes||pref.futures)&&!pref.compact)FetchQuotes();}
 void TickDashboard(){
  if(!OfflinePreview){var now=DateTime.UtcNow;if(pref.ntp&&now>=nextSync&&!NetworkClock.Busy)SyncTime();if((pref.quotes||pref.futures)&&!pref.compact&&Visible&&WindowState!=FormWindowState.Minimized&&now>=nextQuotes&&!fetching)FetchQuotes();}
  if(Visible&&WindowState!=FormWindowState.Minimized)canvas.Invalidate();
 }
 async void SyncTime(){if(NetworkClock.Busy||OfflinePreview)return;nextSync=DateTime.UtcNow.AddMinutes(30);syncButton.Enabled=false;try{await Task.Factory.StartNew(()=>NetworkClock.Sync());}finally{if(!IsDisposed){syncButton.Enabled=true;canvas.Invalidate();}}}
 List<Market> FutureMarkets(){var all=Engine.data.catalog.Where(m=>pref.selected.Contains(m.product)).ToList();futuresPage%=Math.Max(1,(all.Count+5)/6);return all.Skip(futuresPage*6).Take(6).ToList();}
 List<Instrument> RequestedInstruments(DateTime now){
  var items=pref.quotes?Instrument.Indexes():new List<Instrument>();var bj=Engine.B(now);bool indexOpen=Engine.Week(bj)&&!Engine.Holiday(bj)&&((bj.TimeOfDay>=TimeSpan.FromHours(9.5)&&bj.TimeOfDay<TimeSpan.FromHours(11.5))||(bj.TimeOfDay>=TimeSpan.FromHours(13)&&bj.TimeOfDay<TimeSpan.FromHours(15)));
  foreach(var item in items)item.ExpectedOpen=indexOpen;
  if(pref.futures)foreach(var m in FutureMarkets()){var item=Instrument.FromMarket(m);item.ExpectedOpen=Engine.Open(m,now);items.Add(item);}return items;
 }
 async void FetchQuotes(){
  if(fetching||OfflinePreview)return;var items=RequestedInstruments(Now);if(items.Count==0){nextQuotes=DateTime.UtcNow.AddSeconds(1);return;}
  fetching=true;var started=DateTime.UtcNow;nextQuotes=started.AddSeconds(1);
  try{var now=Now;var result=await Task.Factory.StartNew(()=>router.Fetch(items,now));if(IsDisposed)return;
   foreach(var q in result.Quotes){var old=quotes.FirstOrDefault(v=>v.Symbol==q.Symbol);if(old==null)quotes.Add(q);else if(q.SourceTime>=old.SourceTime){quotes.Remove(old);quotes.Add(q);}else old.Status="保留较新快照";}
   foreach(var item in items.Where(i=>!result.Quotes.Any(q=>q.Symbol==i.Key))){var old=quotes.FirstOrDefault(q=>q.Symbol==item.Key);if(old!=null)old.Status="更新失败 · 旧数据";}
   lastQuoteFetch=NetworkClock.UtcNow;quoteStatus=result.Status+" · "+(DateTime.UtcNow-started).TotalMilliseconds.ToString("0")+"ms";
  }catch(Exception){if(!IsDisposed){quoteStatus="更新失败 · 保留旧数据";foreach(var q in quotes)q.Status="更新失败 · 旧数据";}}
  finally{fetching=false;nextQuotes=nextQuotes>DateTime.UtcNow?nextQuotes:DateTime.UtcNow.AddMilliseconds(100);if(!IsDisposed)canvas.Invalidate();}
 }
 Point CanvasPoint(Point p){return new Point((int)((p.X-drawX)/drawScale),(int)((p.Y-drawY)/drawScale));}
 void Box(Graphics g,float x,float y,float w,float h,Color color){using(var brush=new SolidBrush(color))g.FillRectangle(brush,x,y,w,h);}
 static PointF Polar(float cx,float cy,float r,double degrees){double a=degrees*Math.PI/180;return new PointF(cx+(float)Math.Cos(a)*r,cy+(float)Math.Sin(a)*r);}
 public static double DialAngle(double minute,double now,bool rotate){return minute/4-90-(rotate?now/4:0);}
 void Line(Graphics g,Color color,float width,PointF a,PointF b){using(var pen=new Pen(color,width))g.DrawLine(pen,a,b);}
 void PaintClock(object sender,PaintEventArgs e){
  var g=e.Graphics;g.Clear(bg);g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
  bool compact=pref.compact;float width=compact?540:1200,height=compact?650:680+(pref.quotes?180:0)+(pref.futures?180:0);
  drawScale=Math.Min(canvas.Width/width,canvas.Height/height);drawX=(canvas.Width-width*drawScale)/2;drawY=(canvas.Height-height*drawScale)/2;
  var state=g.Save();g.TranslateTransform(drawX,drawY);g.ScaleTransform(drawScale,drawScale);
  var now=Now;var bj=Engine.B(now);var zone=TimeZoneInfo.FindSystemTimeZoneById(zoneIds[pref.zone]);var local=TimeZoneInfo.ConvertTimeFromUtc(now,zone);var ms=VisibleMarkets();hits.Clear();futuresNext=Rectangle.Empty;
  string offset=zone.GetUtcOffset(now).TotalHours.ToString("+0.#;-0.#;0",CultureInfo.InvariantCulture);
  string title=new[]{"黄金交易时间","国内重点品种","自选期货"}[filter.SelectedIndex];
  TextAt(g,"GLOBAL MARKETS  /  全球交易时钟",compact?8:10,gold,24,15,true);
  TextAt(g,title,compact?20:27,Color.White,24,39,true);
  if(compact){TextAt(g,local.ToString("HH:mm:ss"),26,Color.White,330,36,true);TextAt(g,zoneNames[pref.zone]+" UTC"+offset,9,gold,335,76);}
  else{TextAt(g,local.ToString("HH:mm:ss"),36,Color.White,900,24,true);TextAt(g,zoneNames[pref.zone]+"  UTC"+offset+"  ·  "+local.ToString("MM.dd dddd",new CultureInfo("zh-CN")),10,gold,900,80);}
  string sync=NetworkClock.Status;if(NetworkClock.LastSync!=DateTime.MinValue)sync+=" · "+Engine.B(NetworkClock.LastSync).ToString("HH:mm")+" 北京";
  TextAt(g,compact?(NetworkClock.Busy?"NTP 校时中":"24小时 · "+zoneNames[pref.zone]+"时段 · "+(NetworkClock.LastSync==DateTime.MinValue?"系统时间":"NTP已校时")):sync,compact?8:9,muted,25,compact?82:88);
  Line(g,Color.FromArgb(39,56,76),1,new PointF(24,112),new PointF(width-24,112));
  if(compact){DrawDial(g,ms,now,270,316,153,true);DrawCompactLegend(g,ms,now);}
  else{
   if(timeline)DrawTimelineNew(g,ms,now,pref.cards?36:150,pref.cards?530:900);
   else DrawDial(g,ms,now,pref.cards?313:600,365,202,false);
   if(pref.cards)DrawCards(g,ms,now);
   if(pref.quotes)DrawQuotes(g,640,width,false);
   if(pref.futures)DrawQuotes(g,640+(pref.quotes?180:0),width,true);
  }
  if(pref.notes)TextAt(g,compact?"点击品种查看规则 · 显示设置可隐藏模块":"时段日历：2026内地休市日已录入；境外按常规周历。节假日、提前收盘及临时调整请核对公告。",compact?8:9,muted,24,height-27);
  g.Restore(state);
 }
 void DrawDial(Graphics g,List<Market> ms,DateTime now,float cx,float cy,float r,bool compact){
  var local=DisplayTime(now);double minute=local.TimeOfDay.TotalMinutes;float rim=r+35;
  using(var brush=new LinearGradientBrush(new RectangleF(cx-rim,cy-rim,rim*2,rim*2),Color.FromArgb(32,48,66),Color.FromArgb(7,13,24),45))g.FillEllipse(brush,cx-rim,cy-rim,rim*2,rim*2);
  using(var pen=new Pen(Color.FromArgb(64,85,106),1))g.DrawEllipse(pen,cx-rim,cy-rim,rim*2,rim*2);
  for(int tick=0;tick<144;tick++){double angle=DialAngle(tick*10,minute,pref.rotate);bool major=tick%6==0;Line(g,major?Color.FromArgb(164,185,202):Color.FromArgb(55,77,99),major?1.6f:1,Polar(cx,cy,r+7,angle),Polar(cx,cy,r+(major?14:11),angle));}
  for(int h=0;h<24;h++){var p=Polar(cx,cy,r+24,DialAngle(h*60,minute,pref.rotate));Center(g,h==0?"24":h.ToString("00"),compact?8:10,h%6==0?Color.White:muted,p.X,p.Y);}
  float band=compact?11:15,step=compact?14:19;
  for(int i=0;i<ms.Count;i++){
   float rr=r-i*step;var m=ms[i];using(var pen=new Pen(Color.FromArgb(25,42,60),band))g.DrawEllipse(pen,cx-rr,cy-rr,rr*2,rr*2);
   foreach(var span in MarketView.ProjectStatus(Engine.Intervals(m,now),now,DisplayZone)){
    double a=span.Start,b=span.End;if(b<=a)continue;
    using(var pen=new Pen(span.Ended?Color.FromArgb(112,118,128):MC(m),band))g.DrawArc(pen,cx-rr,cy-rr,rr*2,rr*2,(float)DialAngle(a,minute,pref.rotate),(float)((b-a)/4));
    string label=String.IsNullOrEmpty(m.product)?m.name:m.product;
    if(!compact)ArcLabel(g,label,cx,cy,rr,DialAngle(a,minute,pref.rotate),(b-a)/4,band);
   }
  }
  double needle=pref.rotate?-90:DialAngle(minute,minute,false);
  Line(g,Color.FromArgb(65,255,209,109),9,Polar(cx,cy,66,needle),Polar(cx,cy,r+39,needle));
  Line(g,Color.FromArgb(255,225,151),2,Polar(cx,cy,66,needle),Polar(cx,cy,r+39,needle));
  var tip=Polar(cx,cy,r+39,needle);using(var brush=new SolidBrush(gold))g.FillEllipse(brush,tip.X-4,tip.Y-4,8,8);
  Center(g,ZoneLabel(now),compact?9:10,muted,cx,cy-21);Center(g,local.ToString("HH:mm:ss"),compact?17:22,Color.White,cx,cy+3);
  Center(g,pref.rotate?"NOW · 固定基准":"24H · 今日时段",compact?7:8,gold,cx,cy+30);
  Center(g,"灰色：已结束  ·  彩色：交易中 / 待开市",compact?8:9,muted,cx,compact?507:610);
  if(!compact){Center(g,pref.rotate?"表盘随时间旋转 · 顶部金线为当前时刻":"固定表盘 · 金线随当前时间移动",10,Color.White,cx,630);}
 }
 void ArcLabel(Graphics g,string text,float cx,float cy,float r,double start,double sweep,float band){
  double available=r*sweep*Math.PI/180-14;if(available<24)return;
  using(var font=new Font("Microsoft YaHei UI",9,FontStyle.Bold,GraphicsUnit.Pixel))using(var brush=new SolidBrush(Color.FromArgb(7,21,37)))using(var format=(StringFormat)StringFormat.GenericTypographic.Clone()){
   var chars=text.Select(c=>c.ToString()).ToList();var widths=chars.Select(c=>g.MeasureString(c,font,PointF.Empty,format).Width+0.5f).ToList();
   while(chars.Count>0&&widths.Sum()>available){chars.RemoveAt(chars.Count-1);widths.RemoveAt(widths.Count-1);}if(chars.Count<2)return;
   using(var clip=new GraphicsPath()){
    float outer=r+band/2-1,inner=r-band/2+1;clip.AddArc(cx-outer,cy-outer,outer*2,outer*2,(float)start,(float)sweep);clip.AddArc(cx-inner,cy-inner,inner*2,inner*2,(float)(start+sweep),(float)-sweep);clip.CloseFigure();var saved=g.Save();g.SetClip(clip,CombineMode.Intersect);
    double mid=start+sweep/2,normal=(mid%360+360)%360;bool flip=normal>0&&normal<180;double position=-widths.Sum()/2;
    for(int i=0;i<chars.Count;i++){double center=position+widths[i]/2;double angle=mid+center/r*180/Math.PI*(flip?-1:1);var point=Polar(cx,cy,r,angle);var state=g.Save();g.TranslateTransform(point.X,point.Y);g.RotateTransform((float)(angle+(flip?-90:90)));g.DrawString(chars[i],font,brush,-widths[i]/2,-font.GetHeight(g)/2,format);g.Restore(state);position+=widths[i];}g.Restore(saved);
   }
  }
 }
 void DrawCards(Graphics g,List<Market> ms,DateTime now){
  TextAt(g,"今日交易时段",11,Color.White,625,124,true);TextAt(g,zoneNames[pref.zone]+"时间 · 外环 → 内环",9,muted,925,126);
  if(ms.Count==0)TextAt(g,"点击「选择品种」添加自选",13,muted,650,215);
  for(int i=0;i<ms.Count;i++){var m=ms[i];float x=625,y=157+i*64,w=550;bool on=Engine.Open(m,now);Box(g,x,y,w,58,panel);Box(g,x,y,4,58,MC(m));
   TextAt(g,m.name,11,Color.White,x+14,y+5,true);TextAt(g,on?"● 时段内":"○ 时段外",9,on?Color.FromArgb(53,232,154):muted,x+w-85,y+7);
   TextAt(g,m.code+"  ·  "+Engine.Countdown(m,now),8,muted,x+14,y+25);
   string today=MarketView.Today(m,now,DisplayZone);TextAt(g,today.Length==0?"今日无计划时段":today,7.5f,MC(m),x+14,y+41);
   hits.Add(Tuple.Create(new Rectangle((int)x,(int)y,(int)w,58),m));
  }
 }
 void DrawCompactLegend(Graphics g,List<Market> ms,DateTime now){
  for(int i=0;i<ms.Count;i++){float x=24+(i%2)*255,y=514+(i/2)*25;Box(g,x,y+5,4,13,MC(ms[i]));TextAt(g,ms[i].name,8.5f,Color.White,x+10,y);TextAt(g,Engine.Open(ms[i],now)?"交易中":"时段外",7.5f,muted,x+174,y+1);hits.Add(Tuple.Create(new Rectangle((int)x,(int)y,246,24),ms[i]));}
 }
 void DrawTimelineNew(Graphics g,List<Market> ms,DateTime now,float x,float w){
  float top=178;
  TextAt(g,"今日时间轴 · "+ZoneLabel(now),12,Color.White,x,128,true);
  for(int h=0;h<=24;h+=3){float xx=x+w*h/24;Line(g,Color.FromArgb(36,53,73),1,new PointF(xx,top),new PointF(xx,top+420));Center(g,h.ToString("00"),8,muted,xx,top-15);}
  for(int i=0;i<ms.Count;i++){float y=top+i*58;var m=ms[i];TextAt(g,m.name,9,MC(m),x,y);Box(g,x,y+24,w,15,panel);foreach(var span in MarketView.ProjectStatus(Engine.Intervals(m,now),now,DisplayZone)){double a=span.Start,b=span.End;if(b>a)Box(g,x+(float)a/1440*w,y+24,(float)(b-a)/1440*w,15,span.Ended?Color.FromArgb(112,118,128):MC(m));}}
  TextAt(g,"灰色：已结束  ·  彩色：交易中 / 待开市",9,muted,x,610);
  float current=x+(float)DisplayTime(now).TimeOfDay.TotalMinutes/1440*w;Line(g,gold,2,new PointF(current,top),new PointF(current,top+420));
 }
 void DrawQuotes(Graphics g,float y,float width,bool future){
  Line(g,Color.FromArgb(39,56,76),1,new PointF(24,y),new PointF(width-24,y));TextAt(g,future?"期货自选 · 主连":"主要指数",12,Color.White,24,y+12,true);
  string status=future?"目标1秒 · 涨跌幅按昨结算 · 各卡片标注来源":"目标1秒 · "+(lastQuoteFetch==DateTime.MinValue?"等待数据":Engine.B(lastQuoteFetch).ToString("HH:mm:ss")+" 北京")+" · "+quoteStatus;TextAt(g,status,8,muted,future?230:150,y+17);
  var items=future?FutureMarkets().Select(Instrument.FromMarket).ToList():Instrument.Indexes();
  if(future){int total=Engine.data.catalog.Count(m=>pref.selected.Contains(m.product));futuresNext=new Rectangle(1020,(int)y+7,155,30);TextAt(g,"下一组 "+(futuresPage+1)+"/"+Math.Max(1,(total+5)/6)+"  →",10,gold,1020,y+13);if(items.Count==0)TextAt(g,"尚未选择期货品种，点击上方「自选品种」添加。",12,muted,24,y+75);}
  for(int i=0;i<items.Count;i++){float x=24+i*194;var item=items[i];var q=quotes.FirstOrDefault(v=>v.Symbol==item.Key);Box(g,x,y+43,182,111,panel);
   // Clip text to each card so unusually long provider/product names cannot leak.
   var state=g.Save();g.SetClip(new RectangleF(x+8,y+45,166,107),CombineMode.Intersect);
   TextAt(g,future?item.Key+" · "+item.Name:item.Name,9,Color.White,x+12,y+50,true);
   Color color=q==null?muted:q.Percent>0?Color.FromArgb(255,94,112):q.Percent<0?Color.FromArgb(53,232,154):muted;
   TextAt(g,q==null?"—":q.Price.ToString("0.##",CultureInfo.InvariantCulture),18,color,x+12,y+69,true);
   TextAt(g,q==null?(item.EastId==null?"暂无主连映射":"暂无有效报价"):q.Percent.ToString("+0.00;-0.00;0.00",CultureInfo.InvariantCulture)+"%  · "+q.Source,9,color,x+12,y+99);
   TextAt(g,q==null?"等待接口返回":q.SourceTime.ToString("MM-dd HH:mm:ss")+" 北京",7.5f,muted,x+12,y+119);
   TextAt(g,q==null?"":q.Status,7,Color.FromArgb(240,190,91),x+12,y+137);g.Restore(state);
  }
  TextAt(g,future?"跟随自选目录，每组六个；主连为连续行情，并非指定交割月合约。":"目标每秒请求；源站延迟以报价时间为准。主源异常自动降级；隐藏模块暂停该模块请求。",8,muted,24,y+160);
 }
 // Deterministic off-screen rendering for layout checks; no network or fabricated quotes.
 public void PreviewZone(int zone){pref.zone=zone;}
 public void UseQuoteSnapshot(List<IndexQuote> snapshot){quotes=snapshot;lastQuoteFetch=DateTime.UtcNow;quoteStatus="实际接口快照";}
 public void RenderPreview(string path,bool compact,bool cards,bool indexes,bool axis,DateTime utc){OfflinePreview=true;PreviewTime=utc;pref.compact=compact;pref.cards=cards;pref.quotes=indexes;timeline=axis;ApplySize();CreateControl();PerformLayout();using(var bitmap=new Bitmap(canvas.Width,canvas.Height)){using(var g=Graphics.FromImage(bitmap))PaintClock(canvas,new PaintEventArgs(g,new Rectangle(0,0,bitmap.Width,bitmap.Height)));bitmap.Save(path,System.Drawing.Imaging.ImageFormat.Png);}}
}


