using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

public class Instrument {
 public string Key,Name,EastId,SinaId;public bool Future,ExpectedOpen;
 public static Instrument FromMarket(Market m){
  var exchanges=new Dictionary<string,string>{{"上期所","113"},{"上期能源","142"},{"大商所","114"},{"郑商所","115"},{"广期所","225"},{"中金所","220"}};
  string code=m.product;bool mapped=exchanges.ContainsKey(m.exchange)&&!code.Contains("-");
  return new Instrument{Key=code,Name=m.name,Future=true,EastId=mapped?exchanges[m.exchange]+"."+((m.exchange=="郑商所"||m.exchange=="中金所")?code+"M":code.ToLowerInvariant()+"m"):null,SinaId=mapped?code+"0":null};
 }
 public static List<Instrument> Indexes(){return QuoteFeed.Symbols.Select((s,i)=>new Instrument{Key=s,Name=QuoteFeed.Names[i],EastId=(s.StartsWith("sh")?"1.":"0.")+s.Substring(2)}).ToList();}
}
public static class LiveFeed {
 public static string Get(string url,string referer,Encoding encoding){
  ServicePointManager.SecurityProtocol|=(SecurityProtocolType)3072;
  var request=(HttpWebRequest)WebRequest.Create(url);request.Timeout=1800;request.ReadWriteTimeout=1800;request.UserAgent="GlobalFuturesClock/1.4";request.Referer=referer;
  request.CachePolicy=new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
  using(var response=request.GetResponse())using(var reader=new StreamReader(response.GetResponseStream(),encoding))return reader.ReadToEnd();
 }
 public static bool Number(object value,out double number){return Double.TryParse(Convert.ToString(value,CultureInfo.InvariantCulture),NumberStyles.Float,CultureInfo.InvariantCulture,out number)&&!Double.IsNaN(number)&&!Double.IsInfinity(number);}
 public static List<IndexQuote> ParseEast(string json,List<Instrument> instruments){
  var result=new List<IndexQuote>();var root=new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string,object>;object dataObject;
  if(root==null||!root.TryGetValue("data",out dataObject)||dataObject==null)return result;
  var data=dataObject as Dictionary<string,object>;object diff;if(data==null||!data.TryGetValue("diff",out diff)||diff==null)return result;
  var keyed=diff as Dictionary<string,object>;IEnumerable<object> rows=keyed!=null?(IEnumerable<object>)keyed.Values:(diff as object[]??new object[0]);
  foreach(var row in rows){var f=row as Dictionary<string,object>;if(f==null||!new[]{"f2","f3","f12","f13","f124"}.All(f.ContainsKey))continue;
   string id=f["f13"]+"."+f["f12"];var item=instruments.FirstOrDefault(i=>String.Equals(i.EastId,id,StringComparison.OrdinalIgnoreCase));double price,percent,stamp;
   if(item==null||!Number(f["f2"],out price)||price<=0||!Number(f["f3"],out percent)||!Number(f["f124"],out stamp)||stamp<1577836800||stamp>4102444800)continue;
   var utc=new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(stamp);
   if(utc>NetworkClock.UtcNow.AddMinutes(5))continue;
   result.Add(new IndexQuote{Symbol=item.Key,Name=item.Name,Price=price,Percent=percent,SourceTime=Engine.B(utc),Source="东财"});
  }return result;
 }
 public static List<IndexQuote> East(List<Instrument> items){var ids=items.Where(i=>i.EastId!=null).Select(i=>i.EastId).Distinct().ToArray();if(ids.Length==0)return new List<IndexQuote>();return ParseEast(Get("https://push2.eastmoney.com/api/qt/ulist.np/get?fltt=2&secids="+String.Join(",",ids)+"&fields=f2,f3,f12,f13,f14,f124&_="+DateTime.UtcNow.Ticks,"https://quote.eastmoney.com/",Encoding.UTF8),items);}
 public static List<IndexQuote> Tencent(List<Instrument> items){return QuoteFeed.Parse(Get("https://qt.gtimg.cn/q="+String.Join(",",items.Select(i=>i.Key).ToArray()),"https://gu.qq.com/",Encoding.GetEncoding("GBK")));}
 public static List<IndexQuote> ParseSina(string body,List<Instrument> items){
  var result=new List<IndexQuote>();foreach(Match match in Regex.Matches(body,"hq_str_nf_(\\w+)=\"([^\"]*)\"")){
   var item=items.FirstOrDefault(i=>i.SinaId==match.Groups[1].Value);if(item==null)continue;var f=match.Groups[2].Value.Split(',');if(f.Length<18)continue;
   double first;bool financial=Number(f[0],out first);int priceIndex=financial?3:8,settleIndex=financial?14:10;double price,settlement;DateTime date;
   if(financial&&f.Length<38)continue;
   string stamp=financial?f[36]+" "+f[37]:f[17]+" "+f[1].PadLeft(6,'0');
   if(!Number(f[priceIndex],out price)||price<=0||!Number(f[settleIndex],out settlement)||settlement<=0||!DateTime.TryParseExact(stamp,financial?"yyyy-MM-dd HH:mm:ss":"yyyy-MM-dd HHmmss",CultureInfo.InvariantCulture,DateTimeStyles.None,out date))continue;
   if(date>Engine.B(NetworkClock.UtcNow).AddMinutes(5))continue;
   result.Add(new IndexQuote{Symbol=item.Key,Name=item.Name,Price=price,Percent=(price/settlement-1)*100,SourceTime=date,Source="新浪"});
  }return result;
 }
 public static List<IndexQuote> Sina(List<Instrument> items){var ids=items.Where(i=>i.SinaId!=null).Select(i=>"nf_"+i.SinaId).ToArray();if(ids.Length==0)return new List<IndexQuote>();return ParseSina(Get("https://hq.sinajs.cn/list="+String.Join(",",ids),"https://finance.sina.com.cn/",Encoding.GetEncoding("GBK")),items);}
 public static bool Stale(IndexQuote q,Instrument item,DateTime utc){return item.ExpectedOpen&&(Engine.B(utc)-q.SourceTime).TotalSeconds>15;}
}
public class QuoteBatch {public List<IndexQuote> Quotes=new List<IndexQuote>();public string Status;}
public class FeedRouter {
 readonly Func<List<Instrument>,List<IndexQuote>> east,tencent,sina;DateTime retryEast=DateTime.MinValue;
 public FeedRouter():this(LiveFeed.East,LiveFeed.Tencent,LiveFeed.Sina){}
 public FeedRouter(Func<List<Instrument>,List<IndexQuote>> a,Func<List<Instrument>,List<IndexQuote>> b,Func<List<Instrument>,List<IndexQuote>> c){east=a;tencent=b;sina=c;}
 public QuoteBatch Fetch(List<Instrument> items,DateTime utc){
  var batch=new QuoteBatch();bool eastFailed=DateTime.UtcNow<retryEast;
  if(!eastFailed)try{batch.Quotes=east(items);if(batch.Quotes.Count==0&&items.Count>0)throw new IOException("主源空响应");}catch{eastFailed=true;retryEast=DateTime.UtcNow.AddSeconds(15);}
  var need=items.Where(i=>{var q=batch.Quotes.FirstOrDefault(v=>v.Symbol==i.Key);return q==null||LiveFeed.Stale(q,i,utc);}).ToList();
  var stock=need.Where(i=>!i.Future).ToList();var futures=need.Where(i=>i.Future).ToList();var backups=new List<IndexQuote>();object gate=new object();
  Parallel.Invoke(()=>{if(stock.Count>0)try{var result=tencent(stock);lock(gate)backups.AddRange(result);}catch{}},()=>{if(futures.Count>0)try{var result=sina(futures);lock(gate)backups.AddRange(result);}catch{}});
  foreach(var q in backups){var old=batch.Quotes.FirstOrDefault(v=>v.Symbol==q.Symbol);if(old==null)batch.Quotes.Add(q);else if(q.SourceTime>old.SourceTime){batch.Quotes.Remove(old);batch.Quotes.Add(q);}}
  foreach(var q in batch.Quotes){var i=items.First(v=>v.Key==q.Symbol);q.Status=LiveFeed.Stale(q,i,utc)?"报价滞后":i.ExpectedOpen?"":"休市快照";}
  int missing=items.Count(i=>!batch.Quotes.Any(q=>q.Symbol==i.Key));batch.Status=(eastFailed?"主源异常 · 备用源接管":"东财主源 · 腾讯/新浪备用")+(missing>0?" · "+missing+"项暂无数据":"");return batch;
 }
}
