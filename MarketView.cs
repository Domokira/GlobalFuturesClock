using System;
using System.Collections.Generic;
using System.Linq;

public class ClockSlice { public double Start,End; }
public static class MarketView {
 // Project UTC intervals onto a local civil day. Split at offset changes so
 // spring-forward gaps and repeated fall-back hours do not stretch the dial.
 public static List<ClockSlice> Project(IEnumerable<Span> spans,DateTime utc,TimeZoneInfo zone){
  var date=TimeZoneInfo.ConvertTimeFromUtc(utc,zone).Date;
  var start=TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(date,DateTimeKind.Unspecified),zone);
  var end=TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(date.AddDays(1),DateTimeKind.Unspecified),zone);
  var pieces=new List<ClockSlice>();
  foreach(var span in spans){var a=span.start>start?span.start:start;var finish=span.end<end?span.end:end;
   while(a<finish){var b=new DateTime(a.Year,a.Month,a.Day,a.Hour,0,0,DateTimeKind.Utc).AddHours(1);if(b>finish)b=finish;
    var offset=zone.GetUtcOffset(a.AddTicks((b-a).Ticks/2));
    double x=Math.Max(0,(a+offset-date).TotalMinutes),y=Math.Min(1440,(b+offset-date).TotalMinutes);
    if(y>x)pieces.Add(new ClockSlice{Start=x,End=y});a=b;
   }
  }
  var result=new List<ClockSlice>();foreach(var p in pieces.OrderBy(v=>v.Start)){if(result.Count>0&&p.Start<=result[result.Count-1].End+0.0001)result[result.Count-1].End=Math.Max(result[result.Count-1].End,p.End);else result.Add(p);}return result;
 }
 public static string Format(double minutes){return minutes>=1440?"24:00":TimeSpan.FromMinutes(minutes).ToString(@"hh\:mm");}
 public static string Today(Market market,DateTime utc,TimeZoneInfo zone){return String.Join("  /  ",Project(Engine.Intervals(market,utc),utc,zone).Select(s=>Format(s.Start)+"–"+Format(s.End)).ToArray());}
}
