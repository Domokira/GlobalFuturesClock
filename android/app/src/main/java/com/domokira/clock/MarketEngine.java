package com.domokira.clock;

import org.json.*;
import java.time.*;
import java.util.*;

public final class MarketEngine {
    public static final ZoneId BEIJING = ZoneId.of("Asia/Shanghai");
    public static class Session { LocalTime start,end; String days; boolean night; }
    public static class Market {
        public String id,name,product,exchange,code,note; public ZoneId zone; public boolean china;
        public final List<Session> sessions=new ArrayList<>();
    }
    public static class Span {
        public final Instant start,end;
        Span(Instant a,Instant b){start=a;end=b;}
    }
    public static class Slice {
        public final double start,end; public final boolean ended;
        Slice(double a,double b,boolean old){start=a;end=b;ended=old;}
    }
    public final List<Market> markets=new ArrayList<>(),catalog=new ArrayList<>();
    public final Set<LocalDate> holidays=new HashSet<>();
    private final Map<Market,List<Span>> cache=new HashMap<>();
    private final Map<Market,LocalDate> cacheDate=new HashMap<>();
    public MarketEngine(String json)throws JSONException{
        JSONObject data=new JSONObject(json);
        parse(data.getJSONArray("markets"),markets);parse(data.getJSONArray("catalog"),catalog);
        JSONArray h=data.getJSONArray("holidays");for(int i=0;i<h.length();i++)holidays.add(LocalDate.parse(h.getString(i)));
    }
    private static ZoneId zone(String z){switch(z){
        case "China Standard Time":return BEIJING;
        case "Central Standard Time":return ZoneId.of("America/Chicago");
        case "Eastern Standard Time":return ZoneId.of("America/New_York");
        case "Tokyo Standard Time":return ZoneId.of("Asia/Tokyo");
        case "India Standard Time":return ZoneId.of("Asia/Kolkata");
        case "GMT Standard Time":return ZoneId.of("Europe/London");
        default:throw new IllegalArgumentException("Unknown zone: "+z);
    }}
    private void parse(JSONArray a,List<Market> out)throws JSONException{
        for(int i=0;i<a.length();i++){JSONObject o=a.getJSONObject(i);Market m=new Market();
            m.id=o.optString("id");m.name=o.getString("name");m.product=o.optString("product");m.exchange=o.optString("exchange");m.code=o.optString("code");m.note=o.optString("note");m.china=o.optBoolean("china");m.zone=zone(o.getString("zone"));
            JSONArray sessions=o.getJSONArray("sessions");for(int j=0;j<sessions.length();j++){JSONObject s=sessions.getJSONObject(j);Session v=new Session();v.start=LocalTime.parse(s.getString("start"));v.end=LocalTime.parse(s.getString("end"));v.days=s.getString("days");v.night=s.optBoolean("night");m.sessions.add(v);}out.add(m);
        }
    }
    public static boolean weekday(LocalDate d){return d.getDayOfWeek().getValue()<6;}
    public List<Span> intervals(Market m,Instant now){
        LocalDate date=now.atZone(m.zone).toLocalDate();if(date.equals(cacheDate.get(m)))return cache.get(m);
        List<Span> out=new ArrayList<>();for(int i=-2;i<=18;i++){LocalDate d=date.plusDays(i);
            for(Session s:m.sessions){if(!s.days.contains(Integer.toString(d.getDayOfWeek().getValue()%7)))continue;
                if(m.china&&holidays.contains(d))continue;
                if(m.china&&s.night){LocalDate next=d.plusDays(1);while(!weekday(next))next=next.plusDays(1);if(holidays.contains(next))continue;}
                LocalTime end=s.end;
                if(m.id.equals("mcx")){boolean dst=ZoneId.of("America/New_York").getRules().isDaylightSavings(d.atTime(12,0).atZone(ZoneId.of("America/New_York")).toInstant());end=LocalTime.parse(dst?"23:30":"23:55");}
                LocalDate finish=end.isAfter(s.start)?d:d.plusDays(1);
                out.add(new Span(d.atTime(s.start).atZone(m.zone).toInstant(),finish.atTime(end).atZone(m.zone).toInstant()));
            }
        }out.sort(Comparator.comparing(v->v.start));cacheDate.put(m,date);cache.put(m,out);return out;
    }
    public boolean open(Market m,Instant now){for(Span s:intervals(m,now))if(!now.isBefore(s.start)&&now.isBefore(s.end))return true;return false;}
    public List<Slice> slices(Market m,Instant now,ZoneId zone){return project(intervals(m,now),now,zone);}
    public static List<Slice> project(List<Span> spans,Instant now,ZoneId zone){
        LocalDate date=now.atZone(zone).toLocalDate();Instant start=date.atStartOfDay(zone).toInstant(),end=date.plusDays(1).atStartOfDay(zone).toInstant();
        List<Slice> out=new ArrayList<>();
        for(Span s:spans){Instant a=s.start.isAfter(start)?s.start:start,finish=s.end.isBefore(end)?s.end:end;boolean ended=!s.end.isAfter(now);
            while(a.isBefore(finish)){Instant b=a.plusSeconds(3600);java.time.zone.ZoneOffsetTransition transition=zone.getRules().nextTransition(a);if(transition!=null&&transition.getInstant().isBefore(b))b=transition.getInstant();if(b.isAfter(finish))b=finish;
                ZoneOffset offset=zone.getRules().getOffset(a);double x=Duration.between(date.atStartOfDay(),LocalDateTime.ofInstant(a,offset)).toMillis()/60000.0;
                double y=x+Duration.between(a,b).toMillis()/60000.0;
                if(y>x)out.add(new Slice(Math.max(0,x),Math.min(1440,y),ended));a=b;
            }
        }
        out.sort(Comparator.comparingDouble(v->v.start));List<Slice> result=new ArrayList<>();
        for(Slice s:out){if(!result.isEmpty()){Slice last=result.get(result.size()-1);if(last.ended==s.ended&&s.start<=last.end+0.00001){result.set(result.size()-1,new Slice(last.start,Math.max(last.end,s.end),last.ended));continue;}}result.add(s);}return result;
    }
    public String countdown(Market m,Instant now){for(Span s:intervals(m,now)){if(!s.end.isAfter(now))continue;boolean active=!s.start.isAfter(now);long sec=Duration.between(now,active?s.end:s.start).getSeconds();return (active?"距收盘 ":"距开盘 ")+String.format(Locale.ROOT,"%02d:%02d:%02d",sec/3600,sec/60%60,sec%60);}return "暂无后续时段";}
    public static String minute(double v){int m=(int)v;return String.format(Locale.ROOT,"%02d:%02d",m/60,m%60);}
}
