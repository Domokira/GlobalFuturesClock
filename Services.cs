using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

// A small SNTP client. It corrects only the application's clock, never Windows.
public static class NetworkClock {
 static readonly object gate=new object();
 static DateTime anchor; static long stamp; static bool synced; static int busy;
 public static string Status="系统时间 · 尚未校时";
 public static DateTime LastSync=DateTime.MinValue;
 public static DateTime UtcNow { get { lock(gate) return synced?anchor.AddSeconds((Stopwatch.GetTimestamp()-stamp)/(double)Stopwatch.Frequency):DateTime.UtcNow; } }
 public static bool Busy { get { return Interlocked.CompareExchange(ref busy,0,0)!=0; } }
 static readonly DateTime epoch=new DateTime(1900,1,1,0,0,0,DateTimeKind.Utc);
 public static void WriteTimestamp(byte[] b,int p,DateTime value){double s=(value-epoch).TotalSeconds;uint whole=(uint)s;uint fraction=(uint)((s-Math.Floor(s))*4294967296.0);for(int i=3;i>=0;i--){b[p+i]=(byte)whole;whole>>=8;b[p+4+i]=(byte)fraction;fraction>>=8;}}
 public static DateTime ReadTimestamp(byte[] b,int p){ulong whole=0,fraction=0;for(int i=0;i<4;i++){whole=(whole<<8)|b[p+i];fraction=(fraction<<8)|b[p+4+i];}return epoch.AddSeconds(whole+fraction/4294967296.0);}
 public static double Validate(byte[] request,byte[] response,DateTime sent,DateTime received){
  if(response.Length<48||(response[0]&7)!=4||((response[0]>>3)&7)<3||(response[0]>>6)==3||response[1]==0||response[1]>15)throw new IOException("无效 NTP 响应");
  for(int i=0;i<8;i++)if(response[24+i]!=request[40+i])throw new IOException("NTP 请求不匹配");
  var t2=ReadTimestamp(response,32);var t3=ReadTimestamp(response,40);
  double delay=(received-sent).TotalMilliseconds-(t3-t2).TotalMilliseconds;
  if(t3<t2||t2.Year<2020||delay< -5||delay>3000)throw new IOException("NTP 响应延迟异常");
  return ((t2-sent).TotalMilliseconds+(t3-received).TotalMilliseconds)/2;
 }
 public static bool Sync(){
  if(Interlocked.Exchange(ref busy,1)!=0)return false;
  Status="NTP 校时中…";
  try{foreach(var host in new[]{"ntp.aliyun.com","time.cloudflare.com","time.windows.com"})try{
   var lookup=Dns.BeginGetHostAddresses(host,null,null);
   using(var wait=lookup.AsyncWaitHandle)if(!wait.WaitOne(2500))throw new IOException("DNS 超时");
   var address=Dns.EndGetHostAddresses(lookup).First(x=>x.AddressFamily==AddressFamily.InterNetwork);
   using(var client=new UdpClient(AddressFamily.InterNetwork)){
    client.Client.ReceiveTimeout=2500;client.Client.SendTimeout=2500;client.Connect(address,123);
    var request=new byte[48];request[0]=0x23;var sent=DateTime.UtcNow;WriteTimestamp(request,40,sent);
    var elapsed=Stopwatch.StartNew();client.Send(request,request.Length);IPEndPoint remote=null;var response=client.Receive(ref remote);var received=sent.Add(elapsed.Elapsed);
    double offset=Validate(request,response,sent,received);
    lock(gate){anchor=received.AddMilliseconds(offset);stamp=Stopwatch.GetTimestamp();synced=true;LastSync=anchor;}
    Status="NTP 已同步 · "+host+" · 偏差 "+offset.ToString("+0;-0;0",CultureInfo.InvariantCulture)+" ms";return true;
   }
  }catch(Exception){ }
  Status=synced?"NTP 重试失败 · 沿用上次校时":"NTP 不可用 · 使用系统时间";return false;
  }finally{Interlocked.Exchange(ref busy,0);}
 }
}

public class IndexQuote { public string Symbol,Name,Source="腾讯",Status=""; public double Price,Percent; public DateTime SourceTime; }
public static class QuoteFeed {
 public static readonly string[] Symbols={"sh000001","sz399001","sz399006","sh000300","sh000905","sh000852"};
 public static readonly string[] Names={"上证指数","深证成指","创业板指","沪深300","中证500","中证1000"};
 public static List<IndexQuote> Parse(string body){
  var quotes=new List<IndexQuote>();
  foreach(Match match in Regex.Matches(body,"v_(\\w+)=\"([^\"]*)\"")){
   string symbol=match.Groups[1].Value;int index=Array.IndexOf(Symbols,symbol);if(index<0)continue;
   var fields=match.Groups[2].Value.Split('~');double price,percent;DateTime date;
   if(fields.Length<=32||!Double.TryParse(fields[3],NumberStyles.Float,CultureInfo.InvariantCulture,out price)||price<=0||Double.IsNaN(price)||Double.IsInfinity(price)||!Double.TryParse(fields[32],NumberStyles.Float,CultureInfo.InvariantCulture,out percent)||Double.IsNaN(percent)||Double.IsInfinity(percent)||!DateTime.TryParseExact(fields[30],"yyyyMMddHHmmss",CultureInfo.InvariantCulture,DateTimeStyles.None,out date))continue;
   if(quotes.Any(q=>q.Symbol==symbol))continue;
   quotes.Add(new IndexQuote{Symbol=symbol,Name=Names[index],Price=price,Percent=percent,SourceTime=date});
  }
  return quotes;
 }
 public static List<IndexQuote> Fetch(){
  ServicePointManager.SecurityProtocol|=(SecurityProtocolType)3072;
  var request=(HttpWebRequest)WebRequest.Create("https://qt.gtimg.cn/q="+String.Join(",",Symbols));
  request.Timeout=8000;request.ReadWriteTimeout=8000;request.UserAgent="GlobalFuturesClock/1.3";request.Referer="https://gu.qq.com/";
  using(var response=request.GetResponse())using(var stream=response.GetResponseStream())using(var reader=new StreamReader(stream,Encoding.GetEncoding("GBK"))){var result=Parse(reader.ReadToEnd());if(result.Count==0)throw new IOException("接口未返回有效指数");return result;}
 }
}
