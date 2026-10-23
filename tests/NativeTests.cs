using System;
using System.Collections.Generic;
using System.IO;
using System.Drawing;
using WhaleComputer;
internal static class NativeTests {
  private static int count;
  private static void Check(bool value,string name){if(!value)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
  private static Dictionary<string,object> Action(string text,string[] risks){return new Dictionary<string,object>{{"kind","wait"},{"description",text},{"risks",risks},{"ms",1}};}
  private static void Main(string[] args){
    Preferences p=new Preferences();
    Check(!p.AutoSensitive,"automation defaults off");
    Check(p.MaxBatches==80,"batch limit defaults to 80");
    PhysicalInputTracker tracker=new PhysicalInputTracker();
    Check(!tracker.Keyboard(-1,0)&&!tracker.Mouse(-1,0)&&tracker.Revision==0,"negative hook notifications ignored");
    Check(!tracker.Keyboard(0,0x10)&&!tracker.Keyboard(0,0x02)&&!tracker.Mouse(0,1)&&!tracker.Mouse(0,2)&&tracker.Revision==0,"injected input excluded including lower-integrity flags");
    Check(tracker.Keyboard(0,0)&&tracker.Mouse(0,0)&&tracker.Revision==2,"physical keyboard and mouse advance the observation revision");
    DateTime captured=new DateTime(2026,10,23,0,0,0,DateTimeKind.Utc);
    Rectangle screenBounds=new Rectangle(0,0,2560,1600);IntPtr foreground=new IntPtr(12);
    Check(ObservationGuard.StaleReason("a","a",captured,captured.AddSeconds(120),screenBounds,screenBounds,foreground,foreground,2,2)==null,"observation valid at exact expiry boundary");
    Check(ObservationGuard.StaleReason("a","b",captured,captured,screenBounds,screenBounds,foreground,foreground,2,2)!=null,"wrong capture ID invalidates observation");
    Check(ObservationGuard.StaleReason("a","a",captured,captured.AddMilliseconds(120001),screenBounds,screenBounds,foreground,foreground,2,2)!=null,"expired observation invalidated");
    Check(ObservationGuard.StaleReason("a","a",captured,captured.AddSeconds(-1),screenBounds,screenBounds,foreground,foreground,2,2)!=null,"clock reversal invalidates observation");
    Check(ObservationGuard.StaleReason("a","a",captured,captured,screenBounds,new Rectangle(0,0,1920,1080),foreground,foreground,2,2)!=null,"resolution change invalidates observation");
    Check(ObservationGuard.StaleReason("a","a",captured,captured,screenBounds,screenBounds,foreground,new IntPtr(13),2,2)!=null,"foreground change invalidates observation");
    Check(ObservationGuard.StaleReason("a","a",captured,captured,screenBounds,screenBounds,foreground,foreground,2,tracker.Revision)==null,"ignored injected input leaves observation valid");
    tracker.Mouse(0,0);
    Check(ObservationGuard.StaleReason("a","a",captured,captured,screenBounds,screenBounds,foreground,foreground,2,tracker.Revision)!=null,"physical input invalidates a previously idle observation");
    Dictionary<string,object> crop=new Dictionary<string,object>{{"x",0},{"y",0},{"width",1920},{"height",1200}};
    Check(CoordinateMap.Crop(screenBounds,1920,1200,crop)==screenBounds,"whole returned image maps exactly to physical display");
    Rectangle source=new Rectangle(400,200,800,600);
    crop=new Dictionary<string,object>{{"x",200},{"y",100},{"width",400},{"height",200}};
    Rectangle region=CoordinateMap.Crop(source,1600,1200,crop);
    Check(region==new Rectangle(500,250,200,100),"2x zoom crop maps back to physical source with offset");
    crop=new Dictionary<string,object>{{"x",1599},{"y",1199},{"width",1},{"height",1}};
    Check(CoordinateMap.Crop(source,1600,1200,crop)==new Rectangle(1199,799,1,1),"one-pixel bottom-right region stays inside source");
    crop["width"]=2;bool invalidCrop=false;try{CoordinateMap.Crop(source,1600,1200,crop);}catch(ArgumentException){invalidCrop=true;}
    Check(invalidCrop,"off-image crop rejected before copying pixels");
    Check(Capture.ImageSize(new Size(2560,1600),1920,0)==new Size(1920,1200),"full capture retains proportional downsampling");
    Check(Capture.ImageSize(new Size(200,100),1920,2)==new Size(400,200),"small region enlarged at 2x");
    Check(Capture.ImageSize(new Size(200,100),1920,1)==new Size(200,100),"1x region preserves native pixel size");
    Size bounded=Capture.ImageSize(new Size(8000,20000),1920,2);
    Check(bounded.Width<=3840&&bounded.Height<=8192&&(long)bounded.Width*bounded.Height<=12000000,"oversized zoom respects image and pixel budget");
    Check(Policy.Classify(Action("购买测试",new string[0]),"test",p).Contains("payment"),"Chinese payment heuristic");
    Check(Policy.Classify(Action("send test",new string[0]),"test",p).Contains("external_message"),"English send heuristic");
    Check(Policy.Classify(Action("普通坐标",new[]{"permissions"}),"test",p).Contains("permissions"),"model-declared category");
    Check(Policy.Classify(Action("点击这里",new string[0]),"删除测试",p).Contains("deletion"),"batch purpose classified");
    p.SensitiveCategories=new[]{"custom","deletion"};p.CustomKeywords=new[]{"实验按钮"};
    Check(!Policy.Classify(Action("付款",new[]{"payment"}),"test",p).Contains("payment"),"user can disable a category");
    Check(Policy.Classify(Action("按实验按钮",new string[0]),"test",p).Contains("custom"),"user custom keyword");
    Check(Policy.Redact(Action("普通文字",new[]{"credentials"})),"credential preview redaction");
    bool threw=false;try{Injector.ParseChord("ctrl+madeup");}catch(ArgumentException){threw=true;}Check(threw,"unknown key rejected before execution");
    threw=false;try{Injector.ParseChord("ctrl+alt+esc");}catch(ArgumentException){threw=true;}Check(threw,"agent cannot invoke emergency chord");
    Check(Injector.ParseChord("ctrl+shift+left").Length==3,"normal chord parsing");
    int[] displays={800,1024,1366,1440,1600,1920,2560,3440,3840};
    foreach(int screen in displays)foreach(int image in new[]{640,1280,1920,2560}) {
      bool bounds=true;int prior=-1;
      for(int x=0;x<image;x++){int mapped=CoordinateMap.Map(x,image,screen);if(mapped<0||mapped>=screen||mapped<prior)bounds=false;prior=mapped;}
      Check(bounds&&CoordinateMap.Map(0,image,screen)==0&&CoordinateMap.Map(image-1,image,screen)==screen-1,"coordinate endpoints and monotonic bounds "+image+" -> "+screen);
    }
    string path=Path.Combine(args[0],"settings-test.json");p.AutoSensitive=true;p.MaxBatches=301;p.Save(path);Preferences loaded=Preferences.Load(path);
    Check(loaded.AutoSensitive&&loaded.CustomKeywords[0]=="实验按钮"&&loaded.SensitiveCategories.Length==2,"both permission choices persist independently");
    Check(loaded.MaxBatches==301,"user-selected long-task batch limit persists");
    loaded.AutoSensitive=false;loaded.Save(path);Check(!Preferences.Load(path).AutoSensitive,"atomic settings replacement");
    foreach(int limit in new[]{0,1001}){loaded.MaxBatches=limit;bool rejected=false;try{loaded.Validate();}catch(ArgumentException){rejected=true;}Check(rejected,"invalid batch limit rejected "+limit);}
    File.WriteAllText(path,"{\"AutoSensitive\":false,\"SensitiveCategories\":[],\"CustomKeywords\":[],\"ManualTimeoutSeconds\":90,\"MaxImageWidth\":1920,\"Grid\":true}");
    Check(Preferences.Load(path).MaxBatches==80,"v1.0 settings migrate without changing existing choices");
    File.WriteAllText(path,"invalid json");threw=false;try{Preferences.Load(path);}catch(ArgumentException){threw=true;}Check(threw,"corrupt settings fail closed");
    Console.WriteLine("NATIVE TESTS "+count+" passed");
  }
}
