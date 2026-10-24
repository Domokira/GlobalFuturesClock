using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Forms;
public static class Validation14 {
 static int count;static void Check(bool value,string label){if(!value)throw new Exception(label);count++;Console.WriteLine("PASS "+label);}
 static DateTime U(string value){return DateTime.Parse(value,System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.AdjustToUniversal|System.Globalization.DateTimeStyles.AssumeUniversal);}
 [STAThread]public static int Main(string[] args){try{
  var folder=AppDomain.CurrentDomain.BaseDirectory;Engine.data=new JavaScriptSerializer().Deserialize<Data>(File.ReadAllText(Path.Combine(folder,"markets.json")));
  if(args.Contains("--network")){
   var items=Instrument.Indexes();items.AddRange(new[]{"AU","AG","JM","LH","LC","PX","IM","TL","SC","EC"}.Select(code=>Instrument.FromMarket(Engine.data.catalog.First(m=>m.product==code))));
   var east=LiveFeed.East(items);foreach(var q in east)Console.WriteLine(q.Symbol+" "+q.Price+" "+q.Percent+" "+q.SourceTime.ToString("s")+" "+q.Source);
   Check(east.Count==items.Count,"Eastmoney indexes and all six futures exchanges");var stocks=LiveFeed.Tencent(items.Where(i=>!i.Future).ToList());Check(stocks.Count==6,"Tencent six index backup");var futures=LiveFeed.Sina(items.Where(i=>i.Future).ToList());foreach(var q in futures)Console.WriteLine(q.Symbol+" backup "+q.Price+" "+q.Percent+" "+q.SourceTime.ToString("s"));Check(futures.Count==10,"Sina commodity and financial futures backup");
   var routed=new FeedRouter(i=>{throw new IOException("forced outage");},LiveFeed.Tencent,LiveFeed.Sina).Fetch(items,NetworkClock.UtcNow);Check(routed.Quotes.Count==items.Count,"live backup takeover after forced primary failure");
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);using(var f=new Clock()){f.PreviewZone(0);f.UseQuoteSnapshot(new FeedRouter().Fetch(items,NetworkClock.UtcNow).Quotes);f.RenderPreview(Path.Combine(folder,"v14-live.png"),false,true,true,false,NetworkClock.UtcNow);}return 0;
  }
  var instant=U("2026-10-23T08:00:00Z");var span=new Span{start=U("2026-10-23T01:00:00Z"),end=U("2026-10-23T07:00:00Z")};
  var bj=TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");var ny=TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
  var a=MarketView.Project(new[]{span},instant,bj);var b=MarketView.Project(new[]{span},instant,ny);
  Check(a.Single().Start==540&&a.Single().End==900,"Beijing day 09:00-15:00");Check(b.Single().Start==0&&b.Single().End==180,"New York date clips same UTC session to 00:00-03:00");
  var spring=new Span{start=U("2026-03-08T05:00:00Z"),end=U("2026-03-09T04:00:00Z")};var springView=MarketView.Project(new[]{spring},U("2026-03-08T12:00:00Z"),ny);
  Check(springView.Count==2&&springView[0].End==120&&springView[1].Start==180&&springView.Sum(s=>s.End-s.Start)==1380,"DST spring day omits nonexistent hour");
  var autumn=new Span{start=U("2026-11-01T04:00:00Z"),end=U("2026-11-02T05:00:00Z")};var autumnView=MarketView.Project(new[]{autumn},U("2026-11-01T12:00:00Z"),ny);Check(autumnView.Count==1&&autumnView[0].End==1440,"DST fall repeated hour folds without wrapping arc");
  var item=Instrument.FromMarket(Engine.data.catalog.First(m=>m.product=="IM"));Check(item.EastId=="220.IMM"&&item.SinaId=="IM0","financial futures provider mapping");
  var items2=new List<Instrument>{Instrument.Indexes()[0],item};DateTime stamp=Engine.B(instant);var stockQuote=new IndexQuote{Symbol=items2[0].Key,SourceTime=stamp,Source="腾讯",Price=1};var futureQuote=new IndexQuote{Symbol="IM",SourceTime=stamp,Source="新浪",Price=2};
  var router=new FeedRouter(i=>{throw new IOException();},i=>new List<IndexQuote>{stockQuote},i=>new List<IndexQuote>{futureQuote});var failover=router.Fetch(items2,instant);Check(failover.Quotes.Count==2&&failover.Status.Contains("备用"),"primary outage triggers both backups");
  items2[0].ExpectedOpen=true;var stale=new IndexQuote{Symbol=items2[0].Key,SourceTime=stamp.AddSeconds(-60),Source="东财",Price=0.8};var recovery=new FeedRouter(i=>new List<IndexQuote>{stale,futureQuote},i=>new List<IndexQuote>{stockQuote},i=>new List<IndexQuote>()).Fetch(items2,instant);Check(recovery.Quotes.First(q=>q.Symbol==stockQuote.Symbol).Source=="腾讯","stale primary replaced by newer backup");
  int backups=0;items2[0].ExpectedOpen=false;new FeedRouter(i=>new List<IndexQuote>{stale,futureQuote},i=>{backups++;return new List<IndexQuote>();},i=>new List<IndexQuote>()).Fetch(items2,instant);Check(backups==0,"closed market snapshots do not trigger stale failover");
  var commodity=Instrument.FromMarket(Engine.data.catalog.First(m=>m.product=="AU"));var fields=Enumerable.Repeat("0",18).ToArray();fields[0]="黄金连续";fields[1]="023000";fields[8]="861.62";fields[10]="869.2";fields[17]="2026-10-23";var sample=LiveFeed.ParseSina("var hq_str_nf_AU0=\""+String.Join(",",fields)+"\";",new List<Instrument>{commodity});Check(sample.Count==1&&Math.Abs(sample[0].Percent-(-0.872066))<0.00001,"commodity percent uses previous settlement");
  string missing="{\"data\":{\"diff\":[{\"f2\":\"-\",\"f3\":0,\"f12\":\"aum\",\"f13\":113,\"f124\":1792780200}]}}";Check(LiveFeed.ParseEast(missing,new List<Instrument>{commodity}).Count==0,"missing price is not rendered as zero");
  Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);using(var f=new Clock()){f.PreviewZone(0);f.RenderPreview(Path.Combine(folder,"v14-beijing.png"),false,true,true,false,instant);f.PreviewZone(3);f.RenderPreview(Path.Combine(folder,"v14-newyork.png"),false,true,true,false,instant);f.RenderPreview(Path.Combine(folder,"v14-timeline.png"),false,true,true,true,instant);f.RenderPreview(Path.Combine(folder,"v14-compact.png"),true,true,false,false,instant);}
  Console.WriteLine(count+" v1.4 checks passed");return 0;
 }catch(Exception ex){Console.WriteLine(ex);return 1;}}
}
