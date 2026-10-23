using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using WhaleComputer;
internal sealed class TestCanvas : Panel { internal TestCanvas(){SetStyle(ControlStyles.Selectable,true);TabStop=true;}protected override void OnMouseDown(MouseEventArgs e){Focus();base.OnMouseDown(e);} }
internal sealed class TestSurface : Form {
  private readonly string stateFile;private readonly TextBox box;private readonly Panel canvas;private readonly Label status;
  private int clicks,right,doubleClicks,drags,wheel;private Point down;
  private Dictionary<string,object> snapshot;
  internal TestSurface(string path){
    stateFile=path;Text="鲸鱼娘插件 · 专用无害测试窗口";StartPosition=FormStartPosition.Manual;Bounds=Screen.PrimaryScreen.Bounds;FormBorderStyle=FormBorderStyle.None;BackColor=Color.FromArgb(236,244,252);Font=new Font("Microsoft YaHei UI",15);AutoScaleMode=AutoScaleMode.None;
    Controls.Add(new Label{Text="鲸鱼娘桌面操控 · 本地测试",Location=new Point(60,285),AutoSize=true,Font=new Font(Font.FontFamily,28,FontStyle.Bold)});
    Controls.Add(new Label{Text="仅测试点击、中文输入、组合键、拖动与滚动；不连接外部服务。",Location=new Point(60,355),AutoSize=true});
    box=new TextBox{Multiline=true,Location=new Point(80,430),Size=new Size(780,150),Font=new Font(Font.FontFamily,22)};Controls.Add(box);box.TextChanged+=delegate{Save();};
    Button button=new Button{Text="测试点击",Location=new Point(80,630),Size=new Size(300,100)};button.Click+=delegate{clicks++;Save();};Controls.Add(button);
    canvas=new TestCanvas{Location=new Point(980,420),Size=new Size(480,420),BackColor=Color.FromArgb(202,227,248)};Controls.Add(canvas);
    canvas.Paint+=delegate(object sender,PaintEventArgs e){e.Graphics.DrawString("鼠标测试区域",Font,Brushes.Navy,40,40);e.Graphics.DrawEllipse(Pens.SteelBlue,80,130,110,110);};
    canvas.MouseDown+=delegate(object sender,MouseEventArgs e){down=e.Location;if(e.Button==MouseButtons.Right)right++;Save();};
    canvas.MouseUp+=delegate(object sender,MouseEventArgs e){if(Math.Abs(e.X-down.X)>30||Math.Abs(e.Y-down.Y)>30)drags++;Save();};
    canvas.MouseDoubleClick+=delegate{doubleClicks++;Save();};canvas.MouseWheel+=delegate(object sender,MouseEventArgs e){wheel+=e.Delta;Save();};
    status=new Label{Location=new Point(80,810),Size=new Size(810,230),Font=new Font(Font.FontFamily,18)};Controls.Add(status);
    Shown+=delegate{Win.ShowWindow(Handle,5);Activate();Win.SetForegroundWindow(Handle);box.Focus();Save();};
    System.Windows.Forms.Timer focus=new System.Windows.Forms.Timer{Interval=200};
    bool priorFocus=false;
    focus.Tick+=delegate{bool current=Win.GetForegroundWindow()==Handle;if(current!=priorFocus){priorFocus=current;Save();}};focus.Start();FormClosed+=delegate{focus.Stop();focus.Dispose();};
    Thread reader=new Thread(delegate(){string line;while((line=Console.ReadLine())!=null){string command=line;try{BeginInvoke((Action)delegate{if(command=="close")Close();if(command=="activate"){Activate();Win.SetForegroundWindow(Handle);Save();}if(command.StartsWith("shot ")){Rectangle b=Screen.PrimaryScreen.Bounds;using(Bitmap shot=new Bitmap(b.Width,b.Height)){using(Graphics g=Graphics.FromImage(shot))g.CopyFromScreen(b.Location,Point.Empty,b.Size);shot.Save(command.Substring(5),System.Drawing.Imaging.ImageFormat.Png);}}if(command=="stop-hotkey"){foreach(ushort k in new ushort[]{17,18,27})SendKey(k,false);foreach(ushort k in new ushort[]{27,18,17})SendKey(k,true);}});}catch(InvalidOperationException){return;}}}){IsBackground=true};reader.Start();
  }
  private static void SendKey(ushort key,bool up){Win.INPUT i=new Win.INPUT();i.type=1;i.data.key.vk=key;i.data.key.flags=up?2u:0u;Win.SendInput(1,new Win.INPUT[]{i},System.Runtime.InteropServices.Marshal.SizeOf(typeof(Win.INPUT)));}
  private object Rect(Control c){Rectangle b=c.RectangleToScreen(c.ClientRectangle);return new{x=b.Left,y=b.Top,w=b.Width,h=b.Height};}
  private void Save(){
    if(status==null)return;
    snapshot=new Dictionary<string,object>{{"handle",Handle.ToInt64()},{"foreground",Win.GetForegroundWindow()==Handle},{"screenWidth",Screen.PrimaryScreen.Bounds.Width},{"screenHeight",Screen.PrimaryScreen.Bounds.Height},{"textbox",Rect(box)},{"canvas",Rect(canvas)},{"button",new{x=80,y=630,w=300,h=100}},{"text",box.Text},{"clicks",clicks},{"rightClicks",right},{"doubleClicks",doubleClicks},{"drags",drags},{"wheel",wheel}};
    status.Text=String.Format("点击：{0}    右键：{1}    双击：{2}\n拖动：{3}    滚轮：{4}\n输入：{5}",clicks,right,doubleClicks,drags,wheel,box.Text);
    File.WriteAllText(stateFile,new JavaScriptSerializer().Serialize(snapshot),new UTF8Encoding(false));
  }
  [STAThread]private static void Main(string[] args){Win.SetProcessDpiAwarenessContext(new IntPtr(-4));Application.EnableVisualStyles();Application.Run(new TestSurface(args[0]));}
}
