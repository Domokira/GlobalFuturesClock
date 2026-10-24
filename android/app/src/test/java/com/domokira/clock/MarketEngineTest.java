package com.domokira.clock;
import org.junit.*;
import static org.junit.Assert.*;
import java.nio.file.*;
import java.time.*;
import java.util.*;

public class MarketEngineTest {
    MarketEngine e;
    @Before public void load()throws Exception{e=new MarketEngine(new String(Files.readAllBytes(Paths.get("src/main/assets/markets.json")),java.nio.charset.StandardCharsets.UTF_8));}
    MarketEngine.Market gold(){return e.markets.stream().filter(m->m.id.equals("shfe-gold")).findFirst().get();}
    @Test public void sundayClosedForAll(){Instant t=Instant.parse("2026-10-25T04:00:00Z");List<MarketEngine.Market> all=new ArrayList<>(e.markets);all.addAll(e.catalog);assertEquals(107,all.size());for(MarketEngine.Market m:all){assertTrue(m.name,e.slices(m,t,MarketEngine.BEIJING).isEmpty());for(int h=0;h<24;h++)assertFalse(m.name,e.open(m,Instant.parse("2026-10-24T16:00:00Z").plusSeconds(h*3600)));}}
    @Test public void overnightGrayAtClose(){Instant close=Instant.parse("2026-10-23T18:30:00Z");assertTrue(e.open(gold(),close.minusSeconds(1)));assertFalse(e.slices(gold(),close.minusSeconds(1),MarketEngine.BEIJING).get(0).ended);assertFalse(e.open(gold(),close));List<MarketEngine.Slice> s=e.slices(gold(),close,MarketEngine.BEIJING);assertEquals(1,s.size());assertEquals(0,s.get(0).start,0);assertEquals(150,s.get(0).end,0);assertTrue(s.get(0).ended);}
    @Test public void holidayAndNight(){assertFalse(e.open(gold(),Instant.parse("2026-09-30T13:00:00Z")));assertFalse(e.open(gold(),Instant.parse("2026-10-01T01:00:00Z")));assertTrue(e.open(gold(),Instant.parse("2026-10-08T01:00:00Z")));assertFalse(e.open(gold(),Instant.parse("2026-10-07T13:00:00Z")));}
    @Test public void timezoneClipsDay(){List<MarketEngine.Span> spans=List.of(new MarketEngine.Span(Instant.parse("2026-10-23T01:00:00Z"),Instant.parse("2026-10-23T07:00:00Z")));Instant t=Instant.parse("2026-10-23T08:00:00Z");assertEquals(540,MarketEngine.project(spans,t,MarketEngine.BEIJING).get(0).start,0);List<MarketEngine.Slice> ny=MarketEngine.project(spans,t,ZoneId.of("America/New_York"));assertEquals(0,ny.get(0).start,0);assertEquals(180,ny.get(0).end,0);}
    @Test public void daylightSavingGapAndRepeat(){ZoneId ny=ZoneId.of("America/New_York");List<MarketEngine.Slice> spring=MarketEngine.project(List.of(new MarketEngine.Span(Instant.parse("2026-03-08T05:00:00Z"),Instant.parse("2026-03-09T04:00:00Z"))),Instant.parse("2026-03-08T12:00:00Z"),ny);assertEquals(2,spring.size());assertEquals(120,spring.get(0).end,0);assertEquals(180,spring.get(1).start,0);List<MarketEngine.Slice> fall=MarketEngine.project(List.of(new MarketEngine.Span(Instant.parse("2026-11-01T04:00:00Z"),Instant.parse("2026-11-02T05:00:00Z"))),Instant.parse("2026-11-01T12:00:00Z"),ny);assertEquals(1,fall.size());assertEquals(1440,fall.get(0).end,0);}
    @Test public void futureAndCurrentRemainColored(){Instant now=Instant.parse("2026-10-23T02:00:00Z");assertTrue(e.open(gold(),now));assertTrue(e.slices(gold(),now,MarketEngine.BEIJING).stream().anyMatch(s->s.start==540&&!s.ended));assertTrue(e.slices(gold(),now,MarketEngine.BEIJING).stream().anyMatch(s->s.start==1260&&!s.ended));}
    @Test public void invalidQuoteIgnored()throws Exception{String bad="{\"data\":{\"diff\":[{\"f2\":\"-\",\"f3\":0,\"f12\":\"000001\",\"f13\":1,\"f124\":1792800000}]}}";assertTrue(NetworkServices.parseEast(bad,NetworkServices.indexes(),Instant.parse("2026-10-25T00:00:00Z")).isEmpty());}
    @Test public void validQuoteParsed()throws Exception{String good="{\"data\":{\"diff\":[{\"f2\":3000.25,\"f3\":1.5,\"f12\":\"000001\",\"f13\":1,\"f124\":1792800000}]}}";List<NetworkServices.Quote> q=NetworkServices.parseEast(good,NetworkServices.indexes(),Instant.parse("2026-10-25T00:00:00Z"));assertEquals(1,q.size());assertEquals(3000.25,q.get(0).price,0);}
}
