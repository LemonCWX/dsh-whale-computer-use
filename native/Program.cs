using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace WhaleComputer {
  internal static class Program {
    internal const string Version="1.1.1";
    internal static readonly object OutputLock=new object();
    private static StreamWriter output;
    internal static void Emit(object value) {
      lock(OutputLock) { output.WriteLine(new JavaScriptSerializer{MaxJsonLength=32*1024*1024}.Serialize(value));output.Flush(); }
    }
    [STAThread] private static void Main(string[] args) {
      output=new StreamWriter(Console.OpenStandardOutput(),new UTF8Encoding(false));
      try {
        try{Win.SetProcessDpiAwarenessContext(new IntPtr(-4));}catch(EntryPointNotFoundException){Win.SetProcessDPIAware();}
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        string settings=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DSHWhaleComputer","settings.json"),testSurface=null;
        for(int i=0;i<args.Length;i++){
          if(args[i]=="--settings"&&i+1<args.Length)settings=Path.GetFullPath(args[++i]);
          else if(args[i]=="--test-surface"&&i+1<args.Length)testSurface=Path.GetFullPath(args[++i]);
          else throw new ArgumentException("未知启动参数。");
        }
        bool first;
        using(Mutex single=new Mutex(true,"Local\\DSHWhaleComputerUse-v1",out first)) {
          if(!first)throw new InvalidOperationException("已有鲸鱼娘桌面操控进程。请先结束另一个实例。");
          try{Application.Run(new Host(settings,testSurface));}finally{single.ReleaseMutex();}
        }
      } catch(Exception e){Console.Error.WriteLine(e.Message);Environment.ExitCode=1;}
    }
  }

  internal sealed class Shot {
    internal string Id;internal DateTime Time;internal Rectangle Bounds,ScreenBounds;internal IntPtr Foreground;internal long PhysicalInput;
  }
  internal sealed class Host : Form {
    private readonly string settingsPath,testSurfacePath;
    private long testSurfaceHandle;
    private Preferences settings;
    private readonly Overlay overlay=new Overlay();
    private NotifyIcon tray;
    private Form dialog;
    private Shot shot;
    private volatile bool stopped,working,cancelled,manual,closing;
    private string cancelReason="已取消";
    private readonly ManualResetEventSlim manualDone=new ManualResetEventSlim(false);
    private readonly Injector input;
    private readonly PhysicalInputTracker physicalInput=new PhysicalInputTracker();
    private Win.Hook keyboardCallback,mouseCallback;private IntPtr keyboardHook,mouseHook;
    private bool stopHotkey,interjectHotkey;
    private DateTime lastRequest=DateTime.UtcNow;
    private Thread worker;
    internal Host(string path,string testSurface) {
      settingsPath=path;testSurfacePath=testSurface;input=new Injector(Check);
      ShowInTaskbar=false;FormBorderStyle=FormBorderStyle.None;Size=new Size(1,1);Opacity=0;StartPosition=FormStartPosition.Manual;Location=new Point(-10000,-10000);
      settings=Preferences.Load(path);
      IntPtr createdHandle=Handle;
    }
    protected override void SetVisibleCore(bool value){base.SetVisibleCore(false);}
    protected override void OnLoad(EventArgs e){base.OnLoad(e);}
    protected override void OnHandleCreated(EventArgs e) {
      base.OnHandleCreated(e);
      stopHotkey=Win.RegisterHotKey(Handle,1,0x4003,27);
      interjectHotkey=Win.RegisterHotKey(Handle,2,0x4006,73);
      if(!stopHotkey||!interjectHotkey) {
        BeginInvoke((Action)delegate{Console.Error.WriteLine("无法注册 Ctrl+Alt+Esc 或 Ctrl+Shift+I，可能被旧版桌面副驾占用。请关闭冲突程序后重新启用插件。");Close();});return;
      }
      keyboardCallback=Keyboard;mouseCallback=Mouse;
      keyboardHook=Win.SetWindowsHookEx(13,keyboardCallback,Win.GetModuleHandle(null),0);
      mouseHook=Win.SetWindowsHookEx(14,mouseCallback,Win.GetModuleHandle(null),0);
      if(keyboardHook==IntPtr.Zero||mouseHook==IntPtr.Zero){BeginInvoke((Action)delegate{Console.Error.WriteLine("无法安装用户接管检测，已拒绝启动。");Close();});return;}
      ContextMenuStrip menu=new ContextMenuStrip();
      menu.Items.Add("打开设置",null,delegate{OpenSettings();});
      menu.Items.Add("插话 / 手动完成",null,delegate{OpenInterject();});
      menu.Items.Add("恢复桌面操控",null,delegate{if(!working){stopped=false;cancelled=false;shot=null;Notice("已恢复。下次模型调用会重新截图。");Program.Emit(new{ @event="resumed" });}});
      menu.Items.Add("停止操控  Ctrl+Alt+Esc",null,delegate{Stop("用户停止了操控",true);});
      menu.Items.Add("退出本机助手",null,delegate{Stop("用户关闭了助手",true);Shutdown();});
      tray=new NotifyIcon{Icon=SystemIcons.Information,Text="鲸鱼娘桌面操控",Visible=true,ContextMenuStrip=menu};tray.DoubleClick+=delegate{OpenSettings();};
      System.Windows.Forms.Timer watchdog=new System.Windows.Forms.Timer{Interval=5000};
      watchdog.Tick+=delegate{if(!working&&overlay.Visible&&(DateTime.UtcNow-lastRequest).TotalMinutes>3){overlay.Hide();shot=null;}};watchdog.Start();
      FormClosed+=delegate{watchdog.Stop();watchdog.Dispose();};
      Program.Emit(new{@event="ready",version=Program.Version,settings=settings,stopHotkey="Ctrl+Alt+Esc",interjectHotkey="Ctrl+Shift+I"});
      Thread reader=new Thread(ReadLoop){IsBackground=true,Name="Whale input pipe"};reader.Start();
    }
    private IntPtr Keyboard(int code,IntPtr w,IntPtr l) {
      if(code>=0&&physicalInput.Keyboard(code,Marshal.ReadInt32(l,8))&&working&&!manual){cancelled=true;cancelReason="用户正在使用键盘，当前批次已中断。";}
      return Win.CallNextHookEx(keyboardHook,code,w,l);
    }
    private IntPtr Mouse(int code,IntPtr w,IntPtr l) {
      if(code>=0&&physicalInput.Mouse(code,Marshal.ReadInt32(l,12))&&working&&!manual){cancelled=true;cancelReason="用户正在使用鼠标，当前批次已中断。";}
      return Win.CallNextHookEx(mouseHook,code,w,l);
    }
    protected override void WndProc(ref Message m) {
      if(m.Msg==0x312){if(m.WParam.ToInt32()==1)Stop("用户按下 Ctrl+Alt+Esc",true);if(m.WParam.ToInt32()==2)OpenInterject();}base.WndProc(ref m);
    }
    private void ReadLoop() {
      try {
        using(StreamReader reader=new StreamReader(Console.OpenStandardInput(),new UTF8Encoding(false,true))) {
          string line;while((line=reader.ReadLine())!=null){
            if(line.Length>1024*1024)throw new InvalidDataException("请求过大。");
            Dictionary<string,object> request=new JavaScriptSerializer{MaxJsonLength=1024*1024}.Deserialize<Dictionary<string,object>>(line);
            if(request==null)throw new InvalidDataException("空请求。");
            BeginInvoke((Action)delegate{Dispatch(request);});
          }
        }
      }catch(Exception e){Console.Error.WriteLine("连接已关闭："+e.Message);}
      try{BeginInvoke((Action)delegate{Stop("DSH 连接断开",false);Shutdown();});}catch(InvalidOperationException){/* Window already closing. */}
    }
    private void Reply(int id,object result){Program.Emit(new{id=id,ok=true,result=result});}
    private void Fail(int id,Exception e){Program.Emit(new{id=id,ok=false,error=e.Message});}
    private void Dispatch(Dictionary<string,object> r) {
      int id=0;
      try {
        id=Json.Int(r,"id");string op=Json.Str(r,"op");lastRequest=DateTime.UtcNow;
        switch(op) {
          case "status":Reply(id,Status());break;
          case "settings":OpenSettings();Reply(id,new{summary="设置窗口已打开，请用户自行勾选。"});break;
          case "notice":Notice(Json.Str(r,"text"));Reply(id,new{summary="已提示用户。"});break;
          case "capture":Reply(id,TakeCapture(r));break;
          case "act":BeginActions(id,r);break;
          case "finish":cancelled=true;cancelReason="任务结束";shot=null;overlay.Hide();Reply(id,new{summary="已结束桌面操控并隐藏覆盖层。",stopped=stopped});break;
          case "stop":Stop("DSH 请求停止",false);Reply(id,new{summary="已停止。用户须从托盘菜单手动恢复。",stopped=true});break;
          case "cancel":Stop("DSH 取消了当前操作",false);Reply(id,new{summary="已取消并停止操控。"});break;
          case "shutdown":Stop("插件已卸载",false);Reply(id,new{summary="正在退出。"});Shutdown();break;
          default:throw new ArgumentException("不支持的操作。模型不能设置权限或解除急停。");
        }
      }catch(Exception e){Fail(id,e);}
    }
    private object Status(){return new{summary=stopped?"已急停；从托盘菜单手动恢复。":working?"正在处理桌面动作。":"桌面助手已就绪。",version=Program.Version,stopped=stopped,busy=working,manual=manual,settings=settings,settingsPath=settingsPath};}
    private string StaleReason(Shot frame,string id) {
      if(frame==null)return "截图缺失或已经使用。";
      return ObservationGuard.StaleReason(frame.Id,id,frame.Time,DateTime.UtcNow,frame.ScreenBounds,Screen.PrimaryScreen.Bounds,frame.Foreground,Win.GetForegroundWindow(),frame.PhysicalInput,physicalInput.Revision);
    }
    private Dictionary<string,object> TakeCapture(Dictionary<string,object> request) {
      if(stopped)throw new InvalidOperationException("已急停。请用户在系统托盘中手动恢复，禁止自动重试。");
      if(working||dialog!=null)throw new InvalidOperationException("正在操作或用户正在输入，请等待并重新截图。");
      Win.EnsureDesktop();EnsureTestSurface();
      Rectangle screen=Screen.PrimaryScreen.Bounds,region=screen;int scale=0;string stale=null;
      object rawZoom;
      if(request.TryGetValue("zoom",out rawZoom)) {
        Dictionary<string,object> zoom=rawZoom as Dictionary<string,object>;
        if(zoom==null)throw new ArgumentException("放大区域必须是对象。");
        scale=Bounded(zoom,"scale",1,2,2);
        stale=StaleReason(shot,Json.Str(request,"captureId"));
        if(stale==null)region=CoordinateMap.Crop(shot.Bounds,Bounded(request,"imageWidth",1,3840,0),Bounded(request,"imageHeight",1,8192,0),zoom);
        else scale=0;
      }
      shot=null;
      overlay.Hide();Application.DoEvents();Thread.Sleep(45);
      Dictionary<string,object> r;
      try {
        // Bind the observation to state BEFORE copying pixels. Activity during
        // capture must invalidate it rather than be accepted as the baseline.
        IntPtr foreground=Win.GetForegroundWindow();long revision=physicalInput.Revision;DateTime time=DateTime.UtcNow;
        r=WhaleComputer.Capture.ScreenShot(settings.MaxImageWidth,region,scale);
        shot=new Shot{Id=Guid.NewGuid().ToString("N"),Time=time,Bounds=region,ScreenBounds=screen,Foreground=foreground,PhysicalInput=revision};
        r["captureId"]=shot.Id;r["foreground"]=Win.Title();r["settings"]=settings;
        r["captureMode"]=scale==0?"full":"region";
        r["region"]=new{x=region.Left-screen.Left,y=region.Top-screen.Top,width=region.Width,height=region.Height};
        if(stale!=null){r["recoveredFromStale"]=true;r["staleReason"]=stale;}
      } finally {
        overlay.Status="正在观察屏幕";overlay.Lines=new string[0];overlay.Markers.Clear();overlay.Manual=false;overlay.Grid=settings.Grid;
        if(!stopped)overlay.RefreshState();
      }
      return r;
    }
    private void EnsureTestSurface() {
      if(testSurfacePath==null)return;
      if(testSurfaceHandle==0){Dictionary<string,object> d=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(testSurfacePath));testSurfaceHandle=Convert.ToInt64(d["handle"]);}
      if(Win.GetForegroundWindow().ToInt64()!=testSurfaceHandle)throw new InvalidOperationException("测试模式只允许专用测试窗口处于前台。");
    }
    private void Check() {
      if(cancelled||stopped||closing)throw new OperationCanceledException(cancelReason);
      Win.EnsureDesktop();EnsureTestSurface();
    }
    private void Stop(string reason,bool emit) {
      cancelled=true;stopped=true;cancelReason=reason;shot=null;overlay.Hide();manualDone.Set();
      if(emit)Program.Emit(new{@event="stop",reason=reason});
    }
    private void Notice(string text){if(tray!=null){tray.BalloonTipTitle="鲸鱼娘桌面操控";tray.BalloonTipText=text.Length>250?text.Substring(0,250):text;tray.ShowBalloonTip(4000);}}
    private void OpenSettings() {
      if(dialog!=null){dialog.Activate();return;}
      cancelled=true;cancelReason="用户打开设置，动作已暂停。";shot=null;overlay.Hide();
      IntPtr previous=Win.GetForegroundWindow();
      SettingsForm f=new SettingsForm(settings,delegate(Preferences p){p.Save(settingsPath);settings=p;Program.Emit(new{@event="settings-changed",settings=p});});
      dialog=f;f.FormClosed+=delegate{dialog=null;Win.SetForegroundWindow(previous);};f.Show();Win.ShowWindow(f.Handle,5);f.Activate();
    }
    private void OpenInterject() {
      if(dialog!=null){dialog.Activate();return;}
      if(!manual){cancelled=true;cancelReason="用户打开插话框，动作已暂停。";}
      shot=null;Program.Emit(new{@event="interject-open"});IntPtr previous=Win.GetForegroundWindow();
      InterjectForm f=new InterjectForm(manual,delegate(string text){cancelled=true;cancelReason="用户发送了插话，请重新规划。";manualDone.Set();Program.Emit(new{@event="interject",text=text});},delegate{manualDone.Set();},delegate{Stop("用户从插话框停止",true);},OpenSettings);
      dialog=f;f.FormClosed+=delegate{dialog=null;Win.SetForegroundWindow(previous);};f.Show();Win.ShowWindow(f.Handle,5);f.Activate();
    }
    private static int Bounded(Dictionary<string,object> a,string k,int lo,int hi,int fallback) {int v=Json.Int(a,k,fallback);if(v<lo||v>hi)throw new ArgumentException(k+" 超出范围。");return v;}
    private static List<Dictionary<string,object>> Validate(Dictionary<string,object> r,int w,int h) {
      object[] raw=Json.Array(r,"actions");if(raw.Length<1||raw.Length>5)throw new ArgumentException("每批必须有 1–5 个动作。");
      List<Dictionary<string,object>> actions=new List<Dictionary<string,object>>();
      foreach(object o in raw){
        Dictionary<string,object> a=o as Dictionary<string,object>;if(a==null)throw new ArgumentException("动作必须是对象。");
        string kind=Json.Str(a,"kind"),desc=Json.Str(a,"description");if(desc.Length<1||desc.Length>300)throw new ArgumentException("请完整描述动作效果。");
        if(!a.ContainsKey("risks"))throw new ArgumentException("缺少风险分类。");foreach(string risk in Json.Strings(a,"risks"))if(Array.IndexOf(Policy.Categories,risk)<0)throw new ArgumentException("风险分类无效。");
        if(kind=="click"||kind=="move"||kind=="drag"||kind=="scroll"){Bounded(a,"x",0,w-1,-1);Bounded(a,"y",0,h-1,-1);}
        switch(kind){
          case "click":if(Array.IndexOf(new[]{"left","right","middle"},Json.Str(a,"button","left"))<0)throw new ArgumentException("鼠标按键无效。");Bounded(a,"count",1,2,1);break;
          case "move":break;
          case "drag":Bounded(a,"toX",0,w-1,-1);Bounded(a,"toY",0,h-1,-1);Bounded(a,"ms",200,3000,600);foreach(string m in Json.Strings(a,"modifiers"))if(m!="ctrl"&&m!="shift"&&m!="alt")throw new ArgumentException("拖拽修饰键无效。");break;
          case "scroll":Bounded(a,"dx",-20,20,0);Bounded(a,"dy",-20,20,0);break;
          case "key":Injector.ParseChord(Json.Str(a,"keys"));break;
          case "type":string text=Json.Str(a,"text");if(text.Length<1||text.Length>4000)throw new ArgumentException("输入长度应为 1–4000。");foreach(char c in text)if(c<32&&c!='\n'&&c!='\r'&&c!='\t')throw new ArgumentException("输入中含有控制字符。");break;
          case "wait":Bounded(a,"ms",1,5000,0);break;
          default:throw new ArgumentException("不支持的动作。");
        }actions.Add(a);
      }return actions;
    }
    private void BeginActions(int id,Dictionary<string,object> r) {
      if(working||dialog!=null)throw new InvalidOperationException("已有动作或用户输入窗口，请等待。");
      if(stopped)throw new InvalidOperationException("已急停，请用户手动恢复。");
      Shot frame=shot;
      int iw=Bounded(r,"imageWidth",1,3840,0),ih=Bounded(r,"imageHeight",1,8192,0);
      string title=Json.Str(r,"title");if(title.Length<1||title.Length>200)throw new ArgumentException("动作标题无效。");
      List<Dictionary<string,object>> actions=Validate(r,iw,ih);
      string stale=StaleReason(frame,Json.Str(r,"captureId"));
      if(stale!=null){shot=null;Reply(id,new{status="stale_capture",summary=stale+" 本批未执行，请根据补拍的新图重新规划。",completed=0,total=actions.Count,staleReason=stale});return;}
      Preferences preferences=settings;
      List<List<string>> risks=new List<List<string>>();bool sensitive=false;
      foreach(Dictionary<string,object> a in actions){List<string> risk=Policy.Classify(a,title,preferences);risks.Add(risk);sensitive|=risk.Count>0;}
      List<Marker> markers=new List<Marker>();List<string> lines=new List<string>();
      for(int i=0;i<actions.Count;i++){
        Dictionary<string,object> a=actions[i];string kind=Json.Str(a,"kind");string line=(i+1)+". "+Json.Str(a,"description");
        if(kind=="type")line+=" · 输入："+(Policy.Redact(a)?"••••（敏感内容已隐藏）":Json.Str(a,"text").Replace("\n"," ↵ ").Replace("\r",""));
        if(kind=="key")line+=" · "+Json.Str(a,"keys");
        if(risks[i].Count>0)line+=" [敏感]";lines.Add(line);
        if(a.ContainsKey("x"))markers.Add(new Marker{X=frame.Bounds.Left-frame.ScreenBounds.Left+Px(a,"x",iw,frame.Bounds.Width),Y=frame.Bounds.Top-frame.ScreenBounds.Top+Px(a,"y",ih,frame.Bounds.Height),ToX=frame.Bounds.Left-frame.ScreenBounds.Left+Px(a,"toX",iw,frame.Bounds.Width),ToY=frame.Bounds.Top-frame.ScreenBounds.Top+Px(a,"toY",ih,frame.Bounds.Height),N=i+1,Red=risks[i].Count>0,Drag=kind=="drag"});
      }
      shot=null;cancelled=false;working=true;manual=sensitive&&!preferences.AutoSensitive;manualDone.Reset();
      overlay.Grid=preferences.Grid;overlay.Manual=manual;overlay.Markers=markers;overlay.Lines=lines.ToArray();
      overlay.SensitiveLines=risks.ConvertAll(delegate(List<string> selected){return selected.Count>0;}).ToArray();
      overlay.Status=manual?"请手动完成；Ctrl+Shift+I → 我已手动完成（等待 "+preferences.ManualTimeoutSeconds+" 秒）":"1 秒后执行 · "+title;
      overlay.RefreshState();
      worker=new Thread(delegate(){RunActions(id,frame,actions,risks,iw,ih,preferences);}){IsBackground=true,Name="Whale desktop actions"};worker.Start();
    }
    private static int Px(Dictionary<string,object> a,string k,int image,int screen){return CoordinateMap.Map(Json.Int(a,k),image,screen);}
    private void RunActions(int id,Shot frame,List<Dictionary<string,object>> actions,List<List<string>> risks,int iw,int ih,Preferences preferences) {
      int completed=0;string status="executed",summary="动作已执行，请查看返回截图验证目标。";string failure=null;
      try {
        if(manual) {
          DateTime deadline=DateTime.UtcNow.AddSeconds(preferences.ManualTimeoutSeconds);
          while(!manualDone.Wait(50)){if(cancelled||stopped)throw new OperationCanceledException(cancelReason);if(DateTime.UtcNow>=deadline)break;}
          if(cancelled||stopped)throw new OperationCanceledException(cancelReason);
          if(manualDone.IsSet){status="manual_completed";summary="用户声明已手动完成；请根据新截图验证，不要重复执行。";}
          else{status="manual_timeout";summary="用户未在等待时间内响应，本批没有自动执行。只可尝试其他安全步骤，不得绕过或自动执行同一敏感操作。";}
        } else {
          input.Wait(1000);Check();
          string stale=StaleReason(frame,frame.Id);
          if(stale!=null){status="stale_capture";throw new OperationCanceledException(stale+" 预览已取消，本批未执行，请根据补拍的新图重新规划。");}
          foreach(int key in new[]{16,17,18,91,92})if((Win.GetAsyncKeyState(key)&0x8000)!=0)throw new OperationCanceledException("用户仍按着修饰键，已取消本批。");
          foreach(Dictionary<string,object> a in actions){
            Check();int x=frame.Bounds.Left+Px(a,"x",iw,frame.Bounds.Width),y=frame.Bounds.Top+Px(a,"y",ih,frame.Bounds.Height);
            switch(Json.Str(a,"kind")){
              case "click":input.Click(x,y,Json.Str(a,"button","left"),Json.Int(a,"count",1));break;
              case "move":input.Move(x,y);break;
              case "type":input.Text(Json.Str(a,"text"));break;
              case "key":input.Chord(Json.Str(a,"keys"));break;
              case "scroll":input.Wheel(x,y,Json.Int(a,"dx"),Json.Int(a,"dy"));break;
              case "drag":input.Drag(x,y,frame.Bounds.Left+Px(a,"toX",iw,frame.Bounds.Width),frame.Bounds.Top+Px(a,"toY",ih,frame.Bounds.Height),Json.Int(a,"ms",600),Json.Strings(a,"modifiers"));break;
              case "wait":input.Wait(Json.Int(a,"ms"));break;
            }completed++;input.Wait(90);
          }
        }
      }catch(OperationCanceledException e){status=stopped?"stopped":status=="stale_capture"?"stale_capture":"interrupted";summary=e.Message;}
      catch(Exception e){status="failed";summary="输入结果不确定，停止后续动作。重新观察后再决定。";failure=e.Message;}
      finally{input.Release();}
      string finalStatus=status,finalSummary=summary,finalFailure=failure;int finalCount=completed;
      try{BeginInvoke((Action)delegate{
        working=false;manual=false;overlay.Markers.Clear();overlay.Lines=new string[0];overlay.Manual=false;
        if(!stopped&&!closing&&dialog==null){overlay.Status=finalSummary;overlay.RefreshState();}else overlay.Hide();
        if(finalStatus=="interrupted")Program.Emit(new{@event="interrupted",reason=finalSummary});
        Reply(id,new{status=finalStatus,summary=finalSummary,completed=finalCount,total=actions.Count,error=finalFailure,risks=risks});
      });}catch(InvalidOperationException){/* Host has shut down; pipe closure reports the unknown result. */}
    }
    private void Shutdown(){closing=true;cancelled=true;manualDone.Set();overlay.Hide();System.Windows.Forms.Timer t=new System.Windows.Forms.Timer{Interval=50};DateTime deadline=DateTime.UtcNow.AddSeconds(2);t.Tick+=delegate{if(worker==null||!worker.IsAlive||DateTime.UtcNow>=deadline){t.Stop();t.Dispose();Close();}};t.Start();}
    protected override void OnFormClosed(FormClosedEventArgs e){closing=true;cancelled=true;manualDone.Set();if(stopHotkey)Win.UnregisterHotKey(Handle,1);if(interjectHotkey)Win.UnregisterHotKey(Handle,2);if(keyboardHook!=IntPtr.Zero)Win.UnhookWindowsHookEx(keyboardHook);if(mouseHook!=IntPtr.Zero)Win.UnhookWindowsHookEx(mouseHook);if(tray!=null){tray.Visible=false;tray.Dispose();}overlay.Dispose();if(dialog!=null)dialog.Close();base.OnFormClosed(e);}
  }
}
