using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace WhaleComputer {
  internal sealed class Marker {
    internal int X,Y,N,ToX,ToY;internal bool Red,Drag;
  }
  internal sealed class Overlay : Form {
    internal string Status="正在观察屏幕";
    internal string[] Lines=new string[0];
    internal bool[] SensitiveLines=new bool[0];
    internal List<Marker> Markers=new List<Marker>();
    internal bool Grid=true,Manual=false;
    internal Overlay() {
      FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.Manual;
      BackColor=Color.Magenta;TransparencyKey=Color.Magenta;Bounds=Screen.PrimaryScreen.Bounds;AutoScaleMode=AutoScaleMode.None;DoubleBuffered=true;
    }
    protected override bool ShowWithoutActivation { get{return true;} }
    protected override CreateParams CreateParams { get{CreateParams p=base.CreateParams;p.ExStyle|=0x08000000|0x00000080|0x00000020|0x00080000;return p;} }
    protected override void WndProc(ref Message m) { if(m.Msg==0x84){m.Result=new IntPtr(-1);return;}if(m.Msg==0x21){m.Result=new IntPtr(3);return;}base.WndProc(ref m); }
    internal void RefreshState() { Bounds=Screen.PrimaryScreen.Bounds;Invalidate();if(!Visible)Show();Win.ShowWindow(Handle,4);Update(); }
    protected override void OnPaint(PaintEventArgs e) {
      base.OnPaint(e);Graphics g=e.Graphics;g.SmoothingMode=SmoothingMode.None;
      float scale=Math.Max(1,Width/1920f);int pad=(int)(18*scale);
      using(Pen border=new Pen(Color.FromArgb(137,208,250),3*scale))g.DrawRectangle(border,2,2,Width-5,Height-5);
      if(Grid) using(Pen grid=new Pen(Color.FromArgb(172,203,218),1)){grid.DashPattern=new float[]{2,13};g.DrawLine(grid,Width/2,4,Width/2,Height-4);g.DrawLine(grid,4,Height/2,Width-4,Height/2);}
      int hudWidth=Math.Min(Width-40,(int)(710*scale));int lineHeight=(int)(25*scale);int hudHeight=(int)(142*scale)+Lines.Length*lineHeight;
      using(Brush panel=new SolidBrush(Color.FromArgb(18,39,62)))g.FillRectangle(panel,pad,pad,hudWidth,hudHeight);
      using(Font head=new Font("Microsoft YaHei UI",16*scale,FontStyle.Bold))using(Brush b=new SolidBrush(Color.FromArgb(176,225,255)))g.DrawString("鲸鱼娘在操控你的电脑",head,b,pad+15*scale,pad+10*scale);
      using(Font body=new Font("Microsoft YaHei UI",10*scale)) {
        float y=pad+48*scale;g.DrawString("按下 Ctrl+Alt+Esc 退出操控",body,Brushes.White,pad+15*scale,y);
        g.DrawString("按下 Ctrl+Shift+I 打开插话框快捷发送消息",body,Brushes.White,pad+15*scale,y+24*scale);
        using(Brush state=new SolidBrush(Manual?Color.FromArgb(255,149,149):Color.FromArgb(137,220,182)))g.DrawString(Status,body,state,new RectangleF(pad+15*scale,y+50*scale,hudWidth-30*scale,46*scale));
        for(int i=0;i<Lines.Length;i++)using(Brush lineColor=new SolidBrush(i<SensitiveLines.Length&&SensitiveLines[i]?Color.FromArgb(255,149,149):Color.White))g.DrawString(Lines[i],body,lineColor,new RectangleF(pad+15*scale,pad+138*scale+i*lineHeight,hudWidth-30*scale,lineHeight),new StringFormat{Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap});
      }
      foreach(Marker m in Markers) {
        Color c=m.Red?Color.FromArgb(246,83,99):Color.FromArgb(61,221,144);int radius=(int)(7*scale);
        using(Brush b=new SolidBrush(c))using(Pen p=new Pen(c,2*scale)) {
          g.DrawEllipse(p,m.X-radius-4,m.Y-radius-4,2*radius+8,2*radius+8);g.FillEllipse(b,m.X-radius,m.Y-radius,2*radius,2*radius);
          if(m.Drag){p.DashStyle=DashStyle.Dash;g.DrawLine(p,m.X,m.Y,m.ToX,m.ToY);g.DrawEllipse(p,m.ToX-7,m.ToY-7,14,14);}
          using(Brush backing=new SolidBrush(Color.FromArgb(18,39,62)))g.FillRectangle(backing,m.X+10*scale,m.Y-22*scale,25*scale,24*scale);
          using(Font n=new Font("Microsoft YaHei UI",12*scale,FontStyle.Bold))g.DrawString(m.N.ToString(),n,b,m.X+11*scale,m.Y-22*scale);
        }
      }
    }
  }

  internal static class Capture {
    internal static Size ImageSize(Size source,int maxWidth,int scale) {
      if(source.Width<1||source.Height<1||maxWidth<1||scale<0||scale>2)throw new ArgumentException("无效截图尺寸。");
      double ratio=scale==0?Math.Min(1,(double)maxWidth/source.Width):scale;
      ratio=Math.Min(ratio,Math.Min(3840.0/source.Width,8192.0/source.Height));
      ratio=Math.Min(ratio,Math.Sqrt(12000000.0/((double)source.Width*source.Height)));
      return new Size(Math.Max(1,(int)Math.Floor(source.Width*ratio)),Math.Max(1,(int)Math.Floor(source.Height*ratio)));
    }
    internal static Dictionary<string,object> ScreenShot(int maxWidth,Rectangle region,int scale) {
      Win.EnsureDesktop();Rectangle b=Screen.PrimaryScreen.Bounds;
      if(!b.Contains(region)||region.Width<1||region.Height<1)throw new ArgumentException("截图区域不在主显示器内。");
      using(Bitmap full=new Bitmap(region.Width,region.Height,PixelFormat.Format24bppRgb)) {
        using(Graphics g=Graphics.FromImage(full))g.CopyFromScreen(region.Location,Point.Empty,region.Size,CopyPixelOperation.SourceCopy);
        Size size=ImageSize(region.Size,maxWidth,scale);int w=size.Width,h=size.Height;
        using(Bitmap scaled=new Bitmap(w,h,PixelFormat.Format24bppRgb))using(MemoryStream stream=new MemoryStream()) {
          using(Graphics g=Graphics.FromImage(scaled)){g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(full,new Rectangle(0,0,w,h));}
          ImageCodecInfo jpeg=null;foreach(ImageCodecInfo c in ImageCodecInfo.GetImageEncoders())if(c.MimeType=="image/jpeg")jpeg=c;
          using(EncoderParameters p=new EncoderParameters(1)){p.Param[0]=new EncoderParameter(System.Drawing.Imaging.Encoder.Quality,90L);scaled.Save(stream,jpeg,p);}
          return new Dictionary<string,object>{{"base64",Convert.ToBase64String(stream.ToArray())},{"imageWidth",w},{"imageHeight",h},{"screenWidth",b.Width},{"screenHeight",b.Height}};
        }
      }
    }
  }

  internal sealed class InterjectForm : Form {
    internal InterjectForm(bool manual,Action<string> send,Action complete,Action stop,Action settings) {
      Text="和鲸鱼娘说一句";ClientSize=new Size(530,260);StartPosition=FormStartPosition.CenterScreen;TopMost=true;Font=new Font("Microsoft YaHei UI",10);AutoScaleMode=AutoScaleMode.None;
      BackColor=Color.FromArgb(242,248,255);
      Controls.Add(new Label{Text=manual?"按指引手动操作后，点击「我已手动完成」。":"输入补充要求。打开此窗口时，当前动作已暂停。",Location=new Point(18,15),Size=new Size(490,28)});
      TextBox text=new TextBox{Multiline=true,Location=new Point(18,49),Size=new Size(490,115),MaxLength=4000,ScrollBars=ScrollBars.Vertical};Controls.Add(text);
      Button submit=new Button{Text="发送插话",Location=new Point(392,183),Size=new Size(116,37)};
      submit.Click+=delegate{if(!String.IsNullOrWhiteSpace(text.Text)){send(text.Text.Trim());Close();}};Controls.Add(submit);
      Button done=new Button{Text="我已手动完成",Location=new Point(235,183),Size=new Size(144,37),Enabled=manual};done.Click+=delegate{complete();Close();};Controls.Add(done);
      Button halt=new Button{Text="停止",Location=new Point(18,183),Size=new Size(92,37)};halt.Click+=delegate{stop();Close();};Controls.Add(halt);
      Button config=new Button{Text="设置",Location=new Point(122,183),Size=new Size(92,37)};config.Click+=delegate{Close();settings();};Controls.Add(config);
      Controls.Add(new Label{Text="发送到当前关联的 DSH 对话；不会切到浏览器。",Location=new Point(18,229),Size=new Size(490,24),ForeColor=Color.DimGray});
      Shown+=delegate{text.Focus();};
      using(Graphics metrics=CreateGraphics()){float scale=metrics.DpiX/96f;if(scale!=1)Scale(new SizeF(scale,scale));}
      Rectangle area=Screen.PrimaryScreen.WorkingArea;StartPosition=FormStartPosition.Manual;Location=new Point(area.Left+(area.Width-Width)/2,area.Top+Math.Max(0,(area.Height-Height)/2));
    }
  }
}
