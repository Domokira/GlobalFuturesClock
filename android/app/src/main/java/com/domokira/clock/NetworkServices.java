package com.domokira.clock;

import java.io.*;
import java.net.*;
import java.nio.charset.Charset;
import java.time.*;
import java.time.format.DateTimeFormatter;
import java.util.*;
import java.util.regex.*;
import org.json.*;

public final class NetworkServices {
    public static class Instrument {String key,name,east,sina;boolean future,open;}
    public static class Quote {String key,source;double price,percent;Instant stamp;}
    public static List<Instrument> indexes(){String[] codes={"sh000001","sz399001","sz399006","sh000300","sh000016","sh000905"};String[] names={"上证指数","深证成指","创业板指","沪深300","上证50","中证500"};List<Instrument> out=new ArrayList<>();for(int n=0;n<codes.length;n++){Instrument i=new Instrument();i.key=codes[n];i.name=names[n];i.east=(i.key.startsWith("sh")?"1.":"0.")+i.key.substring(2);out.add(i);}return out;}
    public static Instrument future(MarketEngine.Market m){Instrument i=new Instrument();i.key=m.product;i.name=m.name;i.future=true;String id=null;switch(m.exchange){case "上期所":id="113";break;case "上期能源":id="142";break;case "大商所":id="114";break;case "郑商所":id="115";break;case "广期所":id="225";break;case "中金所":id="220";break;}if(id!=null&&!m.product.contains("-")){i.east=id+"."+((id.equals("115")||id.equals("220"))?m.product+"M":m.product.toLowerCase(Locale.ROOT)+"m");i.sina=m.product+"0";}return i;}
    private static String get(String url,String referer,String charset)throws Exception{
        HttpURLConnection c=(HttpURLConnection)new URL(url).openConnection();c.setConnectTimeout(1800);c.setReadTimeout(1800);c.setRequestProperty("Referer",referer);c.setRequestProperty("User-Agent","GlobalFuturesClock-Android/1.4.1");c.setUseCaches(false);
        try(InputStream in=c.getInputStream();ByteArrayOutputStream out=new ByteArrayOutputStream()){byte[] b=new byte[8192];int n;while((n=in.read(b))!=-1){out.write(b,0,n);if(out.size()>2000000)throw new IOException("Response too large");}return out.toString(Charset.forName(charset).name());}finally{c.disconnect();}
    }
    private static boolean valid(Quote q,Instant now){return q.price>0&&Double.isFinite(q.price)&&Double.isFinite(q.percent)&&q.stamp!=null&&q.stamp.isAfter(Instant.parse("2020-01-01T00:00:00Z"))&&!q.stamp.isAfter(now.plusSeconds(300));}
    static List<Quote> parseEast(String body,List<Instrument> items,Instant now)throws Exception{
        List<Quote> out=new ArrayList<>();JSONObject data=new JSONObject(body).optJSONObject("data");if(data==null)return out;Object diff=data.opt("diff");JSONArray rows=new JSONArray();if(diff instanceof JSONArray)rows=(JSONArray)diff;else if(diff instanceof JSONObject){Iterator<String> keys=((JSONObject)diff).keys();while(keys.hasNext())rows.put(((JSONObject)diff).get(keys.next()));}
        for(int n=0;n<rows.length();n++){JSONObject row=rows.getJSONObject(n);String id=row.optString("f13")+"."+row.optString("f12");for(Instrument i:items)if(id.equalsIgnoreCase(i.east)){Quote q=new Quote();q.key=i.key;q.source="东财";q.price=row.optDouble("f2",Double.NaN);q.percent=row.optDouble("f3",Double.NaN);q.stamp=Instant.ofEpochSecond(row.optLong("f124"));if(valid(q,now))out.add(q);}}
        return out;
    }
    private long retryEast;
    public Map<String,Quote> fetch(List<Instrument> items,Instant now){
        Map<String,Quote> result=new HashMap<>();List<String> ids=new ArrayList<>();for(Instrument i:items)if(i.east!=null)ids.add(i.east);
        if(!ids.isEmpty()&&System.currentTimeMillis()>=retryEast)try{List<Quote> quotes=parseEast(get("https://push2.eastmoney.com/api/qt/ulist.np/get?fltt=2&secids="+String.join(",",ids)+"&fields=f2,f3,f12,f13,f14,f124&_="+System.currentTimeMillis(),"https://quote.eastmoney.com/","UTF-8"),items,now);if(quotes.isEmpty())throw new IOException();for(Quote q:quotes)result.put(q.key,q);}catch(Exception e){retryEast=System.currentTimeMillis()+15000;}
        List<Instrument> stocks=new ArrayList<>(),futures=new ArrayList<>();for(Instrument i:items){Quote q=result.get(i.key);if(q==null||(i.open&&q.stamp.isBefore(now.minusSeconds(15)))){if(i.future&&i.sina!=null)futures.add(i);else if(!i.future)stocks.add(i);}}
        if(!stocks.isEmpty())try{List<String> keys=new ArrayList<>();for(Instrument i:stocks)keys.add(i.key);String body=get("https://qt.gtimg.cn/q="+String.join(",",keys),"https://gu.qq.com/","GBK");for(Instrument i:stocks){Matcher m=Pattern.compile("v_"+i.key+"=\"([^\"]*)\"").matcher(body);if(!m.find())continue;String[] f=m.group(1).split("~",-1);if(f.length<33)continue;try{Quote q=new Quote();q.key=i.key;q.source="腾讯";q.price=Double.parseDouble(f[3]);q.percent=Double.parseDouble(f[32]);q.stamp=LocalDateTime.parse(f[30],DateTimeFormatter.ofPattern("yyyyMMddHHmmss")).atZone(MarketEngine.BEIJING).toInstant();merge(result,q,now);}catch(Exception ignored){}}}catch(Exception ignored){}
        if(!futures.isEmpty())try{List<String> keys=new ArrayList<>();for(Instrument i:futures)keys.add("nf_"+i.sina);String body=get("https://hq.sinajs.cn/list="+String.join(",",keys),"https://finance.sina.com.cn/","GBK");for(Instrument i:futures){Matcher m=Pattern.compile("hq_str_nf_"+i.sina+"=\"([^\"]*)\"").matcher(body);if(!m.find())continue;String[] f=m.group(1).split(",",-1);if(f.length<18)continue;try{boolean fin=f[0].matches("[0-9.]+");if(fin&&f.length<38)continue;Quote q=new Quote();q.key=i.key;q.source="新浪";q.price=Double.parseDouble(f[fin?3:8]);double settlement=Double.parseDouble(f[fin?14:10]);q.percent=(q.price/settlement-1)*100;String stamp=fin?f[36]+" "+f[37]:f[17]+" "+String.format(Locale.ROOT,"%06d",Integer.parseInt(f[1]));q.stamp=LocalDateTime.parse(stamp,DateTimeFormatter.ofPattern(fin?"yyyy-MM-dd HH:mm:ss":"yyyy-MM-dd HHmmss")).atZone(MarketEngine.BEIJING).toInstant();merge(result,q,now);}catch(Exception ignored){}}}catch(Exception ignored){}
        return result;
    }
    private static void merge(Map<String,Quote> out,Quote q,Instant now){Quote old=out.get(q.key);if(valid(q,now)&&(old==null||q.stamp.isAfter(old.stamp)))out.put(q.key,q);}
    private static final long NTP_EPOCH=2208988800L;
    private static void writeTime(byte[] b,int p,long ms){long sec=ms/1000+NTP_EPOCH,frac=(ms%1000)*0x100000000L/1000;for(int i=3;i>=0;i--){b[p+i]=(byte)sec;sec>>>=8;b[p+4+i]=(byte)frac;frac>>>=8;}}
    private static long readTime(byte[] b,int p){long sec=0,frac=0;for(int i=0;i<4;i++){sec=(sec<<8)|(b[p+i]&255);frac=(frac<<8)|(b[p+4+i]&255);}return (sec-NTP_EPOCH)*1000+frac*1000/0x100000000L;}
    public static long sync()throws Exception{
        Exception last=new IOException("NTP unavailable");for(String host:new String[]{"ntp.aliyun.com","time.cloudflare.com"})try(DatagramSocket socket=new DatagramSocket()){
            socket.setSoTimeout(2500);socket.connect(InetAddress.getByName(host),123);byte[] request=new byte[48];request[0]=0x23;long t1=System.currentTimeMillis(),mono=android.os.SystemClock.elapsedRealtime();writeTime(request,40,t1);socket.send(new DatagramPacket(request,48));byte[] response=new byte[48];DatagramPacket p=new DatagramPacket(response,48);socket.receive(p);long t4=t1+android.os.SystemClock.elapsedRealtime()-mono;
            int mode=response[0]&7,stratum=response[1]&255;if(p.getLength()<48||mode!=4||(response[0]&192)==192||stratum<1||stratum>15)throw new IOException("Invalid NTP header");for(int i=0;i<8;i++)if(response[24+i]!=request[40+i])throw new IOException("NTP origin mismatch");long t2=readTime(response,32),t3=readTime(response,40);if(t3<t2||t3<1577836800000L||t4-t1>5000)throw new IOException("Invalid NTP time");return ((t2-t1)+(t3-t4))/2;
        }catch(Exception e){last=e;}throw last;
    }
}
