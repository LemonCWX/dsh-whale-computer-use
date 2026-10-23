using System;
using System.Collections.Generic;
using System.IO;
using WhaleComputer;
internal static class NativeTests {
  private static int count;
  private static void Check(bool value,string name){if(!value)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
  private static Dictionary<string,object> Action(string text,string[] risks){return new Dictionary<string,object>{{"kind","wait"},{"description",text},{"risks",risks},{"ms",1}};}
  private static void Main(string[] args){
    Preferences p=new Preferences();
    Check(!p.AutoSensitive,"automation defaults off");
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
    string path=Path.Combine(args[0],"settings-test.json");p.AutoSensitive=true;p.Save(path);Preferences loaded=Preferences.Load(path);
    Check(loaded.AutoSensitive&&loaded.CustomKeywords[0]=="实验按钮"&&loaded.SensitiveCategories.Length==2,"both permission choices persist independently");
    loaded.AutoSensitive=false;loaded.Save(path);Check(!Preferences.Load(path).AutoSensitive,"atomic settings replacement");
    File.WriteAllText(path,"invalid json");threw=false;try{Preferences.Load(path);}catch(ArgumentException){threw=true;}Check(threw,"corrupt settings fail closed");
    Console.WriteLine("NATIVE TESTS "+count+" passed");
  }
}
