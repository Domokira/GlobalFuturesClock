using System;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Forms;
public static class Validation {
 static int count;
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
 [STAThread] public static int Main(string[] args){try{
  if(args.Contains("--network")){Console.WriteLine("NTP success: "+NetworkClock.Sync()+" / "+NetworkClock.Status);var snapshot=QuoteFeed.Fetch();foreach(var q in snapshot)Console.WriteLine(q.Name+" "+q.Price+" "+q.Percent+" "+q.SourceTime.ToString("s"));Engine.data=new JavaScriptSerializer().Deserialize<Data>(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"markets.json")));Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);using(var live=new Clock()){live.OfflinePreview=true;live.UseQuoteSnapshot(snapshot);live.RenderPreview(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"preview-live.png"),false,true,true,false,NetworkClock.UtcNow);}return 0;}
  Engine.data=new JavaScriptSerializer().Deserialize<Data>(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"markets.json")));
  Check(Math.Abs(Clock.DialAngle(600,600,true)+90)<0.0001,"current instant aligned with fixed top line");
  Check(Math.Abs(Clock.DialAngle(660,600,true)+75)<0.0001,"future time clockwise of NOW");
  Check(Math.Abs(Clock.DialAngle(600,660,true)+105)<0.0001,"dial moves counterclockwise with elapsed time");
  Check(Math.Abs((Clock.DialAngle(0,1440,true)-Clock.DialAngle(0,0,true))+360)<0.0001,"midnight rotation wraps one full turn");
  var t=new DateTime(2026,10,23,8,0,0,DateTimeKind.Utc);var request=new byte[48];request[0]=0x23;NetworkClock.WriteTimestamp(request,40,t);
  var response=new byte[48];response[0]=0x24;response[1]=2;Array.Copy(request,40,response,24,8);NetworkClock.WriteTimestamp(response,32,t.AddMilliseconds(150));NetworkClock.WriteTimestamp(response,40,t.AddMilliseconds(160));
  Check(Math.Abs(NetworkClock.Validate(request,response,t,t.AddMilliseconds(110))-100)<1,"NTP four-timestamp offset includes network delay");
  Check(Math.Abs((NetworkClock.ReadTimestamp(request,40)-t).TotalMilliseconds)<1,"NTP timestamp roundtrip");
  response[24]^=1;bool rejected=false;try{NetworkClock.Validate(request,response,t,t);}catch(IOException){rejected=true;}Check(rejected,"NTP rejects mismatched origin");response[24]^=1;response[1]=0;rejected=false;try{NetworkClock.Validate(request,response,t,t);}catch(IOException){rejected=true;}Check(rejected,"NTP rejects kiss-of-death packet");
  var fields=Enumerable.Repeat("",33).ToArray();fields[3]="3210.12";fields[30]="20261023150000";fields[32]="-1.23";string body="v_sh000001=\""+String.Join("~",fields)+"\";";
  var quotes=QuoteFeed.Parse(body);Check(quotes.Count==1&&quotes[0].Percent==-1.23&&quotes[0].SourceTime.Day==23,"quote fields and source timestamp parsed");
  Check(QuoteFeed.Parse("v_sh000001=\"broken\";").Count==0,"malformed quote rejected");Check(QuoteFeed.Parse(body.Replace("3210.12","NaN")).Count==0,"nonfinite quote rejected");
  Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
  string folder=AppDomain.CurrentDomain.BaseDirectory;
  using(var form=new Clock()){form.OfflinePreview=true;form.RenderPreview(Path.Combine(folder,"preview-full.png"),false,true,true,false,t);form.RenderPreview(Path.Combine(folder,"preview-dial.png"),false,false,false,false,t);form.RenderPreview(Path.Combine(folder,"preview-timeline.png"),false,true,true,true,t);form.RenderPreview(Path.Combine(folder,"preview-compact.png"),true,true,false,false,t);form.RenderPreview(Path.Combine(folder,"preview-midnight.png"),false,true,false,false,new DateTime(2026,10,23,16,0,0,DateTimeKind.Utc));}
  Console.WriteLine(count+" checks passed; five previews rendered.");return 0;
 }catch(Exception ex){Console.WriteLine(ex);return 1;}}
}
