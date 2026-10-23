using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace WhaleComputer {
  internal static class CoordinateMap {
    internal static int Map(int pixel,int image,int screen) {
      if(image<1||screen<1||pixel<0||pixel>=image)throw new ArgumentOutOfRangeException("pixel");
      return (int)Math.Round(pixel*((double)screen-1)/Math.Max(1,image-1));
    }
  }
  internal static class Win {
    [DllImport("user32.dll")] internal static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
    [DllImport("user32.dll", SetLastError=true)] internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr hwnd,int command);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int length);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", SetLastError=true)] internal static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("user32.dll", SetLastError=true)] internal static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] internal static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)] internal static extern bool GetUserObjectInformation(IntPtr obj, int index, StringBuilder value, int length, out int needed);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] internal static extern uint GetDoubleClickTime();
    [DllImport("user32.dll")] internal static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
    [DllImport("user32.dll", SetLastError=true)] internal static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [StructLayout(LayoutKind.Sequential)] internal struct LASTINPUTINFO { internal uint cbSize; internal uint dwTime; }
    [StructLayout(LayoutKind.Sequential)] internal struct INPUT { internal uint type; internal UNION data; }
    [StructLayout(LayoutKind.Explicit)] internal struct UNION {
      [FieldOffset(0)] internal MOUSEINPUT mouse;
      [FieldOffset(0)] internal KEYBDINPUT key;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct MOUSEINPUT { internal int dx,dy,data; internal uint flags,time; internal UIntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct KEYBDINPUT { internal ushort vk,scan; internal uint flags,time; internal UIntPtr extra; }
    internal delegate IntPtr Hook(int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError=true)] internal static extern IntPtr SetWindowsHookEx(int kind, Hook callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] internal static extern IntPtr GetModuleHandle(string module);

    internal static string Title() { StringBuilder b=new StringBuilder(512); GetWindowText(GetForegroundWindow(),b,b.Capacity); return b.ToString(); }
    internal static uint InputTick() { LASTINPUTINFO i=new LASTINPUTINFO(); i.cbSize=(uint)Marshal.SizeOf(i); GetLastInputInfo(ref i); return i.dwTime; }
    internal static void EnsureDesktop() {
      IntPtr d=OpenInputDesktop(0,false,1);
      if (d==IntPtr.Zero) throw new InvalidOperationException("无法访问当前桌面。可能已锁屏或显示 UAC，请用户手动处理。");
      try {
        int n; StringBuilder name=new StringBuilder(256);
        if (!GetUserObjectInformation(d,2,name,512,out n) || !String.Equals(name.ToString(),"Default",StringComparison.OrdinalIgnoreCase))
          throw new InvalidOperationException("当前不是普通桌面。请手动处理锁屏或管理员授权。");
      } finally { CloseDesktop(d); }
    }
  }

  internal sealed class Injector {
    private readonly HashSet<ushort> heldKeys=new HashSet<ushort>();
    private readonly HashSet<string> heldButtons=new HashSet<string>();
    private readonly Action check;
    internal Injector(Action guard) { check=guard; }
    internal static readonly Dictionary<string,ushort> Keys=BuildKeys();
    private static Dictionary<string,ushort> BuildKeys() {
      Dictionary<string,ushort> k=new Dictionary<string,ushort>(StringComparer.OrdinalIgnoreCase);
      for(char c='a';c<='z';c++) k[c.ToString()]=(ushort)Char.ToUpperInvariant(c);
      for(char c='0';c<='9';c++) k[c.ToString()]=(ushort)c;
      for(int i=1;i<=24;i++) k["f"+i]=(ushort)(0x6f+i);
      string[] names={"backspace","tab","enter","shift","ctrl","alt","pause","capslock","esc","space","pageup","pagedown","end","home","left","up","right","down","printscreen","insert","delete","win","menu","numlock","scrolllock"};
      ushort[] codes={8,9,13,16,17,18,19,20,27,32,33,34,35,36,37,38,39,40,44,45,46,91,93,144,145};
      for(int i=0;i<names.Length;i++) k[names[i]]=codes[i];
      k["control"]=17;k["return"]=13;k["escape"]=27;k["windows"]=91;
      k["plus"]=187;k["minus"]=189;k["comma"]=188;k["period"]=190;k["slash"]=191;k["backslash"]=220;k["semicolon"]=186;k["quote"]=222;k["leftbracket"]=219;k["rightbracket"]=221;k["grave"]=192;
      for(int i=0;i<10;i++) k["num"+i]=(ushort)(96+i);
      return k;
    }
    internal static ushort[] ParseChord(string value) {
      string[] parts=value.Split('+'); if(parts.Length<1||parts.Length>5) throw new ArgumentException("组合键过长。");
      List<ushort> result=new List<ushort>();
      foreach(string p in parts) { ushort c; if(!Keys.TryGetValue(p.Trim(),out c)||result.Contains(c)) throw new ArgumentException("不支持或重复的按键："+p); result.Add(c); }
      if(result.Contains(17)&&result.Contains(18)&&result.Contains(27)) throw new ArgumentException("模型不能模拟急停快捷键。");
      if(result.Contains(17)&&result.Contains(16)&&result.Contains(73)) throw new ArgumentException("插话快捷键只供用户使用。");
      return result.ToArray();
    }
    private static void Send(Win.INPUT input) {
      uint n=Win.SendInput(1,new Win.INPUT[]{input},Marshal.SizeOf(typeof(Win.INPUT)));
      if(n!=1) throw new Win32Exception(Marshal.GetLastWin32Error(),"Windows 未接收输入；目标可能具有更高权限。请停止并手动处理。");
    }
    private static Win.INPUT Mouse(uint flags,int x,int y,int data) { Win.INPUT i=new Win.INPUT();i.data.mouse.flags=flags;i.data.mouse.dx=x;i.data.mouse.dy=y;i.data.mouse.data=data;return i; }
    private static Win.INPUT Key(ushort vk,ushort scan,uint flags) { Win.INPUT i=new Win.INPUT();i.type=1;i.data.key.vk=vk;i.data.key.scan=scan;i.data.key.flags=flags;return i; }
    private static uint Extended(ushort vk) { return (vk>=33&&vk<=46)||vk==91||vk==93 ? 1u:0u; }
    internal void Wait(int ms) { for(int left=ms;left>0;left-=20) { check();Thread.Sleep(Math.Min(20,left)); } check(); }
    internal void Move(int x,int y) {
      check(); Rectangle b=SystemInformation.VirtualScreen;
      int nx=(int)Math.Round((x-b.Left)*65535.0/Math.Max(1,b.Width-1));
      int ny=(int)Math.Round((y-b.Top)*65535.0/Math.Max(1,b.Height-1));
      Send(Mouse(0xc001,nx,ny,0));
    }
    private void Down(string button) { check();heldButtons.Add(button);Send(Mouse(button=="right"?8u:button=="middle"?32u:2u,0,0,0)); }
    private void Up(string button) { Send(Mouse(button=="right"?16u:button=="middle"?64u:4u,0,0,0));heldButtons.Remove(button); }
    private void KeyDown(ushort c) { check();heldKeys.Add(c);Send(Key(c,0,Extended(c))); }
    private void KeyUp(ushort c) { Send(Key(c,0,2|Extended(c)));heldKeys.Remove(c); }
    internal void Click(int x,int y,string button,int count) { Move(x,y);Wait(35);for(int i=0;i<count;i++){Down(button);Wait(25);Up(button);if(i+1<count)Wait((int)Math.Min(80,Win.GetDoubleClickTime()/3));} }
    internal void Chord(string value) { ushort[] c=ParseChord(value);try{foreach(ushort v in c)KeyDown(v);Wait(35);}finally{for(int j=c.Length-1;j>=0;j--)if(heldKeys.Contains(c[j]))KeyUp(c[j]);} }
    internal void Text(string text) {
      foreach(char c in text) {
        check();
        if(c=='\r')continue;
        if(c=='\n'){Chord("enter");continue;} if(c=='\t'){Chord("tab");continue;}
        Send(Key(0,c,4));Send(Key(0,c,6));Wait(2);
      }
    }
    internal void Wheel(int x,int y,int dx,int dy) {
      Move(x,y);Wait(40);
      for(int i=0;i<Math.Abs(dy);i++){check();Send(Mouse(0x800,0,0,Math.Sign(dy)*120));Wait(15);}
      for(int i=0;i<Math.Abs(dx);i++){check();Send(Mouse(0x1000,0,0,Math.Sign(dx)*120));Wait(15);}
    }
    internal void Drag(int x,int y,int tx,int ty,int ms,string[] modifiers) {
      try {
        foreach(string m in modifiers) KeyDown(Keys[m]);
        Move(x,y);Wait(60);Down("left");Wait(60);
        int steps=Math.Max(10,ms/20);
        for(int i=1;i<=steps;i++){Move(x+(int)Math.Round((tx-x)*(double)i/steps),y+(int)Math.Round((ty-y)*(double)i/steps));Wait(ms/steps);}
        Up("left");
      } finally { Release(); }
    }
    internal void Release() {
      foreach(string b in new List<string>(heldButtons)) {try{Up(b);}catch(Win32Exception){/* Best-effort release when desktop changed. */}}
      foreach(ushort k in new List<ushort>(heldKeys)) {try{KeyUp(k);}catch(Win32Exception){/* Windows may already reject this desktop. */}}
      heldButtons.Clear();heldKeys.Clear();
    }
  }
}
