using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace WhaleComputer {
  public sealed class Preferences {
    public bool AutoSensitive { get; set; }
    public string[] SensitiveCategories { get; set; }
    public string[] CustomKeywords { get; set; }
    public int ManualTimeoutSeconds { get; set; }
    public int MaxImageWidth { get; set; }
    public bool Grid { get; set; }
    public Preferences() { AutoSensitive=false;SensitiveCategories=(string[])Policy.Categories.Clone();CustomKeywords=new string[0];ManualTimeoutSeconds=90;MaxImageWidth=1920;Grid=true; }
    internal void Validate() {
      if(SensitiveCategories==null||SensitiveCategories.Length>Policy.Categories.Length)throw new ArgumentException("敏感类别无效。");
      foreach(string c in SensitiveCategories)if(Array.IndexOf(Policy.Categories,c)<0)throw new ArgumentException("未知敏感类别："+c);
      if(CustomKeywords==null||CustomKeywords.Length>100)throw new ArgumentException("自定义关键词最多 100 个。");
      foreach(string w in CustomKeywords)if(String.IsNullOrWhiteSpace(w)||w.Length>80)throw new ArgumentException("每个关键词需要 1–80 个字符。");
      if(ManualTimeoutSeconds<10||ManualTimeoutSeconds>600)throw new ArgumentException("手动等待时间应为 10–600 秒。");
      if(MaxImageWidth<640||MaxImageWidth>3840)throw new ArgumentException("截图最大宽度应为 640–3840。");
    }
    internal static Preferences Load(string path) {
      if(!File.Exists(path))return new Preferences();
      Preferences p=new JavaScriptSerializer().Deserialize<Preferences>(File.ReadAllText(path,Encoding.UTF8));
      if(p==null)throw new ArgumentException("设置文件为空。");p.Validate();return p;
    }
    internal void Save(string path) {
      Validate();Directory.CreateDirectory(Path.GetDirectoryName(path));
      string temp=path+".tmp";
      File.WriteAllText(temp,new JavaScriptSerializer().Serialize(this),new UTF8Encoding(false));
      if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
    }
  }

  internal static class Policy {
    internal static readonly string[] Categories={"payment","deletion","external_message","account","permissions","credentials","software","system","custom"};
    internal static readonly string[] Labels={"付款、转账、购买、订阅","删除文件、清空或取消数据","发送消息、提交或公开发布","账号注册、注销和资料变更","权限、共享范围和管理员授权","密码、验证码及其他凭据","安装、卸载和运行下载的软件","系统配置、关机和重启","自定义关键词"};
    private static readonly string[] Patterns={
      "支付|付款|转账|购买|订阅|结账|充值|\\b(pay|payment|purchase|buy|checkout|transfer|subscribe)\\b",
      "删除|清空|格式化|移至回收站|取消订单|\\b(delete|erase|remove|format|empty|cancel)\\b",
      "发送|发给|提交|发布|上传|回复|\\b(send|submit|publish|upload|reply|post)\\b",
      "注册|注销|账号变更|账户变更|修改资料|修改密码|重置密码|\\b(register|signup|sign.up|account|password.reset)\\b",
      "授权|权限|管理员|共享|\\b(permission|authorize|admin|administrator|share|sharing)\\b",
      "密码|验证码|密钥|口令|\\b(password|passcode|credential|secret|otp|api.key)\\b",
      "安装|卸载|运行下载|\\b(install|uninstall|installer|downloaded.executable)\\b",
      "关机|重启|系统设置|注册表|防火墙|\\b(shutdown|reboot|registry|firewall|system.settings)\\b",
    };
    internal static List<string> Classify(Dictionary<string,object> a,string title,Preferences settings) {
      HashSet<string> selected=new HashSet<string>(settings.SensitiveCategories);
      HashSet<string> detected=new HashSet<string>(Json.Strings(a,"risks"));
      string text=title+" "+Json.Str(a,"description")+" "+Json.Str(a,"text");
      for(int i=0;i<Patterns.Length;i++)if(Regex.IsMatch(text,Patterns[i],RegexOptions.IgnoreCase))detected.Add(Categories[i]);
      string keys=Json.Str(a,"keys").ToLowerInvariant();
      if(keys.Split('+').Length>0&&Array.IndexOf(keys.Split('+'),"delete")>=0)detected.Add("deletion");
      foreach(string word in settings.CustomKeywords)if(text.IndexOf(word,StringComparison.OrdinalIgnoreCase)>=0)detected.Add("custom");
      detected.IntersectWith(selected);return new List<string>(detected);
    }
    internal static bool Redact(Dictionary<string,object> a) {
      return Array.IndexOf(Json.Strings(a,"risks"),"credentials")>=0 || Regex.IsMatch(Json.Str(a,"description"),Patterns[5],RegexOptions.IgnoreCase);
    }
  }

  internal sealed class SettingsForm : Form {
    private readonly CheckedListBox categories=new CheckedListBox();
    private readonly CheckBox automate=new CheckBox(), grid=new CheckBox();
    private readonly TextBox words=new TextBox();
    private readonly NumericUpDown timeout=new NumericUpDown(),width=new NumericUpDown();
    internal SettingsForm(Preferences current, Action<Preferences> save) {
      Text="鲸鱼娘 · 桌面操控设置";ClientSize=new Size(560,675);StartPosition=FormStartPosition.CenterScreen;MinimumSize=new Size(576,714);
      Font=new Font("Microsoft YaHei UI",10);BackColor=Color.FromArgb(245,249,253);AutoScaleMode=AutoScaleMode.None;
      Label heading=new Label{Text="哪些操作需要敏感保护",Location=new Point(22,18),Size=new Size(510,30),Font=new Font(Font.FontFamily,14,FontStyle.Bold)};Controls.Add(heading);
      categories.Location=new Point(24,54);categories.Size=new Size(510,225);categories.CheckOnClick=true;categories.BorderStyle=BorderStyle.None;categories.BackColor=BackColor;
      for(int i=0;i<Policy.Labels.Length;i++)categories.Items.Add(Policy.Labels[i],Array.IndexOf(current.SensitiveCategories,Policy.Categories[i])>=0);Controls.Add(categories);
      Controls.Add(new Label{Text="自定义关键词（每行一个，匹配操作说明和输入内容）",Location=new Point(24,288),Size=new Size(510,26)});
      words.Location=new Point(24,318);words.Size=new Size(510,94);words.Multiline=true;words.ScrollBars=ScrollBars.Vertical;words.Text=String.Join(Environment.NewLine,current.CustomKeywords);Controls.Add(words);
      automate.Location=new Point(24,428);automate.Size=new Size(510,30);automate.Text="自动执行敏感操作（完全权限）";automate.Checked=current.AutoSensitive;Controls.Add(automate);
      Controls.Add(new Label{Text="关闭：红色指引，等待你手动完成。开启：预览后自动执行。\n此开关不提供 Windows 管理员权限。",Location=new Point(44,461),Size=new Size(495,46),ForeColor=Color.FromArgb(77,92,112)});
      Controls.Add(new Label{Text="等待用户（秒）",Location=new Point(24,521),Size=new Size(128,26)});
      timeout.Location=new Point(151,518);timeout.Size=new Size(90,30);timeout.Minimum=10;timeout.Maximum=600;timeout.Value=current.ManualTimeoutSeconds;Controls.Add(timeout);
      Controls.Add(new Label{Text="截图最大宽度",Location=new Point(279,521),Size=new Size(127,26)});
      width.Location=new Point(410,518);width.Size=new Size(124,30);width.Minimum=640;width.Maximum=3840;width.Increment=160;width.Value=current.MaxImageWidth;Controls.Add(width);
      grid.Location=new Point(24,562);grid.Size=new Size(510,26);grid.Text="显示四分区辅助线（操控边框始终显示）";grid.Checked=current.Grid;Controls.Add(grid);
      Button submit=new Button{Text="保存设置",Location=new Point(392,615),Size=new Size(142,38)};
      submit.Click+=delegate {
        try {
          List<string> c=new List<string>();foreach(int i in categories.CheckedIndices)c.Add(Policy.Categories[i]);
          List<string> keywords=new List<string>();foreach(string line in words.Lines)if(!String.IsNullOrWhiteSpace(line))keywords.Add(line.Trim());
          Preferences p=new Preferences{SensitiveCategories=c.ToArray(),AutoSensitive=automate.Checked,CustomKeywords=keywords.ToArray(),ManualTimeoutSeconds=(int)timeout.Value,MaxImageWidth=(int)width.Value,Grid=grid.Checked};
          p.Validate();save(p);Close();
        } catch(Exception e) { MessageBox.Show(this,e.Message,"无法保存",MessageBoxButtons.OK,MessageBoxIcon.Error); }
      };Controls.Add(submit);
      Button cancel=new Button{Text="取消",Location=new Point(270,615),Size=new Size(104,38)};cancel.Click+=delegate{Close();};Controls.Add(cancel);
      using(Graphics metrics=CreateGraphics()){float scale=metrics.DpiX/96f;if(scale!=1)Scale(new SizeF(scale,scale));}
      Rectangle area=Screen.PrimaryScreen.WorkingArea;StartPosition=FormStartPosition.Manual;Location=new Point(area.Left+(area.Width-Width)/2,area.Top+Math.Max(0,(area.Height-Height)/2));
    }
  }

  internal static class Json {
    internal static string Str(Dictionary<string,object> d,string key,string fallback="") { object v;return d.TryGetValue(key,out v)&&v is string?(string)v:fallback; }
    internal static int Int(Dictionary<string,object> d,string key,int fallback=0) { object v;if(!d.TryGetValue(key,out v))return fallback;if(!(v is int))throw new ArgumentException(key+" 必须是整数。");return (int)v; }
    internal static object[] Array(Dictionary<string,object> d,string key) { object v;if(!d.TryGetValue(key,out v))return new object[0];object[] a=v as object[];if(a!=null)return a;ArrayList list=v as ArrayList;if(list!=null)return list.ToArray();throw new ArgumentException(key+" 必须是数组。"); }
    internal static string[] Strings(Dictionary<string,object> d,string key) { List<string> result=new List<string>();foreach(object v in Array(d,key)){if(!(v is string))throw new ArgumentException(key+" 必须是字符串数组。");result.Add((string)v);}return result.ToArray(); }
  }
}
