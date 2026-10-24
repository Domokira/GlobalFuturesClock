package com.domokira.clock;
import android.test.ActivityInstrumentationTestCase2;
public class LaunchTest extends ActivityInstrumentationTestCase2<MainActivity> {
    public LaunchTest(){super(MainActivity.class);}
    public void testLaunchAndPreferences()throws Throwable{
        MainActivity a=getActivity();assertNotNull(a.engine);assertEquals(91,a.engine.catalog.size());
        runTestOnUiThread(()->{a.zoneIndex=3;a.group=2;a.rebuild();a.refresh();assertNotNull(a.time.getText());assertTrue(a.visible.size()>0);a.showIndexes=false;a.showFutures=false;a.rebuild();assertTrue(a.requested.isEmpty());a.zoneIndex=0;a.group=0;a.rebuild();});
        getInstrumentation().waitForIdleSync();assertFalse(a.isFinishing());
    }
}
