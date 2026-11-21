// ClawdPet.cs —— Clawd 桌宠 v3（表情泡泡 / 拎着甩 / 快捷按钮 / 梦境小游戏）
// 只依赖 Windows 自带的 .NET Framework 4（C# 5），用同目录的 build.ps1 编译。
//
// v3 从 Chrome 扩展版（ClawdPet-chrome\content.js）移植过来：
//   平时的反应都是头顶的小表情泡泡，文字只留给正事（Claude Code 状态、提醒、番茄钟、吃东西的结果、「说句话」、第一次见面）；
//   拎起来像钟摆一样绕手指晃、手脚一直乱蹬，晃久了落地会晕，狠狠扔到屏幕边上会糊在墙上再滑下来；
//   会坐、趴、东张西望、想事情、挠头、伸懒腰、转圈、打滚、做操、打喷嚏、对着小电脑办公，会跳到别的窗口标题栏上、坐在边上晃腿；
//   会扒着屏幕左右两边往上爬（扔过去速度不大也会扒住墙），爬到顶扒一会儿再松手或蹬墙跳回来；
//   会像螃蟹一样在地面（任务栏上沿）挖沙把自己埋起来，只露眼睛，被吓到就整个缩进去；
//   自己打盹、做梦、冒鼻涕泡；右键点它会冒出一排快捷按钮：喂一口、扔个球、睡一会儿、做个梦、待在这儿别动、更多（再右键一下 = 完整菜单）。
//
// 命令行：
//   ClawdPet.exe                      启动桌宠（只允许一个实例，重复启动会把它叫出来）
//   ClawdPet.exe --signal <事件>      给正在运行的桌宠发信号：thinking / done / attention / end / hello
//                                     （从 stdin 读 Claude Code hook 的 JSON，拿到会话 id、项目名、通知内容）
//   ClawdPet.exe --preview out.png    渲染所有动作的预览图
//   ClawdPet.exe --preview2 out.png   渲染 v3 新动作的预览图（拎着晃、坐、趴、打滚、办公、鼻涕泡、表情泡泡、快捷按钮……）
//   ClawdPet.exe --pitch out.png      渲染半俯视小足球场（四只在场上踢）的预览图
//   ClawdPet.exe --dream              只打开梦境小游戏窗口（测试用，不会启动桌宠）
//   ClawdPet.exe --music out.wav      把梦境小游戏的背景音乐存成 WAV
//   ClawdPet.exe --sfx <目录>         把所有音效各存一个 WAV，再连成一个 all.wav 方便试听
//   ClawdPet.exe --icon out.ico       生成图标文件
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("Clawd Desktop Pet")]
[assembly: System.Reflection.AssemblyProduct("ClawdPet")]
[assembly: System.Reflection.AssemblyVersion("3.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("3.0.0.0")]

namespace ClawdPet
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length > 0)
            {
                string a = args[0].Trim().ToLowerInvariant();
                if (a == "--signal" || a == "-s") { Signal.Send(args.Length > 1 ? args[1] : "done", Signal.ReadStdin()); return 0; }
                if (a == "--preview") { Preview.Render(args.Length > 1 ? args[1] : "clawd-preview.png"); return 0; }
                if (a == "--preview2") { Preview.Render2(args.Length > 1 ? args[1] : "clawd-preview2.png"); return 0; }
                if (a == "--pitch") { PetForm.PitchPreview(args.Length > 1 ? args[1] : "clawd-pitch.png"); return 0; }
                if (a == "--icon")
                {
                    File.WriteAllBytes(args.Length > 1 ? args[1] : "clawd.ico", Sprite.BuildIco(new int[] { 16, 20, 24, 32, 40, 48, 64, 256 }));
                    return 0;
                }
                if (a == "--music") { File.WriteAllBytes(args.Length > 1 ? args[1] : "clawd-dream.wav", DreamMusic.Wav()); return 0; }   // 把梦里的背景音乐存成 WAV 听听
                if (a == "--sfx") { Sfx.Export(args.Length > 1 ? args[1] : "clawd-sfx"); return 0; }                                // 把所有音效存成 WAV（还有连起来的 all.wav）
                if (a == "--sfx-test") { File.WriteAllText(args.Length > 1 ? args[1] : "clawd-sfx-test.txt", Sfx.SelfTest()); return Sfx.Failed > 0 ? 2 : 0; }   // 真的用声卡放几声
                if (a == "--dream" || a == "--dream-bench")
                {
                    try { Native.SetProcessDPIAware(); } catch { }
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    float dpi = 1f;
                    using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) dpi = Math.Max(1f, g.DpiX / 96f);
                    DreamForm df = new DreamForm(Sprite.Dreams[new Random().Next(Sprite.Dreams.Length)], dpi, r => { });
                    // --dream-bench <out.txt>：自动开局、自动往右跑 6 秒，记下帧间隔后自己关掉（测流畅度用）
                    if (a == "--dream-bench") df.Bench(args.Length > 1 ? args[1] : "dream-bench.txt");
                    Application.Run(df);
                    return 0;
                }
            }
            bool created;
            using (Mutex m = new Mutex(true, "Local\\ClawdDesktopPet_v1", out created))
            {
                if (!created) { Signal.Send("hello", ""); return 0; }
                try { Native.SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new PetForm());
                GC.KeepAlive(m);
            }
            return 0;
        }
    }

    // ------------------------------------------------------------------ 信号队列（给 Claude Code hooks 用）
    // 每个信号写成一个小文件，桌宠轮询后删掉，连续来几个也不会丢。
    static class Signal
    {
        public static string Dir()
        {
            string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClawdPet");
            try { Directory.CreateDirectory(d); } catch { }
            return d;
        }

        // 信号队列放在用户目录下的 .clawdpet\signals，不放 AppData：
        // Claude 桌面版是打包应用，它启动的 hooks 往 AppData 里写的文件会被系统悄悄转存到它自己的专用目录
        // （AppData\Local\Packages\Claude_…\LocalCache），从桌面 / 开始菜单打开的桌宠看不到，就收不到「开始干活」了。
        // 用户目录不会被这样转存，hooks 和桌宠不管从哪儿启动，看到的都是同一个文件夹。
        public static string QueueDir()
        {
            string home = "";
            try { home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); } catch { }
            string d = home.Length > 0 ? Path.Combine(Path.Combine(home, ".clawdpet"), "signals") : Path.Combine(Dir(), "signals");
            try { Directory.CreateDirectory(d); } catch { }
            return d;
        }

        public static string ReadStdin()
        {
            try
            {
                if (!Console.IsInputRedirected) return "";
                string result = "";
                Thread t = new Thread(() =>
                {
                    try { using (StreamReader r = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8)) result = r.ReadToEnd(); }
                    catch { }
                });
                t.IsBackground = true;
                t.Start();
                if (!t.Join(800)) return "";
                return result ?? "";
            }
            catch { return ""; }
        }

        public static void Send(string cmd, string json)
        {
            string sid = "", cwd = "", msg = "";
            if (!string.IsNullOrEmpty(json))
            {
                sid = JsonField(json, "session_id");
                cwd = JsonField(json, "cwd");
                msg = JsonField(json, "message");
            }
            string project = "";
            if (cwd.Length > 0) { try { project = Path.GetFileName(cwd.TrimEnd('\\', '/')); } catch { } }
            string body = cmd.Trim().ToLowerInvariant() + "\n" + sid + "\n" + project + "\n" + msg.Replace("\r", " ").Replace("\n", " ");
            string dir = QueueDir();
            try
            {
                foreach (string f in Directory.GetFiles(dir))
                    if ((DateTime.UtcNow - File.GetLastWriteTimeUtc(f)).TotalSeconds > 60) File.Delete(f);
            }
            catch { }
            string name = Path.Combine(dir, DateTime.UtcNow.Ticks.ToString("D20") + "-" + Process.GetCurrentProcess().Id + ".sig");
            try
            {
                File.WriteAllText(name + ".tmp", body, Encoding.UTF8);
                File.Move(name + ".tmp", name);
            }
            catch { }
        }

        static string JsonField(string json, string key)
        {
            Match m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            return m.Success ? Unescape(m.Groups[1].Value) : "";
        }

        static string Unescape(string s)
        {
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '\\' || i + 1 >= s.Length) { sb.Append(c); continue; }
                char n = s[++i];
                switch (n)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': case 'f': break;
                    case 'u':
                        if (i + 4 < s.Length)
                        {
                            int code;
                            if (int.TryParse(s.Substring(i + 1, 4), System.Globalization.NumberStyles.HexNumber, null, out code))
                            {
                                sb.Append((char)code);
                                i += 4;
                            }
                        }
                        break;
                    default: sb.Append(n); break;
                }
            }
            return sb.ToString();
        }
    }

    // ------------------------------------------------------------------ Win32
    static class Native
    {
        public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        public static Rectangle WindowMonitorBounds(IntPtr hwnd)
        {
            IntPtr h = MonitorFromWindow(hwnd, 2);
            MONITORINFO mi = new MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            if (h != IntPtr.Zero && GetMonitorInfo(h, ref mi))
                return Rectangle.FromLTRB(mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Right, mi.rcMonitor.Bottom);
            return Rectangle.Empty;
        }
        [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr hMon, ref MONITORINFO mi);

        // 比 Screen.FromPoint 便宜得多（后者每次都会新建一个 DC）
        public static void MonitorAt(int x, int y, out Rectangle bounds, out Rectangle work)
        {
            POINT p; p.X = x; p.Y = y;
            IntPtr h = MonitorFromPoint(p, 2);
            MONITORINFO mi = new MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            if (h != IntPtr.Zero && GetMonitorInfo(h, ref mi))
            {
                bounds = Rectangle.FromLTRB(mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Right, mi.rcMonitor.Bottom);
                work = Rectangle.FromLTRB(mi.rcWork.Left, mi.rcWork.Top, mi.rcWork.Right, mi.rcWork.Bottom);
                return;
            }
            bounds = SystemInformation.VirtualScreen;
            work = bounds;
        }
        public static Rectangle WorkAt(int x, int y) { Rectangle b, w; MonitorAt(x, y, out b, out w); return w; }
        public static Rectangle BoundsAt(int x, int y) { Rectangle b, w; MonitorAt(x, y, out b, out w); return b; }
        // 这个点上有没有显示器（探头时要确认屏幕边外面没有别的显示器）
        public static bool HasMonitorAt(int x, int y) { POINT p; p.X = x; p.Y = y; return MonitorFromPoint(p, 0) != IntPtr.Zero; }

        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int index);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
        [DllImport("kernel32.dll")] public static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int v, int size);

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;

        // 分层窗口：整张带透明度的图连同位置、大小一次交给系统
        [StructLayout(LayoutKind.Sequential)] public struct PT { public int X, Y; public PT(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)] public struct SZ { public int W, H; public SZ(int w, int h) { W = w; H = h; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] public struct BLEND { public byte Op, Flags, Alpha, Format; }
        [DllImport("user32.dll", SetLastError = true)] public static extern bool UpdateLayeredWindow(IntPtr h, IntPtr hdcDst, ref PT dst, ref SZ size, IntPtr hdcSrc, ref PT src, int key, ref BLEND blend, int flags);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);

        public static double IdleSeconds()
        {
            LASTINPUTINFO l = new LASTINPUTINFO();
            l.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
            if (!GetLastInputInfo(ref l)) return 0;
            return unchecked((uint)Environment.TickCount - l.dwTime) / 1000.0;
        }
    }


    // ------------------------------------------------------------------ 姿势
    enum EyePose { Open, Blink, Closed, Happy, Squint, Wide, Dizzy, Focus, Heart, None }
    enum ArmPose { Normal, Raised, Up, Down, Out, UpOut, DownOut }
    enum LegPose { Stand, WalkA, WalkB, DangleA, DangleB, Tucked, Kick, Spread, Hang }

    class Pose
    {
        public EyePose Eyes = EyePose.Open;
        public ArmPose ArmL = ArmPose.Normal, ArmR = ArmPose.Normal;
        public LegPose Legs = LegPose.Stand;
        public int LookX, LookY, BodyDy, Lift, Facing = 1;
        public int LegDx, LegDx2, LegPh;      // Hang：两组腿各自甩出去几格、哪组长哪组短
        public int Acc;                       // 好几只时好分辨的小饰品：0 没有，1 眼镜，2 帽子，3..6 随季节换（春小花 / 夏西瓜 / 秋枫叶 / 冬围巾）
        public int BlanketH, BlanketC;        // 盖被子：盖到第几行（0 = 没盖，5 = 拉到下巴）、哪种颜色（0 蓝 / 1 粉 / 2 绿 / 3 黄）
        public bool Squash, Blush, Breath;

        public long Key()
        {
            long k = (int)Eyes;
            k = k * 16 + (int)ArmL; k = k * 16 + (int)ArmR; k = k * 16 + (int)Legs;
            k = k * 4 + (LookX + 1); k = k * 4 + (LookY + 1); k = k * 8 + BodyDy; k = k * 4 + Lift; k = k * 4 + (Facing + 1);
            k = k * 2 + (Squash ? 1 : 0); k = k * 2 + (Blush ? 1 : 0); k = k * 2 + (Breath ? 1 : 0);
            k = k * 8 + (LegDx + 3); k = k * 8 + (LegDx2 + 3); k = k * 2 + (LegPh & 1);
            k = k * 8 + (Acc & 7);
            k = k * 8 + (BlanketH & 7); k = k * 4 + (BlanketC & 3);
            return k;
        }

        public bool Same(Pose o) { return o != null && o.Key() == Key(); }
    }

    // 调色板像素画：每个字符 = 1 格，'.' 透明。palette 写成 "C=#D9A05B,c=#6B3E1F"
    class Art
    {
        public string Name; public string[] Rows; public string[][] Frames; public bool Food;
        readonly Dictionary<char, Color> pal = new Dictionary<char, Color>();

        public Art(string name, string[] rows, string palette)
        {
            Name = name; Rows = rows;
            foreach (string kv in palette.Split(','))
            {
                string t = kv.Trim();
                if (t.Length >= 3 && t[1] == '=') pal[t[0]] = ColorTranslator.FromHtml(t.Substring(2));
            }
        }
        public bool Col(char ch, out Color c) { return pal.TryGetValue(ch, out c); }
        public int W { get { return Rows[0].Length; } }
        public int H { get { return Rows.Length; } }
    }

    // ------------------------------------------------------------------ 像素绘制
    // 画布 24x18 格，脚底线在第 17 行。身体 16x10，位于 (4,4)。
    static class Sprite
    {
        public const int Ground = 17;
        public static readonly Color BodyC = Color.FromArgb(215, 119, 87);
        public static readonly Color EyeC = Color.FromArgb(24, 22, 20);
        public static readonly Color BlushC = Color.FromArgb(247, 172, 156);
        public static readonly Color HeartC = Color.FromArgb(233, 84, 108);
        public static readonly Color ZC = Color.FromArgb(150, 166, 190);
        public static readonly Color NoteC = Color.FromArgb(124, 140, 255);
        public static readonly Color StarC = Color.FromArgb(246, 196, 72);
        public static readonly Color BangC = Color.FromArgb(229, 72, 77);
        public static readonly Color DropC = Color.FromArgb(108, 180, 238);
        public static readonly Color PuffC = Color.FromArgb(214, 208, 196);
        public static readonly Color CrumbC = Color.FromArgb(180, 103, 63);
        public static readonly Color TomatoC = Color.FromArgb(226, 72, 60);
        public static readonly Color LeafC = Color.FromArgb(92, 170, 80);
        public static readonly Color BubbleFill = Color.FromArgb(250, 249, 245);
        public static readonly Color BubbleBorder = Color.FromArgb(61, 57, 41);
        public static readonly Color TextC = Color.FromArgb(31, 30, 29);
        public static readonly Color SpinText = Color.FromArgb(193, 95, 60);
        public static readonly Color NoseRing = Color.FromArgb(95, 164, 216);
        public static readonly Color NoseFill = Color.FromArgb(214, 238, 255);   // 色键窗口没有半透明，用不透明的淡蓝
        public static readonly Color NosePop = Color.FromArgb(143, 196, 234);
        public static readonly Color BtnHover = Color.FromArgb(236, 232, 220);
        public static readonly Color BtnDown = Color.FromArgb(224, 218, 203);
        public static readonly Color SteamC = Color.FromArgb(230, 226, 218);
        public static readonly Color GlowC = Color.FromArgb(191, 227, 255);

        public static readonly string[] Heart = { ".XX.XX.", "XXXXXXX", "XXXXXXX", ".XXXXX.", "..XXX..", "...X..." };
        public static readonly string[] Zed = { "XXXXX", "...X.", "..X..", ".X...", "XXXXX" };
        public static readonly string[] Note = { "..XXX", "..X.X", "..X..", "XXX..", "XXX.." };
        public static readonly string[] Star = { "..X..", "..X..", "XXXXX", "..X..", "..X.." };
        public static readonly string[] Bang = { "XX", "XX", "XX", "XX", "..", "XX" };
        public static readonly string[] Drop = { ".X.", ".X.", "XXX", "XXX", ".X." };
        public static readonly string[] Puff = { ".XX.", "XXXX", ".XX." };
        public static readonly string[] Crumb = { "XX", "XX" };
        public static readonly string[] Tomato = { "...G...", "..GGG..", ".RRGRR.", "RRRRRRR", "RRRRRRR", "RRRRRRR", ".RRRRR." };
        public static readonly string[] QMark = { ".XXX.", "X...X", "...X.", "..X..", ".....", "..X.." };
        public static readonly string[] Anger = { "XX.XX", "X...X", ".....", "X...X", "XX.XX" };
        public static readonly string[] Bulb = { ".XXX.", "XXXXX", "XXXXX", ".XXX.", ".....", ".XXX." };
        public static readonly string[] DotsA = { "X...." }, DotsB = { "X.X.." }, DotsC = { "X.X.X" };
        public static readonly string[] Steam = { ".X", "X.", ".X" };
        // 办公时偶尔飘起来的小代码符号
        public static readonly string[] CodeTag = { "..X...X.X..", ".X....X..X.", "X....X....X", ".X..X....X.", "..X.X...X.." };
        public static readonly string[] CodeBrace = { ".XX...XX.", ".X.....X.", "X.......X", ".X.....X.", ".XX...XX." };

        // Claude Code 风格的 ✻ 转圈
        public static readonly string[][] Spin = {
            new string[] { ".......", ".......", ".......", "...X...", ".......", ".......", "......." },
            new string[] { ".......", ".......", "...X...", "..XXX..", "...X...", ".......", "......." },
            new string[] { ".......", ".X.X.X.", "..XXX..", ".XXXXX.", "..XXX..", ".X.X.X.", "......." },
            new string[] { "...X...", ".X.X.X.", "..XXX..", "XXXXXXX", "..XXX..", ".X.X.X.", "...X..." },
            new string[] { "X..X..X", ".X.X.X.", "..XXX..", "XXX.XXX", "..XXX..", ".X.X.X.", "X..X..X" },
        };
        public static readonly int[] SpinSeq = { 0, 1, 2, 3, 4, 3, 2, 1 };

        static readonly string[] Compact = {
            "..XXXXXXXXXXXX..",
            "..XXXXXXXXXXXX..",
            "..XXEXXXXXXEXX..",
            "..XXEXXXXXXEXX..",
            "XXXXXXXXXXXXXXXX",
            "XXXXXXXXXXXXXXXX",
            "..XXXXXXXXXXXX..",
            "..XXXXXXXXXXXX..",
            "...X.X....X.X...",
            "...X.X....X.X...",
        };

        // ---------------- 零食 / 球 / 蝴蝶 / 梦 / 按钮图标 / 小电脑
        public static readonly Art[] Snacks = {
            new Art("饼干", new string[] { ".CCC.", "CcCCC", "CCCcC", "CcCCC", ".CCC." }, "C=#D9A05B,c=#6B3E1F"),
            new Art("苹果", new string[] { "..L..", ".RLR.", "RRRRR", "RRRRR", ".RRR." }, "R=#E2483C,L=#5CAA50"),
            new Art("甜甜圈", new string[] { ".PPP.", "PPPPP", "PP.PP", "DDDDD", ".DDD." }, "P=#F08DB0,D=#D9A05B"),
        };
        const string BallPal = "R=#E9546C,W=#FAF9F5,B=#6CB4EE,Y=#F6C448";
        static readonly char[] BallQ = { 'R', 'W', 'B', 'Y' };
        static Art[] ballArts;
        public static string[] BallRows(int k)
        {
            char a = BallQ[(k + 400) & 3], b = BallQ[(k + 401) & 3], c = BallQ[(k + 402) & 3], d = BallQ[(k + 403) & 3];   // 左上 右上 右下 左下
            string ab = new string(a, 3) + new string(b, 3), dc = new string(d, 3) + new string(c, 3);
            return new string[] { "." + a + a + b + b + ".", ab, ab, dc, dc, "." + d + d + c + c + "." };
        }
        public static Art BallArt(int k)
        {
            if (ballArts == null)
            {
                ballArts = new Art[4];
                for (int i = 0; i < 4; i++) ballArts[i] = new Art("球", BallRows(i), BallPal);
            }
            return ballArts[k & 3];
        }
        public static readonly string[][] FlyFrames = { new string[] { "W...W", "WWBWW", ".WBW.", "WW.WW" }, new string[] { ".W.W.", ".WBW.", "..B..", "....." } };
        static readonly string[] FlyCols = { "#7C8CFF", "#F6C448", "#E9546C", "#5CC8A8" };
        static Art[] flyArts;
        public static Art FlyArt(int i)
        {
            if (flyArts == null)
            {
                flyArts = new Art[FlyCols.Length];
                for (int j = 0; j < FlyCols.Length; j++) { flyArts[j] = new Art("蝴蝶", FlyFrames[0], "W=" + FlyCols[j] + ",B=#3D3929"); flyArts[j].Frames = FlyFrames; }
            }
            return flyArts[((i % flyArts.Length) + flyArts.Length) % flyArts.Length];
        }
        public static readonly Art[] Dreams = MakeDreams();
        static Art[] MakeDreams()
        {
            Art fly = new Art("蝴蝶", FlyFrames[0], "W=#7C8CFF,B=#3D3929"); fly.Frames = FlyFrames;
            Art[] d = {
                new Art("饼干", Snacks[0].Rows, "C=#D9A05B,c=#6B3E1F"), new Art("苹果", Snacks[1].Rows, "R=#E2483C,L=#5CAA50"),
                new Art("甜甜圈", Snacks[2].Rows, "P=#F08DB0,D=#D9A05B"), new Art("球", BallRows(0), BallPal),
                new Art("爱心", Heart, "X=#E9546C"), fly, new Art("音符", Note, "X=#7C8CFF"),
            };
            d[0].Food = d[1].Food = d[2].Food = true;
            return d;
        }
        public static readonly Art ZzArt = new Art("睡", Zed, "X=#7F8FB0");
        public static readonly Art CloudArt = new Art("梦", new string[] { "..XXX..", ".XXXXX.", "XXXXXXX", ".XXXXX." }, "X=#8F86D0");
        public static readonly Art PinArt = new Art("别动", new string[] { ".XXX.", "XXoXX", "XXXXX", ".XXX.", "..X..", "..X.." }, "X=#E5484D,o=#FAF9F5");
        public static readonly Art MoreArt = new Art("更多", new string[] { "X.X.X" }, "X=#1F1E1D");
        // 小电脑（从背面看到的屏幕盖）+ 咖啡杯，画在小人身前（格子坐标，左上角在画布的 (5,12)）
        static readonly Art Laptop = new Art("电脑", new string[] {
            ".OOOOOOOOOOOO.",
            ".OLLLLLLLsSLO.",
            ".OLLLLLLLSSLO.",
            ".OLLLLLLLLLLO.",
            "DDDDDDDDDDDDDD" }, "O=#3D3929,L=#C9CCD3,s=#F6C448,S=#E9546C,D=#9DA1AA");
        static readonly Art Mug = new Art("杯子", new string[] { "WWW.", "MMMH", "MMMH", "MMM." }, "W=#FAF9F5,M=#6CB4EE,H=#4F95CC");

        // ---------------- 待机小道具（画在身子前面；格子坐标，相对小人 24x18 画布的左上角）
        static readonly string[] BookRows = {
            "..............",
            "..............",
            ".WWWWWkkWWWWW.",
            "WllllWkkWllllW",
            "WWWWWWkkWWWWWW",
            "WlllWWkkWlllWW",
            "BBBBBBBBBBBBBB" };
        static readonly string[] BookFlip = {      // 翻页：右边一页竖起来
            "........WW....",
            "........WlW...",
            ".WWWWWkkWWWW..",
            "WllllWkkWllllW",
            "WWWWWWkkWWWWWW",
            "WlllWWkkWlllWW",
            "BBBBBBBBBBBBBB" };
        static readonly Art BookArt = new Art("书", BookRows, "W=#FAF9F5,l=#B9B2A3,k=#D8D2C4,B=#5B7DB1");
        static readonly string[] UkeRows = {
            "..........nHH",
            "........nn...",
            ".ooo..nn.....",
            "ooooonn......",
            "oosoo........",
            ".ooo........." };
        static readonly Art UkeArt = new Art("尤克里里", UkeRows, "o=#E0A867,s=#3D3929,n=#7A4A22,H=#3D3929");
        static readonly string[] BroomRows = {
            "S....",
            ".S...",
            ".S...",
            "..S..",
            "..S..",
            "..S..",
            "..S..",
            "..S..",
            ".bbb.",
            "YYYYY",
            "Y.Y.Y" };
        static readonly Art BroomArt = new Art("扫帚", BroomRows, "S=#8A5A2B,b=#C04A3A,Y=#E3B858");
        static readonly string[] BarbellRows = {
            "PP..................PP",
            "PPXXXXXXXXXXXXXXXXXXPP",
            "PP..................PP" };
        static readonly Art BarbellArt = new Art("杠铃", BarbellRows, "P=#3D3929,X=#9DA1AA");
        static readonly string[] CastleRows = {
            "....FF...",
            "....s....",
            "...SSS...",
            "S.SSsSS.S",
            "SSSSSSSSS",
            "SsSSDSSsS",
            "SSSSDSSSS",
            "sSSsSsSSs" };
        static readonly Art CastleArt = new Art("沙堡", CastleRows, "S=#E8C78E,s=#D6B074,F=#E5484D,D=#B98F55");
        static readonly string[] PhonesRows = {
            "..HHHHHHHHHHHHHH..",
            ".H..............H.",
            "CC..............CC",
            "CC..............CC",
            "CC..............CC" };
        static readonly Art PhonesArt = new Art("耳机", PhonesRows, "H=#3D3929,C=#E5484D");
        static readonly string[] PaperA = { "WWWW", "WWWw", "WWww" }, PaperB = { ".WW.", "WWWw", ".ww." };
        static readonly Art PaperArt = new Art("纸", PaperA, "W=#FAF9F5,w=#C9C3B5");
        public static readonly string[] PlaneRows = {
            "oW.....",
            "oWWWo..",
            ".wwwWWo",
            "..wo..." };
        public static readonly Art PlaneArt = new Art("纸飞机", PlaneRows, "W=#FAF9F5,w=#D8D2C4,o=#8F8A7E");
        public static readonly Art PoleArt = new Art("木杆", new string[] { "Ld" }, "L=#C98E52,d=#9A6332,D=#6E4520");
        static readonly Dictionary<int, string[]> poleCache = new Dictionary<int, string[]>();
        // 木杆：2 格宽、len 格长，每隔几格一道深色的节
        public static string[] PoleRows(int len)
        {
            len = Math.Max(1, len);
            string[] r;
            if (poleCache.TryGetValue(len, out r)) return r;
            if (poleCache.Count > 400) poleCache.Clear();
            r = new string[len];
            for (int i = 0; i < len; i++) r[i] = i == 0 ? "dd" : (i % 7 == 4 ? "DD" : "Ld");
            poleCache[len] = r;
            return r;
        }

        // 斜着的木杆：杆子底的中线在 (0, 0)，顶在 (dx, -dy)（单位：格，dy > 0，斜度不超过 45°）。
        // 每一行 2 格宽，一行一行错开画成台阶；ox = 图的左边比杆子底中线靠左几格
        public static string[] SlantPoleRows(double dx, double dy, out int ox)
        {
            int h = Math.Max(1, (int)Math.Round(dy));
            int[] col = new int[h];
            int mn = int.MaxValue, mx = int.MinValue;
            for (int i = 0; i < h; i++)   // i = 0 是最上面一行
            {
                double t = (h - i - 0.5) / h;   // 0 = 杆子底，1 = 杆子顶
                int c = (int)Math.Floor(dx * t - 0.5);
                col[i] = c; mn = Math.Min(mn, c); mx = Math.Max(mx, c);
            }
            ox = mn;
            int w = mx - mn + 2;
            string[] r = new string[h];
            char[] line = new char[w];
            for (int i = 0; i < h; i++)
            {
                for (int k = 0; k < w; k++) line[k] = '.';
                int c = col[i] - mn;
                char a = 'L', b = 'd';
                if (i == 0) a = b = 'd'; else if (i % 7 == 4) a = b = 'D';   // 和竖着的杆子一样：顶头一截深色，每隔几格一道节
                line[c] = a; line[c + 1] = b;
                r[i] = new string(line);
            }
            return r;
        }

        static readonly Dictionary<string[], string[]> flipCache = new Dictionary<string[], string[]>();
        public static string[] FlipRows(string[] rows)
        {
            string[] f;
            if (flipCache.TryGetValue(rows, out f)) return f;
            f = new string[rows.Length];
            for (int i = 0; i < rows.Length; i++) { char[] ch = rows[i].ToCharArray(); Array.Reverse(ch); f[i] = new string(ch); }
            flipCache[rows] = f;
            return f;
        }
        static readonly Dictionary<int, string[]> castleCache = new Dictionary<int, string[]>();
        static string[] CastleFrom(int first)   // 只画第 first 行往下（堆到一半 / 塌掉一半）
        {
            string[] r;
            if (castleCache.TryGetValue(first, out r)) return r;
            r = new string[CastleRows.Length];
            for (int i = 0; i < r.Length; i++) r[i] = i < first ? new string('.', CastleRows[i].Length) : CastleRows[i];
            castleCache[first] = r;
            return r;
        }

        // kind：book / uke / broom / barbell / castle / phones / paper / plane；frame：动画帧；bodyDy：身子往下沉了几格
        public static bool Prop(string kind, int frame, int facing, int bodyDy, out Art art, out string[] rows, out int px, out int py)
        {
            art = null; rows = null; px = py = 0;
            bool left = facing < 0;
            switch (kind)
            {
                case "book": art = BookArt; rows = frame == 1 ? BookFlip : BookRows; px = 5; py = 8 + bodyDy; break;
                case "uke": art = UkeArt; rows = left ? FlipRows(UkeRows) : UkeRows; px = left ? 6 : 5; py = 7 + bodyDy; break;
                case "broom": art = BroomArt; rows = left ? FlipRows(BroomRows) : BroomRows; px = left ? -frame : 19 + frame; py = 6; break;
                case "barbell": art = BarbellArt; rows = BarbellRows; px = 1; py = frame == 1 ? 1 : 8 + bodyDy; break;
                case "castle":
                    {
                        int[] first = { 8, 6, 3, 0, 3, 6 };   // 0 = 还没堆，1..3 一点点堆高，4、5 塌掉
                        int f = Math.Max(0, Math.Min(5, frame));
                        if (f == 0) return false;
                        art = CastleArt; rows = CastleFrom(first[f]); px = left ? -7 : 22; py = 9;
                    }
                    break;
                case "phones": art = PhonesArt; rows = PhonesRows; px = 3; py = 2 + bodyDy; break;
                case "paper": art = PaperArt; rows = frame == 1 ? PaperB : PaperA; px = left ? 7 : 13; py = 10 + bodyDy; break;
                case "plane": art = PlaneArt; rows = left ? FlipRows(PlaneRows) : PlaneRows; px = left ? -8 : 25; py = 1; break;
                default: return false;
            }
            return true;
        }

        static readonly StringFormat Fmt = MakeFmt();
        static StringFormat MakeFmt()
        {
            StringFormat f = (StringFormat)StringFormat.GenericTypographic.Clone();
            f.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
            f.Trimming = StringTrimming.None;
            return f;
        }

        static readonly Dictionary<int, SolidBrush> brushes = new Dictionary<int, SolidBrush>();
        public static SolidBrush BrushOf(Color c)
        {
            SolidBrush b;
            int k = c.ToArgb();
            if (!brushes.TryGetValue(k, out b)) { b = new SolidBrush(c); brushes[k] = b; }
            return b;
        }
        public static int Half(int S) { return Math.Max(2, (S + 1) / 2); }

        static void Cell(Graphics g, Brush b, int ox, int oy, int s, int x, int y, int w, int h)
        {
            if (w > 0 && h > 0) g.FillRectangle(b, ox + x * s, oy + y * s, w * s, h * s);
        }

        static void Dots(Graphics g, Brush b, int ox, int oy, int s, int x, int y, int[] pts)
        {
            for (int i = 0; i + 1 < pts.Length; i += 2) Cell(g, b, ox, oy, s, x + pts[i], y + pts[i + 1], 1, 1);
        }

        public static void Draw(Graphics g, Pose p, int ox, int oy, int s)
        {
            oy -= p.Lift * s;
            int bl = 4, bw = 16, bt = 4, bh = 10;
            if (p.Squash) { bl = 3; bw = 18; bt = 6; bh = 9; }
            bt += p.BodyDy;
            if (p.Breath) { bt += 1; bh -= 1; }
            int legTop = bt + bh;

            using (SolidBrush body = new SolidBrush(BodyC))
            using (SolidBrush eye = new SolidBrush(EyeC))
            using (SolidBrush blush = new SolidBrush(BlushC))
            {
                int[] lx = { 5, 8, 14, 17 };
                for (int i = 0; i < 4; i++)
                {
                    int len = Ground - legTop, dx = 0;
                    bool odd = (i & 1) == 1, drawn = false;
                    switch (p.Legs)
                    {
                        case LegPose.WalkA: if (odd) { len -= 1; dx = p.Facing; } break;
                        case LegPose.WalkB: if (!odd) { len -= 1; dx = p.Facing; } break;
                        case LegPose.DangleA: len = odd ? 3 : 4; break;
                        case LegPose.DangleB: len = odd ? 4 : 3; break;
                        case LegPose.Tucked: len = 0; break;
                        case LegPose.Kick: if (i == (p.Facing > 0 ? 3 : 0)) { len -= 2; dx = p.Facing * 2; } break;
                        case LegPose.Spread: if (i == 0) dx = -2; else if (i == 3) dx = 2; break;
                        case LegPose.Hang:
                            {
                                // 悬空的腿：上段连着身体，下段跟着甩（两组腿各甩各的）
                                int ddx = odd ? p.LegDx2 : p.LegDx, L = ((i + p.LegPh) & 1) != 0 ? 3 : 4, mid = ddx / 2;
                                Cell(g, body, ox, oy, s, lx[i], legTop, 2, 1);
                                Cell(g, body, ox, oy, s, lx[i] + mid, legTop + 1, 2, 1);
                                Cell(g, body, ox, oy, s, lx[i] + ddx, legTop + 2, 2, L - 2);
                                drawn = true;
                            }
                            break;
                    }
                    if (!drawn) Cell(g, body, ox, oy, s, lx[i] + dx, legTop, 2, len);
                }

                Arm(g, body, ox, oy, s, p.ArmL, bl, bt, false);
                Arm(g, body, ox, oy, s, p.ArmR, bl, bt, true);
                Cell(g, body, ox, oy, s, bl, bt, bw, bh);

                int ey = bt + 2 + p.LookY;
                int el = 7 + p.LookX, er = 15 + p.LookX;
                int eh = p.Squash ? 2 : 3;
                switch (p.Eyes)
                {
                    case EyePose.Open:
                        Cell(g, eye, ox, oy, s, el, ey, 2, eh); Cell(g, eye, ox, oy, s, er, ey, 2, eh); break;
                    case EyePose.Blink:
                        Cell(g, eye, ox, oy, s, el, ey + eh - 1, 2, 1); Cell(g, eye, ox, oy, s, er, ey + eh - 1, 2, 1); break;
                    case EyePose.Closed:
                        Cell(g, eye, ox, oy, s, 6, bt + 4, 3, 1); Cell(g, eye, ox, oy, s, 15, bt + 4, 3, 1); break;
                    case EyePose.Happy:
                        {
                            int[] pts = { 1, 0, 0, 1, 2, 1, 0, 2, 2, 2 };
                            Dots(g, eye, ox, oy, s, 6 + p.LookX, ey, pts); Dots(g, eye, ox, oy, s, 15 + p.LookX, ey, pts);
                        }
                        break;
                    case EyePose.Squint:
                        Dots(g, eye, ox, oy, s, 7, ey, new int[] { 0, 0, 1, 1, 0, 2 });
                        Dots(g, eye, ox, oy, s, 15, ey, new int[] { 1, 0, 0, 1, 1, 2 });
                        break;
                    case EyePose.Wide:
                        Cell(g, eye, ox, oy, s, el, ey - 1, 2, 4); Cell(g, eye, ox, oy, s, er, ey - 1, 2, 4); break;
                    case EyePose.Dizzy:
                        {
                            int[] pts = { 0, 0, 2, 0, 1, 1, 0, 2, 2, 2 };
                            Dots(g, eye, ox, oy, s, 6, ey, pts); Dots(g, eye, ox, oy, s, 15, ey, pts);
                        }
                        break;
                    case EyePose.Focus:
                        Cell(g, eye, ox, oy, s, el, ey + 1, 2, 2); Cell(g, eye, ox, oy, s, er, ey + 1, 2, 2); break;
                    case EyePose.Heart:
                        {
                            int[] pts = { 0, 0, 2, 0, 0, 1, 1, 1, 2, 1, 1, 2 };
                            Dots(g, eye, ox, oy, s, 6 + p.LookX, ey, pts); Dots(g, eye, ox, oy, s, 15 + p.LookX, ey, pts);
                        }
                        break;
                    case EyePose.None: break;   // 转身时看到的是后脑勺
                }
                if (p.Blush && p.Eyes != EyePose.None)
                {
                    Cell(g, blush, ox, oy, s, 5, bt + 5, 2, 1);
                    Cell(g, blush, ox, oy, s, 17, bt + 5, 2, 1);
                }
                DrawAcc(g, p, ox, oy, s, bt);
            }
            if (p.BlanketH > 0) DrawBlanket(g, p.BlanketH, p.BlanketC, ox, oy, s);
        }

        // ---------------- 小被子：盖在躺着的小人身上，从脚边往上拉到下巴，脸露在外面
        // H/h 翻过来的被头（米白 + 折痕），Q 被面，o 小圆点，q 被子底边
        static readonly string[] BlanketRows = {
            ".HHHHHHHHHHHHHHHHHHHH.",
            "hhhhhhhhhhhhhhhhhhhhhh",
            "QQQoQQQQQQoQQQQQQoQQQQ",
            "QQQQQQQoQQQQQQoQQQQQQQ",
            "qqqqqqqqqqqqqqqqqqqqqq",
        };
        static readonly Color BlanketHem = Color.FromArgb(0xFB, 0xF6, 0xEA), BlanketFold = Color.FromArgb(0xE9, 0xDF, 0xC9);
        static readonly Color[][] BlanketCols = {
            new[] { Color.FromArgb(0x93, 0xBB, 0xEB), Color.FromArgb(0xEA, 0xF3, 0xFD), Color.FromArgb(0x6F, 0x97, 0xCE) },   // 蓝
            new[] { Color.FromArgb(0xF3, 0xA9, 0xC0), Color.FromArgb(0xFD, 0xEB, 0xF1), Color.FromArgb(0xD8, 0x84, 0x9F) },   // 粉
            new[] { Color.FromArgb(0xA6, 0xD5, 0x9C), Color.FromArgb(0xEE, 0xF8, 0xEA), Color.FromArgb(0x7E, 0xB6, 0x75) },   // 绿
            new[] { Color.FromArgb(0xF5, 0xD2, 0x7A), Color.FromArgb(0xFF, 0xF7, 0xDD), Color.FromArgb(0xD8, 0xAE, 0x4E) },   // 黄
        };

        // h：露出来几行（拉被子 / 掀被子时一行一行变）；被头总在最上面，下面接着被面
        static void DrawBlanket(Graphics g, int h, int col, int ox, int oy, int s)
        {
            int n = BlanketRows.Length;
            h = Math.Min(n, h);
            Color[] c = BlanketCols[col & 3];
            for (int j = 0; j < h; j++)
            {
                string row = BlanketRows[j < 2 ? j : n - h + j];
                int y = Ground - h + j;
                for (int i = 0; i < row.Length; i++)
                {
                    char ch = row[i];
                    if (ch == '.') continue;
                    Color k = ch == 'H' ? BlanketHem : ch == 'h' ? BlanketFold : ch == 'Q' ? c[0] : ch == 'o' ? c[1] : c[2];
                    Cell(g, BrushOf(k), ox, oy, s, 1 + i, y, 1, 1);
                }
            }
        }

        // ---------------- 小饰品（好几只 Clawd 时好分辨）：画在身上或者头顶那几行（画布第 0..3 行本来是空的）
        static readonly Color AccDark = Color.FromArgb(0x3D, 0x39, 0x29), AccRed = Color.FromArgb(0xE5, 0x48, 0x4D), AccFrame = Color.FromArgb(0x2F, 0x4A, 0x7A);
        static readonly Color AccStem = Color.FromArgb(0x4E, 0x9A, 0x44), AccPetal = Color.FromArgb(0xF0, 0x8D, 0xB0), AccRind = Color.FromArgb(0x5C, 0xAA, 0x50);
        static readonly Color AccMelon = Color.FromArgb(0xE2, 0x48, 0x3C), AccMaple = Color.FromArgb(0xE8, 0x74, 0x2A), AccMapleDark = Color.FromArgb(0xC8, 0x45, 0x2B);
        static readonly Color AccWool = Color.FromArgb(0xFA, 0xF9, 0xF5), AccTwig = Color.FromArgb(0x8A, 0x5A, 0x2B);

        // 第四只的饰品跟着季节换：3 春（小花）/ 4 夏（西瓜）/ 5 秋（枫叶）/ 6 冬（围巾）
        public static int SeasonAcc()
        {
            int m = DateTime.Now.Month;
            return m >= 3 && m <= 5 ? 3 : m >= 6 && m <= 8 ? 4 : m >= 9 && m <= 11 ? 5 : 6;
        }

        static void DrawAcc(Graphics g, Pose p, int ox, int oy, int s, int bt)
        {
            switch (p.Acc)
            {
                case 1:   // 眼镜：两个镜框套在眼睛外面（眼珠在框里转），中间一道鼻梁，两边镜腿；转到背面看不到
                    if (p.Eyes == EyePose.None) break;
                    {
                        SolidBrush f = BrushOf(AccFrame);
                        int y0 = bt + 1;
                        Cell(g, f, ox, oy, s, 5, y0, 6, 1); Cell(g, f, ox, oy, s, 5, y0 + 4, 6, 1);
                        Cell(g, f, ox, oy, s, 5, y0 + 1, 1, 3); Cell(g, f, ox, oy, s, 10, y0 + 1, 1, 3);
                        Cell(g, f, ox, oy, s, 13, y0, 6, 1); Cell(g, f, ox, oy, s, 13, y0 + 4, 6, 1);
                        Cell(g, f, ox, oy, s, 13, y0 + 1, 1, 3); Cell(g, f, ox, oy, s, 18, y0 + 1, 1, 3);
                        Cell(g, f, ox, oy, s, 11, y0 + 1, 2, 1);
                        Cell(g, f, ox, oy, s, 4, y0 + 1, 1, 1); Cell(g, f, ox, oy, s, 19, y0 + 1, 1, 1);
                    }
                    break;
                case 2:   // 帽子：黑色小礼帽 + 红帽带
                    Cell(g, BrushOf(AccDark), ox, oy, s, 8, bt - 1, 8, 1);
                    Cell(g, BrushOf(AccDark), ox, oy, s, 9, bt - 4, 6, 3);
                    Cell(g, BrushOf(AccRed), ox, oy, s, 9, bt - 2, 6, 1);
                    break;
                case 3:   // 春：头顶右边别一朵小花
                    Cell(g, BrushOf(AccStem), ox, oy, s, 17, bt - 1, 1, 1);
                    Cell(g, BrushOf(AccPetal), ox, oy, s, 16, bt - 3, 1, 1);
                    Cell(g, BrushOf(AccPetal), ox, oy, s, 18, bt - 3, 1, 1);
                    Cell(g, BrushOf(AccPetal), ox, oy, s, 17, bt - 4, 1, 1);
                    Cell(g, BrushOf(AccPetal), ox, oy, s, 17, bt - 2, 1, 1);
                    Cell(g, BrushOf(StarC), ox, oy, s, 17, bt - 3, 1, 1);
                    break;
                case 4:   // 夏：头顶一片西瓜（绿皮朝下，红瓤上有黑籽）
                    Cell(g, BrushOf(AccRind), ox, oy, s, 13, bt - 1, 6, 1);
                    Cell(g, BrushOf(AccMelon), ox, oy, s, 13, bt - 2, 6, 1);
                    Cell(g, BrushOf(AccMelon), ox, oy, s, 14, bt - 3, 4, 1);
                    Cell(g, BrushOf(EyeC), ox, oy, s, 15, bt - 2, 1, 1);
                    Cell(g, BrushOf(EyeC), ox, oy, s, 17, bt - 2, 1, 1);
                    Cell(g, BrushOf(EyeC), ox, oy, s, 16, bt - 3, 1, 1);
                    break;
                case 5:   // 秋：头顶左边落了一片枫叶
                    Cell(g, BrushOf(AccMaple), ox, oy, s, 9, bt - 4, 1, 1);
                    Cell(g, BrushOf(AccMaple), ox, oy, s, 7, bt - 3, 5, 1);
                    Cell(g, BrushOf(AccMapleDark), ox, oy, s, 9, bt - 3, 1, 1);
                    Cell(g, BrushOf(AccMaple), ox, oy, s, 8, bt - 2, 3, 1);
                    Cell(g, BrushOf(AccTwig), ox, oy, s, 9, bt - 1, 1, 1);
                    break;
                case 6:   // 冬：脸下面围一圈红围巾（比身子宽一点，像绕了一圈），一截尾巴垂下来，末端白流苏
                    Cell(g, BrushOf(AccRed), ox, oy, s, 3, bt + 6, 18, 2);
                    for (int x = 5; x < 19; x += 4) Cell(g, BrushOf(AccWool), ox, oy, s, x, bt + 6, 1, 2);
                    Cell(g, BrushOf(AccRed), ox, oy, s, 15, bt + 8, 3, 2);
                    Cell(g, BrushOf(AccWool), ox, oy, s, 15, bt + 10, 1, 1);
                    Cell(g, BrushOf(AccWool), ox, oy, s, 17, bt + 10, 1, 1);
                    break;
            }
        }

        // 每种手的姿势：相对身体左上角 (bl, bt) 的若干个矩形 {x, y, w, h}（左手；右手左右对称）
        static readonly int[][][] ArmRects = {
            new int[][] { new int[] { -3, 5, 3, 2 } },                              // Normal
            new int[][] { new int[] { -3, 4, 3, 2 } },                              // Raised
            new int[][] { new int[] { -3, 0, 2, 4 }, new int[] { -2, 3, 2, 2 } },   // Up
            new int[][] { new int[] { -2, 5, 2, 4 } },                              // Down
            new int[][] { new int[] { -4, 4, 4, 2 } },                              // Out
            new int[][] { new int[] { -4, 1, 2, 3 }, new int[] { -3, 3, 3, 2 } },   // UpOut
            new int[][] { new int[] { -3, 6, 3, 2 }, new int[] { -4, 7, 2, 3 } },   // DownOut
        };

        static void Arm(Graphics g, Brush b, int ox, int oy, int s, ArmPose a, int bl, int bt, bool right)
        {
            foreach (int[] r in ArmRects[(int)a])
            {
                int x = bl + r[0];
                if (right) x = 24 - x - r[2];   // 画布 24 格宽，左右对称
                Cell(g, b, ox, oy, s, x, bt + r[1], r[2], r[3]);
            }
        }

        // 把姿势画成 24x18 的 1 像素/格 小图，返回 ARGB 数组
        public static int[] CellPixels(Pose p)
        {
            using (Bitmap b = new Bitmap(24, 18, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.None;
                    Draw(g, p, 0, 0, 1);
                }
                int[] px = new int[24 * 18];
                BitmapData bd = b.LockBits(new Rectangle(0, 0, 24, 18), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                for (int y = 0; y < 18; y++)
                    Marshal.Copy(new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride), px, y * 24, 24);
                b.UnlockBits(bd);
                return px;
            }
        }

        // ---------------- 果冻 / 打滚 / 钟摆变形
        // u、v 是小人画布里的格子坐标；屏幕点 X = E + A*u + C*v，Y = F + B*u + D*v
        public struct Affine { public double A, B, C, D, E, F; }

        // 以脚底（拖拽时以抓住的点）为锚点缩放 + 斜切；打滚绕身体中心转，被拎着绕抓住的点（钟摆）转
        public static Affine MakeXf(int S, double Ax, double Ay, double anchorY, double sq, double lean, double flip, double rot, double swing, double pivX)
        {
            double sx = (1 - sq * 0.6) * flip, sy = 1 + sq, k = -lean, ayc = anchorY / S;
            Affine m;
            m.A = sx * S; m.B = 0; m.C = k * sy * S; m.D = sy * S;
            m.E = Ax - m.A * 12 - m.C * ayc; m.F = Ay - m.D * ayc;
            double ang = rot != 0 ? rot : swing;
            if (ang != 0)
            {
                double co = Math.Cos(ang), si = Math.Sin(ang);
                double px = rot != 0 ? Ax : Ax + pivX, py = rot != 0 ? Ay - 8 * S - 4 * S * Math.Abs(Math.Sin(rot)) : Ay;
                Affine r;
                r.A = co * m.A - si * m.B; r.B = si * m.A + co * m.B;
                r.C = co * m.C - si * m.D; r.D = si * m.C + co * m.D;
                r.E = co * (m.E - px) - si * (m.F - py) + px; r.F = si * (m.E - px) + co * (m.F - py) + py;
                m = r;
            }
            return m;
        }

        public static Affine Shift(Affine m, double dx, double dy) { m.E += dx; m.F += dy; return m; }

        public static Rectangle XfBounds(Affine m, int cw, int chh)
        {
            double minX = 1e9, maxX = -1e9, minY = 1e9, maxY = -1e9;
            for (int i = 0; i < 4; i++)
            {
                double u = (i & 1) != 0 ? cw : 0, v = (i & 2) != 0 ? chh : 0;
                double x = m.E + m.A * u + m.C * v, y = m.F + m.B * u + m.D * v;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }
            return Rectangle.FromLTRB((int)Math.Floor(minX) - 1, (int)Math.Floor(minY) - 1, (int)Math.Ceiling(maxX) + 2, (int)Math.Ceiling(maxY) + 2);
        }

        // 最近邻采样，绝不混色（透明色键窗口不会出现毛边）
        public static void RasterXf(int[] cell, int cw, int chh, int[] dst, int dw, int dh, Affine m, Rectangle clip)
        {
            Rectangle bb = Rectangle.Intersect(XfBounds(m, cw, chh), clip);
            bb = Rectangle.Intersect(bb, new Rectangle(0, 0, dw, dh));
            if (bb.Width <= 0 || bb.Height <= 0) return;
            int x0 = bb.Left, x1 = bb.Right, y0 = bb.Top, y1 = bb.Bottom;
            if (m.B == 0 && m.A > 0 && m.D > 0)
            {
                // 没有旋转、没有翻转：每一行只对应小人的一行格子，按格子算出横向区间整段填充
                for (int y = y0; y < y1; y++)
                {
                    double v = (y + 0.5 - m.F) / m.D;
                    int cy = (int)Math.Floor(v);
                    if (cy < 0 || cy >= chh) continue;
                    double baseX = m.E + m.C * v - 0.5;
                    int row = y * dw, crow = cy * cw;
                    for (int c = 0; c < cw; c++)
                    {
                        int col = cell[crow + c];
                        if ((col >> 24) == 0) continue;
                        int xs = (int)Math.Ceiling(baseX + m.A * c), xe = (int)Math.Ceiling(baseX + m.A * (c + 1));
                        if (xs < x0) xs = x0;
                        if (xe > x1) xe = x1;
                        for (int x = xs; x < xe; x++) dst[row + x] = col;
                    }
                }
                return;
            }
            // 一般情况：逐像素反算回格子
            double det = m.A * m.D - m.B * m.C;
            if (Math.Abs(det) < 1e-9) return;
            double ia = m.D / det, ib = -m.C / det, ic = -m.B / det, id = m.A / det;
            for (int y = y0; y < y1; y++)
            {
                double dy = y + 0.5 - m.F, dx0 = x0 + 0.5 - m.E;
                double u = ia * dx0 + ib * dy, v = ic * dx0 + id * dy;
                int row = y * dw;
                for (int x = x0; x < x1; x++, u += ia, v += ic)
                {
                    if (u < 0 || v < 0) continue;
                    int cu = (int)u, cv = (int)v;
                    if (cu >= cw || cv >= chh) continue;
                    int col = cell[cv * cw + cu];
                    if ((col >> 24) != 0) dst[row + x] = col;
                }
            }
        }

        // ---------------- 小电脑 + 咖啡杯（直接画进像素缓冲区；屏幕的光会把身子顶边染一点蓝）
        static int Blend(int c, Color t, double k)
        {
            int r = (c >> 16) & 255, g = (c >> 8) & 255, b = c & 255;
            r = (int)(r + (t.R - r) * k); g = (int)(g + (t.G - g) * k); b = (int)(b + (t.B - b) * k);
            return unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
        }
        static void BufCell(int[] dst, int dw, int dh, Rectangle clip, int x, int y, int s, int col)
        {
            int xs = Math.Max(Math.Max(0, clip.Left), x), ys = Math.Max(Math.Max(0, clip.Top), y);
            int xe = Math.Min(Math.Min(dw, clip.Right), x + s), ye = Math.Min(Math.Min(dh, clip.Bottom), y + s);
            for (int yy = ys; yy < ye; yy++)
                for (int xx = xs; xx < xe; xx++) dst[yy * dw + xx] = col;
        }
        static void BufArt(int[] dst, int dw, int dh, Rectangle clip, Art a, string[] rows, int x, int y, int s)
        {
            for (int r = 0; r < rows.Length; r++)
                for (int c = 0; c < rows[r].Length; c++)
                {
                    Color col;
                    if (rows[r][c] == '.' || !a.Col(rows[r][c], out col)) continue;
                    BufCell(dst, dw, dh, clip, x + c * s, y + r * s, s, col.ToArgb());
                }
        }
        // ox, oy：小人画布左上角在缓冲区里的位置；empty：缓冲区里「透明」的值；glow：屏幕光的强度（0..1）
        public static void DrawLaptop(int[] dst, int dw, int dh, Rectangle clip, int ox, int oy, int s, int facing, int empty, double glow)
        {
            // 屏幕的光从盖子上沿漏出来：只染身子颜色的像素（眼睛不染）
            int bodyC = BodyC.ToArgb();
            for (int row = 10; row <= 11; row++)
            {
                double k = row == 11 ? glow : glow * 0.45;
                int y0 = Math.Max(Math.Max(0, clip.Top), oy + row * s), y1 = Math.Min(Math.Min(dh, clip.Bottom), oy + (row + 1) * s);
                int x0 = Math.Max(Math.Max(0, clip.Left), ox + 7 * s), x1 = Math.Min(Math.Min(dw, clip.Right), ox + 17 * s);
                for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x1; x++)
                    {
                        int c = dst[y * dw + x];
                        if (c == bodyC && c != empty) dst[y * dw + x] = Blend(c, GlowC, k);
                    }
            }
            BufArt(dst, dw, dh, clip, Laptop, Laptop.Rows, ox + 5 * s, oy + 12 * s, s);
            string[] mug = Mug.Rows;
            if (facing < 0)
            {
                mug = new string[Mug.Rows.Length];
                for (int i = 0; i < mug.Length; i++) { char[] ch = Mug.Rows[i].ToCharArray(); Array.Reverse(ch); mug[i] = new string(ch); }
            }
            BufArt(dst, dw, dh, clip, Mug, mug, ox + (facing < 0 ? 1 : 19) * s, oy + 13 * s, s);
        }
        public static Rectangle LaptopRect(int ox, int oy, int s) { return new Rectangle(ox, oy + 10 * s, 24 * s, 7 * s); }

        // ---------------- 挖沙：沙堆（24x3 格，中间低一点，好让眼睛露出来）
        public static readonly Color SandA = Color.FromArgb(0xE8, 0xC7, 0x8E), SandB = Color.FromArgb(0xD6, 0xB0, 0x74);
        static readonly Art Mound = new Art("沙堆", new string[] {
            ".." + "sSs" + new string('.', 14) + "sSs" + "..",
            "." + "sSSSs" + new string('.', 12) + "sSSSs" + ".",
            "SsSSSSsSSSSSsSSSSSsSSsSS" }, "S=#E8C78E,s=#D6B074");
        // x0, y0：沙堆左上角（缓冲区坐标）；level：0..3，几行高（不到 1 时隔一列画一列，像在塌）
        public static void DrawMound(int[] dst, int dw, int dh, Rectangle clip, int x0, int y0, int s, double level)
        {
            int vis = Math.Max(0, Math.Min(3, (int)Math.Ceiling(level - 0.001)));
            bool thin = level < 1;
            for (int r = 3 - vis; r < 3; r++)
                for (int c = 0; c < 24; c++)
                {
                    Color col;
                    char ch = Mound.Rows[r][c];
                    if (ch == '.' || (thin && (c & 1) == 1) || !Mound.Col(ch, out col)) continue;
                    BufCell(dst, dw, dh, clip, x0 + c * s, y0 + r * s, s, col.ToArgb());
                }
        }

        // ---------------- 小图形 / 气泡 / 牌子
        public static void Glyph(Graphics g, string[] rows, Brush b, int x, int y, int u)
        {
            for (int r = 0; r < rows.Length; r++)
                for (int c = 0; c < rows[r].Length; c++)
                    if (rows[r][c] == 'X') g.FillRectangle(b, x + c * u, y + r * u, u, u);
        }

        public static void DrawArt(Graphics g, string[] rows, Art pal, int x, int y, int u)
        {
            for (int r = 0; r < rows.Length; r++)
                for (int c = 0; c < rows[r].Length; c++)
                {
                    Color col;
                    if (rows[r][c] == '.' || !pal.Col(rows[r][c], out col)) continue;
                    g.FillRectangle(BrushOf(col), x + c * u, y + r * u, u, u);
                }
        }

        // 被咬掉几口的零食（从右边往左啃）
        public static string[] SnackRows(Art s, int bite)
        {
            if (bite <= 0) return s.Rows;
            string[] rows = new string[s.Rows.Length];
            int w = s.W;
            for (int r = 0; r < rows.Length; r++)
            {
                char[] ch = s.Rows[r].ToCharArray();
                for (int c = 0; c < ch.Length; c++) if (c >= w - bite * 2 + (r & 1)) ch[c] = '.';
                rows[r] = new string(ch);
            }
            return rows;
        }

        public static void TomatoGlyph(Graphics g, int x, int y, int u)
        {
            using (SolidBrush red = new SolidBrush(TomatoC))
            using (SolidBrush leaf = new SolidBrush(LeafC))
                for (int r = 0; r < Tomato.Length; r++)
                    for (int c = 0; c < Tomato[r].Length; c++)
                    {
                        char ch = Tomato[r][c];
                        if (ch == 'R') g.FillRectangle(red, x + c * u, y + r * u, u, u);
                        else if (ch == 'G') g.FillRectangle(leaf, x + c * u, y + r * u, u, u);
                    }
        }

        // 像素风圆角框（缺角）
        public static void Pill(Graphics g, int bx, int by, int bw, int bh, int p)
        {
            SolidBrush border = BrushOf(BubbleBorder), fill = BrushOf(BubbleFill);
            g.FillRectangle(fill, bx + p, by + p, bw - 2 * p, bh - 2 * p);
            g.FillRectangle(border, bx + p, by, bw - 2 * p, p);
            g.FillRectangle(border, bx + p, by + bh - p, bw - 2 * p, p);
            g.FillRectangle(border, bx, by + p, p, bh - 2 * p);
            g.FillRectangle(border, bx + bw - p, by + p, p, bh - 2 * p);
        }

        // spinFrame < 0：给转圈留位置但不画（由调用方每帧单独画），spinAt 返回转圈的位置
        public static Rectangle DrawBubble(Graphics g, string text, bool spin, int spinFrame, Font font, int p,
            int anchorX, int bottomY, int minX, int maxX, int maxTextW, int minY, out Point spinAt)
        {
            TextRenderingHint old = g.TextRenderingHint;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            int spinW = spin ? 9 * p : 0;
            int avail = Math.Max(10 * p, maxTextW - spinW);
            SizeF ts = g.MeasureString(text, font, avail, Fmt);
            int tw = (int)Math.Ceiling(ts.Width) + 1, th = (int)Math.Ceiling(ts.Height);
            int padX = 4 * p, padY = 3 * p;
            int ch = Math.Max(th, spin ? 7 * p : 0);
            int bw = 2 * p + 2 * padX + spinW + tw;
            int bh = 2 * p + 2 * padY + ch;
            int bx = anchorX - bw / 2;
            if (bx + bw > maxX) bx = maxX - bw;
            if (bx < minX) bx = minX;
            int by = bottomY - 2 * p - bh;
            if (by < minY) by = minY;

            Pill(g, bx, by, bw, bh, p);
            {
                SolidBrush border = BrushOf(BubbleBorder), fill = BrushOf(BubbleFill);
                int tx = Math.Max(bx + 4 * p, Math.Min(anchorX, bx + bw - 4 * p));
                int yb = by + bh - p;
                g.FillRectangle(fill, tx - 2 * p, yb, 4 * p, p);
                g.FillRectangle(border, tx - 2 * p, yb + p, p, p);
                g.FillRectangle(fill, tx - p, yb + p, 2 * p, p);
                g.FillRectangle(border, tx + p, yb + p, p, p);
                g.FillRectangle(border, tx - p, yb + 2 * p, 2 * p, p);
            }
            int cx0 = bx + p + padX, cy0 = by + p + padY;
            spinAt = new Point(cx0, cy0 + (ch - 7 * p) / 2);
            if (spin)
            {
                if (spinFrame >= 0) Glyph(g, Spin[spinFrame], BrushOf(BodyC), spinAt.X, spinAt.Y, p);
                cx0 += spinW;
            }
            g.DrawString(text, font, BrushOf(spin ? SpinText : TextC), new RectangleF(cx0, cy0 + (ch - th) / 2, tw + 2, th + 2), Fmt);
            g.TextRenderingHint = old;
            return new Rectangle(bx, by, bw, bh + 2 * p);
        }

        // 快捷按钮的小提示（画在按钮条上面，不挡住脸；上面没地方了才画到下面）
        public static Size TipSize(Graphics g, string text, Font font, int p)
        {
            SizeF ts = g.MeasureString(text, font, 1000, Fmt);
            int tw = (int)Math.Ceiling(ts.Width) + 1, th = (int)Math.Ceiling(ts.Height);
            return new Size(2 * p + 6 * p + tw, 2 * p + 2 * p + th);
        }

        public static Rectangle DrawBarTip(Graphics g, string text, Font font, int p, Rectangle bar, Rectangle btn, int minX, int maxX, int minY)
        {
            int y = bar.Top - 2 * p - TipSize(g, text, font, p).Height;
            if (y < minY) y = bar.Bottom + 2 * p;
            return DrawTip(g, text, font, p, btn.X + btn.Width / 2, y, minX, maxX);
        }

        public static Rectangle DrawTip(Graphics g, string text, Font font, int p, int centerX, int topY, int minX, int maxX)
        {
            TextRenderingHint old = g.TextRenderingHint;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            SizeF ts = g.MeasureString(text, font, 1000, Fmt);
            int tw = (int)Math.Ceiling(ts.Width) + 1, th = (int)Math.Ceiling(ts.Height);
            int bw = 2 * p + 6 * p + tw, bh = 2 * p + 2 * p + th;
            int bx = centerX - bw / 2;
            if (bx + bw > maxX) bx = maxX - bw;
            if (bx < minX) bx = minX;
            Pill(g, bx, topY, bw, bh, p);
            g.DrawString(text, font, BrushOf(TextC), new RectangleF(bx + 4 * p, topY + 2 * p, tw + 2, th + 2), Fmt);
            g.TextRenderingHint = old;
            return new Rectangle(bx, topY, bw, bh);
        }

        // 番茄钟小牌子
        public static Rectangle DrawBadge(Graphics g, string text, Font font, int p, int x, int y, bool alignRight)
        {
            TextRenderingHint old = g.TextRenderingHint;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            SizeF ts = g.MeasureString(text, font, 1000, Fmt);
            int tw = (int)Math.Ceiling(ts.Width) + 1, th = (int)Math.Ceiling(ts.Height);
            int ch = Math.Max(th, 7 * p);
            int bw = 2 * p + 3 * p + 7 * p + 2 * p + tw + 3 * p;
            int bh = 2 * p + 2 * p + ch;
            int bx = alignRight ? x - bw : x;
            Pill(g, bx, y, bw, bh, p);
            TomatoGlyph(g, bx + 4 * p, y + 3 * p + (ch - 7 * p) / 2, p);
            g.DrawString(text, font, BrushOf(TextC), new RectangleF(bx + 13 * p, y + 3 * p + (ch - th) / 2, tw + 2, th + 2), Fmt);
            g.TextRenderingHint = old;
            return new Rectangle(bx, y, bw, bh);
        }

        // ---------------- 头顶的小表情泡泡
        // kind：heart / note / q / bang / sweat / star / zz / anger / idea / dots（动画省略号）/ dream（云朵泡泡，里面是梦到的东西）
        static void EmoteGlyph(string kind, double age, out string[] rows, out Color col)
        {
            col = TextC;
            switch (kind)
            {
                case "heart": rows = Heart; col = HeartC; break;
                case "note": rows = Note; col = NoteC; break;
                case "bang": rows = Bang; col = BangC; break;
                case "sweat": rows = Drop; col = DropC; break;
                case "star": rows = Star; col = StarC; break;
                case "zz": rows = Zed; col = ZC; break;
                case "anger": rows = Anger; col = BangC; break;
                case "idea": rows = Bulb; col = StarC; break;
                case "dots":
                    {
                        int f = ((int)Math.Floor(age / 0.35)) % 3;
                        rows = f == 0 ? DotsA : (f == 1 ? DotsB : DotsC);
                    }
                    break;
                default: rows = QMark; break;
            }
        }

        public static void EmoteGeom(double age, int ax, int headY, int S, int side, out int left, out int top, out int u)
        {
            u = Half(S);
            int bw = 11, bh = 10;
            int rise = (int)Math.Round(Math.Max(0, 1 - age / 0.15) * 2) * u;
            left = (int)Math.Round(ax + side * 7.0 * S) - (int)Math.Round(bw * u / 2.0);
            top = Math.Max(0, (int)Math.Round(headY - 0.5 * S) - (bh + 2) * u + rise);
        }

        public static Rectangle EmoteRect(string kind, double age, int ax, int headY, int S, int side)
        {
            int left, top, u;
            EmoteGeom(age, ax, headY, S, side, out left, out top, out u);
            return new Rectangle(left - u, top - u, 13 * u, (kind == "dream" ? 17 : 14) * u);
        }

        public static void DrawEmote(Graphics g, string kind, Art icon, double age, double left0, int ax, int headY, int S, int side)
        {
            if (left0 < 0.25 && (((int)Math.Floor(left0 * 24)) & 1) == 0) return;   // 快消失时闪一下
            int left, top, u;
            EmoteGeom(age, ax, headY, S, side, out left, out top, out u);
            const int gw = 7, gh = 6, bw = 11, bh = 10, m = 5;
            bool thought = kind == "dream" && icon != null;   // 梦：云朵泡泡，下面是一串小圆点
            SolidBrush fill = BrushOf(BubbleFill), border = BrushOf(BubbleBorder);
            g.FillRectangle(fill, left + u, top + u, (bw - 2) * u, (bh - 2) * u);
            if (!thought) { g.FillRectangle(fill, left + (m - 1) * u, top + (bh - 1) * u, 3 * u, u); g.FillRectangle(fill, left + m * u, top + bh * u, u, u); }
            g.FillRectangle(border, left + u, top, (bw - 2) * u, u);
            g.FillRectangle(border, left, top + u, u, (bh - 2) * u);
            g.FillRectangle(border, left + (bw - 1) * u, top + u, u, (bh - 2) * u);
            if (thought)
            {
                g.FillRectangle(border, left + u, top + (bh - 1) * u, (bw - 2) * u, u);
                // 两个小圆圈，从头顶飘上去
                int d1 = left + (m - side * 2) * u, d2 = left + (m - side * 4) * u, hu = (u + 1) / 2;
                g.FillRectangle(border, d1 - u, top + bh * u + hu, 3 * u, 3 * u);
                g.FillRectangle(fill, d1, top + (bh + 1) * u + hu, u, u);
                g.FillRectangle(border, d2, top + (bh + 3) * u + hu, 2 * u, 2 * u);
            }
            else
            {
                g.FillRectangle(border, left + u, top + (bh - 1) * u, (m - 2) * u, u);
                g.FillRectangle(border, left + (m + 2) * u, top + (bh - 1) * u, (bw - m - 3) * u, u);
                g.FillRectangle(border, left + (m - 1) * u, top + bh * u, u, u);
                g.FillRectangle(border, left + (m + 1) * u, top + bh * u, u, u);
                g.FillRectangle(border, left + m * u, top + (bh + 1) * u, u, u);
            }
            if (thought)
            {
                string[] ir = icon.Frames != null ? icon.Frames[((int)Math.Floor(age * 3)) & 1] : icon.Rows;
                int gx = left + (2 + (gw - ir[0].Length) / 2) * u, gy = top + (2 + (gh - ir.Length) / 2) * u;
                DrawArt(g, ir, icon, gx, gy, u);
                return;
            }
            string[] rows; Color col;
            EmoteGlyph(kind, age, out rows, out col);
            int rx = left + (2 + (gw - rows[0].Length) / 2) * u, ry = top + (2 + (gh - rows.Length) / 2) * u;
            Glyph(g, rows, BrushOf(col), rx, ry, u);
        }

        // 梦泡泡的可点区域
        public static Rectangle DreamHitRect(double age, int ax, int headY, int S, int side)
        {
            int left, top, u;
            EmoteGeom(age, ax, headY, S, side, out left, out top, out u);
            return new Rectangle(left, top, 11 * u, 15 * u);
        }

        // ---------------- 鼻涕泡：像素圆圈，两眼正中间、眼睛下面一点，往下鼓
        // ucx、vcy、r 都是小人画布里的格子坐标，跟着身子一起变形
        public static Rectangle NoseRect(Affine m, double ucx, double vcy, double r, int S)
        {
            double X = m.E + m.A * ucx + m.C * vcy, Y = m.F + m.B * ucx + m.D * vcy;
            int uu = Math.Max(1, (S + 1) / 2);
            double R = r * S / uu;
            int Ri = (int)Math.Ceiling(R + 1), ox = (int)Math.Round(X), oy = (int)Math.Round(Y);
            return new Rectangle(ox - Ri * uu - 1, oy - Ri * uu - 1, (2 * Ri + 1) * uu + 2, (2 * Ri + 1) * uu + 2);
        }

        public static void DrawNose(Graphics g, Affine m, double ucx, double vcy, double r, int S)
        {
            double X = m.E + m.A * ucx + m.C * vcy, Y = m.F + m.B * ucx + m.D * vcy;
            int uu = Math.Max(1, (S + 1) / 2);
            double R = r * S / uu;
            int Ri = (int)Math.Ceiling(R + 1), ox = (int)Math.Round(X), oy = (int)Math.Round(Y);
            SolidBrush ring = BrushOf(NoseRing), fill = BrushOf(NoseFill);
            for (int j = -Ri; j <= Ri; j++)
                for (int i = -Ri; i <= Ri; i++)
                {
                    double d = Math.Sqrt(i * i + j * j);
                    if (d > R + 0.5) continue;
                    g.FillRectangle(d > R - 0.7 ? ring : fill, ox + i * uu, oy + j * uu, uu, uu);
                }
            if (R >= 2) g.FillRectangle(BrushOf(Color.White), ox + (int)Math.Round(-R * 0.45) * uu, oy + (int)Math.Round(-R * 0.45) * uu, uu, uu);
        }

        // ---------------- 右键点它时头顶冒出来的一排快捷按钮
        public static int BarUnit(int S) { return Math.Max(2, (int)Math.Round(S * 0.6)); }

        // 按钮条和它的小提示不跟着小人一起缩：小人调得再小，也至少按「中号」（7 格）画，免得看不清、点不准
        public static int BarScale(int S, float dpi) { return Math.Max(S, (int)Math.Round(7 * dpi)); }

        public static Size BarSize(int S, int n)
        {
            int bu = BarUnit(S), pu = Math.Max(2, S / 2), pad = pu, gap = Math.Max(2, pu / 2 + 1), bs = 9 * bu + 1;
            return new Size(2 * pu + 2 * pad + n * bs + (n - 1) * gap, 2 * pu + 2 * pad + bs);
        }

        public static void BarLayout(int S, int n, int centerX, int bottomY, int minX, int maxX, int minY, out Rectangle bar, out Rectangle[] btns)
        {
            int bu = BarUnit(S), pu = Math.Max(2, S / 2), pad = pu, gap = Math.Max(2, pu / 2 + 1), bs = 9 * bu + 1;
            Size sz = BarSize(S, n);
            int w = sz.Width, h = sz.Height;
            int x = centerX - w / 2;
            if (x + w > maxX - 2) x = maxX - 2 - w;
            if (x < minX + 2) x = minX + 2;
            int y = bottomY - h;
            if (y < minY) y = minY;
            bar = new Rectangle(x, y, w, h);
            btns = new Rectangle[n];
            for (int i = 0; i < n; i++) btns[i] = new Rectangle(x + pu + pad + i * (bs + gap), y + pu + pad, bs, bs);
        }

        // hot：鼠标停在哪个按钮上；pressed：按下的那个；toggled：处在「开」状态的那个（-1 = 没有）
        public static void DrawBar(Graphics g, Rectangle bar, Rectangle[] btns, Art[] icons, int hot, int pressed, int toggled, int S)
        {
            int pu = Math.Max(2, S / 2), bu = BarUnit(S);
            Pill(g, bar.X, bar.Y, bar.Width, bar.Height, pu);
            for (int i = 0; i < btns.Length && i < icons.Length; i++)
            {
                Rectangle b = btns[i];
                if (i == pressed || i == toggled) g.FillRectangle(BrushOf(BtnDown), b);
                else if (i == hot) g.FillRectangle(BrushOf(BtnHover), b);
                if (i == toggled) { g.FillRectangle(BrushOf(BodyC), b.X + 2, b.Bottom - 3, b.Width - 4, 2); }
                Art a = icons[i];
                int iw = a.W * bu, ih = a.H * bu;
                DrawArt(g, a.Rows, a, b.X + (b.Width - iw) / 2, b.Y + (b.Height - ih) / 2, bu);
            }
        }

        // ---------------- 图标
        static void Pixels(Graphics g, string[] rows, int ox, int oy, int u)
        {
            using (SolidBrush body = new SolidBrush(BodyC))
            using (SolidBrush eye = new SolidBrush(EyeC))
                for (int r = 0; r < rows.Length; r++)
                    for (int c = 0; c < rows[r].Length; c++)
                    {
                        char ch = rows[r][c];
                        if (ch == 'X') g.FillRectangle(body, ox + c * u, oy + r * u, u, u);
                        else if (ch == 'E') g.FillRectangle(eye, ox + c * u, oy + r * u, u, u);
                    }
        }

        public static Bitmap IconBitmap(int size)
        {
            Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.None;
                int fs = size / 24, cs = size / 16;
                if (fs >= 1 && fs * 24 >= cs * 16)
                {
                    int ox = (size - 24 * fs) / 2;
                    int oy = (size - 13 * fs) / 2 - 4 * fs;
                    Draw(g, new Pose(), ox, oy, fs);
                }
                else
                {
                    int u = Math.Max(1, cs);
                    Pixels(g, Compact, (size - 16 * u) / 2, (size - 10 * u) / 2, u);
                }
            }
            return bmp;
        }

        static byte[] DibBytes(Bitmap b)
        {
            int W = b.Width, H = b.Height;
            int maskStride = ((W + 31) / 32) * 4;
            MemoryStream ms = new MemoryStream();
            BinaryWriter w = new BinaryWriter(ms);
            w.Write(40); w.Write(W); w.Write(H * 2); w.Write((short)1); w.Write((short)32);
            w.Write(0); w.Write(W * H * 4 + maskStride * H); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
            for (int y = H - 1; y >= 0; y--)
                for (int x = 0; x < W; x++)
                {
                    Color c = b.GetPixel(x, y);
                    w.Write(c.B); w.Write(c.G); w.Write(c.R); w.Write(c.A);
                }
            for (int y = H - 1; y >= 0; y--)
            {
                byte[] row = new byte[maskStride];
                for (int x = 0; x < W; x++)
                    if (b.GetPixel(x, y).A == 0) row[x / 8] |= (byte)(0x80 >> (x % 8));
                w.Write(row);
            }
            w.Flush();
            return ms.ToArray();
        }

        public static byte[] BuildIco(int[] sizes)
        {
            List<byte[]> imgs = new List<byte[]>();
            foreach (int sz in sizes) using (Bitmap b = IconBitmap(sz)) imgs.Add(DibBytes(b));
            MemoryStream ms = new MemoryStream();
            BinaryWriter w = new BinaryWriter(ms);
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                int sz = sizes[i];
                w.Write((byte)(sz >= 256 ? 0 : sz)); w.Write((byte)(sz >= 256 ? 0 : sz));
                w.Write((byte)0); w.Write((byte)0);
                w.Write((short)1); w.Write((short)32);
                w.Write(imgs[i].Length); w.Write(offset);
                offset += imgs[i].Length;
            }
            foreach (byte[] d in imgs) w.Write(d);
            w.Flush();
            return ms.ToArray();
        }

        public static Icon MakeIcon(int size)
        {
            byte[] ico = BuildIco(new int[] { 16, 20, 24, 32, 40, 48 });
            return new Icon(new MemoryStream(ico), size, size);
        }
    }

    // ------------------------------------------------------------------ 预览图
    static class Preview
    {
        static void Add(List<KeyValuePair<string, Pose>> list, string name, Action<Pose> f)
        {
            Pose p = new Pose(); f(p); list.Add(new KeyValuePair<string, Pose>(name, p));
        }

        static void Jelly(Graphics g, Pose p, int x, int y, int s, double sq, double lean, double anchorRow)
        {
            int W = 34 * s, H = 26 * s;
            int[] buf = new int[W * H];
            Sprite.Affine m = Sprite.MakeXf(s, 17 * s, 5 * s + anchorRow * s, anchorRow * s, sq, lean, 1, 0, 0, 0);
            Sprite.RasterXf(Sprite.CellPixels(p), 24, 18, buf, W, H, m, new Rectangle(0, 0, W, H));
            BlitBuf(g, buf, W, H, x, y);
        }

        static void BlitBuf(Graphics g, int[] buf, int W, int H, int x, int y)
        {
            using (Bitmap b = new Bitmap(W, H, PixelFormat.Format32bppArgb))
            {
                BitmapData bd = b.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                Marshal.Copy(buf, 0, bd.Scan0, buf.Length);
                b.UnlockBits(bd);
                g.DrawImage(b, new Rectangle(x, y, W, H), 0, 0, W, H, GraphicsUnit.Pixel);
            }
        }

        public static void Render(string path)
        {
            List<KeyValuePair<string, Pose>> list = new List<KeyValuePair<string, Pose>>();
            Add(list, "idle", p => { });
            Add(list, "blink", p => p.Eyes = EyePose.Blink);
            Add(list, "look", p => { p.LookX = -1; p.LookY = -1; });
            Add(list, "walk A", p => p.Legs = LegPose.WalkA);
            Add(list, "walk B", p => p.Legs = LegPose.WalkB);
            Add(list, "think", p => { p.ArmR = ArmPose.Raised; p.LookX = 1; p.LookY = -1; });
            Add(list, "typing", p => { p.ArmL = ArmPose.Raised; p.LookY = 1; });
            Add(list, "happy", p => { p.Eyes = EyePose.Happy; p.Blush = true; p.ArmL = p.ArmR = ArmPose.Raised; });
            Add(list, "wave", p => { p.Eyes = EyePose.Happy; p.Blush = true; p.ArmR = ArmPose.Up; });
            Add(list, "drag", p => { p.Eyes = EyePose.Squint; p.ArmL = p.ArmR = ArmPose.Up; p.Legs = LegPose.DangleA; });
            Add(list, "fall", p => { p.Eyes = EyePose.Wide; p.ArmL = p.ArmR = ArmPose.Up; p.Legs = LegPose.DangleB; });
            Add(list, "sleep", p => { p.Eyes = EyePose.Closed; p.Legs = LegPose.Tucked; p.BodyDy = 3; });
            Add(list, "dizzy", p => { p.Eyes = EyePose.Dizzy; p.ArmL = p.ArmR = ArmPose.Down; });
            Add(list, "yawn", p => { p.Eyes = EyePose.Closed; p.ArmL = p.ArmR = ArmPose.Up; });
            Add(list, "alert", p => { p.Eyes = EyePose.Wide; p.ArmL = ArmPose.Up; p.ArmR = ArmPose.Raised; });
            Add(list, "eat", p => { p.Eyes = EyePose.Happy; p.Blush = true; p.ArmL = ArmPose.Raised; });

            int s = 5, cols = 8, cw = 26 * s, chh = 21 * s;
            int rows = (list.Count + cols - 1) / cols;
            int jellyH = 30 * s, extra = 46 * s;
            using (Bitmap bmp = new Bitmap(cols * cw, rows * chh + jellyH + extra))
            using (Graphics g = Graphics.FromImage(bmp))
            using (Font label = new Font("Segoe UI", 12f, GraphicsUnit.Pixel))
            using (Font bf = new Font("Microsoft YaHei UI", s * 2.7f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Font badgeF = new Font("Microsoft YaHei UI", s * 2.3f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (SolidBrush lb = new SolidBrush(Color.FromArgb(90, 90, 90)))
            {
                g.Clear(Color.FromArgb(240, 238, 230));
                g.SmoothingMode = SmoothingMode.None;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                for (int i = 0; i < list.Count; i++)
                {
                    int x = (i % cols) * cw + s, y = (i / cols) * chh + s;
                    Sprite.Draw(g, list[i].Value, x, y, s);
                    g.DrawString(list[i].Key, label, lb, x + 2, y + 18 * s);
                }

                // 果冻形变示例
                int jy = rows * chh;
                Pose idle = new Pose();
                Pose drag = new Pose(); drag.Eyes = EyePose.Squint; drag.ArmL = drag.ArmR = ArmPose.Up; drag.Legs = LegPose.DangleA;
                Pose crouch = new Pose(); crouch.Eyes = EyePose.Happy; crouch.ArmL = crouch.ArmR = ArmPose.Down;
                Pose jump = new Pose(); jump.Eyes = EyePose.Happy; jump.Blush = true; jump.ArmL = jump.ArmR = ArmPose.Up;
                string[] jn = { "land -0.35", "crouch", "breathe", "jump +0.3", "lean", "drag swing" };
                Jelly(g, idle, 0, jy, s, -0.35, 0, 17);
                Jelly(g, crouch, 34 * s, jy, s, -0.25, 0, 17);
                Jelly(g, idle, 68 * s, jy, s, 0.03, 0, 17);
                Jelly(g, jump, 102 * s, jy, s, 0.3, 0, 17);
                Jelly(g, idle, 136 * s, jy, s, 0, 0.25, 17);
                Jelly(g, drag, 170 * s, jy, s, 0.12, 0.4, 7);
                for (int i = 0; i < jn.Length; i++) g.DrawString(jn[i], label, lb, i * 34 * s + 4, jy + 26 * s);

                int by0 = jy + jellyH;
                using (SolidBrush dark = new SolidBrush(Color.FromArgb(32, 33, 36)))
                    g.FillRectangle(dark, 0, by0 + 23 * s, bmp.Width, extra - 23 * s);
                int p = Math.Max(2, s / 2);
                Point sp;
                Sprite.DrawBubble(g, "ClawdPet 搞定！（用时 2m 13s）", false, 0, bf, p, 30 * s, by0 + 20 * s, 0, bmp.Width, 70 * s, 0, out sp);
                Sprite.DrawBubble(g, "Clauding… 1m 23s ×2", true, 4, bf, p, 95 * s, by0 + 20 * s, 0, bmp.Width, 60 * s, 0, out sp);
                Sprite.DrawBubble(g, "Claude 想用 Bash，等你批准~", false, 0, bf, p, 160 * s, by0 + 20 * s, 0, bmp.Width, 60 * s, 0, out sp);
                Sprite.DrawBadge(g, "24:59", badgeF, p, 186 * s, by0 + 3 * s, false);

                int gx = 4 * s, gy = by0 + 27 * s, u = Math.Max(2, (s + 1) / 2);
                string[][] gl = { Sprite.Heart, Sprite.Zed, Sprite.Note, Sprite.Star, Sprite.Bang, Sprite.Drop, Sprite.Puff, Sprite.Crumb };
                Color[] gc = { Sprite.HeartC, Sprite.ZC, Sprite.NoteC, Sprite.StarC, Sprite.BangC, Sprite.DropC, Sprite.PuffC, Sprite.CrumbC };
                for (int i = 0; i < gl.Length; i++)
                    using (SolidBrush b = new SolidBrush(gc[i])) Sprite.Glyph(g, gl[i], b, gx + i * 10 * u, gy, u);
                using (SolidBrush b = new SolidBrush(Sprite.BodyC))
                    for (int i = 0; i < Sprite.Spin.Length; i++) Sprite.Glyph(g, Sprite.Spin[i], b, gx + (84 + i * 10) * u, gy, u);
                int ix = gx + 140 * u;
                foreach (int sz in new int[] { 16, 24, 32, 48 })
                {
                    using (Bitmap ib = Sprite.IconBitmap(sz)) g.DrawImage(ib, ix, gy);
                    ix += sz + 12;
                }
                bmp.Save(path, ImageFormat.Png);
            }
        }

        // ================================================================ v3 新动作的预览（--preview2）
        class Scene
        {
            public string Name; public Pose P = new Pose();
            public double Sq, Lean, Flip = 1, Rot, Swing, PivX, AnchorRow = 17, Hop;
            public string Emote; public Art Icon; public double Age = 0.6;
            public double Nose; public Art Snack; public int Bite; public bool Bar; public int Hot = -1, Toggled = -1; public string Tip;
            public bool Ledge, Grab, Laptop, Wall, Air, Ball; public int SitOff, BallDx = 21, BallDy = 11;
            public int WallSide;   // 爬墙：-1 左墙 / +1 右墙（画一道墙在脚那边）
            public double Sink, Mound;   // 挖沙：往下沉了几格（地面以下不画）、沙堆有几行高
            public string Prop; public int PropFrame;   // 待机小道具
            public int Pole, PoleBot, PoleX = 11;   // 木杆：多长（格）、杆子底比脚低几格（爬上去了）、杆子左边在第几格
            public double Slant, SlantTop = 12;      // 斜靠在窗角的木杆：角度（从水平量，0 = 没有）；从小人到窗角还有几格（站着推时 = 杆子多长）
            public List<Pose> Stack = new List<Pose>();   // 叠高高：头上一层层站着的伙伴
            public Pose Friend; public int FriendDx, ShiftX;   // 旁边的伙伴（击掌）：离多远；整幅往左挪几格
            public string Say;   // 头上的说话气泡
            public bool Pitch; public int ScoreA = -1, ScoreB;   // 小足球场：草地 + 右边一个球门；比分牌
            public int Office; public bool FriendLaptop;          // 一排小工位（几个隔间，小人坐第一个）；旁边的伙伴也对着小电脑
            public int Sz;                                        // 这一格单独用的格子大小（0 = 跟整张图一样）
            public List<Particle> Parts = new List<Particle>();
        }

        static Scene Sc(string name) { Scene c = new Scene(); c.Name = name; return c; }
        static readonly string[] BubSPrev = { ".X.", "X.X", ".X." }, BubLPrev = { ".XX.", "X..X", "X..X", ".XX." };
        static readonly Color BubCPrev = Color.FromArgb(0x8E, 0xC9, 0xEE);

        static void Drag(Scene c, double swing, int f, int kick)
        {
            ArmPose[] flail = { ArmPose.UpOut, ArmPose.Out, ArmPose.DownOut, ArmPose.Out };
            c.P.Legs = LegPose.Hang; c.P.LegDx = kick; c.P.LegDx2 = -kick; c.P.LegPh = f & 1;
            c.P.ArmL = flail[f & 3]; c.P.ArmR = flail[(f + 2) & 3]; c.P.Eyes = EyePose.Wide;
            c.Swing = swing; c.AnchorRow = 5; c.Sq = 0.12; c.Grab = true; c.Air = true;
        }

        static void RenderScene(Graphics g, Scene c, int x0, int y0, int cw, int chh, int s, Font tipF, Font label)
        {
            using (SolidBrush cellBg = new SolidBrush(Color.FromArgb(250, 248, 242)))
                g.FillRectangle(cellBg, x0 + 2, y0 + 2, cw - 4, chh - 4);
            int offX = (cw - 24 * s) / 2 + c.ShiftX * s, offY = chh - 24 * s;       // 小人画布在格子里的位置（局部坐标）
            int foot = offY + 17 * s;
            // 地面 / 窗口标题栏 / 屏幕边
            using (SolidBrush ground = new SolidBrush(Color.FromArgb(214, 208, 196)))
            {
                if (c.Ledge)
                {
                    int top = foot - c.SitOff * s;
                    using (SolidBrush win = new SolidBrush(Color.FromArgb(226, 229, 236)))
                    using (SolidBrush tb = new SolidBrush(Color.FromArgb(196, 202, 214)))
                    {
                        g.FillRectangle(win, x0 + 6, y0 + top, cw - 12, chh - top - 4);
                        g.FillRectangle(tb, x0 + 6, y0 + top, cw - 12, 5 * s);
                    }
                }
                else if (!c.Air) g.FillRectangle(ground, x0 + 6, y0 + foot, cw - 12, 2);
                if (c.Wall) g.FillRectangle(ground, x0 + cw - 8, y0 + 4, 3, chh - 8);
                if (c.WallSide < 0) g.FillRectangle(ground, x0 + offX - 3, y0 + 4, 3, chh - 8);
                if (c.WallSide > 0) g.FillRectangle(ground, x0 + offX + 24 * s, y0 + 4, 3, chh - 8);
            }
            if (c.Office > 0)
            {
                string[] orows = PetForm.OfficeRows(c.Office);
                Sprite.DrawArt(g, orows, new Art("o", orows, PetForm.OfficePal), x0 + offX - 4 * s, y0 + foot - PetForm.CUB_H * s, s);
            }
            if (c.ScoreA >= 0)
            {
                string[] br = PetForm.BoardRows(c.ScoreA, c.ScoreB);
                int bu = Math.Max(2, s / 2);
                Sprite.DrawArt(g, br, new Art("b", br, PetForm.BoardPal), x0 + cw / 2 - 15 * bu / 2, y0 + 4 * s, bu);
            }
            int W = cw, H = chh;
            int[] buf = new int[W * H];
            double anchorY = c.AnchorRow * s;
            double Ax = offX + 12 * s, Ay = offY + anchorY - c.Hop + c.Sink * s;
            Sprite.Affine m = Sprite.MakeXf(s, Ax, Ay, anchorY, c.Sq, c.Lean, c.Flip, c.Rot, c.Swing, c.PivX);
            Sprite.RasterXf(Sprite.CellPixels(c.P), 24, 18, buf, W, H, m, c.Sink > 0 ? new Rectangle(0, 0, W, foot) : new Rectangle(0, 0, W, H));
            {
                // 叠高高：每一层的脚站在下面那只的头顶上
                int ry = offY; Pose below = c.P;
                foreach (Pose rp in c.Stack)
                {
                    ry -= (13 - below.BodyDy) * s;
                    Sprite.Affine mr = Sprite.MakeXf(s, offX + 12 * s, ry + 17 * s, 17 * s, 0, 0, 1, 0, 0, 0);
                    Sprite.RasterXf(Sprite.CellPixels(rp), 24, 18, buf, W, H, mr, new Rectangle(0, 0, W, H));
                    below = rp;
                }
                if (c.Friend != null)
                {
                    Sprite.Affine mf = Sprite.MakeXf(s, offX + (12 + c.FriendDx) * s, offY + 17 * s, 17 * s, 0, 0, 1, 0, 0, 0);
                    Sprite.RasterXf(Sprite.CellPixels(c.Friend), 24, 18, buf, W, H, mf, new Rectangle(0, 0, W, H));
                }
            }
            if (c.Laptop) Sprite.DrawLaptop(buf, W, H, new Rectangle(0, 0, W, H), offX, offY, s, c.P.Facing, 0, 0.35);
            if (c.FriendLaptop && c.Friend != null) Sprite.DrawLaptop(buf, W, H, new Rectangle(0, 0, W, H), offX + c.FriendDx * s, offY, s, c.Friend.Facing, 0, 0.35);
            if (c.Mound > 0) Sprite.DrawMound(buf, W, H, new Rectangle(0, 0, W, H), offX, foot - 3 * s, s, c.Mound);
            if (c.Pole > 0)
            {
                int pb = foot + c.PoleBot * s;
                if (c.PoleBot > 0) using (SolidBrush gr = new SolidBrush(Color.FromArgb(214, 208, 196))) g.FillRectangle(gr, x0 + 6, y0 + pb, cw - 12, 2);
                Sprite.DrawArt(g, Sprite.PoleRows(c.Pole), Sprite.PoleArt, x0 + offX + c.PoleX * s, y0 + pb - c.Pole * s, s);
            }
            if (c.Slant > 0)
            {
                // 斜靠在窗口左上角的木杆（窗口在右边）：站着时杆子底在身边；爬着时杆子从脚底下经过
                double a = c.Slant, dx = Math.Cos(a), dy = -Math.Sin(a);
                double floorY, bx, cxp, cyp;   // 地面、杆子底中线、杆子中线上靠着窗角的那一点
                if (c.Rot == 0)
                {
                    floorY = foot; bx = Ax + 12 * s;
                    cxp = bx + dx * c.SlantTop * s; cyp = floorY + dy * c.SlantTop * s;
                }
                else
                {
                    floorY = chh - 4 * s;
                    double hh = (8 + 4 * Math.Sin(a)) * s;
                    double qx = Ax + Math.Sin(a) * (hh + s), qy = Ay - hh + Math.Cos(a) * (hh + s);   // 脚底下一格就是杆子中线
                    double k0 = (floorY - qy) / Math.Sin(a);
                    bx = qx - dx * k0;
                    cxp = qx + dx * c.SlantTop * s; cyp = qy + dy * c.SlantTop * s;
                }
                int wx = (int)Math.Round(cxp + Math.Sin(a) * s), wy = (int)Math.Round(cyp + Math.Cos(a) * s);
                using (SolidBrush win = new SolidBrush(Color.FromArgb(226, 229, 236)))
                using (SolidBrush tb = new SolidBrush(Color.FromArgb(196, 202, 214)))
                {
                    g.FillRectangle(win, x0 + wx, y0 + wy, cw - 6 - wx, chh - 4 - wy);
                    g.FillRectangle(tb, x0 + wx, y0 + wy, cw - 6 - wx, 5 * s);
                }
                using (SolidBrush ground = new SolidBrush(Color.FromArgb(214, 208, 196))) g.FillRectangle(ground, x0 + 6, y0 + (int)floorY, cw - 12, 2);
                double tx = cxp + dx * 5 * s, ty = cyp + dy * 5 * s;
                int ox;
                string[] pr = Sprite.SlantPoleRows((tx - bx) / s, (floorY - ty) / s, out ox);
                Sprite.DrawArt(g, pr, Sprite.PoleArt, x0 + (int)Math.Round(bx) + ox * s, y0 + (int)Math.Round(floorY) - pr.Length * s, s);
            }
            BlitBuf(g, buf, W, H, x0, y0);
            {
                Art pa; string[] pr; int ppx, ppy;
                if (c.Prop != null && Sprite.Prop(c.Prop, c.PropFrame, c.P.Facing, c.P.BodyDy, out pa, out pr, out ppx, out ppy))
                    Sprite.DrawArt(g, pr, pa, x0 + offX + ppx * s, y0 + offY + ppy * s, s);
            }
            Sprite.Affine mg = Sprite.Shift(m, x0, y0);
            if (c.Nose > 0) Sprite.DrawNose(g, mg, 12 + c.P.Facing * 0.3, 4 + c.P.BodyDy + 5 + c.Nose * 0.95, c.Nose, s);
            if (c.Snack != null)
                Sprite.DrawArt(g, Sprite.SnackRows(c.Snack, c.Bite), c.Snack, x0 + offX + 12 * s + c.P.Facing * s - 5 * s / 2, y0 + offY + (Sprite.Ground - 3) * s - 5 * s, s);
            if (c.Ball) { Art b = Sprite.BallArt(1); Sprite.DrawArt(g, b.Rows, b, x0 + offX + c.BallDx * s, y0 + offY + c.BallDy * s, s); }
            int u = Math.Max(2, (s + 1) / 2);
            foreach (Particle q in c.Parts)
                Sprite.Glyph(g, q.Shape, Sprite.BrushOf(q.Color), x0 + offX + (int)q.X, y0 + offY + (int)q.Y, q.U > 0 ? q.U : u);
            if (c.Grab)
            {
                // 抓住的点（鼠标）
                int gx = x0 + (int)Math.Round(Ax + c.PivX), gy = y0 + (int)Math.Round(Ay);
                using (Pen pen = new Pen(Color.FromArgb(40, 40, 40), 2))
                {
                    g.DrawLine(pen, gx - 6, gy, gx + 6, gy); g.DrawLine(pen, gx, gy - 6, gx, gy + 6);
                    g.DrawLine(pen, gx, gy - 6, gx, y0 + 4);
                }
            }
            int side = c.P.Facing >= 0 ? 1 : -1;
            if (c.Emote != null)
            {
                double sy = 1 + c.Sq;
                int headY = (int)Math.Round(c.Rot != 0 ? Ay - 20 * s : Ay + sy * s * (4 + c.P.BodyDy - c.AnchorRow));
                if (c.Sink > 0) headY = Math.Min(headY, offY + 14 * s);
                Sprite.DrawEmote(g, c.Emote, c.Icon, c.Age, 2, x0 + (int)Ax, y0 + headY, s, side);
            }
            if (c.Say != null)
            {
                Point spinAt;
                Sprite.DrawBubble(g, c.Say, false, -1, tipF, Math.Max(2, s / 2), x0 + (int)Ax, y0 + offY + 3 * s, x0 + 4, x0 + cw - 4, cw - 6 * s, y0 + 2, out spinAt);
            }
            if (c.Bar)
            {
                Rectangle bar; Rectangle[] btns;
                Art[] icons = { Sprite.Snacks[0], Sprite.BallArt(0), Sprite.ZzArt, Sprite.CloudArt, Sprite.PinArt, Sprite.MoreArt };
                int us = Sprite.BarScale(s, 1f), up = Math.Max(2, us / 2);
                Sprite.BarLayout(us, icons.Length, x0 + (int)Ax, y0 + offY - 2 * s, x0, x0 + cw, y0 + 2, out bar, out btns);
                Sprite.DrawBar(g, bar, btns, icons, c.Hot, -1, c.Toggled, us);
                if (c.Tip != null && c.Hot >= 0)
                    using (Font tf = new Font("Microsoft YaHei UI", Math.Max(12f, us * 2.4f), FontStyle.Regular, GraphicsUnit.Pixel))
                        Sprite.DrawBarTip(g, c.Tip, tf, up, bar, btns[c.Hot], x0 + 2, x0 + cw - 2, y0 + 2);
            }
            using (SolidBrush lb = new SolidBrush(Color.FromArgb(70, 68, 62)))
                g.DrawString(c.Name, label, lb, x0 + 8, y0 + chh - 4 * s);
        }

        static Particle Pt(string[] shape, Color col, double x, double y, int u)
        {
            Particle q = new Particle(); q.Shape = shape; q.Color = col; q.X = x; q.Y = y; q.U = u; q.Life = 1; return q;
        }

        public static void Render2(string path)
        {
            int s = 5;
            List<Scene> list = new List<Scene>();
            Scene c;
            // 1) 拎起来：钟摆 + 手脚乱蹬
            c = Sc("拎着晃 swing -0.8"); Drag(c, -0.8, 0, 2); list.Add(c);
            c = Sc("拎着 swing 0"); Drag(c, 0, 1, -1); list.Add(c);
            c = Sc("拎着晃 swing +0.8"); Drag(c, 0.8, 2, -2); c.P.Eyes = EyePose.Squint; list.Add(c);
            c = Sc("抓着侧边 晃 +0.5"); Drag(c, 0.5, 3, 1); c.PivX = 6 * s; c.AnchorRow = 8; c.P.Eyes = EyePose.Happy; c.P.Blush = true; list.Add(c);
            c = Sc("扔到屏幕边 糊墙"); c.P.ArmL = c.P.ArmR = ArmPose.Up; c.P.Legs = LegPose.Spread; c.P.Eyes = EyePose.Squint; c.Sq = 0.35; c.Wall = true; c.Air = true; c.Emote = "bang"; list.Add(c);
            c = Sc("晃晕了 dizzy"); c.P.Eyes = EyePose.Dizzy; c.P.ArmL = c.P.ArmR = ArmPose.Down; c.Lean = 0.12; c.Emote = "anger";
            for (int i = 0; i < 3; i++) { double a = i * 2.1; c.Parts.Add(Pt(Sprite.Star, Sprite.StarC, 11 * s + Math.Cos(a) * 9 * s, 1 * s + Math.Sin(a) * 1.5 * s, 0)); }
            list.Add(c);
            // 2) 待机动作
            c = Sc("坐 sit"); c.P.BodyDy = 2; c.P.ArmL = c.P.ArmR = ArmPose.Down; c.Sq = -0.04; list.Add(c);
            c = Sc("坐在标题栏边上 ledge"); c.P.Legs = LegPose.Hang; c.P.ArmL = c.P.ArmR = ArmPose.Down; c.P.LegDx = 1; c.P.LegDx2 = -1; c.Ledge = true; c.SitOff = 3; list.Add(c);
            c = Sc("趴 lie"); c.P.BodyDy = 3; c.P.Legs = LegPose.Tucked; c.P.ArmL = c.P.ArmR = ArmPose.Out; c.Sq = -0.08; list.Add(c);
            c = Sc("想事情 ponder"); c.P.ArmR = ArmPose.Raised; c.P.LookX = 1; c.P.LookY = -1; c.Emote = "dots"; c.Age = 0.8; list.Add(c);
            c = Sc("转圈 背影 spin"); c.P.ArmL = c.P.ArmR = ArmPose.Out; c.P.Eyes = EyePose.None; c.Flip = -0.6; c.Sq = 0.05; list.Add(c);
            c = Sc("打滚 roll"); c.P.Eyes = EyePose.Squint; c.P.ArmL = c.P.ArmR = ArmPose.Up; c.P.Legs = LegPose.Tucked; c.Rot = 2.2; list.Add(c);
            // 3) 更多动作
            c = Sc("做操 exercise"); c.P.ArmL = c.P.ArmR = ArmPose.Up; c.P.Legs = LegPose.Spread; c.P.Eyes = EyePose.Happy; c.Sq = 0.14; c.Hop = 1.6 * s; list.Add(c);
            c = Sc("挠头 scratch"); c.P.ArmR = ArmPose.UpOut; c.P.Eyes = EyePose.Squint; c.Emote = "q"; list.Add(c);
            c = Sc("东张西望 look"); c.P.LookX = -1; c.P.LookY = -1; c.P.ArmR = ArmPose.Raised; c.Lean = -0.09; c.Sq = 0.06; list.Add(c);
            c = Sc("在边上晃 teeter"); c.P.ArmL = ArmPose.UpOut; c.P.ArmR = ArmPose.DownOut; c.P.Eyes = EyePose.Wide; c.P.LookX = 1; c.P.LookY = 1; c.Lean = 0.38; c.Ledge = true; c.Emote = "sweat"; list.Add(c);
            c = Sc("伸手够鼠标 reach"); c.P.ArmL = c.P.ArmR = ArmPose.Up; c.P.LookY = -1; c.Sq = 0.2; c.Hop = 0.6 * s; list.Add(c);
            c = Sc("咯咯笑 giggle"); c.P.ArmL = ArmPose.Up; c.P.ArmR = ArmPose.Raised; c.P.Eyes = EyePose.Happy; c.P.Blush = true; c.Lean = 0.12;
            c.Parts.Add(Pt(Sprite.Note, Sprite.NoteC, 21 * s, 1 * s, 0)); c.Parts.Add(Pt(Sprite.Heart, Sprite.HeartC, 0, 3 * s, 0)); list.Add(c);
            // 4) 办公 / 吃东西
            c = Sc("办公 work"); c.P.BodyDy = 2; c.P.ArmL = ArmPose.Raised; c.P.Eyes = EyePose.Focus; c.P.LookY = 1; c.Laptop = true; c.Emote = "idea";
            c.Parts.Add(Pt(Sprite.Steam, Sprite.SteamC, 20 * s, 10 * s, 0)); list.Add(c);
            c = Sc("办公 写代码"); c.P.BodyDy = 2; c.P.ArmR = ArmPose.Raised; c.P.Eyes = EyePose.Focus; c.P.LookY = 1; c.Laptop = true; c.P.Facing = -1;
            c.Parts.Add(Pt(Sprite.CodeTag, Sprite.NoteC, 14 * s, 2 * s, Math.Max(1, s / 2))); c.Parts.Add(Pt(Sprite.Steam, Sprite.SteamC, 2 * s, 10 * s, 0)); list.Add(c);
            c = Sc("喂一口 饼干"); c.P.Eyes = EyePose.Heart; c.P.Blush = true; c.P.ArmL = ArmPose.Raised; c.Snack = Sprite.Snacks[0]; c.Sq = -0.06;
            c.Parts.Add(Pt(Sprite.Crumb, Sprite.CrumbC, 9 * s, 13 * s, 0)); list.Add(c);
            c = Sc("啃了两口 苹果"); c.P.Eyes = EyePose.Heart; c.P.Blush = true; c.P.ArmR = ArmPose.Raised; c.Snack = Sprite.Snacks[1]; c.Bite = 1; list.Add(c);
            c = Sc("吃不下了 摇头"); c.P.Eyes = EyePose.Squint; c.P.ArmL = c.P.ArmR = ArmPose.Down; c.Lean = 0.14; c.Emote = "sweat"; list.Add(c);
            c = Sc("踢球 kick"); c.P.Legs = LegPose.Kick; c.P.ArmL = c.P.ArmR = ArmPose.Out; c.P.Eyes = EyePose.Happy; c.Ball = true; list.Add(c);
            // 5) 睡觉 / 做梦
            c = Sc("睡觉 + 鼻涕泡"); c.P.Eyes = EyePose.Closed; c.P.Legs = LegPose.Tucked; c.P.BodyDy = 3; c.Nose = 2.4; c.Sq = -0.03;
            c.Parts.Add(Pt(Sprite.Zed, Sprite.ZC, 19 * s, 0, 0)); list.Add(c);
            c = Sc("小鼻涕泡（刚冒）"); c.P.Eyes = EyePose.Closed; c.P.Legs = LegPose.Tucked; c.P.BodyDy = 3; c.Nose = 1.0; c.P.Facing = -1; list.Add(c);
            c = Sc("坐在边上睡"); c.P.Eyes = EyePose.Closed; c.P.Legs = LegPose.Hang; c.P.ArmL = c.P.ArmR = ArmPose.Down; c.Ledge = true; c.SitOff = 3; c.Nose = 1.8; c.Lean = 0.06; list.Add(c);
            c = Sc("打瞌睡 doze"); c.P.BodyDy = 2; c.P.ArmL = c.P.ArmR = ArmPose.Down; c.P.Eyes = EyePose.Focus; c.Sq = -0.12; c.Lean = 0.1; list.Add(c);
            c = Sc("做梦（点泡泡玩游戏）"); c.P.Eyes = EyePose.Closed; c.P.Legs = LegPose.Tucked; c.P.BodyDy = 3; c.Emote = "dream"; c.Icon = Sprite.Dreams[2]; list.Add(c);
            c = Sc("睡着被摸 偷笑"); c.P.Eyes = EyePose.Happy; c.P.Legs = LegPose.Tucked; c.P.BodyDy = 3; c.P.Blush = true; c.Nose = 2.0;
            c.Parts.Add(Pt(Sprite.Heart, Sprite.HeartC, 9 * s, 0, 0)); list.Add(c);
            // 盖小被子（四只各一种颜色）
            c = Sc("盖被子睡 + 鼻涕泡"); c.P.Eyes = EyePose.Closed; c.P.Legs = LegPose.Tucked; c.P.BodyDy = 3; c.P.BlanketH = 5; c.P.BlanketC = 0; c.Nose = 2.2;
            c.Parts.Add(Pt(Sprite.Zed, Sprite.ZC, 19 * s, 0, 0)); list.Add(c);
            c = Sc("刚躺下 拉被子"); c.P.Eyes = EyePose.Closed; c.P.Legs = LegPose.Tucked; c.P.BodyDy = 3; c.P.Acc = 1; c.P.BlanketH = 3; c.P.BlanketC = 1; list.Add(c);
            c = Sc("被子里伸出手"); c.P.Eyes = EyePose.Closed; c.P.Legs = LegPose.Tucked; c.P.BodyDy = 3; c.P.Acc = 2; c.P.ArmL = c.P.ArmR = ArmPose.Out; c.P.BlanketH = 5; c.P.BlanketC = 2; list.Add(c);
            c = Sc("起床 掀被子"); c.P.Eyes = EyePose.Happy; c.P.ArmL = c.P.ArmR = ArmPose.Up; c.P.Acc = Sprite.SeasonAcc(); c.P.BlanketH = 2; c.P.BlanketC = 3; list.Add(c);
            // 6) 快捷按钮条
            c = Sc("快捷按钮：喂一口"); c.Bar = true; c.Hot = 0; c.Tip = "喂一口"; c.P.Blush = true; c.P.Eyes = EyePose.Happy; list.Add(c);
            c = Sc("快捷按钮：扔个球"); c.Bar = true; c.Hot = 1; c.Tip = "扔个球"; c.P.LookY = -1; c.P.Eyes = EyePose.Happy; list.Add(c);
            c = Sc("快捷按钮：做个梦"); c.Bar = true; c.Hot = 3; c.Tip = "做个梦（小游戏）"; c.P.LookY = -1; list.Add(c);
            c = Sc("待在这儿别动（开）"); c.Bar = true; c.Hot = 4; c.Toggled = 4; c.Tip = "随便走走"; c.P.BodyDy = 2; c.P.ArmL = c.P.ArmR = ArmPose.Down; list.Add(c);
            c = Sc("最小号：按钮不跟着缩"); c.Sz = 4; c.Bar = true; c.Hot = 3; c.Tip = "做个梦（小游戏）"; c.P.LookY = -1; list.Add(c);
            c = Sc("走路 4 帧 ①"); c.P.Legs = LegPose.WalkA; c.P.LookX = 1; c.Lean = 0.08; c.Sq = -0.03; list.Add(c);
            c = Sc("走路 ② 并拢 抬起"); c.P.LookX = 1; c.Lean = 0.08; c.Sq = 0.04; c.Hop = 0.4 * s; list.Add(c);
            c = Sc("走路 ③"); c.P.Legs = LegPose.WalkB; c.P.LookX = 1; c.Lean = 0.08; c.Sq = -0.03; list.Add(c);
            // 7) 爬墙：转 90°，脚贴着屏幕边（左墙 +π/2，右墙 -π/2；往上爬时 facing = 墙的那一边）
            c = Sc("爬左墙 往上"); c.Rot = Math.PI / 2; c.WallSide = -1; c.Air = true; c.P.Facing = -1; c.P.LookX = -1;
            c.P.Legs = LegPose.WalkA; c.P.ArmL = ArmPose.Raised; c.Sq = 0.05; c.Lean = -0.1; list.Add(c);
            c = Sc("爬左墙 迈另一步"); c.Rot = Math.PI / 2; c.WallSide = -1; c.Air = true; c.P.Facing = -1; c.P.LookX = -1;
            c.P.Legs = LegPose.WalkB; c.P.ArmR = ArmPose.Raised; c.Sq = -0.03; c.Lean = -0.1; list.Add(c);
            c = Sc("扒在左墙 往屏幕里看"); c.Rot = Math.PI / 2; c.WallSide = -1; c.Air = true; c.P.Facing = -1; c.P.LookY = -1;
            c.P.ArmL = c.P.ArmR = ArmPose.Raised; list.Add(c);
            c = Sc("爬右墙 往上"); c.Rot = -Math.PI / 2; c.WallSide = 1; c.Air = true; c.P.Facing = 1; c.P.LookX = 1;
            c.P.Legs = LegPose.WalkA; c.P.ArmL = ArmPose.Raised; c.Sq = 0.05; c.Lean = 0.1; list.Add(c);
            c = Sc("扒在右墙 往下爬"); c.Rot = -Math.PI / 2; c.WallSide = 1; c.Air = true; c.P.Facing = -1; c.P.LookX = -1;
            c.P.Legs = LegPose.WalkB; c.P.ArmR = ArmPose.Raised; c.Lean = -0.1; list.Add(c);
            c = Sc("扒上墙（转到一半）"); c.Rot = Math.PI / 4; c.WallSide = 0; c.P.Facing = -1; c.P.ArmL = c.P.ArmR = ArmPose.Up; list.Add(c);
            // 8) 挖沙躲起来：地面以下的部分不画，像沉进沙里 / 躲到任务栏后面
            c = Sc("挖沙 dig"); c.Sink = 2.5; c.Mound = 1.2; c.P.ArmL = ArmPose.DownOut; c.P.ArmR = ArmPose.Down; c.P.Eyes = EyePose.Squint; c.P.LookY = 1;
            c.P.Legs = LegPose.Spread; c.Lean = 0.12; c.Sq = -0.04;
            for (int i = 0; i < 5; i++) c.Parts.Add(Pt(Sprite.Crumb, i % 2 == 0 ? Sprite.SandA : Sprite.SandB, (i < 3 ? 1 + i * 2 : 18 + i) * s, (11 - (i % 3) * 2) * s, 0));
            list.Add(c);
            c = Sc("沉下去一半"); c.Sink = 4.5; c.Mound = 2.2; c.P.ArmL = ArmPose.Down; c.P.ArmR = ArmPose.DownOut; c.P.Eyes = EyePose.Focus; c.P.LookY = 1; c.Lean = -0.1;
            c.Parts.Add(Pt(Sprite.Crumb, Sprite.SandA, 3 * s, 12 * s, 0)); c.Parts.Add(Pt(Sprite.Crumb, Sprite.SandB, 20 * s, 11 * s, 0)); list.Add(c);
            c = Sc("埋好了 只露眼睛"); c.Sink = 7; c.Mound = 3; c.P.Legs = LegPose.Tucked; c.P.LookX = -1; list.Add(c);
            c = Sc("全躲起来 只剩沙堆"); c.Sink = 14; c.Mound = 3; c.P.Legs = LegPose.Tucked; list.Add(c);
            c = Sc("钻出来 + 沙子"); c.Mound = 1.5; c.Hop = 3 * s; c.P.ArmL = c.P.ArmR = ArmPose.Up; c.P.Eyes = EyePose.Wide; c.Sq = 0.2; c.Emote = "bang";
            for (int i = 0; i < 6; i++) c.Parts.Add(Pt(Sprite.Crumb, i % 2 == 0 ? Sprite.SandA : Sprite.SandB, (2 + i * 4) * s, (12 - (i % 2) * 3) * s, 0));
            list.Add(c);
            c = Sc("抖掉沙子 shake"); c.P.ArmL = ArmPose.Out; c.P.ArmR = ArmPose.UpOut; c.P.Eyes = EyePose.Squint; c.Lean = 0.18;
            for (int i = 0; i < 4; i++) c.Parts.Add(Pt(Sprite.Crumb, i % 2 == 0 ? Sprite.SandA : Sprite.SandB, (3 + i * 5) * s, (8 + (i % 2) * 4) * s, 0));
            list.Add(c);
            // 9) 更多待机
            c = Sc("看书 read"); c.P.BodyDy = 2; c.P.Legs = LegPose.Tucked; c.P.ArmL = c.P.ArmR = ArmPose.Raised; c.P.LookY = 1; c.Prop = "book"; list.Add(c);
            c = Sc("看书 翻页 偷笑"); c.P.BodyDy = 2; c.P.Legs = LegPose.Tucked; c.P.ArmL = c.P.ArmR = ArmPose.Raised; c.P.LookY = 1; c.P.Eyes = EyePose.Happy; c.P.Blush = true; c.Prop = "book"; c.PropFrame = 1; c.Emote = "idea"; list.Add(c);
            c = Sc("坐边上弹尤克里里 晃腿"); c.P.Legs = LegPose.Hang; c.P.LegDx = 2; c.P.LegDx2 = -1; c.P.ArmR = ArmPose.Raised; c.P.ArmL = ArmPose.Normal; c.P.Eyes = EyePose.Happy; c.Prop = "uke"; c.Lean = 0.06;
            c.Ledge = true; c.SitOff = 3; c.Parts.Add(Pt(Sprite.Note, Sprite.NoteC, 21 * s, 3 * s, 0)); list.Add(c);
            c = Sc("弹琴（朝左）晃腿"); c.P.Facing = -1; c.P.Legs = LegPose.Hang; c.P.LegDx = -2; c.P.LegDx2 = 1; c.P.LegPh = 1; c.P.ArmL = ArmPose.Raised; c.P.ArmR = ArmPose.Raised; c.P.Eyes = EyePose.Closed; c.Prop = "uke";
            c.Ledge = true; c.SitOff = 3; list.Add(c);
            c = Sc("站在伙伴头上：盘腿弹"); c.P.BodyDy = 2; c.P.Legs = LegPose.Tucked; c.P.ArmR = ArmPose.Raised; c.P.ArmL = ArmPose.Normal; c.P.Eyes = EyePose.Happy; c.Prop = "uke"; list.Add(c);
            c = Sc("扫地 sweep"); c.P.Legs = LegPose.WalkA; c.P.ArmL = c.P.ArmR = ArmPose.Raised; c.P.LookX = 1; c.P.LookY = 1; c.Prop = "broom";
            c.Parts.Add(Pt(Sprite.Puff, Sprite.PuffC, 24 * s, 14 * s, 0)); list.Add(c);
            c = Sc("扫地（朝左）"); c.P.Facing = -1; c.P.Legs = LegPose.WalkB; c.P.ArmL = c.P.ArmR = ArmPose.Raised; c.P.LookX = -1; c.P.LookY = 1; c.Prop = "broom"; c.PropFrame = 1; list.Add(c);
            c = Sc("举重 放下"); c.P.ArmL = c.P.ArmR = ArmPose.Raised; c.P.BodyDy = 1; c.P.Legs = LegPose.Spread; c.P.Eyes = EyePose.Focus; c.Prop = "barbell"; list.Add(c);
            c = Sc("举重 举起"); c.P.ArmL = c.P.ArmR = ArmPose.Up; c.P.Eyes = EyePose.Squint; c.Prop = "barbell"; c.PropFrame = 1; c.Sq = 0.06; list.Add(c);
            c = Sc("堆沙堡 一半"); c.P.BodyDy = 2; c.P.Legs = LegPose.Tucked; c.P.ArmR = ArmPose.Out; c.P.LookX = 1; c.P.LookY = 1; c.P.Eyes = EyePose.Focus; c.Prop = "castle"; c.PropFrame = 2;
            c.Parts.Add(Pt(Sprite.Crumb, Sprite.SandA, 25 * s, 12 * s, 0)); list.Add(c);
            c = Sc("堆好了 美滋滋"); c.P.BodyDy = 2; c.P.Legs = LegPose.Tucked; c.P.ArmR = ArmPose.Raised; c.P.LookX = 1; c.P.LookY = 1; c.P.Eyes = EyePose.Happy; c.P.Blush = true; c.Prop = "castle"; c.PropFrame = 3; c.Emote = "heart"; list.Add(c);
            c = Sc("吐泡泡 bubbles"); c.P.Eyes = EyePose.Happy; c.P.LookX = 1; c.P.LookY = -1;
            c.Parts.Add(Pt(BubSPrev, BubCPrev, 17 * s, 6 * s, 0)); c.Parts.Add(Pt(BubLPrev, BubCPrev, 19 * s, 1 * s, 0)); c.Parts.Add(Pt(BubSPrev, BubCPrev, 15 * s, -2 * s, 0)); list.Add(c);
            c = Sc("戴耳机听歌"); c.P.BodyDy = 1; c.P.Legs = LegPose.WalkA; c.P.Eyes = EyePose.Closed; c.Prop = "phones";
            c.Parts.Add(Pt(Sprite.Note, Sprite.NoteC, 21 * s, 1 * s, 0)); list.Add(c);
            c = Sc("叠纸飞机"); c.P.ArmL = ArmPose.Raised; c.P.LookX = 1; c.P.LookY = 1; c.P.Eyes = EyePose.Focus; c.Prop = "paper"; list.Add(c);
            c = Sc("扔纸飞机"); c.P.ArmR = ArmPose.Up; c.P.LookX = 1; c.P.LookY = -1; c.P.Eyes = EyePose.Wide; c.P.Legs = LegPose.WalkA; c.Prop = "plane"; list.Add(c);
            // 10) 架木杆爬窗口
            c = Sc("把木杆推上去"); c.P.ArmR = ArmPose.Up; c.P.ArmL = ArmPose.Raised; c.P.LookX = 1; c.P.LookY = -1; c.P.Eyes = EyePose.Focus; c.P.Legs = LegPose.Spread; c.Pole = 30; c.PoleX = 23; c.Emote = "idea"; list.Add(c);
            c = Sc("杆子倒过去靠住窗角"); c.ShiftX = -11; c.Air = true; c.P.ArmR = ArmPose.Up; c.P.ArmL = ArmPose.Raised; c.P.LookX = 1; c.P.LookY = -1; c.P.Eyes = EyePose.Wide; c.P.Legs = LegPose.Spread; c.Slant = 1.08; c.SlantTop = 30; c.Emote = "note"; list.Add(c);
            c = Sc("顺着斜杆往上爬"); c.ShiftX = -13; c.Hop = 7 * s; c.Rot = -1.08; c.Air = true; c.P.Facing = 1; c.P.LookX = 1; c.P.Legs = LegPose.WalkA; c.P.ArmL = ArmPose.Raised; c.Sq = 0.05; c.Lean = 0.1; c.Slant = 1.08; c.SlantTop = 12; list.Add(c);
            c = Sc("斜杆上 歇口气"); c.ShiftX = -13; c.Hop = 7 * s; c.Rot = -1.08; c.Air = true; c.P.Facing = 1; c.P.ArmL = c.P.ArmR = ArmPose.Raised; c.P.LookX = -1; c.P.LookY = -1; c.Slant = 1.08; c.SlantTop = 12; c.Emote = "note"; list.Add(c);
            // 11) 好几只 Clawd：小饰品（眼镜、帽子、四季）、叠高高、击掌
            c = Sc("第二只：眼镜"); c.P.Acc = 1; c.P.LookX = 1; list.Add(c);
            c = Sc("眼镜 眨眼 / 开心"); c.P.Acc = 1; c.P.Eyes = EyePose.Happy; c.P.Blush = true; list.Add(c);
            c = Sc("第三只：帽子"); c.P.Acc = 2; c.P.ArmR = ArmPose.Up; c.P.Eyes = EyePose.Happy; list.Add(c);
            c = Sc("第四只 春：小花"); c.P.Acc = 3; list.Add(c);
            c = Sc("第四只 夏：西瓜"); c.P.Acc = 4; c.P.Eyes = EyePose.Happy; list.Add(c);
            c = Sc("第四只 秋：枫叶"); c.P.Acc = 5; c.P.LookX = -1; list.Add(c);
            c = Sc("第四只 冬：围巾"); c.P.Acc = 6; c.P.Eyes = EyePose.Happy; c.P.Blush = true; list.Add(c);
            c = Sc("围巾 睡觉"); c.P.Acc = 6; c.P.Eyes = EyePose.Closed; c.P.Legs = LegPose.Tucked; c.P.BodyDy = 3; list.Add(c);
            {
                Pose r1 = new Pose(); r1.Acc = 1; r1.Eyes = EyePose.Happy; r1.Blush = true; r1.ArmL = r1.ArmR = ArmPose.Raised;
                c = Sc("叠高高 两只"); c.P.ArmL = c.P.ArmR = ArmPose.Up; c.P.LookY = -1; c.P.Eyes = EyePose.Happy; c.Stack.Add(r1); list.Add(c);
            }
            {
                Pose r1 = new Pose(); r1.Acc = 2; r1.ArmL = r1.ArmR = ArmPose.Up; r1.LookY = -1;
                Pose r2 = new Pose(); r2.Acc = 6; r2.Eyes = EyePose.Happy; r2.Blush = true; r2.ArmL = ArmPose.Up; r2.ArmR = ArmPose.UpOut;
                c = Sc("叠高高 三只"); c.P.Acc = 1; c.P.ArmL = c.P.ArmR = ArmPose.Up; c.P.LookY = -1; c.P.Eyes = EyePose.Squint; c.Stack.Add(r1); c.Stack.Add(r2);
                c.Parts.Add(Pt(Sprite.Star, Sprite.StarC, 1 * s, -18 * s, 0)); c.Parts.Add(Pt(Sprite.Star, Sprite.StarC, 21 * s, -22 * s, 0)); list.Add(c);
            }
            {
                Pose r1 = new Pose(); r1.BodyDy = 2; r1.ArmL = r1.ArmR = ArmPose.Down; r1.LookX = 1;
                c = Sc("头上坐着一只 驮着走"); c.P.ArmL = c.P.ArmR = ArmPose.Up; c.P.Legs = LegPose.WalkA; c.P.LookX = 1; c.P.LookY = -1; c.Lean = 0.06; c.Stack.Add(r1); list.Add(c);
            }
            {
                Pose f = new Pose(); f.Acc = 2; f.Facing = -1; f.ArmL = ArmPose.Up; f.Eyes = EyePose.Happy; f.Blush = true; f.LookX = -1;
                c = Sc("击掌"); c.ShiftX = -10; c.P.ArmR = ArmPose.Up; c.P.Eyes = EyePose.Happy; c.P.Blush = true; c.P.LookX = 1; c.Friend = f; c.FriendDx = 20;
                c.Parts.Add(Pt(Sprite.Star, Sprite.StarC, 20 * s, -1 * s, 0)); list.Add(c);
            }
            {
                Pose f = new Pose(); f.Facing = -1; f.ArmL = ArmPose.UpOut; f.Eyes = EyePose.Wide; f.LookX = -1;
                c = Sc("击掌 手伸出去"); c.ShiftX = -10; c.P.ArmR = ArmPose.UpOut; c.P.Eyes = EyePose.Wide; c.P.LookX = 1; c.Friend = f; c.FriendDx = 20; list.Add(c);
            }
            {
                // 一起踢球：一只把球吊过去，伙伴叉开腿张着手等着接
                Pose f = new Pose(); f.Acc = 1; f.Facing = -1; f.Legs = LegPose.Spread; f.ArmL = ArmPose.Up; f.ArmR = ArmPose.Out; f.LookX = -1; f.LookY = -1;
                c = Sc("一起踢球 吊个高球"); c.ShiftX = -11; c.P.Legs = LegPose.Kick; c.P.ArmL = c.P.ArmR = ArmPose.Out; c.P.Eyes = EyePose.Happy; c.P.LookX = 1; c.P.LookY = -1;
                c.Friend = f; c.FriendDx = 22; c.Ball = true; c.BallDx = 20; c.BallDy = -6;
                c.Parts.Add(Pt(Sprite.Puff, Sprite.PuffC, 21 * s, 14 * s, 0)); list.Add(c);
            }
            {
                Pose f = new Pose(); f.Facing = -1; f.Legs = LegPose.Spread; f.ArmL = f.ArmR = ArmPose.Out; f.LookX = -1; f.LookY = -1; f.Eyes = EyePose.Wide;
                c = Sc("头球传过去"); c.ShiftX = -11; c.Hop = 6 * s; c.P.Legs = LegPose.DangleA; c.P.ArmL = c.P.ArmR = ArmPose.Up; c.P.Eyes = EyePose.Squint; c.P.LookY = -1;
                c.Friend = f; c.FriendDx = 22; c.Ball = true; c.BallDx = 9; c.BallDy = -8;
                c.Parts.Add(Pt(Sprite.Star, Sprite.StarC, 16 * s, -9 * s, 0)); list.Add(c);
            }
            // （四只踢比赛的半俯视球场太大，放不进这一格：用 --pitch 单独画）
            // 13) 交流：聊天、安慰、围观
            {
                Pose f = new Pose(); f.Acc = 1; f.Facing = -1; f.ArmL = f.ArmR = ArmPose.Down; f.LookX = -1;
                c = Sc("聊天"); c.ShiftX = -11; c.P.ArmR = ArmPose.Raised; c.P.LookX = 1; c.Lean = 0.05; c.Friend = f; c.FriendDx = 22; c.Say = "今天写了几行代码？"; list.Add(c);
            }
            {
                Pose f = new Pose(); f.Acc = 1; f.Facing = -1; f.ArmL = ArmPose.Raised; f.LookX = -1; f.Eyes = EyePose.Happy;
                c = Sc("聊天 回一句"); c.ShiftX = -11; c.P.ArmL = c.P.ArmR = ArmPose.Down; c.P.LookX = 1; c.Friend = f; c.FriendDx = 22; c.Say = "三行，删了两行"; c.Emote = "q"; list.Add(c);
            }
            {
                Pose f = new Pose(); f.Acc = 2; f.Facing = -1; f.Eyes = EyePose.Dizzy; f.ArmL = f.ArmR = ArmPose.Down;
                c = Sc("伙伴晕了 拍拍它"); c.ShiftX = -11; c.P.ArmR = ArmPose.UpOut; c.P.Eyes = EyePose.Happy; c.P.LookX = 1; c.Lean = 0.08; c.Friend = f; c.FriendDx = 20; c.Emote = "heart";
                for (int i = 0; i < 3; i++) { double a = i * 2.1; c.Parts.Add(Pt(Sprite.Star, Sprite.StarC, 31 * s + Math.Cos(a) * 8 * s, 1 * s + Math.Sin(a) * 1.5 * s, 0)); }
                list.Add(c);
            }
            {
                Pose f = new Pose(); f.Acc = 6; f.Facing = -1; f.BodyDy = 2; f.ArmL = f.ArmR = ArmPose.Out; f.Eyes = EyePose.Happy; f.LookX = -1;
                c = Sc("坐着看伙伴弹琴"); c.ShiftX = -11; c.P.BodyDy = 2; c.P.Legs = LegPose.Tucked; c.P.ArmR = ArmPose.Raised; c.P.ArmL = ArmPose.Normal; c.P.Eyes = EyePose.Happy; c.Prop = "uke"; c.Lean = 0.06;
                c.Friend = f; c.FriendDx = 23;
                c.Parts.Add(Pt(Sprite.Note, Sprite.NoteC, 21 * s, 3 * s, 0)); c.Parts.Add(Pt(Sprite.Note, Sprite.NoteC, 34 * s, -2 * s, 0)); list.Add(c);
            }
            {
                // 一起上班：一排小工位，各自对着小电脑敲
                Pose f = new Pose(); f.Acc = 1; f.BodyDy = 2; f.ArmL = ArmPose.Normal; f.ArmR = ArmPose.Raised; f.Eyes = EyePose.Focus; f.LookY = 1;
                c = Sc("一起上班"); c.ShiftX = -10; c.Office = 2; c.Laptop = true; c.P.BodyDy = 2; c.P.ArmL = ArmPose.Raised; c.P.Eyes = EyePose.Focus; c.P.LookY = 1;
                c.Friend = f; c.FriendDx = PetForm.CUB_W; c.FriendLaptop = true; list.Add(c);
                Pose f2 = new Pose(); f2.Acc = 1; f2.BodyDy = 2; f2.ArmL = f2.ArmR = ArmPose.Up; f2.Eyes = EyePose.Happy;
                c = Sc("上班 看看隔壁"); c.ShiftX = -10; c.Office = 2; c.Laptop = true; c.P.BodyDy = 2; c.P.LookX = 1; c.P.Eyes = EyePose.Focus;
                c.Friend = f2; c.FriendDx = PetForm.CUB_W; c.FriendLaptop = true; list.Add(c);
            }

            int cols = 6, cw = 48 * s, chh = 50 * s;
            int rows = (list.Count + cols - 1) / cols;
            int stripH = 46 * s;
            using (Bitmap bmp = new Bitmap(cols * cw, rows * chh + stripH))
            using (Graphics g = Graphics.FromImage(bmp))
            using (Font label = new Font("Microsoft YaHei UI", 13f, GraphicsUnit.Pixel))
            using (Font tipF = new Font("Microsoft YaHei UI", Math.Max(12f, s * 2.5f), FontStyle.Regular, GraphicsUnit.Pixel))
            {
                g.Clear(Color.FromArgb(232, 229, 220));
                g.SmoothingMode = SmoothingMode.None;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                for (int i = 0; i < list.Count; i++)
                {
                    GraphicsState st = g.Save();
                    g.SetClip(new Rectangle((i % cols) * cw, (i / cols) * chh, cw, chh));
                    RenderScene(g, list[i], (i % cols) * cw, (i / cols) * chh, cw, chh, list[i].Sz > 0 ? list[i].Sz : s, tipF, label);
                    g.Restore(st);
                }
                // 表情泡泡一排 + 零食 / 球 / 蝴蝶
                int y0 = rows * chh;
                using (SolidBrush bg = new SolidBrush(Color.FromArgb(250, 248, 242))) g.FillRectangle(bg, 2, y0 + 2, bmp.Width - 4, stripH - 4);
                string[] kinds = { "heart", "note", "q", "bang", "sweat", "star", "zz", "anger", "idea", "dots", "dream" };
                using (SolidBrush lb = new SolidBrush(Color.FromArgb(70, 68, 62)))
                {
                    for (int i = 0; i < kinds.Length; i++)
                    {
                        int ax = 12 * s + i * 17 * s - 7 * s, headY = y0 + 22 * s;
                        Sprite.DrawEmote(g, kinds[i], kinds[i] == "dream" ? Sprite.Dreams[0] : null, kinds[i] == "dots" ? 0.8 : 0.6, 2, ax, headY, s, 1);
                        g.DrawString(kinds[i], label, lb, ax + 7 * s - 18, headY + 1 * s);
                    }
                    int ox = 12 * s, oy = y0 + 34 * s;
                    g.DrawString("零食 / 球 / 蝴蝶：", label, lb, ox - 8 * s, oy + 1 * s);
                    ox += 14 * s;
                    foreach (Art a in Sprite.Snacks) { Sprite.DrawArt(g, a.Rows, a, ox, oy, s); ox += 8 * s; }
                    for (int k = 0; k < 4; k++) { Art b = Sprite.BallArt(k); Sprite.DrawArt(g, b.Rows, b, ox, oy, s); ox += 8 * s; }
                    for (int k = 0; k < 4; k++) { Art f = Sprite.FlyArt(k); Sprite.DrawArt(g, f.Frames[k & 1], f, ox, oy, s); ox += 7 * s; }
                }
                bmp.Save(path, ImageFormat.Png);
            }
        }
    }

    // ------------------------------------------------------------------ Clawd 自己的界面风格（右键菜单、提醒对话框）
    // 奶油色底、深色像素描边（四个角是台阶形的缺角）、暖色高亮、橙色勾，和气泡 / 网页版菜单一个味道
    static class ClawdUi
    {
        public static readonly Color Bg = Color.FromArgb(0xFA, 0xF9, 0xF5);
        public static readonly Color Border = Color.FromArgb(0x3D, 0x39, 0x29);
        public static readonly Color Text = Color.FromArgb(0x1F, 0x1E, 0x1D);
        public static readonly Color Hover = Color.FromArgb(0xF3, 0xF1, 0xEA);
        public static readonly Color Sep = Color.FromArgb(0xEC, 0xE9, 0xE0);
        public static readonly Color Accent = Color.FromArgb(0xD7, 0x77, 0x57);
        public static readonly Color AccentHot = Color.FromArgb(0xE5, 0x8A, 0x69);
        public static readonly Color Dim = Color.FromArgb(0x75, 0x73, 0x6B);
        public static readonly Color Disabled = Color.FromArgb(0xB5, 0xB1, 0xA6);
        public static readonly Color ChipLine = Color.FromArgb(0xDA, 0xD6, 0xCB);
        public static readonly Color InputLine = Color.FromArgb(0xCF, 0xCB, 0xBF);

        public static int Px(float dpi) { return Math.Max(2, (int)Math.Round(2 * dpi)); }

        // 台阶形缺角的多边形（p = 一级台阶的大小）
        public static Point[] StepShape(Rectangle r, int p)
        {
            int l = r.Left, t = r.Top, rr = r.Right, b = r.Bottom;
            return new Point[] {
                new Point(l + 2 * p, t), new Point(rr - 2 * p, t), new Point(rr - 2 * p, t + p), new Point(rr - p, t + p),
                new Point(rr - p, t + 2 * p), new Point(rr, t + 2 * p), new Point(rr, b - 2 * p), new Point(rr - p, b - 2 * p),
                new Point(rr - p, b - p), new Point(rr - 2 * p, b - p), new Point(rr - 2 * p, b), new Point(l + 2 * p, b),
                new Point(l + 2 * p, b - p), new Point(l + p, b - p), new Point(l + p, b - 2 * p), new Point(l, b - 2 * p),
                new Point(l, t + 2 * p), new Point(l + p, t + 2 * p), new Point(l + p, t + p), new Point(l + 2 * p, t + p) };
        }

        public static Region StepRegion(Size sz, int p)
        {
            using (GraphicsPath gp = new GraphicsPath())
            {
                gp.AddPolygon(StepShape(new Rectangle(Point.Empty, sz), p));
                return new Region(gp);
            }
        }

        // 深色描边 + 奶油色底
        public static void PaintPanel(Graphics g, Size sz, int p)
        {
            g.SmoothingMode = SmoothingMode.None;
            g.FillPolygon(Sprite.BrushOf(Border), StepShape(new Rectangle(Point.Empty, sz), p));
            g.FillPolygon(Sprite.BrushOf(Bg), StepShape(new Rectangle(p, p, sz.Width - 2 * p, sz.Height - 2 * p), p));
        }

        public static GraphicsPath RoundRect(Rectangle r, int rad)
        {
            GraphicsPath gp = new GraphicsPath();
            int d = Math.Max(2, rad * 2);
            gp.AddArc(r.X, r.Y, d, d, 180, 90);
            gp.AddArc(r.Right - d - 1, r.Y, d, d, 270, 90);
            gp.AddArc(r.Right - d - 1, r.Bottom - d - 1, d, d, 0, 90);
            gp.AddArc(r.X, r.Bottom - d - 1, d, d, 90, 90);
            gp.CloseFigure();
            return gp;
        }

        public static void FillRound(Graphics g, Rectangle r, int rad, Color fill, Color line)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath gp = RoundRect(r, rad))
            {
                g.FillPath(Sprite.BrushOf(fill), gp);
                if (line != Color.Empty) using (Pen pen = new Pen(line, 1)) g.DrawPath(pen, gp);
            }
            g.SmoothingMode = old;
        }

        // 橙色的 ✓
        public static void Check(Graphics g, Rectangle r, float dpi)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float w = Math.Min(r.Width, r.Height) * 0.62f, cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
            using (Pen pen = new Pen(Accent, Math.Max(2f, 2.2f * dpi)))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                g.DrawLines(pen, new PointF[] { new PointF(cx - w * 0.5f, cy), new PointF(cx - w * 0.12f, cy + w * 0.38f), new PointF(cx + w * 0.5f, cy - w * 0.4f) });
            }
            g.SmoothingMode = old;
        }
    }

    // 右键菜单（托盘图标的菜单也是它）的画法
    class ClawdMenuRenderer : ToolStripProfessionalRenderer
    {
        readonly float dpi; readonly int p;
        public ClawdMenuRenderer(float dpi) { this.dpi = dpi; p = ClawdUi.Px(dpi); RoundedEdges = false; }
        int D(double v) { return (int)Math.Round(v * dpi); }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) { ClawdUi.PaintPanel(e.Graphics, e.ToolStrip.Size, p); }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }   // 描边在背景里画了
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }        // 不要系统那条灰色竖栏

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            ToolStripItem it = e.Item;
            if (!it.Selected || !it.Enabled) return;
            Rectangle r = new Rectangle(D(4), 1, it.Width - D(8), it.Height - 2);
            ClawdUi.FillRound(e.Graphics, r, D(5), ClawdUi.Hover, Color.Empty);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? ClawdUi.Text : ClawdUi.Disabled;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) { ClawdUi.Check(e.Graphics, e.ImageRectangle, dpi); }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (Pen pen = new Pen(ClawdUi.Sep, Math.Max(1, D(1))))
                e.Graphics.DrawLine(pen, D(10), y, e.Item.Width - D(10), y);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = ClawdUi.Text; base.OnRenderArrow(e); }
    }

    // 菜单里的一排小按钮（番茄钟 / 大小 / 陪它玩）：自己画。鼠标点按钮；键盘 ← → 选、Enter 按
    class ChipRowItem : ToolStripItem
    {
        readonly string label; readonly float dpi;
        readonly List<string> names = new List<string>();
        readonly List<Action> acts = new List<Action>();
        public Func<int, bool> IsOn;          // 哪个按钮是「当前选中」（比如现在的大小），画成橙色
        // 点在这一排的空白处：菜单先别关（记下时间，子菜单和外层菜单都会问一遍，过一会儿自己失效）
        public static int KeepOpenT = -10000;
        public static bool KeepOpenNow() { return unchecked(Environment.TickCount - KeepOpenT) < 400; }
        int hot = -1, keyIdx, downIdx = -1;
        bool mouseClick;
        Rectangle[] rects = new Rectangle[0];

        public ChipRowItem(string label, float dpi) { this.label = label; this.dpi = dpi; Text = label; }

        public void ClearChips() { names.Clear(); acts.Clear(); keyIdx = 0; hot = -1; }
        public void AddChip(string name, Action act) { names.Add(name); acts.Add(act); }
        int D(double v) { return (int)Math.Round(v * dpi); }

        public override Size GetPreferredSize(Size constrainingSize)
        {
            Size lab = TextRenderer.MeasureText(label, Font);
            int x = D(12) + Math.Max(lab.Width, D(44)) + D(4);
            foreach (string n in names) x += TextRenderer.MeasureText(n, Font).Width + D(16) + D(5);
            return new Size(x + D(10), Font.Height + D(16));
        }

        void Calc()
        {
            Size lab = TextRenderer.MeasureText(label, Font);
            int x = D(12) + Math.Max(lab.Width, D(44)) + D(4), h = Font.Height + D(8), y = (Height - h) / 2;
            rects = new Rectangle[names.Count];
            for (int i = 0; i < names.Count; i++)
            {
                int w = TextRenderer.MeasureText(names[i], Font).Width + D(16);
                rects[i] = new Rectangle(x, y, w, h);
                x += w + D(5);
            }
        }

        int ChipAt(Point pt) { for (int i = 0; i < rects.Length; i++) if (rects[i].Contains(pt)) return i; return -1; }

        protected override void OnPaint(PaintEventArgs e)
        {
            Calc();
            Graphics g = e.Graphics;
            TextRenderer.DrawText(g, label, Font, new Rectangle(D(12), 0, Math.Max(10, Width - D(12)), Height), ClawdUi.Dim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            for (int i = 0; i < rects.Length; i++)
            {
                bool on = IsOn != null && IsOn(i);
                bool focus = Selected && hot < 0 && !mouseClick && i == keyIdx;   // 键盘选中的那个
                Color fill = on ? (i == hot ? ClawdUi.AccentHot : ClawdUi.Accent) : (i == hot || focus ? ClawdUi.Hover : ClawdUi.Bg);
                Color line = on ? ClawdUi.Accent : (focus ? ClawdUi.Accent : ClawdUi.ChipLine);
                ClawdUi.FillRound(g, rects[i], D(5), fill, line);
                TextRenderer.DrawText(g, names[i], Font, rects[i], on ? Color.White : ClawdUi.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = ChipAt(e.Location);
            if (h != hot) { hot = h; Invalidate(); }
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (hot != -1) { hot = -1; Invalidate(); } }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); mouseClick = true; downIdx = ChipAt(e.Location); }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            int i = mouseClick ? downIdx : keyIdx;
            mouseClick = false; downIdx = -1;
            if (i < 0 || i >= acts.Count) { KeepOpenT = Environment.TickCount; return; }
            acts[i]();
        }

        // 键盘：← → 在这一排里挑
        protected override bool ProcessDialogKey(Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            if ((k == Keys.Left || k == Keys.Right) && names.Count > 0)
            {
                keyIdx = (keyIdx + (k == Keys.Right ? 1 : -1) + names.Count) % names.Count;
                mouseClick = false; hot = -1;
                Invalidate();
                return true;
            }
            if (k == Keys.Enter || k == Keys.Space) mouseClick = false;
            return base.ProcessDialogKey(keyData);
        }
    }

    // ------------------------------------------------------------------ 提醒对话框（同样的奶油色像素风，标题栏可以拖）
    class ReminderDialog : Form
    {
        public readonly NumericUpDown Minutes = new NumericUpDown();
        public readonly TextBox Message = new TextBox();
        readonly float dpi; readonly int p;
        bool dragTitle; Point dragOff;

        public ReminderDialog(float dpi)
        {
            this.dpi = dpi; p = ClawdUi.Px(dpi);
            Text = "让 Clawd 提醒你";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = ClawdUi.Bg;
            ForeColor = ClawdUi.Text;
            Font = new Font("Microsoft YaHei UI", 9.5f);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            int pad = p + D(14), padT = p + D(10), padB = p + D(12);

            TableLayoutPanel t = new TableLayoutPanel();
            t.AutoSize = true; t.AutoSizeMode = AutoSizeMode.GrowAndShrink; t.ColumnCount = 2; t.RowCount = 4; t.BackColor = ClawdUi.Bg;
            Label title = new Label();
            title.Text = "让 Clawd 提醒你"; title.AutoSize = true; title.Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold);
            title.Margin = new Padding(0, 0, 0, D(8)); title.Cursor = Cursors.SizeAll;
            Label l1 = new Label(); l1.Text = "几分钟后："; l1.AutoSize = true; l1.Anchor = AnchorStyles.Left;
            Label l2 = new Label(); l2.Text = "提醒内容："; l2.AutoSize = true; l2.Anchor = AnchorStyles.Left;
            FlowLayoutPanel minRow = new FlowLayoutPanel(); minRow.AutoSize = true; minRow.WrapContents = false; minRow.Margin = new Padding(0, D(3), 0, D(3));
            Minutes.Minimum = 1; Minutes.Maximum = 1440; Minutes.Value = 10; Minutes.Width = D(80);
            Minutes.BorderStyle = BorderStyle.FixedSingle; Minutes.BackColor = Color.White; Minutes.ForeColor = ClawdUi.Text;
            Label lmin = new Label(); lmin.Text = "分钟"; lmin.AutoSize = true; lmin.Anchor = AnchorStyles.Left; lmin.Margin = new Padding(D(4), D(4), 0, 0);
            minRow.Controls.Add(Minutes); minRow.Controls.Add(lmin);
            Message.Text = "该休息一下了~"; Message.Width = D(240);
            Message.BorderStyle = BorderStyle.FixedSingle; Message.BackColor = Color.White; Message.ForeColor = ClawdUi.Text;
            Message.Margin = new Padding(0, D(3), 0, D(3));
            FlowLayoutPanel bar = new FlowLayoutPanel(); bar.AutoSize = true; bar.FlowDirection = FlowDirection.RightToLeft; bar.Anchor = AnchorStyles.Right;
            bar.Margin = new Padding(0, D(10), 0, 0); bar.BackColor = ClawdUi.Bg;
            Button ok = MakeButton("好的", true); ok.DialogResult = DialogResult.OK;
            Button cancel = MakeButton("取消", false); cancel.DialogResult = DialogResult.Cancel;
            bar.Controls.Add(ok); bar.Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;     // Enter = 好的，Esc = 取消
            t.Controls.Add(title, 0, 0); t.SetColumnSpan(title, 2);
            t.Controls.Add(l1, 0, 1); t.Controls.Add(minRow, 1, 1);
            t.Controls.Add(l2, 0, 2); t.Controls.Add(Message, 1, 2);
            t.Controls.Add(bar, 0, 3); t.SetColumnSpan(bar, 2);
            Controls.Add(t);
            // 按表格量好的大小摆放，四周留出描边和空隙
            Size ps = t.GetPreferredSize(Size.Empty);
            t.Location = new Point(pad, padT);
            t.Size = ps;
            ClientSize = new Size(ps.Width + 2 * pad, ps.Height + padT + padB);

            // 拖标题（或者对话框空白处）移动，始终留在屏幕里
            MouseEventHandler down = (s, e) => { if (e.Button == MouseButtons.Left) { dragTitle = true; dragOff = new Point(Cursor.Position.X - Left, Cursor.Position.Y - Top); } };
            MouseEventHandler move = (s, e) =>
            {
                if (!dragTitle) return;
                Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
                int x = Math.Max(wa.Left, Math.Min(wa.Right - Width, Cursor.Position.X - dragOff.X));
                int y = Math.Max(wa.Top, Math.Min(wa.Bottom - Height, Cursor.Position.Y - dragOff.Y));
                Location = new Point(x, y);
            };
            MouseEventHandler up = (s, e) => { dragTitle = false; };
            foreach (Control c in new Control[] { title, t, this })
            {
                c.MouseDown += down; c.MouseMove += move; c.MouseUp += up;
            }
            Shown += (s, e) => { Native.SetForegroundWindow(Handle); Message.Focus(); Message.SelectAll(); };
        }

        int D(double v) { return (int)Math.Round(v * dpi); }

        Button MakeButton(string text, bool primary)
        {
            Button b = new Button();
            b.Text = text;
            b.AutoSize = true;
            b.MinimumSize = new Size(D(72), D(30));
            b.FlatStyle = FlatStyle.Flat;
            b.Cursor = Cursors.Hand;
            b.Margin = new Padding(D(6), 0, 0, 0);
            if (primary)
            {
                b.BackColor = ClawdUi.Accent; b.ForeColor = Color.White;
                b.FlatAppearance.BorderColor = ClawdUi.Accent;
                b.FlatAppearance.MouseOverBackColor = ClawdUi.AccentHot;
                b.FlatAppearance.MouseDownBackColor = ClawdUi.AccentHot;
            }
            else
            {
                b.BackColor = Color.White; b.ForeColor = ClawdUi.Text;
                b.FlatAppearance.BorderColor = ClawdUi.InputLine;
                b.FlatAppearance.MouseOverBackColor = ClawdUi.Hover;
                b.FlatAppearance.MouseDownBackColor = ClawdUi.Sep;
            }
            return b;
        }

        protected override void OnPaintBackground(PaintEventArgs e) { ClawdUi.PaintPanel(e.Graphics, ClientSize, p); }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (ClientSize.Width > 0 && ClientSize.Height > 0) Region = ClawdUi.StepRegion(ClientSize, p);
            Invalidate();
        }
    }

    // ------------------------------------------------------------------ 梦里的背景音乐
    // 一首自己写的 8-bit 小曲（F 大调，126 拍/分，约 80 秒），打开小游戏时在后台合成成 WAV，循环放。
    // 不是一段旋律来回转，而是有起承转合：
    //   前奏（分解和弦铺进来）→ A（主题）→ A'（主题 + 对位，换个收尾）→ B（转到 Dm / Bb，长音、抒情，鼓收轻）
    //   → 过门（旋律一级级往上爬，低音推八分，小鼓滚奏）→ A''（主题回来，加三度和声，全编制）→ 尾声（接回前奏）
    // 音色：旋律是带一点颤音和回声的 25% 方波，和声 / 对位 / 反拍和弦是柔和的 50% 方波，低音和琶音是三角波，
    // 鼓用噪声合成。方波用 PolyBLEP 去掉混叠（不然高音会发虚走调，听着诡异）。
    static class DreamMusic
    {
        const int Rate = 32000;
        const double Bpm = 126;
        const int W_P25 = 0, W_P50 = 1, W_TRI = 2;
        static byte[] wav;
        static readonly object gate = new object();

        class Sec { public string[] Chords, Mel; public int Bass, Acc, Drums; public bool Counter, Harmony; }

        static Sec Part(string chords, string mel, int bass, int acc, int drums, bool counter, bool harmony)
        {
            Sec s = new Sec();
            s.Chords = chords.Split(' '); s.Mel = mel.Split('|');
            s.Bass = bass; s.Acc = acc; s.Drums = drums; s.Counter = counter; s.Harmony = harmony;
            return s;
        }

        // 旋律一小节一段（用 | 分开），每段是「音名 拍数」一对一对，r = 休止；每小节 4 拍
        const string A1to4 = "C5 .5 F5 .5 A5 1 G5 .5 F5 .5 C5 1|E5 .5 A5 .5 C6 1 A5 1 G5 1|D5 .5 F5 .5 Bb5 1.5 A5 .5 G5 1|E5 1 G5 .5 F5 .5 E5 1 C5 1";
        const string A5to8 = "C5 .5 F5 .5 A5 1 C6 .5 A5 .5 F5 1|D5 .5 F5 .5 A5 1 G5 .5 F5 .5 D5 1|Bb4 .5 D5 .5 F5 .5 G5 .5 A5 1 Bb5 1|A5 .5 G5 .5 E5 .5 C5 .5 G5 2";
        const string A5to8End = "F5 1 A5 1 D6 1.5 C6 .5|Bb5 1 A5 .5 G5 .5 F5 1 D5 1|E5 .5 F5 .5 G5 1 C6 1 Bb5 1|A5 3 r 1";

        // 低音：0 二分音符 / 1 蹦跳 / 2 抒情 / 3 推八分 / 4 全音符；伴奏：0 分解和弦 / 1 反拍和弦 / 2 铺底；鼓：0 无 / 1 轻 / 2 律动 / 3 轻柔 / 4 蓄力 / 5 满 / 6 收尾
        static readonly Sec[] Song = {
            Part("F Bb F C", "r 4|r 4|r 4|r 2 C5 .5 D5 .5 E5 .5 G5 .5", 0, 0, 1, false, false),
            Part("F Am Bb C F Dm Gm7 C7", A1to4 + "|" + A5to8, 1, 1, 2, false, false),
            Part("F Am Bb C Dm Bb C F", A1to4 + "|" + A5to8End, 1, 1, 2, true, false),
            Part("Dm Bb F C Dm Bb Gm A7", "A5 2 F5 1 D5 1|D5 1.5 F5 .5 Bb5 2|A5 1.5 G5 .5 F5 1 C5 1|E5 2 G5 2|A5 2 D6 1 C6 1|Bb5 1.5 A5 .5 G5 1 F5 1|G5 1 Bb5 1 D6 1 Bb5 1|C#6 2 A5 1 E5 1", 2, 2, 3, false, false),
            Part("Bb C Bb C7", "F5 .5 Bb5 .5 D6 .5 Bb5 .5 F5 .5 Bb5 .5 D6 1|G5 .5 C6 .5 E6 .5 C6 .5 G5 .5 C6 .5 E6 1|F5 .5 Bb5 .5 D6 .5 F6 .5 D6 .5 Bb5 .5 D6 1|E6 1 D6 .5 C6 .5 Bb5 .5 A5 .5 G5 1", 3, 1, 4, false, false),
            Part("F Am Bb C Dm Bb C7 F", A1to4 + "|" + A5to8End, 1, 1, 5, false, true),
            Part("F F", "C6 .5 A5 .5 F5 .5 C5 .5 F5 2|r 4", 4, 0, 6, false, false) };

        public static bool Ready { get { return wav != null; } }
        public static byte[] Wav() { lock (gate) { if (wav == null) wav = Build(); return wav; } }

        static int Midi(string s)
        {
            int pc = new int[] { 9, 11, 0, 2, 4, 5, 7 }["ABCDEFG".IndexOf(s[0])], i = 1;
            if (s[i] == 'b') { pc--; i++; } else if (s[i] == '#') { pc++; i++; }
            return 12 * (int.Parse(s.Substring(i)) + 1) + pc;
        }

        // 和弦名 → 根音（0..11）和组成音的音程
        static void Chord(string name, out int root, out int[] iv)
        {
            root = new int[] { 9, 11, 0, 2, 4, 5, 7 }["ABCDEFG".IndexOf(name[0])];
            int i = 1;
            if (name.Length > 1 && name[1] == 'b') { root = (root + 11) % 12; i++; } else if (name.Length > 1 && name[1] == '#') { root = (root + 1) % 12; i++; }
            string q = name.Substring(i);
            iv = q == "m" ? new int[] { 0, 3, 7 } : q == "m7" ? new int[] { 0, 3, 7, 10 } : q == "7" ? new int[] { 0, 4, 7, 10 } : new int[] { 0, 4, 7 };
        }

        static bool InChord(int m, int root, int[] iv) { foreach (int x in iv) if ((m - root - x) % 12 == 0) return true; return false; }

        // 从 lo 往上数的和弦音
        static List<int> Voicing(int root, int[] iv, int lo, int count)
        {
            List<int> r = new List<int>();
            for (int m = lo; r.Count < count && m < lo + 36; m++) if (InChord(m, root, iv)) r.Add(m);
            return r;
        }

        // 和声：旋律下面 3~9 个半音里最高的和弦音
        static int Below(int m, int root, int[] iv) { for (int h = m - 3; h >= m - 9; h--) if (InChord(h, root, iv)) return h; return -1; }

        static double Blep(double t, double dt)
        {
            if (t < dt) { t /= dt; return t + t - t * t - 1; }
            if (t > 1 - dt) { t = (t - 1) / dt; return t * t + t + t + 1; }
            return 0;
        }

        // 一个音：起音 attack 秒，衰减到 sustain，结尾短短地收掉；lp = 低通（Hz，0 = 不滤）；超出结尾的部分绕回开头（循环接得上）
        static void Tone(float[] buf, double midi, double start, double dur, double amp, int wave, double sustain, double lp, bool vib, double attack)
        {
            int s0 = (int)(start * Rate), len = (int)(dur * Rate), n = buf.Length;
            double f = 440 * Math.Pow(2, (midi - 69) / 12.0), ph = 0, y = 0;
            double a = lp > 0 ? 1 - Math.Exp(-2 * Math.PI * lp / Rate) : 1;
            double rel = Math.Min(0.05, dur * 0.3);
            for (int i = 0; i < len; i++)
            {
                double tt = (double)i / Rate;
                double env = tt < attack ? tt / attack : sustain + (1 - sustain) * Math.Exp(-(tt - attack) / 0.1);
                double left = dur - tt;
                if (left < rel) env *= Math.Max(0, left / rel);
                double v = vib && tt > 0.18 ? 1 + 0.005 * Math.Sin(2 * Math.PI * 5.5 * tt) * Math.Min(1, (tt - 0.18) / 0.2) : 1;
                double dt = f * v / Rate, w;
                if (wave == W_TRI) w = 1 - 4 * Math.Abs(ph - 0.5);
                else
                {
                    double duty = wave == W_P25 ? 0.25 : 0.5;
                    w = (ph < duty ? 1 : -1) + Blep(ph, dt);
                    double p2 = ph - duty; if (p2 < 0) p2 += 1;
                    w -= Blep(p2, dt);
                    w -= 2 * duty - 1;   // 去掉直流
                }
                y += a * (w - y);
                buf[(s0 + i) % n] += (float)(y * env * amp);
                ph += dt; ph -= Math.Floor(ph);
            }
        }

        // ---- 鼓（噪声每次用同一个种子：每次合成出来都一样）
        static uint noise = 22222;
        static double Noise() { unchecked { noise = noise * 1664525 + 1013904223; } return (noise >> 8) / 8388608.0 - 1; }

        static void Kick(float[] buf, double t, double amp)
        {
            int s0 = (int)(t * Rate), n = buf.Length; double ph = 0;
            for (int i = 0; i < (int)(0.26 * Rate); i++)
            {
                double tt = (double)i / Rate, f = 48 + 75 * Math.Exp(-tt / 0.03);
                ph += f / Rate;
                buf[(s0 + i) % n] += (float)(Math.Sin(2 * Math.PI * ph) * Math.Exp(-tt / 0.11) * Math.Min(1, tt / 0.002) * amp);
            }
        }

        static void Snare(float[] buf, double t, double amp)
        {
            int s0 = (int)(t * Rate), n = buf.Length; double lp = 0;
            for (int i = 0; i < (int)(0.2 * Rate); i++)
            {
                double tt = (double)i / Rate, nz = Noise();
                lp += 0.5 * (nz - lp);
                double v = lp * Math.Exp(-tt / 0.08) * 0.9 + Math.Sin(2 * Math.PI * 185 * tt) * Math.Exp(-tt / 0.04) * 0.5;
                buf[(s0 + i) % n] += (float)(v * Math.Min(1, tt / 0.001) * amp);
            }
        }

        static void Hat(float[] buf, double t, double amp, double decay)
        {
            int s0 = (int)(t * Rate), n = buf.Length; double prev = 0;
            for (int i = 0; i < (int)(decay * 5 * Rate); i++)
            {
                double tt = (double)i / Rate, nz = Noise(), hp = nz - prev;   // 一阶差分：只留高频，像镲片
                prev = nz;
                buf[(s0 + i) % n] += (float)(hp * Math.Exp(-tt / decay) * amp);
            }
        }

        static byte[] Build()
        {
            noise = 22222;
            double beat = 60.0 / Bpm, bar = 4 * beat;
            int bars = 0;
            foreach (Sec s in Song) bars += s.Chords.Length;
            int n = (int)Math.Round(bars * bar * Rate);
            float[] mix = new float[n], lead = new float[n];
            int b0 = 0;
            foreach (Sec s in Song)
            {
                int nb = s.Chords.Length;
                for (int j = 0; j < nb; j++)
                {
                    double t0 = (b0 + j) * bar;
                    int root; int[] iv;
                    Chord(s.Chords[j], out root, out iv);
                    // 旋律（还有 A'' 里跟着旋律走的三度和声）
                    string[] tok = (j < s.Mel.Length ? s.Mel[j] : "r 4").Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    double tb = 0;
                    for (int k = 0; k + 1 < tok.Length; k += 2)
                    {
                        double d = double.Parse(tok[k + 1], System.Globalization.CultureInfo.InvariantCulture);
                        if (tb + d > 4) d = 4 - tb;
                        if (tok[k] != "r" && d > 0)
                        {
                            int m = Midi(tok[k]);
                            Tone(lead, m, t0 + tb * beat, d * beat * 0.92, 0.2, W_P25, 0.72, 3600, d >= 1, 0.006);
                            if (s.Harmony) { int h = Below(m, root, iv); if (h > 0) Tone(mix, h, t0 + tb * beat, d * beat * 0.9, 0.09, W_P50, 0.7, 2400, false, 0.006); }
                        }
                        tb += d;
                    }
                    // 对位（A'）：每小节两个二分音符，和弦的三音、五音
                    if (s.Counter)
                    {
                        List<int> v = Voicing(root, iv, 57, 3);
                        Tone(mix, v[1], t0, 2 * beat * 0.95, 0.08, W_P50, 0.8, 2000, false, 0.02);
                        Tone(mix, v[2], t0 + 2 * beat, 2 * beat * 0.95, 0.08, W_P50, 0.8, 2000, false, 0.02);
                    }
                    // 低音
                    int r = 36 + root;
                    while (r < 40) r += 12;
                    while (r > 51) r -= 12;
                    double[] bp;   // (相对根音的半音, 起拍, 拍数) 一组一组
                    switch (s.Bass)
                    {
                        case 0: bp = new double[] { 0, 0, 2, 7, 2, 2 }; break;
                        case 1: bp = new double[] { 0, 0, 1, 7, 1, .5, 12, 1.5, .5, 0, 2, 1, 7, 3, 1 }; break;
                        case 2: bp = new double[] { 0, 0, 2, 7, 2, 1, 12, 3, 1 }; break;
                        case 3: bp = new double[] { 0, 0, .5, 12, .5, .5, 0, 1, .5, 12, 1.5, .5, 0, 2, .5, 12, 2.5, .5, 0, 3, .5, 12, 3.5, .5 }; break;
                        default: bp = new double[] { 0, 0, 4 }; break;
                    }
                    for (int k = 0; k + 2 < bp.Length; k += 3)
                        Tone(mix, r + bp[k], t0 + bp[k + 1] * beat, bp[k + 2] * beat * (s.Bass == 3 ? 0.7 : 0.88), 0.3, W_TRI, 0.85, 0, false, 0.004);
                    // 伴奏
                    if (s.Acc == 0)
                    {
                        List<int> v = Voicing(root, iv, 55, 4);
                        int[] pat = { 0, 1, 2, 3, 2, 1, 2, 1 };
                        for (int k = 0; k < 8; k++) Tone(mix, v[pat[k]], t0 + k * beat / 2, beat / 2 * 0.8, 0.07, W_TRI, 0.5, 0, false, 0.004);
                    }
                    else if (s.Acc == 1)
                    {
                        List<int> v = Voicing(root, iv, 55, 3);
                        for (int k = 0; k < 4; k++)
                            foreach (int m in v) Tone(mix, m, t0 + (k + 0.5) * beat, beat * 0.22, 0.04, W_P50, 0.3, 1800, false, 0.003);
                    }
                    else
                    {
                        List<int> v = Voicing(root, iv, 57, 3);
                        foreach (int m in v) Tone(mix, m, t0, bar * 0.98, 0.05, W_TRI, 1.0, 0, false, 0.25);
                    }
                    // 鼓
                    switch (s.Drums)
                    {
                        case 1:
                            if (j >= 2) { Hat(mix, t0 + beat, 0.03, 0.03); Hat(mix, t0 + 3 * beat, 0.03, 0.03); }
                            break;
                        case 2: case 5:
                            Kick(mix, t0, 0.42); Kick(mix, t0 + 2 * beat, 0.38);
                            if (s.Drums == 5) Kick(mix, t0 + 2.5 * beat, 0.28);
                            Snare(mix, t0 + beat, 0.15); Snare(mix, t0 + 3 * beat, 0.15);
                            for (int k = 0; k < 8; k++) Hat(mix, t0 + k * beat / 2, k % 2 == 1 ? 0.05 : 0.035, 0.025);
                            if (s.Drums == 5 && j == 0) Hat(mix, t0, 0.08, 0.35);   // 回到主题：镲一声
                            break;
                        case 3:
                            Kick(mix, t0, 0.3);
                            for (int k = 0; k < 4; k++) Hat(mix, t0 + k * beat, 0.025, 0.03);
                            Snare(mix, t0 + 3 * beat, 0.05);
                            break;
                        case 4:
                            for (int k = 0; k < 4; k++) Kick(mix, t0 + k * beat, 0.36);
                            if (j < nb - 1) { Snare(mix, t0 + beat, 0.14); Snare(mix, t0 + 3 * beat, 0.14); }
                            else for (int k = 0; k < 8; k++) Snare(mix, t0 + 2 * beat + k * beat / 4, 0.05 + 0.015 * k);   // 最后半小节小鼓滚奏，越来越响
                            for (int k = 0; k < 8; k++) Hat(mix, t0 + k * beat / 2, 0.035, 0.025);
                            break;
                        case 6:
                            if (j == 0) { Kick(mix, t0, 0.4); Hat(mix, t0, 0.06, 0.4); }
                            break;
                    }
                }
                b0 += nb;
            }
            // 旋律加一点回声（附点八分音符那么远），让它不那么干
            int dly = (int)(0.75 * beat * Rate);
            for (int i = 0; i < n; i++)
            {
                float v = lead[i];
                if (v == 0) continue;
                mix[i] += v; mix[(i + dly) % n] += v * 0.22f; mix[(i + 2 * dly) % n] += v * 0.08f;
            }
            double peak = 1e-6;
            for (int i = 0; i < n; i++) peak = Math.Max(peak, Math.Abs(mix[i]));
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(ms))
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + n * 2); w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
                w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(n * 2);
                for (int i = 0; i < n; i++)
                {
                    double v = mix[i] / peak * 1.15;
                    v = v / (1 + Math.Abs(v) * 0.3);   // 轻轻压一下，不会破音
                    w.Write((short)Math.Round(Math.Max(-1, Math.Min(1, v)) * 29000));
                }
                w.Flush();
                return ms.ToArray();
            }
        }
    }

    // ------------------------------------------------------------------ 音效：8-bit 小声音，全在程序里现场合成（不带音频文件）
    // 用 waveOut 播：每个声音开一路短短的输出，播完就关（不一直占着声卡，也就不会拦着电脑睡眠），几个声音能叠在一起响，
    // 也不会打断梦里的背景音乐。合成、开声卡都在后台线程里做，界面不卡。
    static class Sfx
    {
        internal static bool On = true;   // 总开关：关掉就一声都不响（记在设置里）
        internal static int Level = 2;    // 音量：1 轻 2 中 3 响（记在设置里）
        internal static readonly string[] VolNames = { "轻", "中", "响" };   // 第 i 个是音量 i+1
        static readonly double[] Gains = { 0, 0.35, 0.7, 1.15 };
        internal static Action<string> Log;   // 调试日志（CLAWD_DEBUG=1）
        internal const int Rate = 22050;
        const int W_P25 = 0, W_P50 = 1, W_TRI = 2, W_SIN = 3, W_NOISE = 4;

        // 所有有名字的声音（--sfx 导出、试听用）
        internal static readonly string[] Names = {
            "boop", "grab", "land", "splat", "dizzy", "dance", "startle", "yawn", "sneeze", "pop", "boing", "clap", "hello", "bye",
            "done", "alert", "remind", "pomo", "rest", "kick", "shot", "bounce", "catch", "post", "whistle", "goal", "final",
            "plane", "clack", "d_jump", "d_spring", "d_coin", "d_stomp", "d_hurt", "d_fall", "d_win", "d_lose" };

        // ---------------- 合成
        static readonly Random rnd = new Random();
        static double Rnd() { return rnd.NextDouble() * 2 - 1; }
        static double Hz(double midi) { return 440 * Math.Pow(2, (midi - 69) / 12.0); }
        static float[] Buf(double secs) { return new float[(int)(secs * Rate)]; }

        static double Blep(double t, double dt)
        {
            if (t < dt) { t /= dt; return t + t - t * t - 1; }
            if (t > 1 - dt) { t = (t - 1) / dt; return t * t + t + t + 1; }
            return 0;
        }

        // 一个音：wave 波形；频率 f0 → f1（指数滑过去）；amp 音量；att 起音秒数；dec > 0 按 dec 秒指数衰减，否则一直平着；结尾几毫秒淡出；
        // vib 颤音深度（半音）、vr 颤音快慢（Hz）；lp 低通（Hz，0 = 不滤）。噪声跟红白机一样：每个周期换一个随机值，频率越高越「嘶」
        static void V(float[] b, double t0, double dur, int wave, double f0, double f1, double amp, double att, double dec, double vib, double vr, double lp)
        {
            int s0 = (int)(t0 * Rate), len = (int)(dur * Rate);
            double ph = 0, y = 0, hold = Rnd(), a = lp > 0 ? 1 - Math.Exp(-2 * Math.PI * lp / Rate) : 1, rel = Math.Min(0.008, dur * 0.3);
            for (int i = 0; i < len && s0 + i < b.Length; i++)
            {
                double t = (double)i / Rate, f = f0 * Math.Pow(f1 / f0, t / dur);
                if (vib > 0) f *= Math.Pow(2, vib / 12 * Math.Sin(2 * Math.PI * vr * t));
                double env = t < att ? t / att : (dec > 0 ? Math.Exp(-(t - att) / dec) : 1);
                if (dur - t < rel) env *= (dur - t) / rel;
                double dt = Math.Min(0.5, f / Rate), w;
                if (wave == W_TRI) w = 1 - 4 * Math.Abs(ph - 0.5);
                else if (wave == W_SIN) w = Math.Sin(2 * Math.PI * ph);
                else if (wave == W_NOISE) { if (ph + dt >= 1) hold = Rnd(); w = hold; }
                else
                {
                    double duty = wave == W_P25 ? 0.25 : 0.5;
                    w = (ph < duty ? 1 : -1) + Blep(ph, dt);
                    double p2 = ph - duty; if (p2 < 0) p2 += 1;
                    w -= Blep(p2, dt) + 2 * duty - 1;
                }
                y += a * (w - y);
                b[s0 + i] += (float)(y * env * amp);
                ph += dt; ph -= Math.Floor(ph);
            }
        }

        // 风铃一样的「叮」：正弦 + 高八度 + 一点三角波（有点红白机味），高的泛音先没
        static void Chime(float[] b, double t0, double f, double amp, double dec)
        {
            V(b, t0, dec * 5, W_SIN, f, f, amp, 0.002, dec, 0, 0, 0);
            V(b, t0, dec * 3, W_SIN, 2 * f, 2 * f, amp * 0.3, 0.002, dec * 0.45, 0, 0, 0);
            V(b, t0, dec * 2, W_TRI, f, f, amp * 0.18, 0.002, dec * 0.6, 0, 0, 0);
            V(b, t0, dec * 2, W_SIN, 3 * f, 3 * f, amp * 0.08, 0.001, dec * 0.25, 0, 0, 0);
        }

        // 弹簧「嘣~」：音高先大幅度来回晃，晃得越来越小
        static void Boing(float[] b, double t0, double dur, double f0, double amp)
        {
            int s0 = (int)(t0 * Rate), len = (int)(dur * Rate); double ph = 0;
            for (int i = 0; i < len && s0 + i < b.Length; i++)
            {
                double t = (double)i / Rate, f = f0 * (1 + 0.35 * t / dur) * (1 + 0.45 * Math.Exp(-t / 0.09) * Math.Sin(2 * Math.PI * 16 * t));
                double env = Math.Min(1, t / 0.003) * Math.Exp(-t / (dur * 0.35)) * Math.Min(1, (dur - t) / 0.01);
                b[s0 + i] += (float)((1 - 4 * Math.Abs(ph - 0.5)) * env * amp);
                ph += f / Rate; ph -= Math.Floor(ph);
            }
        }

        // 裁判哨：很高的正弦，哨子里的小球滚着，声音是一抖一抖的
        static void Whistle(float[] b, double t0, double dur, double amp)
        {
            int s0 = (int)(t0 * Rate), len = (int)(dur * Rate); double ph = 0;
            for (int i = 0; i < len && s0 + i < b.Length; i++)
            {
                double t = (double)i / Rate, tr = Math.Sin(2 * Math.PI * 34 * t), f = 2650 * (1 + 0.012 * tr);
                double env = Math.Min(1, t / 0.012) * Math.Min(1, (dur - t) / 0.03) * (0.72 + 0.28 * tr);
                b[s0 + i] += (float)((Math.Sin(2 * Math.PI * ph) + 0.06 * Rnd()) * env * amp);
                ph += f / Rate; ph -= Math.Floor(ph);
            }
        }

        // 拨弦（尤克里里）：Karplus-Strong，一圈噪声来回平均，越来越圆润、越来越小
        static float[] PluckBuf(double midi)
        {
            float[] b = Buf(0.9);
            int p = Math.Max(2, (int)Math.Round(Rate / Hz(midi)));
            float[] ring = new float[p];
            double lp = 0;
            for (int i = 0; i < p; i++) { lp += 0.6 * (Rnd() - lp); ring[i] = (float)lp; }   // 先滤一下：拨得软一点
            int idx = 0;
            for (int i = 0; i < b.Length; i++)
            {
                int nx = idx + 1 == p ? 0 : idx + 1;
                float cur = ring[idx];
                ring[idx] = 0.5f * (cur + ring[nx]) * 0.995f;
                idx = nx;
                double env = Math.Min(1, i / 30.0) * Math.Min(1, (b.Length - i) / 400.0);
                b[i] = (float)(cur * 0.5 * env);
            }
            return b;
        }

        // 聊天时的「叽里咕噜」：一个字一下，音高在几个音里乱跳（像动物在说话），最后一下往上挑或者往下落
        static float[] BabbleBuf(int n)
        {
            int[] steps = { 0, 2, 4, 5, 7, 9 };
            float[] b = Buf(n * 0.075 + 0.1);
            for (int i = 0; i < n; i++)
            {
                double f = Hz(74 + steps[rnd.Next(steps.Length)]), d = 0.05 + rnd.NextDouble() * 0.015;
                double f1 = i == n - 1 ? f * (rnd.Next(2) == 0 ? 1.15 : 0.88) : f * 1.04;
                V(b, i * 0.075, d, W_P50, f, f1, 0.16, 0.004, 0.04, 0, 0, 1900);
            }
            return b;
        }

        internal static float[] Make(string name)
        {
            float[] b;
            switch (name)
            {
                case "boop":   // 戳它一下：「啵」地往上一挑
                    b = Buf(0.12);
                    V(b, 0, 0.11, W_P50, 520, 860, 0.3, 0.002, 0.05, 0, 0, 2800);
                    V(b, 0, 0.09, W_SIN, 1040, 1720, 0.12, 0.002, 0.04, 0, 0, 0);
                    break;
                case "grab":   // 被拎起来：「咦！」两下往上
                    b = Buf(0.15);
                    V(b, 0, 0.05, W_P50, 700, 760, 0.22, 0.002, 0.05, 0, 0, 3000);
                    V(b, 0.05, 0.09, W_P50, 1040, 1200, 0.22, 0.002, 0.06, 0, 0, 3000);
                    break;
                case "land":   // 摔在地上：噗
                    b = Buf(0.16);
                    V(b, 0, 0.14, W_SIN, 160, 55, 0.6, 0.001, 0.05, 0, 0, 0);
                    V(b, 0, 0.06, W_NOISE, 2400, 600, 0.22, 0.001, 0.02, 0, 0, 1800);
                    break;
                case "splat":  // 糊在墙上：啪叽
                    b = Buf(0.3);
                    V(b, 0, 0.1, W_NOISE, 9000, 1500, 0.32, 0.001, 0.03, 0, 0, 4000);
                    V(b, 0, 0.18, W_SIN, 140, 50, 0.6, 0.001, 0.06, 0, 0, 0);
                    V(b, 0.04, 0.22, W_P50, 420, 140, 0.13, 0.004, 0.09, 0.6, 18, 1600);
                    break;
                case "dizzy":  // 晕了：头上的小鸟转圈「啾啾啾」
                    b = Buf(0.55);
                    for (int i = 0; i < 3; i++) V(b, i * 0.15, 0.11, W_SIN, 1900 - i * 200, 1550 - i * 200, 0.17, 0.004, 0.06, 0.9, 28, 0);
                    break;
                case "dance":  // 双击跳舞：一串往上的小琶音
                    {
                        b = Buf(0.5);
                        double[] ns = { 72, 76, 79, 84 };
                        for (int i = 0; i < 4; i++) V(b, i * 0.07, i == 3 ? 0.22 : 0.08, W_P50, Hz(ns[i]), Hz(ns[i]), 0.16, 0.002, i == 3 ? 0.12 : 0.05, 0, 0, 3200);
                        V(b, 0, 0.3, W_TRI, Hz(48), Hz(48), 0.25, 0.003, 0.14, 0, 0, 0);
                    }
                    break;
                case "startle": // 吓一跳：「哇！」
                    b = Buf(0.2);
                    V(b, 0, 0.17, W_P25, 620, 1500, 0.22, 0.002, 0.1, 0.4, 30, 3600);
                    break;
                case "yawn":   // 打哈欠：慢慢往下的一声「啊~」
                    b = Buf(0.9);
                    V(b, 0, 0.85, W_TRI, 620, 300, 0.2, 0.12, 0, 0.25, 5, 1500);
                    break;
                case "sneeze": // 打喷嚏：「哈——啾！」
                    b = Buf(0.62);
                    V(b, 0, 0.4, W_NOISE, 3000, 5000, 0.05, 0.3, 0, 0, 0, 2500);
                    V(b, 0.42, 0.16, W_NOISE, 14000, 6000, 0.36, 0.002, 0.04, 0, 0, 6000);
                    V(b, 0.42, 0.1, W_P50, 900, 500, 0.12, 0.002, 0.04, 0, 0, 2500);
                    break;
                case "pop":    // 鼻涕泡破了 / 从沙里蹦出来：啵
                    b = Buf(0.08);
                    V(b, 0, 0.06, W_SIN, 1300, 380, 0.36, 0.001, 0.025, 0, 0, 0);
                    V(b, 0, 0.012, W_NOISE, 12000, 12000, 0.12, 0.001, 0.004, 0, 0, 0);
                    break;
                case "boing":  // 跳到伙伴头上：弹簧「嘣~」
                    b = Buf(0.4);
                    Boing(b, 0, 0.38, 240, 0.3);
                    break;
                case "clap":   // 击掌：啪
                    b = Buf(0.12);
                    V(b, 0, 0.09, W_NOISE, 16000, 6000, 0.42, 0.0005, 0.018, 0, 0, 7000);
                    V(b, 0.012, 0.06, W_NOISE, 16000, 6000, 0.22, 0.0005, 0.015, 0, 0, 6000);
                    break;
                case "hello":  // 叫出来一只 / 叫到鼠标这儿：嘟噜噜~（往上）
                    b = Buf(0.4);
                    V(b, 0, 0.06, W_P50, Hz(76), Hz(76), 0.16, 0.002, 0.05, 0, 0, 3000);
                    V(b, 0.06, 0.06, W_P50, Hz(81), Hz(81), 0.16, 0.002, 0.05, 0, 0, 3000);
                    V(b, 0.12, 0.24, W_P50, Hz(88), Hz(88), 0.16, 0.002, 0.1, 0.2, 7, 3000);
                    break;
                case "bye":    // 送走：往下的三个音
                    b = Buf(0.45);
                    V(b, 0, 0.08, W_P50, Hz(84), Hz(84), 0.15, 0.002, 0.06, 0, 0, 3000);
                    V(b, 0.09, 0.08, W_P50, Hz(79), Hz(79), 0.15, 0.002, 0.06, 0, 0, 3000);
                    V(b, 0.18, 0.25, W_P50, Hz(72), Hz(72), 0.15, 0.002, 0.1, 0, 0, 3000);
                    break;
                case "done":   // Claude 干完活：叮叮叮叮~（往上走的琶音，最后一声长）
                    {
                        b = Buf(1.0);
                        double[] dn = { 79, 84, 88, 91 };
                        for (int i = 0; i < 4; i++) Chime(b, i * 0.075, Hz(dn[i]), i == 3 ? 0.22 : 0.15, i == 3 ? 0.18 : 0.08);
                    }
                    break;
                case "alert":  // Claude 在等你批准 / 久坐提醒：叮——咚（往上两声）
                    b = Buf(1.3);
                    Chime(b, 0, Hz(81), 0.24, 0.14);
                    Chime(b, 0.2, Hz(88), 0.26, 0.2);
                    break;
                case "remind": // 提醒到点：叮叮、叮叮（像小闹钟）
                    b = Buf(1.1);
                    for (int i = 0; i < 4; i++) Chime(b, i * 0.13 + (i >= 2 ? 0.22 : 0), Hz(88), 0.2, 0.07);
                    break;
                case "pomo":   // 专注完成：叮、叮、叮、叮~~（大三和弦往上，最后一声长）
                    b = Buf(1.6);
                    Chime(b, 0, Hz(72), 0.2, 0.1); Chime(b, 0.17, Hz(76), 0.2, 0.1); Chime(b, 0.34, Hz(79), 0.2, 0.12); Chime(b, 0.51, Hz(84), 0.24, 0.22);
                    break;
                case "rest":   // 休息结束：咚、叮（提醒回来干活）
                    b = Buf(1.2);
                    Chime(b, 0, Hz(72), 0.22, 0.12); Chime(b, 0.22, Hz(79), 0.24, 0.2);
                    break;
                case "kick":   // 踢球：嘭
                    b = Buf(0.1);
                    V(b, 0, 0.09, W_SIN, 260, 90, 0.5, 0.001, 0.03, 0, 0, 0);
                    V(b, 0, 0.02, W_NOISE, 9000, 4000, 0.18, 0.0005, 0.006, 0, 0, 5000);
                    break;
                case "shot":   // 大脚射门：嘭 + 呼
                    b = Buf(0.3);
                    V(b, 0, 0.12, W_SIN, 220, 70, 0.65, 0.001, 0.045, 0, 0, 0);
                    V(b, 0, 0.03, W_NOISE, 12000, 5000, 0.28, 0.0005, 0.008, 0, 0, 6000);
                    V(b, 0.02, 0.26, W_NOISE, 3000, 900, 0.1, 0.06, 0.08, 0, 0, 2200);
                    break;
                case "bounce": // 球落地弹一下：嘟
                    b = Buf(0.07);
                    V(b, 0, 0.06, W_SIN, 330, 160, 0.32, 0.001, 0.02, 0, 0, 0);
                    break;
                case "catch":  // 守门员抱住球：噗
                    b = Buf(0.1);
                    V(b, 0, 0.08, W_SIN, 180, 80, 0.45, 0.001, 0.03, 0, 0, 0);
                    V(b, 0, 0.05, W_NOISE, 3000, 1200, 0.18, 0.001, 0.015, 0, 0, 2000);
                    break;
                case "post":   // 打在门柱上：当~
                    b = Buf(0.6);
                    V(b, 0, 0.08, W_SIN, 240, 90, 0.38, 0.001, 0.03, 0, 0, 0);
                    foreach (double p in new double[] { 1180, 1777, 2630 }) V(b, 0, 0.55, W_SIN, p, p * 0.995, 0.14, 0.001, 0.14, 0, 0, 0);
                    break;
                case "whistle": // 开球：一声「嘟——」
                    b = Buf(0.55);
                    Whistle(b, 0, 0.5, 0.17);
                    break;
                case "final":  // 终场：嘟、嘟、嘟——
                    b = Buf(1.25);
                    Whistle(b, 0, 0.16, 0.17); Whistle(b, 0.26, 0.16, 0.17); Whistle(b, 0.52, 0.7, 0.17);
                    break;
                case "goal":   // 进球：号角一样往上的短旋律，底下观众「哗」一下
                    {
                        b = Buf(1.1);
                        double[] gn = { 67, 72, 76, 79 }, gt = { 0, 0.09, 0.18, 0.27 }, gd = { 0.08, 0.08, 0.08, 0.62 };
                        for (int i = 0; i < 4; i++)
                        {
                            V(b, gt[i], gd[i], W_P25, Hz(gn[i]), Hz(gn[i]), 0.17, 0.004, i == 3 ? 0 : 0.06, i == 3 ? 0.15 : 0, 6, 3600);
                            V(b, gt[i], gd[i], W_P50, Hz(gn[i] - 5), Hz(gn[i] - 5), 0.07, 0.004, i == 3 ? 0.4 : 0.06, 0, 0, 2400);
                        }
                        V(b, 0.27, 0.62, W_TRI, Hz(48), Hz(48), 0.28, 0.004, 0.3, 0, 0, 0);
                        V(b, 0.05, 0.95, W_NOISE, 1500, 900, 0.06, 0.25, 0.35, 0, 0, 1300);
                    }
                    break;
                case "plane":  // 纸飞机扔出去：咻~
                    b = Buf(0.6);
                    V(b, 0, 0.55, W_NOISE, 4500, 1600, 0.13, 0.12, 0.18, 0, 0, 3000);
                    break;
                case "clack":  // 竹竿靠在窗角上：哒
                    b = Buf(0.12);
                    V(b, 0, 0.05, W_TRI, 900, 600, 0.36, 0.0005, 0.015, 0, 0, 0);
                    V(b, 0, 0.03, W_NOISE, 6000, 3000, 0.22, 0.0005, 0.008, 0, 0, 4000);
                    V(b, 0.035, 0.05, W_TRI, 800, 560, 0.18, 0.0005, 0.012, 0, 0, 0);
                    break;
                // ---- 梦里的小游戏
                case "d_jump":
                    b = Buf(0.15);
                    V(b, 0, 0.14, W_P50, 330, 800, 0.17, 0.002, 0.1, 0, 0, 3600);
                    break;
                case "d_spring":
                    b = Buf(0.35);
                    Boing(b, 0, 0.33, 330, 0.26);
                    break;
                case "d_coin": // 收集到东西：叮叮（两个音往上）
                    b = Buf(0.32);
                    V(b, 0, 0.06, W_P50, Hz(83), Hz(83), 0.15, 0.001, 0, 0, 0, 4000);
                    V(b, 0.06, 0.24, W_P50, Hz(88), Hz(88), 0.15, 0.001, 0.09, 0, 0, 4000);
                    break;
                case "d_stomp": // 踩扁 bug：噗叽
                    b = Buf(0.15);
                    V(b, 0, 0.12, W_P50, 600, 150, 0.2, 0.001, 0.05, 0, 0, 2000);
                    V(b, 0, 0.05, W_NOISE, 4000, 1500, 0.18, 0.001, 0.015, 0, 0, 3000);
                    break;
                case "d_hurt": // 被 bug 碰到：哎呀（往下抖的一声）
                    b = Buf(0.35);
                    V(b, 0, 0.32, W_P25, 700, 220, 0.18, 0.002, 0.15, 0.8, 16, 2600);
                    break;
                case "d_fall": // 掉下去：咻——往下
                    b = Buf(0.5);
                    V(b, 0, 0.45, W_TRI, 900, 200, 0.2, 0.005, 0.25, 0, 0, 0);
                    break;
                case "d_win":  // 睡得好香：小段过关的旋律
                    {
                        b = Buf(1.3);
                        double[] wn = { 72, 76, 79, 84, 79, 84 }, wt = { 0, 0.1, 0.2, 0.3, 0.45, 0.55 }, wd = { 0.09, 0.09, 0.09, 0.13, 0.09, 0.6 };
                        for (int i = 0; i < wn.Length; i++) V(b, wt[i], wd[i], W_P25, Hz(wn[i]), Hz(wn[i]), 0.15, 0.003, i == 5 ? 0.35 : 0.08, i == 5 ? 0.15 : 0, 6, 3600);
                        V(b, 0, 0.3, W_TRI, Hz(48), Hz(48), 0.25, 0.003, 0.2, 0, 0, 0);
                        V(b, 0.55, 0.6, W_TRI, Hz(43), Hz(43), 0.25, 0.003, 0.3, 0, 0, 0);
                    }
                    break;
                case "d_lose": // 被吓醒：往下滑的三声「哇哇哇~」
                    b = Buf(1.2);
                    V(b, 0, 0.22, W_P50, Hz(67), Hz(66), 0.15, 0.004, 0.2, 0, 0, 2400);
                    V(b, 0.25, 0.22, W_P50, Hz(66), Hz(65), 0.15, 0.004, 0.2, 0, 0, 2400);
                    V(b, 0.5, 0.65, W_P50, Hz(65), Hz(63), 0.15, 0.004, 0.5, 0.5, 6, 2400);
                    break;
                default: b = new float[0]; break;
            }
            int fade = Math.Min(b.Length, Rate / 100);   // 最后 10 毫秒淡出：没响完的尾巴也不会「咔」一下截断
            for (int i = 0; i < fade; i++) b[b.Length - 1 - i] *= (float)i / fade;
            return b;
        }

        // ---------------- 播放
        [StructLayout(LayoutKind.Sequential)]
        struct WAVEFORMATEX { public short wFormatTag, nChannels; public int nSamplesPerSec, nAvgBytesPerSec; public short nBlockAlign, wBitsPerSample, cbSize; }
        [StructLayout(LayoutKind.Sequential)]
        struct WAVEHDR { public IntPtr lpData; public int dwBufferLength, dwBytesRecorded; public IntPtr dwUser; public int dwFlags, dwLoops; public IntPtr lpNext, reserved; }
        [DllImport("winmm.dll")] static extern int waveOutOpen(out IntPtr h, uint dev, ref WAVEFORMATEX f, IntPtr cb, IntPtr inst, int flags);
        [DllImport("winmm.dll")] static extern int waveOutPrepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutWrite(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutUnprepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutReset(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveOutClose(IntPtr h);

        class Req { public string Name; public Func<float[]> Build; public double Gain, Semis; }
        class Voice { public IntPtr Dev, Hdr, Data; public double Until; }

        static readonly object gate = new object();
        static readonly Queue<Req> pending = new Queue<Req>();
        static readonly Dictionary<string, float[]> bank = new Dictionary<string, float[]>();
        static readonly Dictionary<string, double> lastPlay = new Dictionary<string, double>();
        static readonly List<Voice> voices = new List<Voice>();
        static readonly Stopwatch clock = Stopwatch.StartNew();
        static readonly AutoResetEvent wake = new AutoResetEvent(false);
        static Thread worker;
        static readonly int HdrSize = Marshal.SizeOf(typeof(WAVEHDR));
        static readonly int FlagsOff = (int)Marshal.OffsetOf(typeof(WAVEHDR), "dwFlags");
        internal static int Opened, Failed, LastError;

        // 同一个声音隔多久才能再响（四只一起落地、球连着弹，只响一下）
        static double MinGap(string name)
        {
            switch (name)
            {
                case "kick": case "catch": return 0.07;
                case "bounce": case "land": return 0.1;
                case "babble": case "pluck": return 0.12;
                case "done": case "alert": case "remind": case "pomo": case "rest": case "goal": case "final": case "whistle": return 0.8;
                default: return 0.045;
            }
        }

        // gain：0..1 再乘总音量；semis：升降几个半音（每只的嗓音高低不一样）
        internal static void Play(string name, double gain, double semis) { Enqueue(name, null, gain, semis); }
        internal static void Pluck(double midi, double gain) { Enqueue("pluck", () => PluckBuf(midi), gain, 0); }
        internal static void Babble(int syllables, double semis, double gain) { int n = Math.Max(2, Math.Min(7, syllables)); Enqueue("babble", () => BabbleBuf(n), gain, semis); }

        static void Enqueue(string name, Func<float[]> build, double gain, double semis)
        {
            if (!On || Level <= 0 || gain <= 0) return;
            double t = clock.Elapsed.TotalSeconds, lt;
            lock (gate)
            {
                if (lastPlay.TryGetValue(name, out lt) && t - lt < MinGap(name)) return;
                if (pending.Count > 8) return;
                lastPlay[name] = t;
                Req q = new Req(); q.Name = name; q.Build = build; q.Gain = gain; q.Semis = semis;
                pending.Enqueue(q);
                if (worker == null)
                {
                    worker = new Thread(Run);
                    worker.IsBackground = true; worker.Name = "ClawdSfx";
                    worker.Start();
                }
            }
            wake.Set();
            if (Log != null) Log("sfx " + name + (semis != 0 ? " " + semis.ToString("+0;-0") : "") + (gain != 1 ? " x" + gain.ToString("F2") : ""));
        }

        static void Run()
        {
            while (true)
            {
                wake.WaitOne(voices.Count > 0 ? 40 : 5000);
                Req[] reqs;
                lock (gate) { reqs = pending.ToArray(); pending.Clear(); }
                foreach (Req q in reqs) { try { if (voices.Count < 10) Start(Render(q)); } catch { } }
                Reap(false);
            }
        }

        // 取出声音（第一次用到才合成，之后直接用），按音高重采样、乘音量，变成 16 位
        static short[] Render(Req q)
        {
            float[] src;
            if (q.Build != null) src = q.Build();
            else if (!bank.TryGetValue(q.Name, out src)) { src = Make(q.Name); bank[q.Name] = src; }
            double ratio = Math.Pow(2, q.Semis / 12.0), g = Gains[Math.Max(0, Math.Min(3, Level))] * q.Gain;
            int n = Math.Max(0, (int)((src.Length - 1) / ratio));
            short[] pcm = new short[n];
            for (int i = 0; i < n; i++)
            {
                double x = i * ratio; int i0 = (int)x; double fr = x - i0;
                double v = (src[i0] * (1 - fr) + src[Math.Min(src.Length - 1, i0 + 1)] * fr) * g;
                v = v / (1 + Math.Abs(v) * 0.2);   // 轻轻压一下，几个声音叠在一起也不会破音
                pcm[i] = (short)Math.Round(Math.Max(-1, Math.Min(1, v)) * 32000);
            }
            return pcm;
        }

        static void Start(short[] pcm)
        {
            if (pcm.Length == 0) return;
            WAVEFORMATEX f = new WAVEFORMATEX();
            f.wFormatTag = 1; f.nChannels = 1; f.nSamplesPerSec = Rate; f.wBitsPerSample = 16; f.nBlockAlign = 2; f.nAvgBytesPerSec = Rate * 2;
            IntPtr dev;
            int r = waveOutOpen(out dev, 0xFFFFFFFF, ref f, IntPtr.Zero, IntPtr.Zero, 0);   // 0xFFFFFFFF = 系统默认的输出设备
            if (r != 0) { Failed++; LastError = r; return; }
            Opened++;
            Voice v = new Voice();
            v.Dev = dev;
            v.Data = Marshal.AllocHGlobal(pcm.Length * 2);
            Marshal.Copy(pcm, 0, v.Data, pcm.Length);
            WAVEHDR h = new WAVEHDR(); h.lpData = v.Data; h.dwBufferLength = pcm.Length * 2;
            v.Hdr = Marshal.AllocHGlobal(HdrSize);
            Marshal.StructureToPtr(h, v.Hdr, false);
            v.Until = clock.Elapsed.TotalSeconds + (double)pcm.Length / Rate + 3;
            voices.Add(v);
            if (waveOutPrepareHeader(dev, v.Hdr, HdrSize) != 0 || waveOutWrite(dev, v.Hdr, HdrSize) != 0) v.Until = 0;
        }

        // 播完了的关掉（WHDR_DONE），万一卡住了到时间也关
        static void Reap(bool all)
        {
            double t = clock.Elapsed.TotalSeconds;
            for (int i = voices.Count - 1; i >= 0; i--)
            {
                Voice v = voices[i];
                if (!all && (Marshal.ReadInt32(v.Hdr, FlagsOff) & 1) == 0 && t < v.Until) continue;
                try { waveOutReset(v.Dev); waveOutUnprepareHeader(v.Dev, v.Hdr, HdrSize); waveOutClose(v.Dev); } catch { }
                Marshal.FreeHGlobal(v.Hdr); Marshal.FreeHGlobal(v.Data);
                voices.RemoveAt(i);
            }
        }

        // 存成 WAV（--sfx 导出试听用）
        internal static byte[] Wav(float[] src, double gain)
        {
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(ms))
            {
                int n = src.Length;
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + n * 2); w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
                w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(n * 2);
                for (int i = 0; i < n; i++) { double v = src[i] * gain; v = v / (1 + Math.Abs(v) * 0.2); w.Write((short)Math.Round(Math.Max(-1, Math.Min(1, v)) * 32000)); }
                w.Flush();
                return ms.ToArray();
            }
        }

        // --sfx <目录>：每个声音存一个 WAV，再把它们连成一个 all.wav（中间空一下）；顺便打印每个声音多长、最响多少
        internal static void Export(string dir)
        {
            Directory.CreateDirectory(dir);
            List<float> all = new List<float>();
            StringBuilder sb = new StringBuilder();
            List<string> names = new List<string>(Names);
            names.Add("pluck"); names.Add("babble");
            foreach (string nm in names)
            {
                float[] b = nm == "pluck" ? PluckBuf(69) : nm == "babble" ? BabbleBuf(5) : Make(nm);
                double peak = 0; bool bad = false;
                foreach (float x in b) { if (float.IsNaN(x) || float.IsInfinity(x)) bad = true; else peak = Math.Max(peak, Math.Abs(x)); }
                sb.AppendFormat("{0,-9} {1,5:F2}s peak={2:F2}{3}\r\n", nm, (double)b.Length / Rate, peak, bad ? " NaN!" : "");
                File.WriteAllBytes(Path.Combine(dir, nm + ".wav"), Wav(b, Gains[2]));
                all.AddRange(b); all.AddRange(new float[(int)(0.35 * Rate)]);
            }
            File.WriteAllBytes(Path.Combine(dir, "all.wav"), Wav(all.ToArray(), Gains[2]));
            File.WriteAllText(Path.Combine(dir, "sfx.txt"), sb.ToString());
        }

        // --sfx-test：真的用声卡放几个声音，等放完，返回打开成功 / 失败几次（看声卡这条路通不通）
        internal static string SelfTest()
        {
            foreach (string nm in new[] { "boop", "done", "kick" }) { Play(nm, 0.6, 0); Thread.Sleep(450); }
            Thread.Sleep(1500);
            return "opened=" + Opened + " failed=" + Failed + " err=" + LastError + " live=" + voices.Count;
        }
    }

    // ------------------------------------------------------------------ 梦境小游戏：在 Clawd 的梦里横版跑跳
    // ← → / A D 走，空格 / ↑ 跳（按久一点跳得高）；收集它梦到的东西、踩扁 bug、跑到枕头那儿就睡个好觉。
    // 画面是 256x128 的像素小画布（1 像素 = Clawd 的 1 格），再整数倍放大。结束时把结果（win / lose / quit）告诉桌宠。
    class DreamForm : Form
    {
        // ---- 顺滑：游戏循环跟着显示器刷新走（DwmFlush 等下一帧），画面用 GDI 的 StretchDIBits 直接放大到窗口上
        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }
        [StructLayout(LayoutKind.Sequential)]
        struct NATIVEMSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int ptX, ptY; }
        [DllImport("user32.dll")] static extern bool PeekMessage(out NATIVEMSG m, IntPtr hwnd, uint min, uint max, uint remove);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
        [DllImport("gdi32.dll")] static extern int SetStretchBltMode(IntPtr hdc, int mode);
        [DllImport("gdi32.dll")] static extern int StretchDIBits(IntPtr hdc, int xd, int yd, int wd, int hd, int xs, int ys, int ws, int hs, int[] bits, ref BITMAPINFOHEADER bmi, uint usage, uint rop);
        [DllImport("dwmapi.dll")] static extern int DwmFlush();
        BITMAPINFOHEADER bmi;
        bool loopOn; int msgFrames;

        const int DW = 256, DH = 128;
        const int MaxHearts = 5;   // 原来 3 颗；掉下去也不再扣心
        public static bool MusicOn = true;   // M 键开关，记在设置里
        System.Media.SoundPlayer music;

        void StartMusic()
        {
            if (!MusicOn || music != null || finished) return;
            if (!DreamMusic.Ready)
            {
                // 第一次放：在后台合成（要一两秒），好了再回到界面线程开始放，打开游戏不卡
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { DreamMusic.Wav(); if (!finished && IsHandleCreated) BeginInvoke((Action)StartMusic); } catch { }
                });
                return;
            }
            try { music = new System.Media.SoundPlayer(new MemoryStream(DreamMusic.Wav())); music.PlayLooping(); }
            catch { music = null; }
        }

        void StopMusic()
        {
            if (music == null) return;
            try { music.Stop(); music.Dispose(); } catch { }
            music = null;
        }
        static readonly string[] SkyHex = { "#141833", "#191D3D", "#1F2248", "#262753", "#2E2C5D", "#373167", "#3F3670", "#473B78" };
        static readonly string[][] BugF = {
            new string[] { "..D.D..", ".GGGGG.", "GGWGWGG", "GGGGGGG", ".D...D." },
            new string[] { "..D.D..", ".GGGGG.", "GGWGWGG", "GGGGGGG", "D..D..D" } };
        static readonly string[] BugFlat = { ".GGGGG.", "GGGGGGG" };
        static readonly Art BugPal = new Art("bug", BugF[0], "G=#5BAF62,D=#2C3A2E,W=#FFFFFF");
        static readonly Art ZPal = new Art("z", Sprite.Zed, "X=#4B4786");
        static readonly Art ZGoal = new Art("z2", Sprite.Zed, "X=#C9C6F0");
        static readonly Color Bg = Color.FromArgb(0x1B, 0x1E, 0x3A);

        class Seg { public double X, W, Y; public Seg(double x, double w, double y) { X = x; W = w; Y = y; } }
        class Item { public double X, Y, Ph; public bool Got; }
        class Bug { public double X, Y, MinX, MaxX, Flat; public int Dir; public bool Alive = true; }
        class Spring { public double X, Y, Sq; }
        class Bit { public double X, Y, VX, VY, Life; public int Col; }
        class Star { public double X, Y, Ph; public bool B; }

        readonly Art theme;
        readonly Action<string> done;
        readonly float dpi;
        readonly Random rnd = new Random();
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly int[] buf = new int[DW * DH];
        readonly Bitmap off = new Bitmap(DW, DH, PixelFormat.Format32bppArgb);
        readonly Dictionary<long, int[]> poseCache = new Dictionary<long, int[]>();
        readonly int[] sky = new int[8];
        readonly Font fTitle, fHud, fMsg, fBtn, fTip;
        readonly int k, M, headH, padH;
        readonly Rectangle stage, bL, bR, bJ, tipR;
        readonly List<KeyValuePair<Rectangle, string>> msgRects = new List<KeyValuePair<Rectangle, string>>();

        // 关卡
        uint seed;
        readonly List<Seg> ground = new List<Seg>(), clouds = new List<Seg>();
        readonly List<Item> items = new List<Item>();
        readonly List<Bug> bugs = new List<Bug>();
        readonly List<Spring> springs = new List<Spring>();
        double goalX, goalY, len;
        // 玩家
        double px, py, pvx, pvy, coyote, inv, pdist, cpX, cpY; bool pground; int pfacing = 1;
        double cam, t; int hearts, got, total, stomped; string over;
        readonly List<Bit> bits = new List<Bit>();
        Star[] stars; double[][] far, zs;
        // 提示框 / 按键
        bool msgUp; string msgText; string[] msgLabels, msgActs; string pressedMsg;
        bool kL, kR, kJ; double jumpBuf; string padDown;
        double lastT; bool finished;
        string lastHud = "";

        public DreamForm(Art theme, float dpi, Action<string> done)
        {
            this.theme = theme; this.dpi = dpi; this.done = done;
            Text = "Clawd 的梦 · " + theme.Name;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Bg;
            KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            try { Icon = Sprite.MakeIcon(SystemInformation.SmallIconSize.Width); } catch { }
            for (int i = 0; i < 8; i++) sky[i] = ColorTranslator.FromHtml(SkyHex[i]).ToArgb();
            fTitle = new Font("Microsoft YaHei UI", 14f * dpi, FontStyle.Bold, GraphicsUnit.Pixel);
            fHud = new Font("Microsoft YaHei UI", 13f * dpi, FontStyle.Regular, GraphicsUnit.Pixel);
            fMsg = new Font("Microsoft YaHei UI", 15f * dpi, FontStyle.Regular, GraphicsUnit.Pixel);
            fBtn = new Font("Microsoft YaHei UI", 15f * dpi, FontStyle.Regular, GraphicsUnit.Pixel);
            fTip = new Font("Microsoft YaHei UI", 12f * dpi, FontStyle.Regular, GraphicsUnit.Pixel);

            Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            k = Math.Max(2, Math.Min((int)(wa.Width * 0.78 / DW), (int)(wa.Height * 0.62 / DH)));
            M = (int)(10 * dpi); headH = (int)(28 * dpi); padH = (int)(38 * dpi);
            stage = new Rectangle(M, M + headH, DW * k, DH * k);
            int py0 = stage.Bottom + (int)(10 * dpi);
            bL = new Rectangle(M, py0, (int)(50 * dpi), padH);
            bR = new Rectangle(bL.Right + (int)(8 * dpi), py0, (int)(50 * dpi), padH);
            bJ = new Rectangle(stage.Right - (int)(84 * dpi), py0, (int)(84 * dpi), padH);
            tipR = new Rectangle(bR.Right + (int)(12 * dpi), py0, Math.Max(10, bJ.Left - bR.Right - (int)(24 * dpi)), padH);
            ClientSize = new Size(stage.Right + M, py0 + padH + M);

            seed = (uint)rnd.Next();
            ResetRun();
            ShowMsg("Clawd 梦到了" + theme.Name + "……\n帮它把梦里的" + theme.Name + "收起来，踩扁捣乱的 bug，\n跑到最后的枕头那儿，它就能睡个好觉。\n（掉下去没关系，会回到刚才站的地方）",
                new string[] { "开始做梦", "让它接着睡" }, new string[] { "start", "quit" });
            Render();

            bmi = new BITMAPINFOHEADER();
            bmi.biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER)); bmi.biWidth = DW; bmi.biHeight = -DH;   // 负数 = 从上往下
            bmi.biPlanes = 1; bmi.biBitCount = 32; bmi.biCompression = 0;
            // 备用：万一 DwmFlush 用不了（比如远程桌面），退回到计时器
            timer.Interval = 15;
            timer.Tick += (s, e) => { if (!loopOn) Step(); };
            Shown += (s, e) =>
            {
                lastT = clock.Elapsed.TotalSeconds;
                loopOn = true; Application.Idle += OnIdle;
                timer.Start();
                if (benchOut == null) StartMusic();
                try { Native.SetForegroundWindow(Handle); Activate(); } catch { }
            };
            Deactivate += (s, e) => { padDown = null; };
        }

        // 消息队列空着的时候一直跑：每跑一帧就等显示器下一次刷新，帧间隔稳定；有消息（点击、按键、桌宠的计时器）就先让出去
        void OnIdle(object sender, EventArgs e)
        {
            NATIVEMSG m;
            while (loopOn && !finished && !PeekMessage(out m, IntPtr.Zero, 0, 0, 0))
            {
                Step();
                if (finished) break;
                if (WindowState == FormWindowState.Minimized || !Visible) { Thread.Sleep(30); continue; }
                if (DwmFlush() != 0) { loopOn = false; break; }   // 合成没开：交给计时器
            }
        }

        void StopLoop()
        {
            if (!loopOn) return;
            loopOn = false;
            Application.Idle -= OnIdle;
        }

        // ---- 测流畅度：自动玩 6 秒，统计帧间隔
        string benchOut; double benchStart = -1, benchLast = -1; readonly List<double> benchGaps = new List<double>();
        public void Bench(string outFile) { benchOut = outFile; }
        void BenchTick()
        {
            if (benchOut == null) return;
            double nowT = clock.Elapsed.TotalSeconds;
            if (benchStart < 0) { benchStart = nowT; Act("start"); }
            if (benchLast >= 0 && !msgUp) benchGaps.Add((nowT - benchLast) * 1000);
            benchLast = nowT;
            padDown = ((int)((nowT - benchStart) / 0.7) % 3 == 2) ? "J" : "R";   // 一直往右跑，时不时跳一下
            if (msgUp && over != null) { Act("again"); }
            if (nowT - benchStart > 6)
            {
                benchGaps.Sort();
                int n = benchGaps.Count;
                double avg = 0; foreach (double v in benchGaps) avg += v; avg = n > 0 ? avg / n : 0;
                string rep = "frames=" + n + " avg=" + avg.ToString("0.0") + "ms p50=" + (n > 0 ? benchGaps[n / 2] : 0).ToString("0.0") +
                    " p95=" + (n > 0 ? benchGaps[(int)(n * 0.95)] : 0).ToString("0.0") + " max=" + (n > 0 ? benchGaps[n - 1] : 0).ToString("0.0") +
                    " over20ms=" + benchGaps.FindAll(v => v > 20).Count + " loop=" + (loopOn ? "vsync" : "timer") + " stage=" + stage.Width + "x" + stage.Height;
                try { File.WriteAllText(benchOut, rep); } catch { }
                benchOut = null;
                Finish("quit");
            }
        }

        // 把 256x128 的画面最近邻放大，直接画到窗口的游戏区域上（不走 GDI+、不经过双缓冲）
        void BlitStage(IntPtr hdc)
        {
            SetStretchBltMode(hdc, 3);   // COLORONCOLOR：像素放大不糊
            StretchDIBits(hdc, stage.X, stage.Y, stage.Width, stage.Height, 0, 0, DW, DH, buf, ref bmi, 0, 0x00CC0020);
        }
        void BlitNow()
        {
            if (!IsHandleCreated) return;
            IntPtr hdc = GetDC(Handle);
            if (hdc == IntPtr.Zero) return;
            try { BlitStage(hdc); } finally { ReleaseDC(Handle, hdc); }
        }

        static int Rnd(double v) { return (int)Math.Floor(v + 0.5); }
        static double Clamp(double v, double a, double b) { return v < a ? a : (v > b ? b : v); }
        static int Hex(string h) { return ColorTranslator.FromHtml(h).ToArgb(); }

        // mulberry32：同一个种子生成同一关
        static Func<double> Seeded(uint seed)
        {
            uint a = seed;
            return () =>
            {
                unchecked
                {
                    a = a + 0x6D2B79F5;
                    uint tt = a;
                    tt = (tt ^ (tt >> 15)) * (tt | 1);
                    tt ^= tt + (tt ^ (tt >> 7)) * (tt | 61);
                    return (tt ^ (tt >> 14)) / 4294967296.0;
                }
            };
        }

        // 随机生成一关：一段段云朵地面（中间有缺口）、飘着的云、要收集的东西、bug、弹簧球、终点的枕头
        void GenLevel(uint sd)
        {
            Func<double> R = Seeded(sd);
            ground.Clear(); clouds.Clear(); items.Clear(); bugs.Clear(); springs.Clear();
            Action<double, double> item = (x0, y0) => { Item it = new Item(); it.X = x0; it.Y = y0; it.Ph = R() * 6; items.Add(it); };
            ground.Add(new Seg(-40, 190, 104));
            double x = 150, y = 104;
            while (x < 1900)   // 关卡短一点（原来 2500）
            {
                double gap = Rnd(10 + R() * 14);   // 缺口窄一点（原来 14~34），高低差也小一点
                double ny = Clamp(y + (R() < 0.3 ? -8 : R() < 0.5 ? 8 : 0), 90, 112);
                for (int kk = 1; kk <= 3; kk++) item(x + gap * kk / 4, Math.Min(y, ny) - 14 - Math.Sin(kk / 4.0 * Math.PI) * 10);   // 缺口上方一道弧
                x += gap;
                double w = 56 + Math.Floor(R() * 110);
                ground.Add(new Seg(x, w, ny));
                if (R() < 0.55)
                {
                    // 上面飘一朵云，云上有东西
                    double cw = 28 + Math.Floor(R() * 26), cy = ny - 25 - Math.Floor(R() * 4);
                    double cxp = x + Math.Floor(R() * Math.Max(1, w - cw));
                    clouds.Add(new Seg(cxp, cw, cy));
                    for (int kk = 0; kk < Math.Floor(cw / 12); kk++) item(cxp + 6 + kk * 12, cy - 8);
                    if (R() < 0.4 && w > 70)
                    {
                        // 再高一层：得踩弹簧球才上得去
                        double c2x = cxp + (R() < 0.5 ? -12 : 12), c2y = cy - 36;
                        clouds.Add(new Seg(c2x, 32, c2y));
                        for (int kk = 0; kk < 3; kk++) item(c2x + 6 + kk * 10, c2y - 8);
                        double sx = cxp - x > 30 ? cxp - 14 : cxp + cw + 14;
                        if (sx > x + 6 && sx < x + w - 6) { Spring sp = new Spring(); sp.X = sx; sp.Y = ny; springs.Add(sp); }
                    }
                }
                else if (w > 70) for (int kk = 0; kk < 3; kk++) item(x + w / 2 - 10 + kk * 10, ny - 12);
                if (w > 90 && R() < 0.45)   // bug 少一点（原来 65%）
                {
                    Bug b = new Bug(); b.X = x + w / 2; b.Y = ny; b.MinX = x + 6; b.MaxX = x + w - 6; b.Dir = R() < 0.5 ? -1 : 1;
                    bugs.Add(b);
                }
                x += w; y = ny;
            }
            x += 22;
            ground.Add(new Seg(x, 160, 104));
            goalX = x + 84; goalY = 104;
            len = x + 160;
        }

        void ResetRun()
        {
            GenLevel(seed);
            px = 30; py = 104; pvx = pvy = 0; pground = true; pfacing = 1; coyote = 0; inv = 0; pdist = 0; cpX = 30; cpY = 104;
            cam = 0; hearts = MaxHearts; got = 0; total = items.Count; stomped = 0; t = 0; bits.Clear(); over = null; jumpBuf = 0;
            stars = new Star[46];
            for (int i = 0; i < stars.Length; i++) { Star s = new Star(); s.X = rnd.NextDouble() * DW; s.Y = rnd.NextDouble() * 74; s.Ph = rnd.NextDouble() * 6; s.B = rnd.NextDouble() < 0.25; stars[i] = s; }
            far = new double[8][];
            for (int i = 0; i < 8; i++) far[i] = new double[] { i * 70 + rnd.NextDouble() * 30, 62 + rnd.NextDouble() * 22, 36 + rnd.NextDouble() * 30 };
            zs = new double[4][];
            for (int i = 0; i < 4; i++) zs[i] = new double[] { rnd.NextDouble() * DW, 30 + rnd.NextDouble() * 60, 3 + rnd.NextDouble() * 4 };
        }

        void ShowMsg(string text, string[] labels, string[] acts)
        {
            msgText = text; msgLabels = labels; msgActs = acts; msgUp = true;
            Invalidate();
        }

        void Act(string a)
        {
            if (a == "start" || a == "again")
            {
                if (a == "again") { seed = (uint)rnd.Next(); ResetRun(); }
                msgUp = false; jumpBuf = 0;
                Invalidate();
            }
            else if (a == "quit") Finish("quit");
            else if (a == "wake") Finish(over ?? "quit");
        }

        void Finish(string result)
        {
            if (finished) return;
            finished = true;
            timer.Stop(); StopLoop(); StopMusic();
            try { done(result); } catch { }
            Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Stop(); StopLoop(); StopMusic();
            if (!finished) { finished = true; try { done(over ?? "quit"); } catch { } }
            base.OnFormClosed(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys kc = keyData & Keys.KeyCode;
            if (kc == Keys.Escape) { Finish(over ?? "quit"); return true; }
            if (kc == Keys.Enter) { if (msgUp) Act(msgActs[0]); return true; }
            if (kc == Keys.M) { MusicOn = !MusicOn; if (MusicOn) StartMusic(); else StopMusic(); Invalidate(); return true; }   // 音乐开 / 关
            if (kc == Keys.Left || kc == Keys.Right || kc == Keys.Up || kc == Keys.Down || kc == Keys.Space
                || kc == Keys.A || kc == Keys.D || kc == Keys.W || kc == Keys.Z || kc == Keys.K) return true;   // 游戏键：自己轮询，别让系统处理
            return base.ProcessCmdKey(ref msg, keyData);
        }

        static bool IsDown(int vk) { return (Native.GetAsyncKeyState(vk) & 0x8000) != 0; }

        // 键盘只在这个窗口在前台时才算；屏幕上的 ◀ ▶ 跳 按钮按住也算
        void PollKeys()
        {
            bool act = Form.ActiveForm == this;
            bool L = (act && (IsDown(0x25) || IsDown(0x41))) || padDown == "L";
            bool R = (act && (IsDown(0x27) || IsDown(0x44))) || padDown == "R";
            bool J = (act && (IsDown(0x20) || IsDown(0x26) || IsDown(0x57) || IsDown(0x5A) || IsDown(0x4B))) || padDown == "J";
            kL = L; kR = R;
            if (J && !kJ)
            {
                kJ = true;
                if (msgUp) { Act(msgActs[0]); return; }
                jumpBuf = 0.18;
            }
            kJ = J;
        }

        void Step()
        {
            if (finished) return;
            double nowT = clock.Elapsed.TotalSeconds, dt = nowT - lastT;
            lastT = nowT;
            if (dt > 0.05) dt = 0.05;
            if (dt < 0) dt = 0;
            BenchTick();
            if (finished) return;
            PollKeys();
            if (finished) return;
            bool msgBefore = msgUp;
            double left = dt;
            while (left > 1e-6) { double h = Math.Min(left, 1.0 / 120); StepDream(h); left -= h; if (finished) return; }
            Render();
            string hud = hearts + "|" + got + "|" + stomped;
            if (hud != lastHud || msgUp != msgBefore) { lastHud = hud; Invalidate(); }   // 头部数字 / 提示框变了：整窗重画（很少）
            else if (msgUp) { if (++msgFrames % 4 == 0) Invalidate(stage); }          // 提示框盖着：背景慢慢动就行
            else BlitNow();                                                             // 正在玩：直接贴到窗口上
        }

        void Burst(double x, double y, int col, int n, double sp)
        {
            for (int i = 0; i < n; i++)
            {
                double a = rnd.NextDouble() * Math.PI * 2, v = sp * (0.4 + rnd.NextDouble() * 0.6);
                Bit b = new Bit(); b.X = x; b.Y = y; b.VX = Math.Cos(a) * v; b.VY = Math.Sin(a) * v - sp * 0.3; b.Life = 0.35 + rnd.NextDouble() * 0.3; b.Col = col;
                bits.Add(b);
            }
        }

        string Summary() { return "收集了 " + got + "/" + total + " 个" + theme.Name + "，踩扁了 " + stomped + " 只 bug。"; }

        void Hurt(bool fell, int dir)
        {
            if (fell)
            {
                // 掉下去不扣心：梦里软乎乎的，回到刚才站过的地方接着跑
                Sfx.Play("d_fall", 1, 0);
                Burst(px, DH - 4, Hex("#E9E5FA"), 8, 50);
                px = cpX; py = cpY; pvx = pvy = 0; cam = Clamp(px - DW * 0.38, 0, len - DW);
                Burst(px, py - 8, Hex("#C9C6F0"), 10, 45);
                inv = 1.2;
                return;
            }
            hearts--;
            Burst(px, py - 8, Hex("#F0718A"), 8, 60);
            Sfx.Play(hearts <= 0 ? "d_lose" : "d_hurt", 1, 0);
            if (hearts <= 0)
            {
                over = "lose";
                ShowMsg("被 bug 吓醒了……\n" + Summary(), new string[] { "再睡一次", "醒来" }, new string[] { "again", "wake" });
                return;
            }
            pvx = dir * 90; pvy = -110; pground = false;
            inv = 2.0;
        }

        void StepDream(double dt)
        {
            t += dt;
            foreach (double[] z in zs) { z[1] -= z[2] * dt; if (z[1] < 8) { z[1] = 96; z[0] = rnd.NextDouble() * DW; } }
            for (int i = bits.Count - 1; i >= 0; i--)
            {
                Bit q = bits[i];
                q.Life -= dt; q.VY += 160 * dt; q.X += q.VX * dt; q.Y += q.VY * dt;
                if (q.Life <= 0) bits.RemoveAt(i);
            }
            foreach (Spring sp in springs) sp.Sq = Math.Max(0, sp.Sq - dt);
            if (msgUp) return;
            // 左右
            int want = (kR ? 1 : 0) - (kL ? 1 : 0);
            if (want != 0) { pvx += want * (pground ? 700 : 620) * dt; pfacing = want; }   // 空中也好控制一点
            else if (pground) { double f = 900 * dt; pvx = Math.Abs(pvx) <= f ? 0 : pvx - Math.Sign(pvx) * f; }
            pvx = Clamp(pvx, -78, 78);
            // 跳：落地前一小会儿按也算，离开边缘一小会儿也还能跳（都放宽了一点），跳得也比原来高一点
            coyote = pground ? 0.14 : Math.Max(0, coyote - dt);
            jumpBuf = Math.Max(0, jumpBuf - dt);
            if (jumpBuf > 0 && coyote > 0) { pvy = -188; pground = false; coyote = 0; jumpBuf = 0; Burst(px, py, Hex("#E9E5FA"), 4, 30); Sfx.Play("d_jump", 1, 0); }
            if (!kJ && pvy < -70) pvy = -70;
            // 横向移动：地面方块的侧面是实心的
            px += pvx * dt;
            foreach (Seg s in ground)
                if (py > s.Y + 0.5 && px + 6 > s.X && px - 6 < s.X + s.W) { px = px < s.X + s.W / 2 ? s.X - 6 : s.X + s.W + 6; pvx = 0; }
            px = Clamp(px, 8, len - 8);
            // 纵向：地面和云都是从上面踩得住
            double prevY = py; bool was = pground;
            pvy = Math.Min(pvy + 520 * dt, 420);
            py += pvy * dt;
            pground = false;
            if (pvy >= 0)
            {
                foreach (Seg s in ground)
                    if (px + 6 > s.X && px - 6 < s.X + s.W && prevY <= s.Y + 0.5 && py >= s.Y) { py = s.Y; pvy = 0; pground = true; cpX = Clamp(px, s.X + 10, s.X + s.W - 10); cpY = s.Y; }
                foreach (Seg s in clouds)
                    if (px + 6 > s.X && px - 6 < s.X + s.W && prevY <= s.Y + 0.5 && py >= s.Y) { py = s.Y; pvy = 0; pground = true; }
                foreach (Spring sp in springs)
                    if (Math.Abs(px - sp.X) < 8 && prevY <= sp.Y - 5 && py >= sp.Y - 6) { py = sp.Y - 6; pvy = -270; pground = false; sp.Sq = 0.25; Burst(sp.X, sp.Y - 6, Hex("#F6C448"), 6, 50); Sfx.Play("d_spring", 1, 0); }
            }
            if (pground && !was) Burst(px, py, Hex("#E9E5FA"), 3, 20);
            if (pground) pdist += Math.Abs(pvx) * dt;
            inv = Math.Max(0, inv - dt);
            if (py > DH + 30) { Hurt(true, 0); return; }
            // bug：从上面踩扁，侧面碰到会疼
            foreach (Bug b in bugs)
            {
                if (b.Flat > 0) { b.Flat -= dt; continue; }
                if (!b.Alive) continue;
                b.X += b.Dir * 10 * dt;   // 爬得慢一点（原来 14）
                if (b.X < b.MinX) { b.X = b.MinX; b.Dir = 1; }
                if (b.X > b.MaxX) { b.X = b.MaxX; b.Dir = -1; }
                if (Math.Abs(px - b.X) < 8 && py > b.Y - 5 && py - 12 < b.Y)
                {
                    if (pvy > 0 && prevY <= b.Y - 1) { b.Alive = false; b.Flat = 0.6; pvy = -150; stomped++; Burst(b.X, b.Y - 3, Hex("#5BAF62"), 8, 50); Sfx.Play("d_stomp", 1, 0); }   // 从上面落下来碰到就算踩扁（更好踩）
                    else if (inv <= 0) { Hurt(false, b.X < px ? 1 : -1); if (over != null) return; }
                }
            }
            foreach (Item it in items)
                if (!it.Got && Math.Abs(px - it.X) < 8 && py > it.Y - 3 && py - 13 < it.Y + 3) { it.Got = true; got++; Burst(it.X, it.Y, Hex("#FFF6C8"), 6, 45); Sfx.Play("d_coin", 1, 0); }
            if (Math.Abs(px - goalX) < 12 && pground && over == null)
            {
                over = "win";
                Burst(px, py - 10, Hex("#F6C448"), 16, 70);
                Sfx.Play("d_win", 1, 0);
                ShowMsg("睡得好香！\n" + Summary(), new string[] { "醒来", "再做一个梦" }, new string[] { "wake", "again" });
            }
            double target = Clamp(px - DW * 0.38, 0, len - DW);
            cam += (target - cam) * Math.Min(1, dt * 8);
        }

        // ---------------- 画到 256x128 的缓冲区
        void R(int x, int y, int w, int h, int col)
        {
            int x0 = Math.Max(0, x), y0 = Math.Max(0, y), x1 = Math.Min(DW, x + w), y1 = Math.Min(DH, y + h);
            for (int yy = y0; yy < y1; yy++)
                for (int xx = x0; xx < x1; xx++) buf[yy * DW + xx] = col;
        }
        void Rows(string[] rows, Art pal, int x, int y)
        {
            for (int r = 0; r < rows.Length; r++)
                for (int c = 0; c < rows[r].Length; c++)
                {
                    Color col;
                    if (rows[r][c] == '.' || !pal.Col(rows[r][c], out col)) continue;
                    R(x + c, y + r, 1, 1, col.ToArgb());
                }
        }
        void CloudTop(int x, int y, int w, int col)
        {
            for (int i = 2; i < w - 5; i += 9) { R(x + i, y - 2, 6, 2, col); R(x + i + 1, y - 3, 4, 1, col); }
        }

        void Render()
        {
            int c0 = Rnd(cam);
            for (int i = 0; i < 8; i++) R(0, i * 16, DW, 16, sky[i]);
            int starOn = Hex("#BDB9EA"), starBig = Hex("#FFF3C4"), starOff = Hex("#4E4A86");
            foreach (Star s in stars)
            {
                int x = (int)Math.Floor(((s.X - c0 * 0.06) % DW + DW) % DW), y = (int)Math.Floor(s.Y);
                bool on = Math.Sin(t * 2.2 + s.Ph) > -0.5;
                int col = on ? (s.B ? starBig : starOn) : starOff;
                R(x, y, 1, 1, col);
                if (s.B && on) { R(x - 1, y, 3, 1, col); R(x, y - 1, 1, 3, col); }
            }
            int mx = Rnd(208 - c0 * 0.03), moon = Hex("#F4E9B8");   // 月牙：大圆减去错开的小圆
            for (int y = -11; y <= 11; y++)
                for (int x = -11; x <= 11; x++)
                    if (x * x + y * y <= 121 && (x - 5) * (x - 5) + (y + 3) * (y + 3) > 100) R(mx + x, 24 + y, 1, 1, moon);
            int farC = Hex("#2A2858");
            foreach (double[] f in far)
            {
                int x = Rnd(((f[0] - c0 * 0.3) % 560 + 560) % 560 - 50), y = Rnd(f[1]), w = Rnd(f[2]);
                R(x, y, w, 10, farC); R(x + 5, y - 3, w - 10, 3, farC); R(x + 11, y - 5, w - 22, 2, farC);
            }
            foreach (double[] z in zs) Rows(Sprite.Zed, ZPal, Rnd(z[0]), Rnd(z[1]));
            int g1 = Hex("#E9E5FA"), g2 = Hex("#D2CBF0"), g3 = Hex("#B7AEE0");
            foreach (Seg s in ground)
            {
                int x = Rnd(s.X - c0), y = (int)s.Y, w = (int)s.W;
                if (x > DW || x + w < 0) continue;
                R(x, y, w, DH - y, g1); R(x, y + 6, w, DH - y - 6, g2);
                R(x, y + 15, w, DH - y - 15, g3); R(x, y, 1, DH - y, g3); R(x + w - 1, y, 1, DH - y, g3);
                CloudTop(x, y, w, g1);
            }
            int c1 = Hex("#F2EFFD"), c2 = Hex("#C4BBE8");
            foreach (Seg s in clouds)
            {
                int x = Rnd(s.X - c0), y = (int)s.Y, w = (int)s.W;
                if (x > DW || x + w < 0) continue;
                R(x, y, w, 4, c1);
                R(x + 1, y + 4, w - 2, 2, c2); R(x + 4, y + 6, w - 8, 1, c2);
                CloudTop(x, y, w, c1);
            }
            foreach (Spring sp in springs)
            {
                int x = Rnd(sp.X - c0);
                if (x < -8 || x > DW + 8) continue;
                Art b = Sprite.BallArt(((int)Math.Floor(t * 3)) & 3);
                Rows(b.Rows, b, x - 3, (int)sp.Y - 6 + (sp.Sq > 0 ? 1 : 0));
            }
            foreach (Item it in items)
            {
                if (it.Got) continue;
                int x = Rnd(it.X - c0);
                if (x < -8 || x > DW + 8) continue;
                string[] rows = theme.Frames != null ? theme.Frames[((int)Math.Floor(t * 6 + it.Ph)) & 1] : theme.Rows;
                Rows(rows, theme, x - (rows[0].Length >> 1), Rnd(it.Y - (rows.Length >> 1) + Math.Sin(t * 3 + it.Ph) * 1.2));
            }
            foreach (Bug b in bugs)
            {
                int x = Rnd(b.X - c0);
                if (x < -8 || x > DW + 8) continue;
                if (b.Alive) Rows(BugF[((int)Math.Floor(t * 5)) & 1], BugPal, x - 3, Rnd(b.Y) - 5);
                else if (b.Flat > 0) Rows(BugFlat, BugPal, x - 3, Rnd(b.Y) - 2);
            }
            // 终点：软乎乎的枕头
            int gx = Rnd(goalX - c0), gy = (int)goalY, white = Hex("#FFFFFF"), stripe = Hex("#B9C3F5");
            R(gx - 11, gy - 7, 22, 6, white); R(gx - 10, gy - 8, 20, 8, white);
            R(gx - 10, gy - 4, 20, 1, stripe); R(gx - 9, gy - 1, 18, 1, stripe);
            if (Math.Sin(t * 3) > 0) Rows(Sprite.Zed, ZGoal, gx + 6, gy - 18);
            // Clawd
            if (!(inv > 0 && (((int)Math.Floor(t * 12)) & 1) == 1))
            {
                bool run = pground && Math.Abs(pvx) > 8;
                bool step = (((int)Math.Floor(pdist / 6)) & 1) == 1;
                Pose p = new Pose();
                p.Eyes = inv > 1 ? EyePose.Squint : (t % 3.3) < 0.12 ? EyePose.Blink : EyePose.Open;
                p.LookX = pfacing; p.LookY = !pground && pvy > 80 ? 1 : 0; p.Facing = pfacing;
                if (!pground) { p.ArmL = p.ArmR = pvy < 0 ? ArmPose.Up : ArmPose.UpOut; p.Legs = pvy < 0 ? LegPose.DangleA : LegPose.DangleB; }
                else if (run) { p.Legs = step ? LegPose.WalkA : LegPose.WalkB; p.ArmL = step ? ArmPose.Raised : ArmPose.Normal; p.ArmR = step ? ArmPose.Normal : ArmPose.Raised; }
                long key = p.Key();
                int[] px24;
                if (!poseCache.TryGetValue(key, out px24)) { px24 = Sprite.CellPixels(p); poseCache[key] = px24; }
                int bx = Rnd(px - 12 - c0), by = Rnd(py - 17);
                for (int y = 0; y < 18; y++)
                {
                    int yy = by + y;
                    if (yy < 0 || yy >= DH) continue;
                    for (int x = 0; x < 24; x++)
                    {
                        int xx = bx + x, col = px24[y * 24 + x];
                        if (xx < 0 || xx >= DW || (col >> 24) == 0) continue;
                        buf[yy * DW + xx] = col;
                    }
                }
            }
            foreach (Bit q in bits) R(Rnd(q.X - c0), Rnd(q.Y), 1, 1, q.Col);
        }

        // ---------------- 窗口绘制
        static GraphicsPath RoundRect(Rectangle r, int rad)
        {
            GraphicsPath p = new GraphicsPath();
            int d = Math.Max(2, rad * 2);
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d - 1, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d - 1, r.Bottom - d - 1, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d - 1, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        void Btn(Graphics g, Rectangle r, string label, bool big, bool down, int arrow)
        {
            bool hover = r.Contains(PointToClient(Cursor.Position));
            Color bg = big ? (down || hover ? Color.FromArgb(0xE5, 0x8A, 0x69) : Sprite.BodyC)
                           : (down ? Color.FromArgb(0x4A, 0x51, 0xA0) : hover ? Color.FromArgb(0x3A, 0x40, 0x80) : Color.FromArgb(0x2D, 0x32, 0x64));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = RoundRect(r, (int)(7 * dpi))) g.FillPath(Sprite.BrushOf(bg), path);
            Color fg = big ? Color.White : Color.FromArgb(0xEC, 0xE9, 0xFB);
            if (arrow != 0)
            {
                // ◀ ▶ 自己画三角形（字体里不一定有这两个符号）
                float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, h = r.Height * 0.22f;
                PointF[] tri = arrow < 0
                    ? new PointF[] { new PointF(cx - h, cy), new PointF(cx + h * 0.8f, cy - h), new PointF(cx + h * 0.8f, cy + h) }
                    : new PointF[] { new PointF(cx + h, cy), new PointF(cx - h * 0.8f, cy - h), new PointF(cx - h * 0.8f, cy + h) };
                g.FillPolygon(Sprite.BrushOf(fg), tri);
            }
            else
            {
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.DrawString(label, fBtn, Sprite.BrushOf(fg), r, sf);
                }
            }
            g.SmoothingMode = SmoothingMode.None;
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Bg);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            // 标题 + 状态（心、收集数、踩扁的 bug）
            g.DrawString("Clawd 的梦 · " + theme.Name, fTitle, Sprite.BrushOf(Color.FromArgb(0xEC, 0xE9, 0xFB)), M, M + (headH - fTitle.Height) / 2);
            string hud = theme.Name + " " + got + "/" + total + "    bug ×" + stomped;
            SizeF hs = g.MeasureString(hud, fHud);
            float hx = stage.Right - hs.Width;
            g.DrawString(hud, fHud, Sprite.BrushOf(Color.FromArgb(0xCF, 0xCB, 0xF2)), hx, M + (headH - hs.Height) / 2);
            int hu = Math.Max(2, (int)(2 * dpi)), hy = M + (headH - 6 * hu) / 2;
            for (int i = 0; i < MaxHearts; i++)
                Sprite.Glyph(g, Sprite.Heart, Sprite.BrushOf(i < hearts ? Color.FromArgb(0xF0, 0x71, 0x8A) : Color.FromArgb(0x4E, 0x4A, 0x86)),
                    (int)hx - (int)(14 * dpi) - (MaxHearts - i) * 9 * hu, hy, hu);
            // 游戏画面（同样用 StretchDIBits，和每帧直接贴的效果一模一样）
            IntPtr hdc = g.GetHdc();
            try { BlitStage(hdc); } finally { g.ReleaseHdc(hdc); }
            // 开场 / 输 / 赢 的提示
            msgRects.Clear();
            if (msgUp)
            {
                using (SolidBrush shade = new SolidBrush(Color.FromArgb(184, 16, 18, 44))) g.FillRectangle(shade, stage);
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    SizeF ts = g.MeasureString(msgText, fMsg, stage.Width - 20, sf);
                    int bh = (int)(38 * dpi), gap = (int)(14 * dpi);
                    float top = stage.Y + (stage.Height - (ts.Height + gap + bh)) / 2;
                    g.DrawString(msgText, fMsg, Sprite.BrushOf(Color.FromArgb(0xEC, 0xE9, 0xFB)), new RectangleF(stage.X + 10, top, stage.Width - 20, ts.Height + 4), sf);
                    int[] ws = new int[msgLabels.Length];
                    int totalW = 0;
                    for (int i = 0; i < msgLabels.Length; i++)
                    {
                        ws[i] = Math.Max((int)((i == 0 ? 96 : 76) * dpi), (int)g.MeasureString(msgLabels[i], fBtn).Width + (int)(28 * dpi));
                        totalW += ws[i] + (i > 0 ? (int)(12 * dpi) : 0);
                    }
                    int bx = stage.X + (stage.Width - totalW) / 2, by = (int)(top + ts.Height + gap);
                    for (int i = 0; i < msgLabels.Length; i++)
                    {
                        Rectangle r = new Rectangle(bx, by, ws[i], bh);
                        msgRects.Add(new KeyValuePair<Rectangle, string>(r, msgActs[i]));
                        Btn(g, r, msgLabels[i], i == 0, pressedMsg == msgActs[i], 0);
                        bx += ws[i] + (int)(12 * dpi);
                    }
                }
            }
            // 底下的按钮：◀ ▶ 提示 跳
            Btn(g, bL, "", false, padDown == "L", -1);
            Btn(g, bR, "", false, padDown == "R", 1);
            using (StringFormat sf = new StringFormat())
            {
                sf.LineAlignment = StringAlignment.Center;
                sf.Trimming = StringTrimming.EllipsisCharacter; sf.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString("← → 走 · 空格跳（按久跳得高）· M 音乐" + (MusicOn ? "开" : "关") + " · Esc 醒来", fTip, Sprite.BrushOf(Color.FromArgb(0x8F, 0x8B, 0xC0)), tipR, sf);
            }
            Btn(g, bJ, "跳", true, padDown == "J", 0);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (msgUp)
                foreach (KeyValuePair<Rectangle, string> kv in msgRects)
                    if (kv.Key.Contains(e.Location)) { pressedMsg = kv.Value; Invalidate(); return; }
            if (bL.Contains(e.Location)) padDown = "L";
            else if (bR.Contains(e.Location)) padDown = "R";
            else if (bJ.Contains(e.Location)) padDown = "J";
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            string pm = pressedMsg;
            pressedMsg = null;
            if (pm != null)
            {
                foreach (KeyValuePair<Rectangle, string> kv in msgRects)
                    if (kv.Value == pm && kv.Key.Contains(e.Location)) { Act(pm); break; }
                if (!finished) Invalidate();
            }
            if (padDown != null) { padDown = null; Invalidate(); }
        }

        Point lastMouse;
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (e.Location != lastMouse) { lastMouse = e.Location; Invalidate(new Rectangle(0, stage.Top, ClientSize.Width, ClientSize.Height - stage.Top)); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose(); off.Dispose();
                fTitle.Dispose(); fHud.Dispose(); fMsg.Dispose(); fBtn.Dispose(); fTip.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // ------------------------------------------------------------------ 小物件（球、蝴蝶）：各自一个小小的透明置顶窗口
    // 用分层窗口（UpdateLayeredWindow）画：图、位置、大小一次交给系统。
    // 以前用透明色窗口，先改大小再等重画，中间新露出来的那块会黑一下（竹竿倒下去时窗口每帧变宽，看得很明显）
    class ObjWin : Form
    {
        readonly bool passThrough;
        Bitmap img; bool imgNew, pushQueued;
        Point at = new Point(-10000, -10000);

        public ObjWin(bool passThrough)
        {
            this.passThrough = passThrough;
            Text = "Clawd";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(6, 6);
            Location = at;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80 | 0x08000000 | 0x8 | 0x80000;   // 工具窗口、不抢焦点、置顶、分层
                if (passThrough) cp.ExStyle |= 0x20;                // 场地、杆子、纸飞机：鼠标直接穿过去
                return cp;
            }
        }
        protected override bool ShowWithoutActivation { get { return true; } }

        // 换一张图：一般紧接着会 MoveTo，到时连同位置一起交给系统；
        // 不挪位置的（比如比分牌进球后换数字），这一帧忙完自己交上去
        public void SetArt(string[] r, Art p, int unit)
        {
            int u = Math.Max(1, unit), w = Math.Max(1, r[0].Length * u), h = Math.Max(1, r.Length * u);
            if (img == null || img.Width != w || img.Height != h)
            {
                if (img != null) img.Dispose();
                img = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            }
            using (Graphics g = Graphics.FromImage(img))
            {
                g.Clear(Color.FromArgb(0, 0, 0, 0));   // 全透明（点得穿）
                g.SmoothingMode = SmoothingMode.None;
                Sprite.DrawArt(g, r, p, 0, 0, u);
            }
            imgNew = true;
            if (IsHandleCreated && !pushQueued && !IsDisposed)
            {
                pushQueued = true;
                BeginInvoke((Action)(() => { pushQueued = false; if (imgNew && !IsDisposed) Push(); }));
            }
        }

        public void MoveTo(int x, int y)
        {
            bool moved = x != at.X || y != at.Y;
            at = new Point(x, y);
            if (imgNew) Push();
            else if (moved && IsHandleCreated)
                Native.SetWindowPos(Handle, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        }

        void Push()
        {
            if (img == null || IsDisposed) return;
            imgNew = false;
            IntPtr scr = Native.GetDC(IntPtr.Zero), mem = Native.CreateCompatibleDC(scr), hb = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                hb = img.GetHbitmap(Color.FromArgb(0));
                old = Native.SelectObject(mem, hb);
                Native.PT dst = new Native.PT(at.X, at.Y), src = new Native.PT(0, 0);
                Native.SZ sz = new Native.SZ(img.Width, img.Height);
                Native.BLEND bf = new Native.BLEND(); bf.Alpha = 255; bf.Format = 1;   // AC_SRC_ALPHA
                Native.UpdateLayeredWindow(Handle, scr, ref dst, ref sz, mem, ref src, 0, ref bf, 2);   // ULW_ALPHA
            }
            finally
            {
                if (old != IntPtr.Zero) Native.SelectObject(mem, old);
                if (hb != IntPtr.Zero) Native.DeleteObject(hb);
                Native.DeleteDC(mem);
                Native.ReleaseDC(IntPtr.Zero, scr);
            }
            UpdateBounds();   // 让 WinForms 记住现在的位置和大小，免得它哪天按旧的尺寸改回去
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        protected override void Dispose(bool disposing)
        {
            if (disposing && img != null) { img.Dispose(); img = null; }
            base.Dispose(disposing);
        }
    }

    // 球 / 蝴蝶的状态（屏幕坐标；X = 中心，Y = 底边）
    class PetObj
    {
        public string Kind;
        public double X, Y, VX, VY, Spin, Flap, Ph, Born, Touched, Life, NextT, TX, TY, HeadDx, HeadUntil, NoLandUntil;
        public bool Held, Rest, Gone, OnHead, Landing;
        public int ColIdx, Frame = -1, LastU;
        public ObjWin Win;
        // 好几只一起踢：最后谁踢的、传给了谁、什么时候踢的、这一轮还要传几脚
        public PetForm LastKicker, PassTo, NoHit; public double KickT = -100, PassT = -100, NoHitUntil; public int Passes;
        public bool Auto;   // Clawd 自己拿出来的球：踢完放一会儿就收起来
        public bool MatchBall; public double PrevX;   // 比赛用的球；上一小步的 x（判断有没有越过球门线）
    }


    // ------------------------------------------------------------------ 桌宠主体
    enum St
    {
        Idle, Walk, Think, Sleep, Drag, Fall, Crouch, Land, Happy, Dance, Wave, Alert, Dizzy, Yawn, Stretch, Sneeze, Eat,
        Sit, Lie, Look, Ponder, Scratch, Spin, Roll, Exercise, Ledge, Teeter, Peek, Reach, Aim, Splat, Refuse, Giggle, Doze, Chase, Kick, Work,
        Climb,  // 扒在屏幕左右边上往上爬
        Burrow, // 挖沙躲起来（像真的螃蟹一样钻进「沙」里，躲到任务栏后面）
        Shake,  // 钻出来以后抖掉身上的沙子
        Read, Guitar, Sweep, Lift, Castle, Bubbles, Music, Plane,   // 更多待机：看书、弹尤克里里、扫地、举重、堆沙堡、吐泡泡、听歌、扔纸飞机
        Pole,   // 架一根木杆，顺着爬到别的窗口标题栏上
        HighFive, // 两只 Clawd 击掌
        Match,    // 在小足球场上踢比赛
        Chat,     // 两只面对面聊几句
        Watch,    // 坐在旁边看伙伴弹琴 / 看书 / 举重……
        Pat       // 伙伴晕了：跑过去拍拍它
    }
    enum Mood { Plain, Happy, Celebrate, Reach }
    enum SleepMode { Afk, Nap, Night }     // Afk = 你离开了（你一回来就醒）；Nap = 自己打个盹（睡够了自己醒）；Night = 夜里睡（动鼠标不醒，到起床时间才起）

    class Particle
    {
        public string[] Shape; public Color Color;
        public double X, Y, VX, VY, Life, Gravity;
        public int U;     // 0 = 默认像素大小
    }

    class CcSession { public double Start, Last; public string Project = ""; }
    class Reminder { public DateTime Due; public string Text; }
    class Plat { public IntPtr H; public int L, T, R, Z; }     // 能跳上去的窗口（Z = 在窗口 z 序里排第几）

    partial class PetForm : Form
    {
        const int CW = 24, CH = 18, FOOT = 17, FORM_WC = 50, TOP_C = 24, BOT_C = 4;
        static readonly Color Key = Color.FromArgb(255, 0, 255);
        static readonly int[] SizeCells = { 4, 5, 7, 9 };
        static readonly string[] SizeNames = { "小", "中", "大", "超大" };
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        // 只有点「说句话」或者打开「偶尔自言自语」才会冒出来
        static readonly string[] IdleLines = {
            "嗯？", "今天也辛苦啦", "我在这儿陪你~", "眼睛累了就看看远处", "喝口水吧", "要不要起来走走？", "嘿嘿",
            "You're absolutely right!", "我是 Clawd~", "有点无聊…陪我玩会儿？", "扔个球给我嘛", "需要帮忙写代码吗？",
            "git commit 了吗？", "记得 Ctrl+S 哦", "要不要先跑一下测试？", "rm -rf？三思啊！", "ultrathink…",
            "把文件拖给我，我帮你复制路径~", "右键点我，会冒出快捷按钮哦", "右键点我两下可以开番茄钟哦"
        };
        static readonly string[] DoneLines = { "搞定！", "写完啦~", "任务完成！", "收工！" };
        static readonly string[] AttentionLines = { "Claude 在等你确认！", "需要你看一眼~", "这里这里！看我！" };
        static readonly string[] HealthLines = {
            "已经连续用电脑 50 分钟啦，起来活动一下吧！", "眼睛累了吧？看看远处~", "喝口水，伸个懒腰！"
        };
        static readonly string[] Verbs = {
            "Clauding", "Pondering", "Noodling", "Cogitating", "Percolating", "Ruminating", "Reticulating",
            "Simmering", "Brewing", "Musing", "Conjuring", "Vibing", "Schlepping", "Moseying", "Honking",
            "Marinating", "Finagling", "思考中", "琢磨中", "酝酿中"
        };
        static readonly ArmPose[] Flail = { ArmPose.UpOut, ArmPose.Out, ArmPose.DownOut, ArmPose.Out };
        // 快捷按钮条：喂一口 / 扔个球 / 睡一会儿 / 做个梦 / 待在这儿别动 / 更多…
        const int BAR_FEED = 0, BAR_BALL = 1, BAR_SLEEP = 2, BAR_DREAM = 3, BAR_STAY = 4, BAR_MORE = 5;
        static readonly Art[] BarIcons = { Sprite.Snacks[0], Sprite.BallArt(0), Sprite.ZzArt, Sprite.CloudArt, Sprite.PinArt, Sprite.MoreArt };

        readonly Random rnd = new Random(Guid.NewGuid().GetHashCode());   // 每只的随机数各不相同（不然几只会做一模一样的事）
        static readonly Stopwatch clock = Stopwatch.StartNew();            // 几只共用一个时钟：一起跳舞时步子对得上
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();

        // ---------------- 好几只 Clawd：第一只（slot 0）管托盘图标和 Claude Code 信号；第二只戴眼镜、第三只戴帽子、第四只的饰品跟着季节换
        static readonly List<PetForm> All = new List<PetForm>();
        const int MAX_PETS = 4;
        static int savedPets = 1;
        readonly int slot;
        bool Main { get { return slot == 0; } }
        bool extrasSpawned;
        // 叠高高：站在谁头上（basePet），对方横着挪了多少就跟着挪多少；刚从谁头上下来一会儿内不会又落回去
        PetForm basePet, ignorePet, braceFor;
        double baseLastCX, ignorePetUntil, rideSince, braceUntil, lastGreet = -100, lastStackT = -100;
        bool stackJoy, fiveDone, fiveLead, shakeNoSand;
        ToolStripMenuItem miAddPet, miDismiss, miTalk;
        // 交流：聊天、围观、安慰、分零食
        static bool petTalk = true;         // 伙伴之间聊天冒文字气泡（关掉就只比划表情）
        static double nextChatAll = 45;     // 下次可以自己聊天的时间（大家共用，免得聊个不停）
        static string[] lastChat;
        PetForm chatWith, watchPet, patPet; string[] chatScript; int chatIdx; bool chatLead;
        double chatNext, talkUntil, nodAt, nextWatchFx, cheerUntil, lastShare = -100; St watchWhat;
        readonly NotifyIcon tray = new NotifyIcon();
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly uint myPid = (uint)Process.GetCurrentProcess().Id;
        ToolStripMenuItem miSleep, miFollow, miChatty, miStay, miAuto, miStopWork, miHealth, miRemind, miClearRemind, miDream, miTimers, miSettings;
        ChipRowItem pomoRow, sizeRow, playRow, friendRow;

        float dpiScale = 1f;
        int sizeIdx = 1, S = 5, offX, offY;
        bool follow, chatty, stay, health = true, greeted;
        Font bubbleFont, badgeFont, tipFont;

        double now, lastT;
        double posX, posY, velX, velY, impact;     // posX = 画布左边，posY = 脚底线（屏幕坐标）
        IntPtr plat = IntPtr.Zero, ignorePlat = IntPtr.Zero; int platL, platT, platR;
        double nextOcclCheck;
        readonly List<Plat> plats = new List<Plat>();
        readonly List<Rectangle> zRects = new List<Rectangle>();
        double platsT = -10;

        // 果冻弹簧
        double sq, sqV, lean, leanV, anchorY, hop;
        // 转圈（flip：-1..1 水平翻转）/ 打滚（rot）/ 坐在边上时身子往下沉（sitOff）
        double flip = 1, rot, sitOff, rollX0; int rollDir = 1, curBodyDy, lastBodyDy, lastFacing = 1;
        // 手脚：两组腿像钟摆一样甩（legA / legB），手乱挥（armPh），拎着时腿一直蹬（kickPh）
        double legA, legAV, legB, legBV, legPh, armPh, kickPh;
        // 被拎着：像钟摆一样绕抓住的点晃（swing）
        double swing, swingV, pivX, accX, dragVX, dragVY, shake, shakeAcc;
        // 地面速度：慢慢加速、慢慢停下；腿按走过的距离迈步
        double gvx, walkPh;
        long lastXfKey = long.MinValue;
        readonly Dictionary<long, int[]> poseCache = new Dictionary<long, int[]>();
        Bitmap destBmp; int[] destBuf;
        // 局部重绘
        bool spriteDirty = true, badgeDirty;
        Rectangle lastDynBox, lastBubbleRect, lastBadgeRect;
        int lastSpinFrame = -1; string lastThinkText;
        Bitmap bubbleCache; string bubbleCacheKey; Point spinAt;
        int lastMinX = int.MinValue, lastMaxX = int.MinValue, lastMinY = int.MinValue;
        string queueDir;

        St state = St.Fall; double stT, stDur = 1e9;
        Mood mood = Mood.Plain, pendingMood = Mood.Plain;
        double pendingJumpV, pendingJumpVX; bool pendingIgnore;
        int facing = 1;
        bool thinkPersistent, pendingDance, pendingDizzy, sneezed, alertSticky, dragHover, fellOff, afterLandWave;
        string afterLandSay;
        Action afterLandThen, walkThen;
        bool hasWalkTarget; double walkTarget, walkSpeed;
        int hopsLeft;
        bool hasAim, aimIgnore; double aimVX, aimVY;
        int peekSide = 1, splatSide = 1;
        readonly List<double[]> lookSeq = new List<double[]>();
        double attnX, attnY, attnUntil = -1;
        double energy = 0.6;
        readonly List<double> fedTimes = new List<double>(), pets = new List<double>();
        double lastReach = -100;
        double nextWorkFx, nextWorkEmote, nextSteam;
        // 爬墙：wallSide = -1 左边 / +1 右边；climbPhase 0 = 转身扒上墙，1 = 在墙上，2 = 爬到底转回来站好
        // climbMode 0 = 扒着不动，1 = 往上爬，-1 = 往下爬；climbV = 沿墙的速度（屏幕坐标，负数 = 往上）
        int wallSide = -1, climbPhase, climbMode; double climbV, climbUntil, nextClingLook; bool climbTop; int clingLookX, clingLookY;
        // 挖沙：burrowPhase 0 = 挖，1 = 埋好了露着眼睛，2 = 害羞整个缩进去，3 = 慢慢探出眼睛，4 = 钻出来
        // sink = 往下沉了几格（地面以下的部分不画）；沙堆画在地面上（屏幕坐标 moundX、moundY），钻出来后 0.5 秒塌掉
        int burrowPhase; double sink, sinkFrom, burrowDig, burrowEnd, phaseT0, shyUntil, nextSand, nextBurrowFx, nextBurrowZ;
        double moundLevel, moundFrom, moundT0 = -10, moundX, moundY; bool burrowSleep, popStartled;
        double climbCooldownUntil;   // 刚从墙上下来：过一会儿才会再去爬，免得爬上爬下停不下来

        // 睡觉
        SleepMode sleepMode = SleepMode.Afk; double napDur, nextNap; bool sleepLedge, guitarLedge; int dozeNods; double jerkUntil, twitchUntil;
        double noseT0, noseLen = 6, noseX, noseY, noseR; bool noseOn;
        double nextTwitch, nextDream; Art lastDream; bool wakeLook;

        // 头顶的小表情泡泡
        string emoteKind; Art emoteIcon; double emoteBorn, emoteUntil;
        Rectangle dreamHit;   // 梦泡泡（窗体坐标），点它钻进梦里

        // 喂一口：零食拿在嘴边
        Art snack; int snackBite, feedQueue;

        // 快捷按钮条
        bool barOn; double barKeepUntil; int barHot = -1, barPress = -1;
        Rectangle barRect; Rectangle[] barBtns = new Rectangle[0];
        int uiS = 7;   // 按钮条 / 小提示用的格子大小（见 Sprite.BarScale）
        bool cursorOnPet;
        Cursor curCursor;

        // 小物件：球、蝴蝶
        readonly List<PetObj> objs = new List<PetObj>();
        PetObj chaseObj, heldObj; int kicksLeft; bool kicked; double lastBonk = -10, nextFly, objGrabDX, objGrabDY;
        bool ballWait; PetForm fiveAfter; static double nextKickAbout = 90;   // 一起踢球：在等伙伴传过来 / 踢完去跟谁击掌 / 下次自己拿球出来玩
        readonly List<double[]> objTrail = new List<double[]>();

        // 梦境小游戏
        DreamForm dreamForm;

        // Claude Code
        readonly Dictionary<string, CcSession> sessions = new Dictionary<string, CcSession>();
        bool ccWorking;
        string thinkVerb = "Clauding";
        double nextSignalPoll, nextIdleCheck, idleSecs;

        // 番茄钟 / 提醒 / 健康
        int pomoMode; DateTime pomoEnd; int lastPomoSecs = -1;
        readonly List<Reminder> reminders = new List<Reminder>();
        double activeSince, lastHealthNag = -1e9;
        long cpuIdle0, cpuKern0, cpuUser0; double cpu; int cpuHigh; double lastCpuNag = -1e9, nextCpu, nextBattery, nextSweat;
        int lastBattNag = -1;

        int lookX, lookY; double nextBlink = 2, blinkUntil, nextGlance;
        Point lastCursor; double lastCursorMove = -100, cursorSpeed, lastStartle = -1e9;

        bool mouseDown, dragging; Point downPt; double downT, grabDX, grabDY;
        readonly List<double[]> trail = new List<double[]>();
        readonly List<double> clicks = new List<double>(); double lastClickT = -10;
        double petDist, lastPetMove; Point lastHover; bool hoverValid; int rubDir, rubDirY;
        readonly List<double> rubFlips = new List<double>();
        double blushUntil;

        string bubble; bool bubbleSpin; double bubbleUntil, nextVerb, nextChatter = 240;
        readonly List<Particle> parts = new List<Particle>();
        double nextFx;
        Pose lastPose;
        bool dirty = true, hiddenForFullscreen;
        double nextTopmost, nextFullCheck;
        // 性能剖析（设置环境变量 CLAWD_PROFILE=1 才开启）
        readonly bool profiling = Environment.GetEnvironmentVariable("CLAWD_PROFILE") == "1";
        double profTick, profPaint, profNext = 10; int profTicks, profPaints; long profPx;
        // 调试日志（设置环境变量 CLAWD_DEBUG=1 才开启）：每只换状态、踢球传球都记一行到 debug.log
        static readonly bool debugLog = Environment.GetEnvironmentVariable("CLAWD_DEBUG") == "1";
        void Dbg(string what)
        {
            if (!debugLog) return;
            try { File.AppendAllText(Path.Combine(Signal.Dir(), "debug.log"), string.Format("{0,8:F2} #{1} x={2,5:F0} {3}\r\n", clock.Elapsed.TotalSeconds, slot, CX / S, what)); } catch { }
        }

        // ---------------- 音效
        // kind：0 = 你直接碰它（戳、拎、扔……开着音效就响）；1 = 正事（Claude 干完 / 等你、番茄钟、提醒）；2 = 它自己玩出来的声音。
        // 1、2 在全屏（看视频、打游戏）时不响；夜里没被你叫醒也不响；2 在专注番茄钟的时候也不响
        const int SND_YOU = 0, SND_NEWS = 1, SND_PLAY = 2;
        static readonly double[] VoiceSemis = { 0, 3, -2, 5 };   // 四只嗓音高低不一样
        double VoicePitch() { return VoiceSemis[slot & 3]; }

        bool SndOk(int kind)
        {
            if (!Sfx.On || Sfx.Level <= 0) return false;
            if (kind == SND_YOU) return true;
            if (hiddenForFullscreen) return false;
            if (NightNow() && now >= nightAwakeUntil) return false;
            if (kind == SND_PLAY && idleSecs > 180) return false;   // 你三分钟没碰电脑（可能在看视频、看文章，或者走开了）：自己玩别出声
            if (kind == SND_PLAY) foreach (PetForm f in All) if (f.pomoMode == 1) return false;
            return true;
        }

        static readonly int[] UkeNotes = { 60, 62, 64, 67, 69, 72, 74 };   // C 大调五声音阶

        // 说话时「叽里咕噜」几声：字越多念得越久（最多 7 下）
        void BabbleFor(string text)
        {
            if (!SndOk(SND_PLAY) || string.IsNullOrEmpty(text)) return;
            int n = 0;
            foreach (char c in text) if (char.IsLetterOrDigit(c)) n++;
            if (n > 0) Sfx.Babble((int)Math.Round(1 + n * 0.55), VoicePitch(), 0.75);
        }

        void Snd(string name, int kind) { Snd(name, kind, 1, false); }
        void Snd(string name, int kind, double gain, bool voiced)
        {
            if (SndOk(kind)) Sfx.Play(name, gain, voiced ? VoicePitch() : 0);
        }

        public PetForm() : this(0) { }

        public PetForm(int slot)
        {
            this.slot = slot;
            All.Add(this);
            if (debugLog && slot == 0) Sfx.Log = s => Dbg(s);
            Text = "Clawd";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Key;
            TransparencyKey = Key;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            DoubleBuffered = true;
            AllowDrop = true;

            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) dpiScale = Math.Max(1f, g.DpiX / 96f);
            LoadSettings();
            if (!Main) { stay = false; follow = false; }   // 后来的几只：各走各的
            ApplySize(false);
            BuildMenu();

            // 共用一个时钟：后叫出来的那几只，各种「下一次」的时间从现在算起
            double tStart = clock.Elapsed.TotalSeconds;
            now = lastT = tStart;
            nextBlink += tStart; nextChatter += tStart; poleCooldownUntil += tStart; profNext += tStart; activeSince = tStart;

            queueDir = Signal.QueueDir();
            if (Main)
            {
                try { tray.Icon = Sprite.MakeIcon(SystemInformation.SmallIconSize.Width); } catch { tray.Icon = SystemIcons.Application; }
                tray.Text = "Clawd 桌宠（右键菜单，双击叫它过来）";
                tray.ContextMenuStrip = menu;
                tray.Visible = true;
                tray.MouseDoubleClick += (s, e) => Summon();

                // 清掉启动前残留的旧信号
                try
                {
                    foreach (string f in Directory.GetFiles(queueDir)) File.Delete(f);
                    string old = Path.Combine(Signal.Dir(), "signal.txt");
                    if (File.Exists(old)) File.Delete(old);
                }
                catch { }
            }

            // 登场：从屏幕上方掉下来（后来的几只掉在别的地方）。
            // 重新打开（更新、重启电脑）时：记得上次站在哪，就在原地冒出来，轻轻落一下，不用从天上重新掉
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            double fx = Main ? 0.72 : 0.15 + 0.7 * rnd.NextDouble();
            posX = wa.Left + wa.Width * fx - 12 * S;
            posY = wa.Top + 16 * S;
            Point sp;
            if (restoring && SavedPos(slot, out sp))
            {
                Rectangle wr = Native.WorkAt(sp.X, sp.Y - 2);
                SetCX(Clamp(sp.X, wr.Left + 12 * S, wr.Right - 12 * S));
                posY = Clamp(sp.Y - 5 * S, wr.Top + 16 * S, wr.Bottom - 5 * S);   // 从原来的高度轻轻落一下：站在窗口上的，窗口还在就落回标题栏上
            }
            StartFall(Mood.Plain);
            if (Main && !greeted)
            {
                // 第一次见面：用文字打个招呼，以后都只挥挥手
                afterLandSay = "嗨，我是 Clawd！右键点我会冒出快捷按钮，再右键一下是完整菜单，也可以把我拎起来甩~";
                greeted = true;
                SaveSettings();
            }
            else afterLandWave = true;
            nextNap = tStart + 60 + rnd.NextDouble() * 120;
            nextFly = tStart + 60 + rnd.NextDouble() * 180;
            Location = FormPos();

            lastCursor = Cursor.Position;
            timer.Interval = 16;
            timer.Tick += (s, e) =>
            {
                if (!profiling) { Tick(); return; }
                double t0 = clock.Elapsed.TotalMilliseconds;
                Tick();
                profTick += clock.Elapsed.TotalMilliseconds - t0; profTicks++;
                if (now >= profNext)
                {
                    try
                    {
                        File.AppendAllText(Path.Combine(Signal.Dir(), "profile.log"), string.Format(
                            "{0:HH:mm:ss} state={1,-7} ticks={2} avgTick={3:F3}ms paints={4} avgPaint={5:F3}ms px/paint={6} interval={7}\r\n",
                            DateTime.Now, state, profTicks, profTick / Math.Max(1, profTicks), profPaints,
                            profPaint / Math.Max(1, profPaints), profPx / Math.Max(1, profPaints), timer.Interval));
                    }
                    catch { }
                    profTick = profPaint = 0; profTicks = profPaints = 0; profPx = 0; profNext = now + 10;
                }
            };
            timer.Start();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80;        // WS_EX_TOOLWINDOW：不出现在 Alt+Tab
                cp.ExStyle |= 0x08000000;  // WS_EX_NOACTIVATE：点它不抢焦点
                cp.ExStyle |= 0x8;         // WS_EX_TOPMOST
                return cp;
            }
        }
        protected override bool ShowWithoutActivation { get { return true; } }

        // ================================================================ 尺寸 / 坐标
        void ApplySize(bool keepCenter)
        {
            double cx = posX + 12 * S;
            S = Math.Max(2, (int)Math.Round(SizeCells[sizeIdx] * dpiScale));
            uiS = Sprite.BarScale(S, dpiScale);
            // 小号的时候按钮条比原来的窗口宽 / 高：窗口跟着撑大一点（多出来的地方是透明的，点得穿）
            Size barSz = Sprite.BarSize(uiS, BarIcons.Length);
            int formW = Math.Max(FORM_WC * S, barSz.Width + 8);
            int topH = TOP_C * S + Math.Max(0, barSz.Height - Sprite.BarSize(S, BarIcons.Length).Height);
            offX = (formW - CW * S) / 2;
            offY = topH;
            ClientSize = new Size(formW, topH + (CH + BOT_C) * S);
            if (bubbleFont != null) bubbleFont.Dispose();
            if (badgeFont != null) badgeFont.Dispose();
            if (tipFont != null) tipFont.Dispose();
            bubbleFont = new Font("Microsoft YaHei UI", Math.Max(12f, S * 2.7f), FontStyle.Regular, GraphicsUnit.Pixel);
            badgeFont = new Font("Microsoft YaHei UI", Math.Max(11f, S * 2.3f), FontStyle.Bold, GraphicsUnit.Pixel);
            tipFont = new Font("Microsoft YaHei UI", Math.Max(12f, uiS * 2.4f), FontStyle.Regular, GraphicsUnit.Pixel);
            if (keepCenter) posX = cx - 12 * S;
            anchorY = FOOT * S;
            lastXfKey = long.MinValue;
            foreach (PetObj o in objs) o.Frame = -1;
            barRect = Rectangle.Empty;
            dirty = true;
        }

        Point FormPos()
        {
            return new Point((int)Math.Round(posX) - offX, (int)Math.Round(posY) - FOOT * S - offY);
        }

        double CX { get { return posX + 12 * S; } }
        void MoveX(double d) { posX += d; }
        void SetCX(double x) { posX = x - 12 * S; }

        Rectangle WorkArea() { return Native.WorkAt((int)(posX + 12 * S), (int)(posY - S)); }
        Rectangle ScreenArea() { return Native.BoundsAt((int)(posX + 12 * S), (int)(posY - S)); }

        // 当前的变形（窗体坐标）
        Sprite.Affine CurXf()
        {
            return Sprite.MakeXf(S, offX + 12 * S, offY + anchorY - hop + sink * S, anchorY, sq, lean, flip, rot, swing, pivX);
        }

        Rectangle SpriteBox() { return Sprite.XfBounds(CurXf(), CW, CH); }

        Rectangle FxBox()
        {
            if (parts.Count == 0) return Rectangle.Empty;
            int u0 = Sprite.Half(S);
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
            foreach (Particle q in parts)
            {
                int u = q.U > 0 ? q.U : u0;
                int x = offX + (int)q.X, y = offY + (int)q.Y;
                x0 = Math.Min(x0, x); y0 = Math.Min(y0, y);
                x1 = Math.Max(x1, x + q.Shape[0].Length * u); y1 = Math.Max(y1, y + q.Shape.Length * u);
            }
            return Rectangle.FromLTRB(x0 - 2, y0 - 2, x1 + 2, y1 + 2);
        }

        void EmoteAnchor(out int ax, out int headY)
        {
            double sy = 1 + sq, Ay = offY + anchorY - hop + sink * S, ayc = anchorY / S;
            if (state == St.Climb && climbPhase == 1 && onPole && poleSlant)
            {
                // 斜杆上：身子斜着，表情冒在朝上那一头的上方
                ax = offX + 12 * S + wallSide * 4 * S;
                headY = (int)Math.Round(Ay - (8 + 4 * Math.Abs(Math.Sin(rot))) * S - 11 * S);
                return;
            }
            if (state == St.Climb && climbPhase == 1)
            {
                // 在墙上：表情冒在横过来的身子上方、头那一端
                int side = facing >= 0 ? 1 : -1;
                ax = offX + 12 * S - wallSide * S - side * 7 * S;
                headY = (int)Math.Round(Ay - 24 * S + 0.5 * S);
                return;
            }
            ax = offX + 12 * S;
            headY = (int)Math.Round(rot != 0 ? Ay - 20 * S : Ay + sy * S * (4 + curBodyDy - ayc));
        }

        Rectangle EmoteBox()
        {
            if (emoteKind == null || bubble != null) return Rectangle.Empty;
            int ax, hy;
            EmoteAnchor(out ax, out hy);
            return Sprite.EmoteRect(emoteKind, now - emoteBorn, ax, hy, S, facing >= 0 ? 1 : -1);
        }

        Rectangle NoseBox() { return noseOn ? Sprite.NoseRect(CurXf(), noseX, noseY, noseR, S) : Rectangle.Empty; }

        bool HasLaptop() { return state == St.Work || state == St.Think; }

        void SnackPos(out int x, out int y)
        {
            x = (int)Math.Round(offX + 12 * S + facing * S - 2.5 * S);
            y = (int)Math.Round(offY + (FOOT - 3 - curBodyDy) * S + Math.Round(Math.Sin(now * 22)) * 0.5 * S) - 5 * S;
        }
        // 待机小道具（书、琴、扫帚……）：在 BuildPose 里定下来，画在身子前面
        string propKind, lastPropKey; int propFrame;
        Rectangle PropBox()
        {
            Art pa; string[] pr; int ppx, ppy;
            if (propKind == null || !Sprite.Prop(propKind, propFrame, facing, curBodyDy, out pa, out pr, out ppx, out ppy)) return Rectangle.Empty;
            return new Rectangle(offX + ppx * S - 2, offY + ppy * S - 2, pr[0].Length * S + 4, pr.Length * S + 4);
        }

        Rectangle SnackBox()
        {
            if (snack == null || state != St.Eat) return Rectangle.Empty;
            int x, y;
            SnackPos(out x, out y);
            return new Rectangle(x - 2, y - S, 5 * S + 4, 7 * S);
        }

        Rectangle DynBox()
        {
            Rectangle r = Union(SpriteBox(), FxBox());
            r = Union(r, EmoteBox());
            r = Union(r, NoseBox());
            r = Union(r, SnackBox());
            r = Union(r, PropBox());
            if (HasLaptop()) r = Union(r, Sprite.LaptopRect(offX, offY, S));
            if (MoundLevelNow() > 0) r = Union(r, MoundRect());
            return r;
        }

        // 气泡 / 牌子 / 按钮条允许画的范围（不超出当前屏幕）
        void ClampRange(out int minX, out int maxX, out int minY)
        {
            Rectangle sb = ScreenArea();
            Point fp = FormPos();
            int W = ClientSize.Width;
            minX = Math.Max(0, sb.Left - fp.X + 2);
            maxX = Math.Min(W, sb.Right - fp.X - 2);
            minY = Math.Max(0, sb.Top - fp.Y + 2);
        }

        static Rectangle Union(Rectangle a, Rectangle b)
        {
            if (a.IsEmpty) return b;
            if (b.IsEmpty) return a;
            return Rectangle.Union(a, b);
        }

        static double Clamp(double v, double a, double b) { return v < a ? a : (v > b ? b : v); }
        string Pick(string[] a) { return a[rnd.Next(a.Length)]; }
        double R01() { return rnd.NextDouble(); }
        bool Air() { return state == St.Drag || state == St.Fall || state == St.Splat; }
        bool Grounded() { return !Air() && state != St.Land && state != St.Crouch && state != St.Climb && state != St.Pole; }
        // 站着休息、可以随时被别的事打断的状态
        static bool Rest(St s)
        {
            return s == St.Idle || s == St.Sit || s == St.Lie || s == St.Look || s == St.Ponder || s == St.Ledge || s == St.Work
                || s == St.Read || s == St.Guitar || s == St.Music || s == St.Bubbles;
        }
        bool Sleeping() { return state == St.Sleep || state == St.Doze; }

        static string FormatDur(double secs)
        {
            int s = (int)secs;
            if (s < 60) return s + "s";
            if (s < 3600) return (s / 60) + "m " + (s % 60) + "s";
            return (s / 3600) + "h " + (s % 3600 / 60) + "m";
        }

        void Notify(string title, string text)
        {
            try { tray.ShowBalloonTip(6000, title, text, ToolTipIcon.None); } catch { }
        }

        // ================================================================ 主循环
        void Tick()
        {
            now = clock.Elapsed.TotalSeconds;
            double dt = now - lastT; lastT = now;
            if (dt > 0.05) dt = 0.05;
            if (dt < 0) dt = 0;
            stT += dt;

            if (Main && now >= nextSignalPoll) { nextSignalPoll = now + 0.25; PollSignals(); }
            if (now >= nextFullCheck) { nextFullCheck = now + 1.0; CheckFullscreen(); UpdTimers(); }
            if (Main && !extrasSpawned && now > 1.5)
            {
                // 上次开着几只，这次也叫出来（一只一只掉下来）
                extrasSpawned = true;
                for (int i = 1; i < Math.Min(MAX_PETS, savedPets); i++) AddPet(false);
                restoring = false;   // 之后再叫出来的照常从天上掉
            }
            if (Main && now >= nextPosSave) { nextPosSave = now + 20; SavePositions(false); }
            if (hiddenForFullscreen) return;
            if (Main && now >= nextTopmost)
            {
                // 隔一会儿把大家重新提到最前面（别的置顶窗口可能盖过来）。由第一只按固定顺序统一来：
                // 以前每只各自隔 2.5 秒把自己提到最上面，叠在一起的两只会时不时换前后、闪一下
                nextTopmost = now + 2.5;
                bool busy = false;
                foreach (PetForm f in All) if (f.menu.Visible || f.dreamForm != null) busy = true;   // 菜单开着 / 在做梦：别把小人盖到上面
                if (!busy) ReassertTopmost();
            }
            if (now >= nextIdleCheck)
            {
                nextIdleCheck = now + 0.5;
                idleSecs = Native.IdleSeconds();
                if (idleSecs > 300) activeSince = now;
            }
            if (now >= nextCpu) { nextCpu = now + 2; SampleCpu(); }
            if (now >= nextBattery) { nextBattery = now + 30; CheckBattery(); }
            {
                int hr = DateTime.Now.Hour;   // 夜里没精神
                energy += ((hr >= 23 || hr < 6 ? 0.3 : 0.55) - energy) * dt / 240;
            }

            TrackCursor(dt);
            UpdateEyes();
            UpdateParticles(dt);
            if (emoteKind != null && now >= emoteUntil) ClearEmote();
            UpdateBubble();
            if (pomoMode != 0)
            {
                int secs = (int)Math.Ceiling((pomoEnd - DateTime.Now).TotalSeconds);
                if (secs != lastPomoSecs) { lastPomoSecs = secs; badgeDirty = true; }
            }
            if (health && cpuHigh >= 3 && now >= nextSweat && Grounded() && !Sleeping()) { nextSweat = now + 1.3; SpawnSweat(); }

            if (mouseDown && !dragging && now - downT > 0.35) BeginDrag();
            if (state == St.Drag && (Control.MouseButtons & MouseButtons.Left) == 0) { mouseDown = false; EndDrag(); }
            if (dragging) FollowDrag();
            if (heldObj != null) UpdHeldObj();
            if (now >= nextFly)
            {
                nextFly = now + 150 + R01() * 240;
                if (FindObj("fly") == null) SpawnFly();
            }
            UpdBar();
            if (!barOn) CheckReach();

            switch (state)
            {
                case St.Idle: UpdIdle(); break;
                case St.Walk: UpdWalk(dt); break;
                case St.Chase: UpdChase(dt); break;
                case St.Think: UpdThink(); break;
                case St.Work: UpdWork(); break;
                case St.Sit: case St.Lie: case St.Look: case St.Ledge:
                    if (CheckAfk()) break;
                    if (stT >= stDur) { if (state == St.Lie) StartStretch(); else StartIdle(); }
                    break;
                case St.Ponder:
                    if (CheckAfk()) break;
                    if (stT >= stDur) { if (R01() < 0.4) { Emo("idea", 1.2); Jump(Mood.Happy, S * 50, true); } else StartIdle(); }
                    break;
                case St.Refuse: if (stT >= stDur) StartIdle(); break;
                case St.Sleep: UpdSleep(); break;
                case St.Doze:   // 打瞌睡：头越来越低，猛地一点又醒过来，几次之后睡着
                    if (stT >= stDur)
                    {
                        if (--dozeNods > 0) { sqV += 5; jerkUntil = now + 0.35; SetState(St.Doze, 1.8 + R01() * 1.4); }
                        else StartSleep(SleepMode.Nap);
                    }
                    break;
                case St.Fall: UpdFall(dt); break;
                case St.Splat:   // 糊在墙上，一会儿滑下来
                    if (stT >= stDur) { pendingDizzy = true; velX = -splatSide * S * 25; velY = S * 10; StartFall(Mood.Plain); }
                    break;
                case St.Crouch: if (stT >= stDur) Launch(pendingMood, pendingJumpV, pendingJumpVX, pendingIgnore); break;
                case St.Aim:
                    if (stT >= stDur) { if (hasAim) { hasAim = false; Launch(Mood.Happy, aimVY, aimVX, aimIgnore); } else StartIdle(); }
                    break;
                case St.Land: if (stT >= stDur) AfterLand(); break;
                case St.Happy:
                    if (mood == Mood.Celebrate && now >= nextFx) { nextFx = now + 0.3; SpawnSparkle(); }
                    if (stT >= stDur) StartIdle();
                    break;
                case St.Giggle:
                    if (now >= nextFx) { nextFx = now + 0.25; if (R01() < 0.5) SpawnNote(); else SpawnHeart(); }
                    if (stT >= stDur) StartHappy(0.5);
                    break;
                case St.Dance:
                    if (now >= nextFx) { nextFx = now + 0.45; SpawnNote(); }
                    if (stT >= stDur) StartIdle();
                    break;
                case St.Wave: case St.Stretch: case St.Scratch: case St.Reach:
                    if (stT >= stDur)
                    {
                        if (state == St.Reach) ReachJump();
                        else if (state == St.Stretch && wakeLook) { wakeLook = false; StartLook(); Emo("q", 1.4); }   // 梦到吃的：醒来先找找
                        else StartIdle();
                    }
                    break;
                case St.Alert:
                    if (now >= nextFx) { nextFx = now + 1.2; SpawnBang(); }
                    if (stT >= stDur) { alertSticky = false; StartIdle(); }
                    break;
                case St.Dizzy:
                    if (now >= nextFx) { nextFx = now + 0.12; SpawnOrbitStar(); }
                    if (stT >= stDur) StartIdle();
                    break;
                case St.Yawn: if (stT >= stDur) StartSleep(sleepMode); break;
                case St.Sneeze:
                    if (!sneezed && stT >= 0.5)
                    {
                        sneezed = true;
                        sqV += 8; leanV += facing * 4;
                        Spawn(Sprite.Puff, Sprite.PuffC, (facing > 0 ? 22 : 0) * S, 9 * S, facing * S * 10, 0, 0.5, 0);
                        Spawn(Sprite.Puff, Sprite.PuffC, (facing > 0 ? 22 : 0) * S, 11 * S, facing * S * 8, S * 2, 0.5, 0);
                    }
                    if (stT >= stDur) StartIdle();
                    break;
                case St.Eat: UpdEat(); break;
                case St.Spin:
                    flip = Math.Cos(stT / 0.45 * Math.PI * 2);
                    if (stT >= stDur) { flip = 1; if (R01() < 0.4) StartDizzy(1.1); else StartHappy(0.6); }
                    break;
                case St.Roll: UpdRoll(); break;
                case St.Exercise:
                    if (stT >= stDur) { Emo("sweat", 1.4); energy = Math.Max(0.1, energy - 0.12); StartIdle(); }
                    break;
                case St.Teeter: UpdTeeter(); break;
                case St.Peek: UpdPeek(); break;
                case St.Kick: UpdKick(); break;
                case St.Climb: UpdClimb(dt); break;
                case St.Pole: UpdPoleClimb(dt); break;
                case St.Sweep: UpdSweep(dt); break;
                case St.Read: case St.Guitar: case St.Lift: case St.Castle: case St.Bubbles: case St.Music: case St.Plane: UpdFun(); break;
                case St.Burrow: UpdBurrow(dt); break;
                case St.Shake:
                    // 抖掉身上的沙子（叠高高时被压得晃起来：不撒沙子，晃完冒汗）
                    if (!shakeNoSand && now >= nextFx && stT < 0.45)
                    {
                        nextFx = now + 0.07;
                        Spawn(Sprite.Crumb, R01() < 0.5 ? Sprite.SandA : Sprite.SandB, (4 + R01() * 16) * S, (5 + R01() * 8) * S, (R01() - 0.5) * S * 16, -S * (2 + R01() * 4), 0.6, S * 60);
                    }
                    if (stT >= stDur) { if (shakeNoSand) { shakeNoSand = false; Emo("sweat", 1.3); } StartIdle(); }
                    break;
                case St.Match: UpdMatchPet(dt); break;
                case St.Chat: UpdChat(); break;
                case St.Watch: UpdWatch(); break;
                case St.Pat: UpdPat(); break;
                case St.HighFive:
                    if (!fiveDone && stT >= 0.3)
                    {
                        // 手碰到一起：啪！
                        fiveDone = true; sqV -= 3; leanV -= facing * 1.5;
                        if (fiveLead)
                        {
                            Snd("clap", SND_PLAY);
                            Spawn(Sprite.Star, Sprite.StarC, (facing > 0 ? 22 : -2) * S, 3 * S, 0, -S * 5, 0.8, 0);
                            SpawnSparkle(); SpawnSparkle();
                        }
                    }
                    if (stT >= stDur) { StartHappy(0.6); if (fiveLead) Emo("star", 1.2); }
                    break;
            }
            if (match != null && match.Host == this) UpdMatch(dt);   // 我在主持比赛
            if (office != null && office.Host == this) UpdOffice();   // 大家在我这儿上班
            if (!Air() && state != St.Climb && state != St.Pole && state != St.Match) CheckSupport(dt);   // 比赛时站在球场上（可能在屏幕半空），位置由比赛定
            // 停下来时不是急刹车，而是滑两步慢慢停住
            if (gvx != 0 && state != St.Walk && state != St.Chase && state != St.Sweep && state != St.Match)
            {
                if (Grounded() && state != St.Roll && state != St.Peek) { GroundMove(0, dt, 9); ClampToSurface(); }
                else gvx = 0;
            }

            UpdDragVel(dt);
            UpdLimbs(dt);
            UpdSwing(dt);
            UpdSpring(dt);
            UpdObjs(dt);
            UpdNose();

            Point fp = FormPos();
            if (fp != Location) Location = fp;
            BuildPose();
            UpdEmoteGeom();
            UpdCursorShape();

            // 变形后的三个角落到了哪个像素：变了才重画身体
            Sprite.Affine xf = CurXf();
            long key = (long)Math.Round(xf.E * 2) * 7919 + (long)Math.Round(xf.F * 2) * 104729
                + (long)Math.Round((xf.E + xf.A * 24) * 2) * 31 + (long)Math.Round((xf.F + xf.B * 24) * 2) * 1299709
                + (long)Math.Round((xf.E + xf.C * 18) * 2) * 15485863 + (long)Math.Round((xf.F + xf.D * 18) * 2) * 179424673;
            if (key != lastXfKey) { lastXfKey = key; spriteDirty = true; }
            string propKey = propKind == null ? null : propKind + propFrame + "/" + facing + "/" + curBodyDy;
            if (propKey != lastPropKey) { lastPropKey = propKey; spriteDirty = true; }
            Invalidations();

            // 平时都跑满 60 帧（顺滑）；只有睡着了、四周也没东西在动时降到 30 帧省电
            bool quietNow = state == St.Sleep && !mouseDown && !dragHover && heldObj == null && dreamForm == null
                && Math.Abs(sqV) < 0.3 && Math.Abs(leanV) < 0.3 && !ObjsMoving();
            int want = quietNow ? 33 : 16;
            if (timer.Interval != want) timer.Interval = want;
        }

        // 只重绘变了的区域：身体 + 粒子 + 表情 + 鼻涕泡 + 零食；气泡转圈；番茄钟牌子
        void Invalidations()
        {
            if (bubble != null || pomoMode != 0 || barOn)
            {
                int mn, mx, my;
                ClampRange(out mn, out mx, out my);
                if (mn != lastMinX || mx != lastMaxX || my != lastMinY) { lastMinX = mn; lastMaxX = mx; lastMinY = my; dirty = true; }   // 贴着屏幕边，气泡要挪位置
            }
            if (dirty)
            {
                dirty = false; spriteDirty = false; badgeDirty = false;
                lastDynBox = DynBox();
                Invalidate();
                return;
            }
            Rectangle dyn = DynBox();
            if (spriteDirty || parts.Count > 0 || (emoteKind != null && bubble == null) || noseOn || (snack != null && state == St.Eat) || dyn != lastDynBox
                || (state != St.Burrow && MoundLevelNow() > 0))
            {
                Rectangle u = Union(lastDynBox, dyn);
                if (!u.IsEmpty) Invalidate(u);
            }
            spriteDirty = false;
            lastDynBox = dyn;
            if (bubble != null && bubbleSpin)
            {
                int frame = Sprite.SpinSeq[((int)(now / 0.11)) % Sprite.SpinSeq.Length];
                string txt = ThinkText();
                if (txt != lastThinkText) { lastThinkText = txt; Invalidate(Rectangle.Inflate(lastBubbleRect, 4 * S, S)); }
                else if (frame != lastSpinFrame)
                {
                    int pu = Math.Max(2, S / 2);
                    Invalidate(new Rectangle(spinAt.X, spinAt.Y, 7 * pu, 7 * pu));
                }
                lastSpinFrame = frame;
            }
            if (badgeDirty)
            {
                badgeDirty = false;
                if (lastBadgeRect.IsEmpty) Invalidate(); else Invalidate(Rectangle.Inflate(lastBadgeRect, 2 * S, S));
            }
        }

        void UpdTimers()
        {
            if (sessions.Count > 0)
            {
                List<string> dead = new List<string>();
                foreach (KeyValuePair<string, CcSession> kv in sessions) if (now - kv.Value.Last > 20 * 60) dead.Add(kv.Key);
                foreach (string k in dead) sessions.Remove(k);
                ccWorking = sessions.Count > 0;
            }
            if (pomoMode != 0 && DateTime.Now >= pomoEnd)
            {
                if (pomoMode == 1)
                {
                    Notify("专注完成！", "休息 5 分钟吧，起来走走~");
                    Snd("pomo", SND_YOU);   // 你自己定的番茄钟、提醒：全屏、夜里也响（开着音效的话）
                    StartPomo(2, 5, false);
                    if (Grounded()) { ResetPlans(); Celebrate(false, "专注完成！休息 5 分钟~"); }
                    else Say("专注完成！休息 5 分钟~", 4);
                }
                else
                {
                    pomoMode = 0; dirty = true;
                    Notify("休息结束", "继续加油！");
                    Snd("rest", SND_YOU);
                    if (Grounded()) { ResetPlans(); StartAlert("休息结束，继续加油！", 6, false); }
                    else Say("休息结束，继续加油！", 4);
                }
            }
            for (int i = reminders.Count - 1; i >= 0; i--)
            {
                if (DateTime.Now < reminders[i].Due) continue;
                string t = reminders[i].Text;
                reminders.RemoveAt(i);
                Notify("Clawd 提醒你", t);
                Snd("remind", SND_YOU);
                if (Grounded()) { ResetPlans(); StartAlert(t, 30, true); } else Say(t, 10);
            }
            if (Main && health && pomoMode == 0 && now - activeSince > 50 * 60 && now - lastHealthNag > 50 * 60 && Grounded() && !Sleeping())
            {
                lastHealthNag = now;
                ResetPlans();
                Snd("alert", SND_NEWS, 0.6, false);
                StartAlert(Pick(HealthLines), 8, false);
            }
            if (health && cpuHigh >= 3 && now - lastCpuNag > 600 && bubble == null && Grounded() && !Sleeping())
            {
                lastCpuNag = now;
                Say("电脑好烫…CPU " + (int)(cpu * 100) + "%", 3.5);
            }
        }

        void SampleCpu()
        {
            long idle, kern, user;
            if (!Native.GetSystemTimes(out idle, out kern, out user)) return;
            bool first = cpuKern0 == 0;
            long di = idle - cpuIdle0, dk = kern - cpuKern0, du = user - cpuUser0;
            cpuIdle0 = idle; cpuKern0 = kern; cpuUser0 = user;
            if (first) return;
            long total = dk + du;
            if (total <= 0) return;
            cpu = Clamp((double)(total - di) / total, 0, 1);
            cpuHigh = cpu > 0.9 ? cpuHigh + 1 : 0;
        }

        void CheckBattery()
        {
            if (!health || !Main) return;
            try
            {
                PowerStatus ps = SystemInformation.PowerStatus;
                BatteryChargeStatus cs = ps.BatteryChargeStatus;
                if ((cs & BatteryChargeStatus.NoSystemBattery) != 0 || cs == BatteryChargeStatus.Unknown || ps.PowerLineStatus != PowerLineStatus.Offline)
                {
                    lastBattNag = -1;
                    return;
                }
                int pct = (int)Math.Round(ps.BatteryLifePercent * 100);
                if (pct <= 20 && (lastBattNag < 0 || pct <= lastBattNag - 5))
                {
                    lastBattNag = pct;
                    Notify("电量低", "只剩 " + pct + "% 了，快充电！");
                    if (Grounded()) { ResetPlans(); StartAlert("电量只剩 " + pct + "%，快充电！", 6, false); }
                }
            }
            catch { }
        }

        // ================================================================ 果冻弹簧 / 手脚 / 钟摆
        void UpdDragVel(double dt)
        {
            if (state == St.Drag && trail.Count >= 2)
            {
                double[] a = trail[0], b = trail[trail.Count - 1];
                double vx = 0, vy = 0;
                if (clock.Elapsed.TotalSeconds - b[0] < 0.06 && b[0] - a[0] > 0.005) { vx = (b[1] - a[1]) / (b[0] - a[0]); vy = (b[2] - a[2]) / (b[0] - a[0]); }
                double pv = dragVX;
                dragVX += (vx - dragVX) * Math.Min(1, dt * 12);
                dragVY += (vy - dragVY) * Math.Min(1, dt * 12);
                if (dt > 0) accX += ((dragVX - pv) / dt - accX) * Math.Min(1, dt * 20);
                shake += (Math.Sqrt(dragVX * dragVX + dragVY * dragVY) - shake) * Math.Min(1, dt * 6);
                if (shake > S * 120) shakeAcc += dt;   // 一直使劲晃：落地会晕
            }
            else if (state == St.Drag) { dragVX *= Math.Max(0, 1 - dt * 12); dragVY *= Math.Max(0, 1 - dt * 12); accX *= Math.Max(0, 1 - dt * 12); shake *= Math.Max(0, 1 - dt * 4); }
            else { dragVX = dragVY = 0; accX = 0; shake *= Math.Max(0, 1 - dt * 4); }
        }

        // 腿像钟摆：被拎着甩的时候跟着晃，一组跟得快、一组慢半拍；手跟着乱挥
        void UpdLimbs(double dt)
        {
            double tgt = 0;
            if (state == St.Drag) tgt = Clamp(-dragVX / (S * 110.0) - swingV * 0.9, -2.4, 2.4);
            else if (state == St.Fall || state == St.Splat) tgt = Clamp(-velX / (S * 140.0), -2, 2);
            legAV += (70 * (tgt - legA) - 4 * legAV) * dt; legA = Clamp(legA + legAV * dt, -2.4, 2.4);
            legBV += (40 * (tgt - legB) - 2.6 * legBV) * dt; legB = Clamp(legB + legBV * dt, -2.4, 2.4);
            double busy = state == St.Drag ? shake / (S * 40.0) : 0;
            legPh += dt * (state == St.Drag ? Math.Min(10, 2.5 + busy * 1.5) : state == St.Fall ? 6 : 2);
            armPh += dt * (state == St.Drag ? Math.Min(12, 4 + busy * 2) : 5);
            // 拎着的时候腿一直在空中蹬（越晃蹬得越快）
            kickPh += dt * (state == St.Drag ? Math.Min(11, 4.5 + busy * 1.2) : state == St.Fall ? 5 : 0);
        }

        // 钟摆：抓住的点是支点，手往右一拉身子往左甩，然后来回荡几下
        void UpdSwing(double dt)
        {
            if (state == St.Drag)
            {
                double arm = -grabDY - 8 * S;                       // 支点到身体重心的距离（抓脚的话重心在上面）
                double Lp = Math.Max(5.0 * S, Math.Abs(arm)), gain = arm > 0 ? 1 : 0.35;
                int n = 3; double hh = dt / n;
                for (int i = 0; i < n; i++)
                {
                    double a = -(S * 520.0 / Lp) * Math.Sin(swing) - 2.2 * swingV + gain * (accX / Lp) * Math.Cos(swing);
                    swingV += a * hh; swing += swingV * hh;
                }
                if (swing > 1) { swing = 1; if (swingV > 0) swingV *= -0.3; }
                if (swing < -1) { swing = -1; if (swingV < 0) swingV *= -0.3; }
                pivX += (grabDX - pivX) * Math.Min(1, dt * 20);
            }
            else if (swing != 0 || swingV != 0)
            {
                swingV += (-70 * swing - 10 * swingV) * dt; swing += swingV * dt;   // 松手后很快摆正
                if (Math.Abs(swing) < 0.003 && Math.Abs(swingV) < 0.02) swing = swingV = 0;
                pivX += (0 - pivX) * Math.Min(1, dt * 10);
            }
            else pivX = 0;
        }

        void UpdSpring(double dt)
        {
            double tq = 0, tl = 0, hp = 0;
            switch (state)
            {
                case St.Idle:
                    tq = 0.025 * Math.Sin(now * 2.4);
                    if (now - lastCursorMove < 2)
                    {
                        double dx = lastCursor.X - CX, r = 30.0 * S;
                        if (Math.Abs(dx) < r) tl = Math.Sign(dx) * 0.07 * (1 - Math.Abs(dx) / r);   // 好奇地凑过去
                    }
                    break;
                case St.Walk: case St.Chase: case St.Match:
                    {
                        // 并拢那一帧身子微微抬起、迈步那一帧落下：一颠一颠；往前走时身子往前倾
                        bool moving = Math.Abs(gvx) > 1.5 * S, passing = (((int)Math.Floor(walkPh)) & 1) == 1;
                        tq = moving ? (passing ? 0.04 : -0.03) : 0;
                        hp = moving && passing ? 0.4 * S : 0;
                        tl = Clamp(gvx / (S * 110.0), -0.16, 0.16);
                    }
                    break;
                case St.Think: case St.Work: tq = -0.03 + 0.012 * Math.Sin(now * 12); break;   // 噼里啪啦敲键盘
                case St.Sit: tq = -0.04 + 0.02 * Math.Sin(now * 2); tl = lookX * 0.03; break;
                case St.Ledge: tq = 0.02 * Math.Sin(now * 2.4); break;
                case St.Lie: tq = -0.08 + 0.02 * Math.Sin(now * 1.5); break;
                case St.Look: { int lx, ly; CurLook(out lx, out ly); tl = lx * 0.09; tq = ly < 0 ? 0.06 : 0; } break;
                case St.Ponder: tq = 0.02 * Math.Sin(now * 2); tl = 0.04; break;
                case St.Scratch: tq = 0.03; tl = 0.05 * Math.Sin(now * 18); break;
                case St.Spin: tq = 0.05; break;
                case St.Exercise: { bool up = (((int)Math.Floor(stT / 0.3)) & 1) == 0; tq = up ? 0.14 : -0.1; hp = up ? 1.6 * S : 0; } break;
                case St.Teeter: tl = facing * (0.3 + 0.12 * Math.Sin(now * 13)); tq = -0.04; break;
                case St.Peek: tl = -peekSide * 0.1; break;
                case St.Chat: tq = now < talkUntil ? 0.03 * Math.Sin(now * 14) : 0.01 * Math.Sin(now * 2); tl = facing * 0.05; break;   // 往对方那边倾一点
                case St.Watch: tq = -0.04 + 0.02 * Math.Sin(now * 2); tl = (watchWhat == St.Guitar || watchWhat == St.Music) ? 0.09 * Math.Sin(now * Math.PI / 0.9) : facing * 0.04; break;
                case St.Pat: tl = facing * 0.08; tq = 0.02 * Math.Sin(now * 18); break;
                case St.Kick: tq = stT < kickAt ? (stT < kickAt - 0.1 ? 0 : -0.1) : 0.1; tl = stT < kickAt - 0.1 ? 0 : -facing * 0.1; break;
                case St.Burrow:
                    if (burrowPhase == 0) { tl = 0.14 * Math.Sin(now * 22); tq = -0.04 + 0.03 * Math.Sin(now * 30); }   // 扭来扭去往下钻
                    else if (burrowPhase == 1) tq = 0.015 * Math.Sin(now * 2);
                    break;
                case St.Shake: tl = 0.18 * Math.Sin(now * 34); tq = 0.03; break;
                case St.Climb:
                    if (climbPhase == 1 && Math.Abs(climbV) > 1.5 * S)
                    {
                        // 和走路一样一颠一颠（转了 90° 以后就是一下一下离开墙面），身子往爬的方向倾
                        bool passing = (((int)Math.Floor(walkPh)) & 1) == 1;
                        tq = passing ? 0.05 : -0.03;
                        tl = facing * Clamp(Math.Abs(climbV) / (S * 110.0), 0, 0.16);
                    }
                    else if (climbPhase == 1) tq = 0.02 * Math.Sin(now * 2.2);
                    break;
                case St.Reach: tq = 0.2 + 0.04 * Math.Sin(now * 12); hp = Math.Abs(Math.Sin(now * 8)) * 0.8 * S; tl = Math.Sign(lastCursor.X - CX) * 0.05; break;
                case St.Aim: tq = -0.15; tl = facing * 0.08; break;
                case St.Splat: tq = 0.35; break;
                case St.Refuse: tl = 0.14 * Math.Sin(now * 26); tq = -0.04; break;
                case St.Giggle: tq = 0.04 * Math.Sin(now * 20); tl = 0.12 * Math.Sin(now * 26); break;
                case St.Sleep: tq = -0.03 + 0.04 * Math.Sin(now * 1.7); if (sleepLedge) tl = facing * 0.06; break;
                case St.Doze: { double k = Math.Min(1, stT / stDur); tq = -0.04 - 0.12 * k; tl = facing * 0.14 * k * k; } break;
                case St.Drag: tq = 0.12 + 0.03 * Math.Sin(kickPh * Math.PI * 2); tl = Clamp(dragVX / (S * 700.0), -0.2, 0.2); break;
                case St.Fall: tq = Clamp(Math.Abs(velY) / (S * 1500.0), 0, 0.22); tl = Clamp(velX / (S * 900.0), -0.3, 0.3); break;
                case St.Crouch: tq = -0.32; break;
                case St.Happy:
                    tq = 0.04 * Math.Sin(now * 9);
                    if (mood == Mood.Celebrate) hp = Math.Abs(Math.Sin(now * Math.PI / 0.3)) * 1.2 * S;
                    break;
                case St.Dance:
                    {
                        double ph = Math.Sin(now * Math.PI / 0.44);
                        tq = -0.05 + 0.12 * Math.Abs(ph); tl = 0.2 * ph; hp = Math.Abs(ph) * 2.2 * S;
                    }
                    break;
                case St.Wave: tl = 0.05 * Math.Sin(now * Math.PI / 0.22); break;
                case St.Alert:
                    {
                        double ph = Math.Abs(Math.Sin(now * Math.PI / 0.36));
                        tq = 0.1 * ph; hp = ph * 1.6 * S;
                    }
                    break;
                case St.Dizzy: tl = 0.16 * Math.Sin(now * 5); tq = -0.05; break;
                case St.Yawn: tq = 0.2 * Math.Min(1, stT / 0.6); break;
                case St.Stretch: tq = stT < 0.6 ? 0.24 : 0; break;
                case St.Sneeze: if (stT < 0.5) { tq = -0.12; tl = -facing * 0.1; } break;
                case St.Eat: tq = -0.06 + 0.14 * Math.Sin(stT * 22); break;
            }
            if (dragHover) { tq = 0.14 + 0.03 * Math.Sin(now * 10); tl = 0; }
            if (state == St.Match && hopH > hp) hp = hopH;   // 比赛里蹦起来（庆祝、头球、扑救）
            hop += (hp - hop) * Math.Min(1, dt * 25);

            const double K = 330, C = 11, KL = 190, CL = 9;
            int n = 3; double h = dt / n;
            for (int i = 0; i < n; i++)
            {
                double a = K * (tq - sq) - C * sqV; sqV += a * h; sq += sqV * h;
                double b = KL * (tl - lean) - CL * leanV; leanV += b * h; lean += leanV * h;
            }
            if (sq < -0.5) { sq = -0.5; if (sqV < 0) sqV = 0; }
            if (sq > 0.6) { sq = 0.6; if (sqV > 0) sqV = 0; }
            lean = Clamp(lean, -0.7, 0.7);
            double ay = dragging ? FOOT * S + grabDY : FOOT * S;
            anchorY += (ay - anchorY) * Math.Min(1, dt * 14);
        }

        // ================================================================ 状态
        void SetState(St s, double dur)
        {
            if (state == St.Sleep && s != St.Sleep) { PopNose(); if (emoteKind == "dream") ClearEmote(); }
            if (s != St.Spin) flip = 1;
            if (s != St.Roll && s != St.Climb) rot = 0;
            if (s != St.Climb) onPole = false;
            if (state == St.Burrow && s != St.Burrow) LeaveBurrowFx();   // 不管因为什么被打断（喂食、信号、拎起来……）：先从沙里钻出来
            if (emoteKind == "dots" && s != St.Ponder && s != St.Work) ClearEmote();
            if (s != St.Think && bubbleSpin) ClearBubble();
            if (s != St.Eat && snack != null) { snack = null; feedQueue = 0; }   // 吃到一半被打断：零食就不要了
            if ((s == St.Work || s == St.Think) && !(state == St.Work || state == St.Think)) { nextWorkFx = now + 2 + R01() * 3; nextWorkEmote = now + 3 + R01() * 4; nextSteam = now + 0.8; }
            if (debugLog && s != state) Dbg(state + " -> " + s + (chaseObj != null ? " (" + chaseObj.Kind + ")" : ""));
            if (state == St.Chat && s != St.Chat) { chatWith = null; chatLead = false; talkUntil = 0; }
            if (state == St.Watch && s != St.Watch) watchPet = null;
            if (state == St.Pat && s != St.Pat) patPet = null;
            if (s != St.Work && s != St.Think) atDesk = false;
            if (inBed && s != St.Yawn && s != St.Sleep) { inBed = false; bedOffT = now; }   // 起床了：被子掀下去
            St prev = state;
            state = s; stT = 0; stDur = dur;
            dirty = true;
            if (All.Count > 1 && IsShow(s) && !IsShow(prev)) MaybeAudience(s);   // 开始表演：伙伴可能过来看
        }

        void ResetPlans()
        {
            chaseObj = null; hasWalkTarget = false; walkThen = null; walkSpeed = 0; afterLandThen = null; hopsLeft = 0; hasAim = false;
            bedBound = false;   // 去睡觉的路上被打断：床位让出来
        }

        void StartIdle()
        {
            if (office != null && office.EndT < 0 && DeskIdx() >= 0) { GoToDesk(); return; }   // 在上班：回自己的工位
            if (ccWorking) { StartThink(true); return; }
            SetState(St.Idle, 1 + R01() * 2.2);
        }

        void StartWalk(int dir, double dur, double speed)
        {
            if (stay) { StartIdle(); return; }   // 待在这儿别动：不走
            facing = dir; hasWalkTarget = false; walkThen = null; walkSpeed = speed;
            SetState(St.Walk, dur);
        }

        void WalkTo(double x, double speed, Action then)
        {
            if (stay) { if (then != null) then(); else StartIdle(); return; }
            double lo, hi;
            SurfaceSpan(out lo, out hi);
            walkTarget = Clamp(x, lo, hi); hasWalkTarget = true; walkSpeed = speed; walkThen = then;
            facing = walkTarget >= CX ? 1 : -1;
            SetState(St.Walk, 30);
        }

        void Arrive()
        {
            Action f = walkThen;
            hasWalkTarget = false; walkThen = null; walkSpeed = 0;
            if (f != null) f(); else StartIdle();
        }

        void StartThink(bool persistent)
        {
            thinkPersistent = persistent;
            SetState(St.Think, persistent ? 1e9 : 3 + R01() * 2.5);
            ShowThink();
        }

        void ShowThink()
        {
            thinkVerb = Pick(Verbs);
            SayRaw(thinkVerb, true, thinkPersistent ? 1e9 : stDur);
            nextVerb = now + 5 + R01() * 3;
        }

        string ThinkText()
        {
            string t = thinkVerb + "…";
            if (thinkPersistent && sessions.Count > 0)
            {
                double start = double.MaxValue;
                foreach (CcSession s in sessions.Values) if (s.Start < start) start = s.Start;
                t += " " + FormatDur(now - start);
                if (sessions.Count > 1) t += " ×" + sessions.Count;
            }
            return t;
        }

        void StartSit() { if (plat != IntPtr.Zero && R01() < 0.55) StartLedge(); else SetState(St.Sit, 4 + R01() * 7); }
        void StartLedge() { SetState(St.Ledge, 5 + R01() * 9); }
        void StartLie(double d) { SetState(St.Lie, d > 0 ? d : 6 + R01() * 8); }
        void StartLook()
        {
            lookSeq.Clear();
            double t = 0;
            int n = 3 + rnd.Next(3);
            int[] xs = { -1, 1, 0 }, ys = { 0, 0, -1, 1 };
            for (int i = 0; i < n; i++) { t += 0.5 + R01() * 0.6; lookSeq.Add(new double[] { t, xs[rnd.Next(3)], ys[rnd.Next(4)] }); }
            SetState(St.Look, t + 0.4);
        }
        void CurLook(out int lx, out int ly)
        {
            lx = 0; ly = 0;
            foreach (double[] e in lookSeq) if (stT >= e[0]) { lx = (int)e[1]; ly = (int)e[2]; }
        }
        void StartPonder() { SetState(St.Ponder, 3 + R01() * 2.5); Emo("dots", stDur); }
        void StartHops() { hopsLeft = 1 + rnd.Next(3); Jump(Mood.Happy, S * (38 + R01() * 14), true); }
        void StartSpin() { SetState(St.Spin, R01() < 0.5 ? 0.9 : 1.35); }
        void StartExercise() { SetState(St.Exercise, 2.6 + R01() * 2); }
        void StartStretch() { SetState(St.Stretch, 1.1); }
        void StartScratch() { SetState(St.Scratch, 1.2 + R01() * 0.6); if (R01() < 0.4) Emo("q", 1.2); }
        void StartTeeter() { SetState(St.Teeter, 1.4 + R01() * 0.6); }
        void StartPeek(int side) { peekSide = side; SetState(St.Peek, 3.5 + R01() * 3); }
        void StartWork() { SetState(St.Work, 15 + R01() * 25); }

        void StartRoll()
        {
            double lo, hi, need = 46 * S, cx = CX;
            SurfaceSpan(out lo, out hi);
            int dir = facing;
            if (dir > 0 && hi - cx < need) dir = -1;
            else if (dir < 0 && cx - lo < need) dir = 1;
            if ((dir > 0 ? hi - cx : cx - lo) < need) { StartHops(); return; }   // 地方不够，改成蹦两下
            rollDir = dir; facing = dir; rollX0 = cx;
            SetState(St.Roll, 0.95);
        }

        // ================================================================ 上床睡觉
        // 真睡（你离开电脑 / 菜单里「睡一会儿」）：慢吞吞走到屏幕右下角，先到的睡最右边，一只挨一只躺下，盖上小被子。
        // 自己随便打的小盹还是原地睡；「待在这儿别动」时原地盖被子睡
        bool inBed, bedBound; int bedSlot = -1; double bedT = -10, bedOffT = -10, bedDur; SleepMode bedMode;

        void GoToBed(SleepMode mode, double dur)
        {
            ResetPlans();
            bedMode = mode; bedDur = dur;
            if (stay || dreamForm != null) { inBed = true; StartYawn(mode, dur); return; }
            bedSlot = PickBedSlot();
            bedBound = true;
            ReassignBedSlots();
            Emo("zz", 1.4);
            Dbg("to bed slot " + bedSlot);
            BedWalk();
        }

        // 第几个床位：0 = 最右边；被别的（正在去 / 已经躺下的）占了就往左排
        // 一起睡：都去右下角排队躺下。离角落近的先走先躺，排得整整齐齐、不用挤过去
        void BedTimeAll()
        {
            if (match != null) match.Host.EndMatch(false);
            List<PetForm> list = new List<PetForm>(All);
            list.Sort((a, b) => b.CX.CompareTo(a.CX));
            foreach (PetForm f in list)
            {
                if (f.IsDisposed || f.hiddenForFullscreen || f.dragging || f.mouseDown || f.inBed || f.bedBound || f.dreamForm != null || !f.Grounded()) continue;
                f.GoToBed(SleepMode.Nap, 90 + f.R01() * 60);
            }
        }

        int PickBedSlot()
        {
            for (int i = 0; i < MAX_PETS; i++)
            {
                bool taken = false;
                foreach (PetForm f in All)
                    if (f != this && f.bedSlot == i && (f.bedBound || f.inBed)) { taken = true; break; }
                if (!taken) return i;
            }
            return 0;
        }

        double BedX(int i) { Rectangle wa = WorkArea(); return wa.Right - (14 + 23 * Math.Max(0, i)) * S; }

        // 正在去睡觉的几只按现在站的位置重新排床位：最右边的睡最右边那张，路上谁也不用从谁身上挤过去。
        // 已经躺下的不动
        static void ReassignBedSlots()
        {
            List<int> free = new List<int>();
            for (int i = 0; i < MAX_PETS; i++)
            {
                bool used = false;
                foreach (PetForm f in All) if (f.inBed && f.bedSlot == i) { used = true; break; }
                if (!used) free.Add(i);
            }
            List<PetForm> going = new List<PetForm>();
            foreach (PetForm f in All) if (f.bedBound && !f.IsDisposed) going.Add(f);
            going.Sort((a, b) => b.CX.CompareTo(a.CX));
            for (int k = 0; k < going.Count && k < free.Count; k++)
            {
                PetForm f = going[k];
                if (f.bedSlot == free[k]) continue;
                f.bedSlot = free[k];
                if (f.state == St.Walk && f.hasWalkTarget)
                {
                    double lo, hi;
                    f.SurfaceSpan(out lo, out hi);
                    f.walkTarget = Clamp(f.BedX(f.bedSlot), lo, hi);
                }
            }
        }

        void BedWalk()
        {
            double x = BedX(bedSlot);
            if (plat != IntPtr.Zero)
            {
                // 站在窗口上：就地穿过标题栏往下跳，顺势往床那边飘
                afterLandThen = BedWalk;
                Jump(Mood.Plain, S * 35, true, Clamp((x - CX) * 0.6, -S * 70, S * 70), true);
                return;
            }
            if (basePet != null) { HopDown(x >= CX ? 1 : -1, BedWalk); return; }
            if (Math.Abs(CX - x) > 2 * S)
            {
                WalkTo(x, Math.Abs(CX - x) > 120 * S ? S * 15 : S * 11, IntoBed);   // 困了走得慢；离得远就走快点
                stDur = 180;   // 去睡觉的路上不会走着走着停下来发呆
                return;
            }
            IntoBed();
        }

        void IntoBed()
        {
            bedBound = false;
            if (bedMode == SleepMode.Afk && idleSecs < 1.0) { StartIdle(); return; }   // 走到半路你回来了：不睡了
            double x = BedX(bedSlot);
            if (plat == IntPtr.Zero && basePet == null && Math.Abs(CX - x) <= 4 * S) SetCX(x);
            facing = -1;
            inBed = true;
            StartYawn(bedMode, bedDur);
        }

        void StartYawn(SleepMode mode, double dur)
        {
            ResetPlans();
            sleepMode = mode; napDur = dur;
            sleepLedge = !inBed && plat != IntPtr.Zero && R01() < 0.45;   // 在窗口上：坐在边上、腿垂下去睡（盖被子时不这么睡）
            SetState(St.Yawn, 1.6);
            Emo("zz", 1.6);
            Snd("yawn", SND_PLAY, 0.8, true);
        }

        void StartSleep(SleepMode mode)
        {
            sleepMode = mode;
            if (inBed) bedT = now;   // 躺下了：开始往上拉被子
            SetState(St.Sleep, 1e9);
            nextFx = now + 0.8;
            noseT0 = now + 1.5; noseLen = 5 + R01() * 4; noseOn = false;
            nextTwitch = now + 8 + R01() * 8; nextDream = now + 5 + R01() * 6; lastDream = null;
        }

        // 自己打个盹：一半是坐着打瞌睡、点几下头才睡着，一半是打个哈欠直接躺下
        void StartNap(double dur)
        {
            ResetPlans();
            nextNap = now + 180 + R01() * 180;
            double d = dur > 0 ? dur : 25 + R01() * 45;
            if (plat == IntPtr.Zero && R01() < 0.5)
            {
                sleepMode = SleepMode.Nap; napDur = d; sleepLedge = false; dozeNods = 2 + rnd.Next(2);
                SetState(St.Doze, 2.2 + R01());
            }
            else StartYawn(SleepMode.Nap, d);
        }

        // ---------------- 晚上自动睡觉：到了睡觉时间都去右下角盖被子睡，夜里动鼠标也不醒，到起床时间陆续起来
        // 半夜被你叫醒（点它、菜单里「叫醒它」、拎起来）：陪你待一会儿，5 分钟没人理就回去接着睡
        internal static bool nightSleep = true;
        internal static int bedHour = 23, wakeHour = 7;
        internal static readonly int[] BedHours = { 21, 22, 23, 0, 1 }, WakeHours = { 6, 7, 8, 9, 10 };
        double nightAwakeUntil = -1, nightWakeAt = -1;

        static bool NightNow()
        {
            if (!nightSleep) return false;
            DateTime d = DateTime.Now;
            int t = d.Hour * 60 + d.Minute, b = bedHour * 60, w = wakeHour * 60;
            return b > w ? (t >= b || t < w) : (t >= b && t < w);
        }

        // 夜里你碰了它：先别回去睡
        void NightTouched() { if (NightNow()) nightAwakeUntil = Math.Max(nightAwakeUntil, now + 300); }

        void Wake(bool startled)
        {
            NightTouched();
            if (startled) { Snd("startle", SND_YOU, 1, true); SpawnBang(); sqV += 5; Jump(Mood.Plain, S * 70, false); }
            else { StartStretch(); wakeLook = lastDream != null && lastDream.Food && R01() < 0.7; }
        }

        // 鼻涕泡破掉
        void PopNose()
        {
            if (!noseOn) return;
            noseOn = false;
            Snd("pop", SND_PLAY, 0.6, false);
            for (int i = 0; i < 6; i++)
            {
                double a = i / 6.0 * Math.PI * 2;
                Spawn(Sprite.Crumb, Sprite.NosePop, (noseX + Math.Cos(a) * noseR) * S, (noseY + Math.Sin(a) * noseR) * S,
                    Math.Cos(a) * S * 12, Math.Sin(a) * S * 12 - S * 3, 0.35, S * 40);
            }
        }

        // 鼻涕泡跟着呼吸一鼓一鼓地变大：两眼正中间、眼睛下面一点，往下鼓
        void UpdNose()
        {
            if (state != St.Sleep || rot != 0 || now < noseT0) { noseOn = false; return; }
            double k = Math.Min(1, (now - noseT0) / noseLen);
            noseR = (0.7 + 1.9 * Math.Min(1, k * 1.1)) * (1 + 0.12 * Math.Sin(now * 3.4));
            int side = facing >= 0 ? 1 : -1, bt = 4 + curBodyDy;
            noseX = 12 + side * 0.3;
            noseY = bt + 5 + noseR * 0.95;
            noseOn = true;
        }

        void SleepTwitch()
        {
            double r = R01();
            if (r < 0.35) { PopNose(); noseT0 = now + 1; facing = -facing; leanV += facing * 3; sqV += 2; }   // 翻个身（鼻涕泡顺手破了）
            else if (r < 0.65) { twitchUntil = now + 0.3; sqV += 3; }                                       // 手一抽
            else Emo(R01() < 0.5 ? "note" : "heart", 1.4);                                                  // 说梦话
        }

        // 做梦：头上冒云朵泡泡，点泡泡可以钻进梦里玩
        void StartDream()
        {
            Art d = Sprite.Dreams[rnd.Next(Sprite.Dreams.Length)];
            lastDream = d;
            emoteKind = "dream"; emoteIcon = d; emoteBorn = now; emoteUntil = now + 6;
        }

        void StartFall(Mood m)
        {
            plat = IntPtr.Zero;
            if (basePet != null) { ignorePet = basePet; ignorePetUntil = now + 0.7; basePet = null; }   // 从别的 Clawd 头上下来：一会儿内不会又落回它头上
            mood = m;
            impact = 0;
            SetState(St.Fall, 1e9);
        }

        // 离开地面：边走边跳带着一点往前冲的劲；从窗口上往下跳时先记下它，免得又落回去
        void Launch(Mood m, double vy, double vx, bool ignore)
        {
            if (plat != IntPtr.Zero && ignore) ignorePlat = plat;
            sitOff = 0;
            velX = vx != 0 ? vx : gvx * 0.6; gvx = 0;
            velY = -vy; sqV += 6;
            StartFall(m);
        }

        // 起跳：先蹲下蓄力（crouch），再弹出去
        void Jump(Mood m, double v, bool crouch) { Jump(m, v, crouch, 0, false); }
        void Jump(Mood m, double v, bool crouch, double vx, bool ignore)
        {
            if (crouch && Grounded())
            {
                pendingJumpV = v; pendingJumpVX = vx; pendingIgnore = ignore; pendingMood = m; mood = m;
                SetState(St.Crouch, 0.09);
                return;
            }
            Launch(m, v, vx, ignore);
        }

        void StartHappy(double dur) { SetState(St.Happy, dur); nextFx = now; }
        void StartDance() { StartDance(true); }
        void StartDance(bool lead)
        {
            SetState(St.Dance, 3.2); nextFx = now;
            if (!lead) return;
            // 旁边闲着的伙伴也跟着跳（共用一个时钟，步子是齐的）
            foreach (PetForm f in All)
                if (f != this && f.Near(this, 60) && Rest(f.state) && f.state != St.Work && f.R01() < 0.75)
                {
                    f.ResetPlans(); f.facing = CX >= f.CX ? 1 : -1; f.StartDance(false); f.Emo("note", 1.2);
                }
        }
        void StartWave(double dur) { SetState(St.Wave, dur); }
        void StartAlert(string text, double dur, bool sticky) { SetState(St.Alert, dur); nextFx = now; alertSticky = sticky; Say(text, dur); }
        void StartDizzy(double dur) { Snd("dizzy", dur >= 1.8 ? SND_YOU : SND_PLAY, dur >= 1.8 ? 1 : 0.6, true); SetState(St.Dizzy, dur); nextFx = now; if (dur >= 1.8) CallComforter(); }   // 被戳晕 / 摔晕：伙伴会跑过来
        void StartSneeze() { sneezed = false; SetState(St.Sneeze, 1.1); Snd("sneeze", SND_PLAY, 0.8, true); }   // 「哈——」在前、「啾」正好对上喷出来那一下

        void Celebrate(bool big, string text)
        {
            Say(text, 3.5);
            for (int i = 0; i < 4; i++) SpawnSparkle();
            Jump(Mood.Celebrate, S * (big ? 115 : 90), true);
        }

        void Startle() { SpawnBang(); sqV += 6; Jump(Mood.Plain, S * 55, false); }

        void Summon()
        {
            Point cp = Cursor.Position;
            Rectangle wa = Native.WorkAt(cp.X, cp.Y);
            posX = Clamp(cp.X, wa.Left + 12 * S, wa.Right - 12 * S) - 12 * S;
            posY = wa.Top + 16 * S;
            velX = 0; velY = 0;
            mouseDown = dragging = false;
            ResetPlans(); sitOff = 0;
            StartFall(Mood.Plain);
            afterLandWave = true;
            Snd("hello", SND_YOU, 1, true);
            if (hiddenForFullscreen) { hiddenForFullscreen = false; Show(); }
        }

        static readonly bool noAfk = Environment.GetEnvironmentVariable("CLAWD_NOAFK") == "1";   // 测试用：你不在电脑前也不睡
        bool CheckAfk()
        {
            if (ccWorking) return false;
            if (NightNow() && now >= nightAwakeUntil) { GoToBed(SleepMode.Night, 0); return true; }   // 到睡觉时间了
            if (!noAfk && idleSecs > 180) { GoToBed(SleepMode.Afk, 0); return true; }
            return false;
        }

        bool WorkHours()
        {
            DateTime d = DateTime.Now;
            return d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday && d.Hour >= 9 && d.Hour < 18;
        }

        // 待机时挑下一件事做：精力足就爱蹦跶，没精神就坐着趴着；「待在这儿别动」时只做原地的事
        void ChooseNext()
        {
            if (TryWantPole()) return;
            List<KeyValuePair<double, Action>> opts = new List<KeyValuePair<double, Action>>();
            Action<double, Action> add = (w, f) => { if (w > 0) opts.Add(new KeyValuePair<double, Action>(w, f)); };
            double e = energy, cx = CX;
            bool move = !stay;
            Rectangle wa = WorkArea();
            if (move) add(24, () => StartWalk(rnd.Next(2) == 0 ? -1 : 1, 2 + R01() * 4, e > 0.7 && R01() < 0.3 ? S * 15 : S * 9));
            add(6, () => SetState(St.Idle, 1.5 + R01() * 2.5));
            add(8 + 10 * (1 - e), StartSit);
            add(6, StartLook);
            add(5, StartPonder);
            add(2 + 5 * e, StartHops);
            add(1 + 3 * e, StartSpin);
            if (move) add(1 + 3 * e, StartRoll);
            add(1 + 3 * e, StartExercise);
            add(3, StartStretch);
            add(3, StartScratch);
            add(2 + 6 * (1 - e), () => StartLie(0));
            add(2 * e, StartDance);
            add(1.2, StartSneeze);
            if (plat == IntPtr.Zero) add(2.5 + 4 * (1 - e), StartBurrow);   // 只在地面（任务栏上沿）挖；原地的，「别动」时也可以
            add((2.5 + 2 * e) * (WorkHours() ? 2.5 : 1), StartWork);   // 工作日白天更爱对着小电脑办公
            // 更多待机
            add(3 + 2 * (1 - e), StartRead);
            add(2 + 2 * e, StartGuitar);
            add(2.5 + 2 * e, StartMusic);
            add(3, StartBubbles);
            add(1 + 3 * e, StartLift);
            if (move) add(2.5, StartSweep);
            if (plat == IntPtr.Zero) add(2.5 + e, StartCastle);
            add(2 + 3 * e, StartPlane);
            if (move && plat == IntPtr.Zero && now >= poleCooldownUntil)
            {
                // 架根木杆爬到别的窗口上（够不着的高窗口更想爬）
                double ptx;
                int pside;
                Plat pp = PoleTarget(out ptx, out pside);
                if (pp != null) add((posY - pp.T > 95 * S ? 12 : 6) + 6 * e, () => PoleTo(pp, ptx, pside));
            }
            if (now - lastCursorMove < 8)
            {
                add(3, () => { facing = lastCursor.X >= CX ? 1 : -1; StartWave(1.4); Emo("note", 1.4); });
                if (move) add(4, () => WalkTo(lastCursor.X + (lastCursor.X > CX ? -14 : 14) * S, S * 14, () => SetState(St.Sit, 4 + R01() * 4)));
            }
            if (now >= nextNap)
            {
                int hr = DateTime.Now.Hour;   // 没精神、夜里更容易打个盹
                add((e < 0.45 ? 6 : 1.5) + (hr >= 22 || hr < 7 ? 4 : 0), () => StartNap(0));
            }
            if (move)
            {
                double tx;
                Plat cl = ClimbTarget(out tx);
                if (cl != null) add(6 + 8 * e, () => ClimbTo(cl, tx));
            }
            if (plat != IntPtr.Zero) { if (move) add(5, () => HopDown(0, null)); add(5, StartLedge); }
            else if (move && (cx < wa.Left + 40 * S || cx > wa.Right - 40 * S))
            {
                int side = cx < (wa.Left + wa.Right) / 2.0 ? -1 : 1;
                if (EdgeFree(side)) add(3, () => WalkTo(side < 0 ? wa.Left + 11 * S : wa.Right - 11 * S, S * 9, () => StartPeek(side)));
            }
            if (move && plat == IntPtr.Zero)
            {
                // 走到近的那一边，扒上墙往上爬（只爬外面没有别的显示器的那条边）
                int side = cx < (wa.Left + wa.Right) / 2.0 ? -1 : 1;
                if (!SideFree(side)) side = -side;
                if (SideFree(side) && now >= climbCooldownUntil)
                {
                    int sd = side;
                    double dist = (sd < 0 ? cx - wa.Left : wa.Right - cx) - 11 * S;   // 身子边到屏幕边还有多远
                    add(dist <= 40 * S ? 25 : 2 + 4 * e, () => WalkTo(sd < 0 ? wa.Left + 11 * S : wa.Right - 11 * S, S * (e > 0.6 ? 14 : 11), () => StartClimb(sd)));
                }
            }
            if (move)
            {
                PetObj ball = AnyBall();
                if (ball != null && !ball.Held)
                    add(8 + 16 * e, () => { StartChase(ball, 2 + rnd.Next(4)); if (All.Count > 1 && R01() < 0.5) InviteToBall(ball); });
                PetObj fly = FindObj("fly");
                if (fly != null && !fly.OnHead) add(16, () => StartChase(fly, -1));
            }
            if (All.Count > 1)
            {
                // 有伙伴：叠高高、打招呼、击掌
                PetForm f = NearestFriend(500);
                if (f != null)
                {
                    PetForm top = f.TowerTop();
                    if (move && now - lastStackT > 18 && top.CanBeClimbed(this) && Math.Abs(top.HeadTop() - posY) < 80 * S)
                        add(10 + 8 * e, () => StackOnto(f));
                    if (f.Near(this, 70) && now - lastGreet > 40 && !f.Sleeping()) add(5, () => GreetFriend(f));
                    if (move && now - lastGreet > 25 && f.basePet == null && f.Rider() == null && Rest(f.state) && !f.Sleeping())
                        add(4 + 4 * e, () => HighFiveWith(f));
                    // 四只都在：偶尔摆开小足球场踢一场
                    if (All.Count == MAX_PETS && match == null && move && now >= nextMatchAll && pomoMode != 1 && AnyBall() == null)
                        add(5 + 5 * e, () => { if (!StartMatch(true)) { nextMatchAll = now + 40; StartIdle(); } });
                    // 走过去聊几句
                    if (move && now >= nextChatAll && pomoMode != 1 && basePet == null && Rider() == null && f.Near(this, 160) && FreeToTalk(f))
                        add(8 + 3 * e, () => StartChat(f));
                    // 自己拿个球出来，叫伙伴一起踢
                    if (move && plat == IntPtr.Zero && basePet == null && Rider() == null && now >= nextKickAbout && AnyBall() == null
                        && f.plat == IntPtr.Zero && f.FreeForBall(WorkArea()))
                        add(3 + 4 * e, () => KickAbout(f));
                }
            }
            double total = 0;
            foreach (KeyValuePair<double, Action> o in opts) total += o.Key;
            double r = R01() * total;
            foreach (KeyValuePair<double, Action> o in opts) { r -= o.Key; if (r <= 0) { o.Value(); return; } }
            StartIdle();
        }

        // 屏幕这一边外面没有别的显示器、也没有任务栏挡着：可以探头
        bool EdgeFree(int side)
        {
            Rectangle wa = WorkArea(), mb = ScreenArea();
            int y = (int)posY - 4 * S;
            if (side < 0) return wa.Left == mb.Left && !Native.HasMonitorAt(mb.Left - 2, y);
            return wa.Right == mb.Right && !Native.HasMonitorAt(mb.Right + 1, y);
        }

        void UpdIdle()
        {
            if (InMatch()) { SetState(St.Match, 1e9); return; }   // 比赛还没完（被拎走又落地了）：回场上
            if (now < braceUntil) return;   // 有伙伴要跳到我头上 / 过来击掌：站着等它（先别睡）
            if (CheckAfk()) return;
            if (follow && !stay && basePet == null && Rider() == null && now - lastCursorMove < 6)
            {
                double dx = lastCursor.X - CX;
                if (Math.Abs(dx) > 20 * S) { StartWalk(dx > 0 ? 1 : -1, 10, S * 9); return; }
            }
            if (stT >= stDur)
            {
                if (basePet != null) ChooseRiding();
                else if (Rider() != null) ChooseCarrying();
                else ChooseNext();
            }
        }

        // ================================================================ 好几只 Clawd 一起玩：叠高高、打招呼、击掌、一起跳舞

        static int AccOf(int slot) { return slot == 1 ? 1 : slot == 2 ? 2 : slot == 3 ? Sprite.SeasonAcc() : 0; }
        static string AccName(int acc)
        {
            switch (acc)
            {
                case 1: return "戴眼镜的";
                case 2: return "戴帽子的";
                case 3: return "别着小花的";
                case 4: return "顶着西瓜的";
                case 5: return "顶着枫叶的";
                case 6: return "围着围巾的";
                default: return "Clawd";
            }
        }

        // 再叫一只出来（最多 4 只）
        static void AddPet(bool save)
        {
            if (All.Count >= MAX_PETS) return;
            for (int s = 1; s < MAX_PETS; s++)
            {
                bool used = false;
                foreach (PetForm f in All) if (f.slot == s) used = true;
                if (used) continue;
                PetForm p = new PetForm(s);
                p.Show();
                if (save) p.Snd("hello", SND_YOU, 1, true);   // 你叫出来的才响（启动时按上次摆回来的不响）
                if (save && All.Count > 0) All[0].SaveSettings();
                return;
            }
        }

        // 送走这一只（第一只不能送走，它管着托盘图标）
        void Dismiss()
        {
            if (Main) return;
            Snd("bye", SND_YOU, 1, true);
            Cleanup();
            All.Remove(this);
            if (All.Count > 0) All[0].SaveSettings();
            Close();
        }

        bool Near(PetForm o, double cells) { return Math.Abs(o.CX - CX) < cells * S && Math.Abs(o.posY - posY) < 30 * S; }

        PetForm Rider() { foreach (PetForm f in All) if (f.basePet == this) return f; return null; }

        PetForm TowerTop() { PetForm t = this; for (int i = 0; i < MAX_PETS; i++) { PetForm r = t.Rider(); if (r == null) break; t = r; } return t; }

        int TowerHeight() { int h = 1; PetForm b = basePet; while (b != null && h < MAX_PETS) { h++; b = b.basePet; } return h; }

        // 我是不是踩在 o 上面（一层层往下找）
        bool Above(PetForm o) { PetForm b = basePet; for (int i = 0; i < MAX_PETS && b != null; i++) { if (b == o) return true; b = b.basePet; } return false; }

        // 头顶在哪（屏幕坐标）：跟着身子的果冻弹簧一起一颤一颤
        double HeadTop() { return posY - hop - (13 - curBodyDy) * S * (1 + sq); }

        // 现在能不能让别人站在头上：站稳了、没在打滚 / 挖沙 / 被压得晃
        bool CanCarry()
        {
            return !IsDisposed && All.Contains(this) && !hiddenForFullscreen && Grounded()
                && state != St.Roll && state != St.Burrow && state != St.Shake && state != St.Peek && state != St.Spin && state != St.Match;
        }

        bool CanBeClimbed(PetForm climber)
        {
            if (this == climber || !CanCarry() || Rider() != null || Above(climber) || climber.Above(this) || TowerHeight() >= MAX_PETS) return false;
            // 头顶上面还得有地方站一只（贴着屏幕顶的标题栏上叠不了）
            double h = HeadTop();
            return h - 16 * S >= Native.WorkAt((int)CX, (int)h).Top;
        }

        PetForm NearestFriend(double cells)
        {
            PetForm best = null; double bd = cells * S;
            foreach (PetForm f in All)
            {
                if (f == this || f.IsDisposed || f.hiddenForFullscreen) continue;
                double d = Math.Abs(f.CX - CX);
                if (d < bd) { bd = d; best = f; }
            }
            return best;
        }

        // 掉下来时脚下有没有别的 Clawd 的头可以站
        PetForm FindPetBelow(double prevFeet, double newFeet, out double top)
        {
            PetForm best = null; top = double.MaxValue;
            foreach (PetForm f in All)
            {
                if (f == this || (f == ignorePet && now < ignorePetUntil) || !f.CanBeClimbed(this)) continue;
                if (Math.Abs(f.CX - CX) > 7 * S) continue;
                double h = f.HeadTop();
                if (h >= prevFeet - 2 && h <= newFeet + 1 && h < top) { top = h; best = f; }
            }
            return best;
        }

        // 叠高高：上面那只的窗口要在下面那只前面
        void RaiseRiders()
        {
            PetForm r = Rider();
            for (int i = 0; i < MAX_PETS && r != null; i++)
            {
                if (r.IsHandleCreated) Native.SetWindowPos(r.Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                r = r.Rider();
            }
        }

        // 有伙伴要过来（跳到头上 / 击掌）：停下手里的事，转过去等着
        void Brace(PetForm who, double secs)
        {
            braceFor = who; braceUntil = now + secs;
            if (Rest(state) || state == St.Walk || state == St.Sweep || state == St.Happy || state == St.Wave)
            {
                ResetPlans(); gvx = 0;
                facing = who.CX >= CX ? 1 : -1;
                SetState(St.Idle, secs);
            }
        }

        // 走到伙伴旁边，跳到它头上（它头上已经有人了就跳到最上面那只头上）
        void StackOnto(PetForm f)
        {
            if (stay || f == null) { StartIdle(); return; }
            PetForm top = f.TowerTop();
            if (!top.CanBeClimbed(this)) { StartIdle(); return; }
            lastStackT = now;
            int side = top.CX >= CX ? 1 : -1;
            double standX = top.CX - side * 16 * S;
            f.Brace(this, 8);
            if (Math.Abs(standX - CX) < 3 * S) { StackJump(top); return; }
            WalkTo(standX, S * 13, () => StackJump(top));
        }

        void StackJump(PetForm f)
        {
            if (!All.Contains(f) || !f.CanBeClimbed(this)) { Emo("q", 1.2); StartIdle(); return; }
            PetForm root = f; while (root.basePet != null) root = root.basePet;
            root.Brace(this, 3);
            double tx = f.CX, top = f.HeadTop();
            double G = S * 520.0, up0 = posY - top;   // >0：头顶比我的脚高
            double apex = Math.Max(up0, 0) + 9 * S;
            double vy = Math.Sqrt(2 * G * apex);
            double tFlight = vy / G + Math.Sqrt(2 * Math.Max(0, apex - up0) / G);
            double vx = Clamp((tx - CX) / Math.Max(0.2, tFlight), -S * 320, S * 320);
            facing = vx >= 0 ? 1 : -1;
            stackJoy = true;
            hasAim = true; aimVY = vy; aimVX = vx == 0 ? 0.001 : vx; aimIgnore = false;
            attnX = tx; attnY = top; attnUntil = now + 1.2;
            SetState(St.Aim, 0.3 + R01() * 0.3);
        }

        // 站在别人头上时：东张西望、挥挥手、跳个舞，站一会儿就跳下去
        void ChooseRiding()
        {
            double ride = now - rideSince, e = energy;
            List<KeyValuePair<double, Action>> opts = new List<KeyValuePair<double, Action>>();
            Action<double, Action> add = (w, f) => { if (w > 0) opts.Add(new KeyValuePair<double, Action>(w, f)); };
            add(6, () => SetState(St.Idle, 1.5 + R01() * 2));
            add(6, StartLook);
            add(4, () => { facing = -facing; StartWave(1.4); Emo("note", 1.4); });
            add(2 + 3 * e, () => StartDance(false));
            add(3, () => SetState(St.Sit, 3 + R01() * 4));
            add(2, StartStretch);
            add(2, StartMusic);
            add(1.5, StartBubbles);
            add(ride < 6 ? 0.5 : 2 + ride / 3, () => HopDown(0, null));   // 站久了就跳下去
            double total = 0;
            foreach (KeyValuePair<double, Action> o in opts) total += o.Key;
            double r = R01() * total;
            foreach (KeyValuePair<double, Action> o in opts) { r -= o.Key; if (r <= 0) { o.Value(); return; } }
            StartIdle();
        }

        // 头上站着伙伴时：乖乖托着，抬头看看，偶尔驮着走两步；托久了会被压得晃起来把它抖下去
        void ChooseCarrying()
        {
            PetForm r0 = Rider();
            double held = r0 != null ? now - r0.rideSince : 0;
            List<KeyValuePair<double, Action>> opts = new List<KeyValuePair<double, Action>>();
            Action<double, Action> add = (w, f) => { if (w > 0) opts.Add(new KeyValuePair<double, Action>(w, f)); };
            add(10, () => SetState(St.Idle, 2 + R01() * 2.5));
            add(5, StartLook);
            add(3, () => { StartWave(1.2); Emo("note", 1.2); });
            add(3, StartPonder);
            if (!stay) add(3, () => StartWalk(rnd.Next(2) == 0 ? -1 : 1, 1.2 + R01() * 1.8, S * 5));   // 驮着慢慢走
            add(held < 8 ? 0.3 : 1 + held / 8, Topple);
            double total = 0;
            foreach (KeyValuePair<double, Action> o in opts) total += o.Key;
            double r = R01() * total;
            foreach (KeyValuePair<double, Action> o in opts) { r -= o.Key; if (r <= 0) { o.Value(); return; } }
            StartIdle();
        }

        // 被压得晃起来：头上的伙伴站不住掉下去
        void Topple()
        {
            ResetPlans();
            shakeNoSand = true;
            SetState(St.Shake, 0.7);
            nextFx = now;
            sqV -= 3;
        }

        // 走过去打个招呼：两只都挥手
        void GreetFriend(PetForm f)
        {
            lastGreet = now; f.lastGreet = now;
            facing = f.CX >= CX ? 1 : -1;
            StartWave(1.4); Emo("note", 1.4);
            if (Rest(f.state) && !f.Sleeping() && f.basePet == null)
            {
                f.ResetPlans(); f.facing = -facing; f.StartWave(1.4); f.Emo("heart", 1.4);
            }
        }

        // 走到伙伴跟前击个掌
        void HighFiveWith(PetForm f)
        {
            if (stay || f == null) { StartIdle(); return; }
            lastGreet = now;
            int side = f.CX >= CX ? 1 : -1;
            f.Brace(this, 7);
            WalkTo(f.CX - side * 20 * S, S * 13, () => DoHighFive(f));
        }

        void DoHighFive(PetForm f)
        {
            if (!All.Contains(f) || !f.Grounded() || f.Sleeping() || Math.Abs(f.CX - CX) > 26 * S) { Emo("q", 1.2); StartIdle(); return; }
            int side = f.CX >= CX ? 1 : -1;
            ResetPlans(); facing = side; fiveDone = false; fiveLead = true;
            SetState(St.HighFive, 1.0);
            f.ResetPlans(); f.braceUntil = 0; f.facing = -side; f.fiveDone = false; f.fiveLead = false;
            f.SetState(St.HighFive, 1.0);
            f.lastGreet = now;
        }

        // ================================================================ 小足球场（半俯视，像电视转播的镜头）：屏幕下方铺开一块斜着看的球场，2 对 2
        // 场地坐标：x 顺着球场长边（屏幕像素；近处那条边线上跟屏幕 x 一样），z 往里的深度（0 = 近边线，Dz = 远边线）。
        // 投到屏幕上：越往里越高（深度压扁成一半），也越窄一点（近大远小）；小人和球是像素画，不跟着缩放。
        // 四只真的跑上去：每只按深度挪到球场上对应的高度，近的盖住远的；球有高度和影子，由主持比赛的那只来算

        class SoccerMatch
        {
            public PetForm Host;   // 由它的计时器推着比赛走（球也由它算）
            public readonly List<PetForm> Players = new List<PetForm>();
            public readonly Dictionary<PetForm, int> Team = new Dictionary<PetForm, int>();      // 0 = 守左边球门、往右攻；1 = 反过来
            public readonly Dictionary<PetForm, bool> Keeper = new Dictionary<PetForm, bool>();  // 守门员（一队两只时离自家球门近的那只）
            public double L, R, GL, GR, Mid, Floor, Top, Dz, Zc, GoalHalf, BarH, NetD, BoxD, BoxHalf;   // 场地范围、两条底线、中线、近边线在屏幕上的 y、远边线的深度……
            public int U;   // 一格多大（跟小人一样）
            public readonly int[] Score = new int[2];
            public int Phase;   // 0 = 站位，1 = 踢，2 = 进球庆祝，3 = 终场庆祝，4 = 走回场边
            public double PhaseT0, StartT, LastTouchT;
            public bool BallOn; public double BX, BZ, BH, BVX, BVZ, BVH, Roll;   // 球：场地坐标 + 离地多高，以及速度
            public PetForm LastKicker; public int ScoredBy = -1, Winner = -1;
            public bool ShotLive, ShotSave; public int ShotTeam; public double ShotUntil;   // 正在飞的一脚射门：哪队射的、注定扑不扑得住
            public PetForm Holder; public double HoldUntil;                                 // 守门员抱着球
            public ObjWin FieldWin, BallWin, ShadowWin, BoardWin;
            public int BoardU, BallFrame = -1;
            public string Order = "";   // 上一次排好的前后顺序
        }
        static SoccerMatch match;
        internal const int MATCH_WIN = 5;
        const double MATCH_SECS = 150, PK = 0.5, PSHRINK = 0.18;   // 深度压扁成一半；远边线比近边线窄 18%
        static double nextMatchAll = 150;   // 下次可以自己摆场地的时间
        internal const string PitchPal = "G=#6DB25A,g=#62A64F,W=#F4F4EE,P=#FFFFFF,N=#A9B2BD,n=#DCE2E8";
        internal const string BoardPal = "K=#3A3631,W=#F7F4EC,A=#FF8C7A,B=#86C2FF";
        internal const string ShadowPal = "k=#447F37";
        internal static readonly string[] BallShadowRows = { ".kkkk.", "kkkkkk", ".kkkk." };
        static readonly int[] TeamRing = { unchecked((int)0xFFFF6F61), unchecked((int)0xFF5AA9FF) };   // 脚下那一圈：红队 / 蓝队
        static readonly int RingShade = unchecked((int)0xFF447F37);
        static readonly string[] Digits = {   // 3x5 像素数字
            "WWW,W.W,W.W,W.W,WWW", ".W.,WW.,.W.,.W.,WWW", "WWW,..W,WWW,W..,WWW", "WWW,..W,WWW,..W,WWW", "W.W,W.W,WWW,..W,..W",
            "WWW,W..,WWW,..W,WWW", "WWW,W..,WWW,W.W,WWW", "WWW,..W,..W,..W,..W", "WWW,W.W,WWW,W.W,WWW", "WWW,W.W,WWW,..W,WWW" };
        static readonly string[] GoalLines = { "进啦！", "GOAL！", "好球！", "漂亮！" };

        static double PitchP(SoccerMatch m, double z) { return 1 - PSHRINK * z / m.Dz; }
        static double PitchSX(SoccerMatch m, double x, double z) { return m.Mid + (x - m.Mid) * PitchP(m, z); }
        static double PitchSY(SoccerMatch m, double z) { return m.Floor - z * PK; }

        // 球场的像素画（一格 u 像素）：草坪（剪草的条纹顺着透视）、白线、中圈、两个禁区、两头立着的球门
        static string[] PitchRows(SoccerMatch m, int u)
        {
            int w = (int)Math.Ceiling((m.R - m.L) / u), h = (int)Math.Ceiling((m.Floor - m.Top) / u);
            char[][] g = new char[h][];
            for (int j = 0; j < h; j++)
            {
                g[j] = new char[w];
                double z = (m.Floor - (m.Top + (j + 0.5) * u)) / PK;
                for (int i = 0; i < w; i++)
                {
                    char c = '.';
                    if (z >= 0 && z <= m.Dz)
                    {
                        double x = m.Mid + (m.L + (i + 0.5) * u - m.Mid) / PitchP(m, z);
                        if (x >= m.L && x <= m.R) c = (((int)Math.Floor((x - m.Mid) / (16.0 * m.U) + 100)) & 1) == 0 ? 'G' : 'g';
                    }
                    g[j][i] = c;
                }
            }
            // 场地坐标上的一点（up = 离地多高，屏幕像素）落在哪一格
            Action<double, double, double, char> dot = (x, z, up, c) =>
            {
                int i = (int)Math.Floor((PitchSX(m, x, z) - m.L) / u), j = (int)Math.Floor((PitchSY(m, z) - up - m.Top) / u);
                if (j == h) j = h - 1;
                if (j >= 0 && j < h && i >= 0 && i < w) g[j][i] = c;
            };
            Action<double, double, double, double, double, double, char> line = (x0, z0, h0, x1, z1, h1, c) =>
            {
                double len = Math.Max(Math.Abs(x1 - x0), Math.Max(Math.Abs(z1 - z0) * PK, Math.Abs(h1 - h0)));
                int n = Math.Max(1, (int)Math.Ceiling(len / (u * 0.5)));
                for (int k = 0; k <= n; k++) { double t = (double)k / n; dot(x0 + (x1 - x0) * t, z0 + (z1 - z0) * t, h0 + (h1 - h0) * t, c); }
            };
            line(m.GL, 0, 0, m.GR, 0, 0, 'W'); line(m.GL, m.Dz, 0, m.GR, m.Dz, 0, 'W');   // 两条边线
            line(m.GL, 0, 0, m.GL, m.Dz, 0, 'W'); line(m.GR, 0, 0, m.GR, m.Dz, 0, 'W');   // 两条底线
            line(m.Mid, 0, 0, m.Mid, m.Dz, 0, 'W');                                         // 中线
            double cr = 18.0 * m.U;
            for (int k = 0; k < 180; k++) { double a = k * Math.PI / 90; dot(m.Mid + Math.Cos(a) * cr, m.Zc + Math.Sin(a) * cr, 0, 'W'); }   // 中圈
            dot(m.Mid, m.Zc, 0, 'W');
            for (int s = 0; s < 2; s++)
            {
                double gx = s == 0 ? m.GL : m.GR, dir = s == 0 ? 1 : -1, bx = gx + dir * m.BoxD;
                line(gx, m.Zc - m.BoxHalf, 0, bx, m.Zc - m.BoxHalf, 0, 'W');
                line(bx, m.Zc - m.BoxHalf, 0, bx, m.Zc + m.BoxHalf, 0, 'W');
                line(bx, m.Zc + m.BoxHalf, 0, gx, m.Zc + m.BoxHalf, 0, 'W');
                dot(gx + dir * m.BoxD * 0.65, m.Zc, 0, 'W');   // 点球点
            }
            // 球门：后面兜着网（后网、顶网、两边的侧网），最后画门柱和横梁盖在网上
            for (int s = 0; s < 2; s++)
            {
                double gx = s == 0 ? m.GL : m.GR, back = gx + (s == 0 ? -1 : 1) * m.NetD;
                double z0 = m.Zc - m.GoalHalf, z1 = m.Zc + m.GoalHalf, bh = m.BarH, nb = m.BarH * 0.75;
                for (int k = 0; k <= 8; k++)
                {
                    double zz = z0 + (z1 - z0) * k / 8;
                    line(back, zz, 0, back, zz, nb, 'n');
                    line(gx, zz, bh, back, zz, nb, 'n');
                }
                for (int k = 1; k <= 3; k++)
                {
                    line(back, z0, nb * k / 4, back, z1, nb * k / 4, 'n');
                    line(gx, z1, bh * k / 4, back, z1, nb * k / 4, 'n');
                    line(gx, z0, bh * k / 4, back, z0, nb * k / 4, 'n');
                }
                line(back, z0, 0, back, z0, nb, 'N'); line(back, z1, 0, back, z1, nb, 'N'); line(back, z0, nb, back, z1, nb, 'N');
                line(gx, z0, bh, back, z0, nb, 'N'); line(gx, z1, bh, back, z1, nb, 'N');
                line(gx, z0, 0, back, z0, 0, 'N'); line(gx, z1, 0, back, z1, 0, 'N');
                line(gx, z1, 0, gx, z1, bh, 'P'); line(gx, z0, bh, gx, z1, bh, 'P'); line(gx, z0, 0, gx, z0, bh, 'P');   // 远门柱、横梁、近门柱
            }
            string[] rows = new string[h];
            for (int j = 0; j < h; j++) rows[j] = new string(g[j]);
            return rows;
        }

        // 比分牌：左边那队（红）的分 : 右边那队（蓝）的分
        internal static string[] BoardRows(int a, int b)
        {
            string[] da = Digits[Math.Min(9, a)].Split(','), db = Digits[Math.Min(9, b)].Split(',');
            char[][] g = new char[9][];
            for (int y = 0; y < 9; y++) g[y] = new string('K', 15).ToCharArray();
            g[0][0] = g[0][14] = g[8][0] = g[8][14] = '.';
            for (int y = 0; y < 5; y++)
                for (int x = 0; x < 3; x++)
                {
                    if (da[y][x] == 'W') g[2 + y][3 + x] = 'A';
                    if (db[y][x] == 'W') g[2 + y][9 + x] = 'B';
                }
            g[3][7] = 'W'; g[5][7] = 'W';
            string[] rows = new string[9];
            for (int y = 0; y < 9; y++) rows[y] = new string(g[y]);
            return rows;
        }

        // 场地、比分牌、球的影子：鼠标直接穿过去
        static ObjWin MakeDeco(string[] rows, string pal, int u, int x, int y)
        {
            ObjWin w = new ObjWin(true);
            w.SetArt(rows, new Art("deco", rows, pal), u);
            w.MoveTo(x, y);
            w.Show();
            try { Native.SetWindowPos(w.Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE); } catch { }
            return w;
        }

        bool InMatch() { return match != null && match.Team.ContainsKey(this); }

        // 能不能拉来踢比赛：醒着、站稳了、没在忙别的、跟我在同一块屏幕（站在窗口上的会自己跳下来）
        bool CanJoinMatch(PetForm f, Rectangle wa)
        {
            return f != null && All.Contains(f) && !f.IsDisposed && !f.hiddenForFullscreen && f.Grounded() && !f.Sleeping() && !f.stay && !f.ccWorking
                && f.dreamForm == null && f.state != St.Burrow && f.state != St.Shake && f.state != St.Eat && f.state != St.Alert && f.state != St.Chat
                && f.WorkArea() == wa;
        }

        // 摆开场地开始比赛；spontaneous = 自己想踢的（四只都得有空）
        bool StartMatch(bool spontaneous)
        {
            if (match != null || All.Count < 2 || hiddenForFullscreen) return false;
            if (office != null && office.EndT < 0) return false;   // 大家在工位上陪 Claude 干活呢
            Rectangle wa = WorkArea();
            List<PetForm> ps = new List<PetForm>();
            foreach (PetForm f in All) if (CanJoinMatch(f, wa)) ps.Add(f);
            if (!ps.Contains(this) || ps.Count < 2 || (spontaneous && ps.Count < All.Count)) return false;
            int s = S;
            double depthPx = 60.0 * s;                     // 球场在屏幕上占多高
            if (wa.Height < depthPx + 60 * s) return false;   // 屏幕太矮，铺不开
            double width = Math.Round(Clamp(wa.Width - 40.0 * s, 150.0 * s, 250.0 * s) / s) * s;   // 两条底线之间
            double margin = 12.0 * s;                      // 底线外面留给球网
            double avg = 0;
            foreach (PetForm f in ps) avg += f.CX;
            avg /= ps.Count;
            double gl = Math.Round(Clamp(avg - width / 2, wa.Left + margin + 2 * s, wa.Right - margin - 2 * s - width));
            SoccerMatch m = new SoccerMatch();
            m.Host = this; m.U = s;
            m.GL = gl; m.GR = gl + width; m.L = gl - margin; m.R = m.GR + margin; m.Mid = gl + Math.Round(width / s / 2) * s;
            m.Floor = wa.Bottom; m.Dz = depthPx / PK; m.Top = m.Floor - depthPx - 2 * s;
            m.Zc = m.Dz / 2; m.GoalHalf = m.Dz * 0.17; m.BarH = 15 * s; m.NetD = 8 * s; m.BoxD = 30 * s; m.BoxHalf = m.Dz * 0.33;
            m.StartT = now; m.Phase = 0; m.PhaseT0 = now; m.LastTouchT = now;
            // 分队：左边一半一队，右边一半一队；一队两只的话离自家球门近的当守门员
            ps.Sort((a, b) => a.CX.CompareTo(b.CX));
            int half = ps.Count / 2;
            for (int i = 0; i < ps.Count; i++) { m.Players.Add(ps[i]); m.Team[ps[i]] = i < half ? 0 : 1; m.Keeper[ps[i]] = false; }
            if (half >= 2) m.Keeper[ps[0]] = true;
            if (ps.Count - half >= 2) m.Keeper[ps[ps.Count - 1]] = true;
            // 原来扔着的球收起来，比赛用自己的球
            PetObj old = AnyBall();
            if (old != null) { PetForm ow = BallOwner(old); if (ow != null) ow.RemoveObj(old); }
            m.FieldWin = MakeDeco(PitchRows(m, s), PitchPal, s, (int)m.L, (int)m.Top);
            m.ShadowWin = MakeDeco(BallShadowRows, ShadowPal, s, -10000, -10000);
            m.BallWin = new ObjWin(true);
            {
                Art a = Sprite.BallArt(0);
                m.BallWin.SetArt(a.Rows, a, s); m.BallWin.MoveTo(-10000, -10000); m.BallWin.Show(); m.BallFrame = 0;
            }
            m.BoardU = Math.Max(2, (int)Math.Round(s * 0.75));
            m.BoardWin = MakeDeco(BoardRows(0, 0), BoardPal, m.BoardU, (int)(m.Mid - 7.5 * m.BoardU), (int)(m.Top - 9 * m.BoardU - s));   // 挂在远边线上方，像球场的大屏
            match = m;
            RaisePets();   // 小人都要在球场前面
            foreach (PetForm f in m.Players)
            {
                f.ResetPlans(); f.chaseObj = null; f.pitchX = double.NaN; f.hopH = f.hopV = 0;
                f.SetState(St.Match, 1e9);
                f.Emo(m.Team[f] == 0 ? "star" : "note", 1.2);
            }
            if (petTalk) Say(ps.Count >= 4 ? "来踢场比赛吧！" : "比一场！", 2);
            nextMatchAll = now + 600 + R01() * 600;
            Dbg("match start: " + ps.Count + " players, field " + (width / s).ToString("F0") + " x " + (m.Dz / s).ToString("F0") + " cells");
            return true;
        }

        // 右键菜单「踢场比赛」/ 信号 match：睡着的叫起来，大家一起踢
        void MatchNow()
        {
            if (match != null) { match.Host.EndMatch(false); return; }
            foreach (PetForm f in All) if (f.Sleeping() && !f.stay) f.Wake(false);
            if (!StartMatch(false)) Emo("q", 1.2);
        }

        // 我在场上该站哪儿（开球前）：守门员在自家门前，前锋在中线自己这边
        void MatchHome(SoccerMatch m, out double hx, out double hz)
        {
            int t = m.Team[this], att = t == 0 ? 1 : -1;
            double own = t == 0 ? m.GL : m.GR;
            if (m.Keeper[this]) { hx = own + att * 7 * S; hz = m.Zc; return; }
            int idx = 0;
            foreach (PetForm f in m.Players) { if (f == this) break; if (m.Team[f] == t && !m.Keeper[f]) idx++; }
            hx = m.Mid - att * (22 + idx * 26) * S;
            hz = m.Zc + (idx == 0 ? (t == 0 ? -0.12 : 0.12) : (t == 0 ? 0.2 : -0.2)) * m.Dz;
        }

        void BackToMatch() { if (InMatch()) SetState(St.Match, 1e9); else StartIdle(); }

        double pitchX = double.NaN, pitchZ, pvx, pvz, hopH, hopV, kickPoseUntil, nextKickT;   // 在球场上的位置、速度；蹦起来多高

        // 这一帧往哪儿跑、跑多快
        void MatchTarget(SoccerMatch m, out double tx, out double tz, out double speed)
        {
            int t = m.Team[this], att = t == 0 ? 1 : -1;
            double own = t == 0 ? m.GL : m.GR;
            tx = pitchX; tz = pitchZ; speed = S * 40;
            if (m.Phase == 0) { MatchHome(m, out tx, out tz); speed = S * 34; return; }
            if (m.Phase == 4) { tz = 0; speed = S * 26; return; }   // 踢完了：走回近边线（任务栏上）
            if (m.Phase != 1 || !m.BallOn) return;                  // 庆祝：原地蹦
            if (m.Holder == this) return;                            // 抱着球：站着看哪儿有空当
            if (m.Keeper[this])
            {
                if (m.ShotLive && m.ShotTeam != t && m.BVX * att < 0)
                {
                    // 射门飞过来了：判断对了就往球要到的地方扑，判断错了就扑反
                    double lineX = own + att * 6 * S, tLine = (lineX - m.BX) / m.BVX;
                    double zAt = tLine > 0 ? m.BZ + m.BVZ * tLine : m.BZ;
                    tx = lineX;
                    if (m.ShotSave) { tz = Clamp(zAt, m.Zc - m.GoalHalf - 4 * S, m.Zc + m.GoalHalf + 4 * S); speed = S * 120; }
                    else { tz = Clamp(m.Zc - (zAt - m.Zc) * 0.4, m.Zc - m.GoalHalf, m.Zc + m.GoalHalf); speed = S * 60; }
                    return;
                }
                bool danger = Math.Abs(m.BX - own) < m.BoxD + 4 * S && Math.Abs(m.BZ - m.Zc) < m.BoxHalf;
                if (danger && m.BH < m.BarH && m.Holder == null) { tx = m.BX - att * 3 * S; tz = m.BZ; speed = S * 52; return; }   // 球进了禁区：冲出去
                // 守在门线前，跟着球横着挪
                tx = own + att * 6 * S; speed = S * 36;
                tz = Clamp(m.Zc + (m.BZ - m.Zc) * 0.6, m.Zc - m.GoalHalf * 0.8, m.Zc + m.GoalHalf * 0.8);
                return;
            }
            PetForm myGk = MatchMate(m, true), oppF = MatchOpp(m, false);
            bool ourBall = myGk != null && (m.Holder == myGk
                || (Math.Abs(m.BX - own) < m.BoxD + 4 * S && Math.Abs(m.BZ - m.Zc) < m.BoxHalf && m.BH < m.BarH && !m.ShotLive));
            if (ourBall)
            {
                // 自家守门员拿着球 / 正去处理：拉开到对手不在的那一边，等着接传球
                tx = m.Mid - att * 12 * S;
                tz = oppF != null && oppF.pitchZ > m.Zc ? m.Zc - 0.28 * m.Dz : m.Zc + 0.28 * m.Dz;
                speed = S * 40;
                return;
            }
            if (m.Holder != null)
            {
                // 对面守门员抱着球：退回来，站在对面前锋和自家球门之间盯人
                if (oppF != null) { tx = oppF.pitchX - att * 14 * S; tz = oppF.pitchZ; } else { tx = m.Mid - att * 20 * S; tz = m.Zc; }
                speed = S * 36;
                return;
            }
            // 球没人管：绕到球后面一点（这样一脚能往对方球门踢），追过去
            tx = m.BX + m.BVX * 0.15 - att * 4 * S; tz = m.BZ + m.BVZ * 0.15;
            speed = S * 46;
        }

        PetForm MatchMate(SoccerMatch m, bool wantKeeper)
        {
            foreach (PetForm f in m.Players) if (f != this && m.Team[f] == m.Team[this] && m.Keeper[f] == wantKeeper && !double.IsNaN(f.pitchX)) return f;
            return null;
        }

        PetForm MatchOpp(SoccerMatch m, bool wantKeeper)
        {
            foreach (PetForm f in m.Players) if (m.Team[f] != m.Team[this] && m.Keeper[f] == wantKeeper && !double.IsNaN(f.pitchX)) return f;
            return null;
        }

        // 传球路线上有没有对手挡着（离那条线不到 r）
        bool LaneBlocked(SoccerMatch m, double x0, double z0, double x1, double z1, double r)
        {
            foreach (PetForm f in m.Players)
            {
                if (m.Team[f] == m.Team[this] || double.IsNaN(f.pitchX)) continue;
                double vx = x1 - x0, vz = z1 - z0, l2 = vx * vx + vz * vz;
                double k = l2 < 1 ? 0 : Clamp(((f.pitchX - x0) * vx + (f.pitchZ - z0) * vz) / l2, 0, 1);
                double ex = x0 + vx * k - f.pitchX, ez = (z0 + vz * k - f.pitchZ) * 0.6;
                if (ex * ex + ez * ez < r * r) return true;
            }
            return false;
        }

        // 贴地踢到某一点：地上滚会越来越慢，按距离使劲（extra = 到了还剩多少速度）
        void KickTo(SoccerMatch m, double kx, double kz, double extra, double lift)
        {
            double ddx = kx - m.BX, ddz = kz - m.BZ, dd = Math.Max(1, Math.Sqrt(ddx * ddx + ddz * ddz));
            double sp = Math.Min(S * 300, 1.35 * dd + extra);
            m.BVX = ddx / dd * sp; m.BVZ = ddz / dd * sp; m.BVH = lift;
        }

        // 撞墙传：往离得近的那条边线踢，弹回来正好到 (tx, tz)
        void BankKick(SoccerMatch m, double tx, double tz)
        {
            double wall = m.BZ > m.Zc ? m.Dz - 2 * S : 2 * S;
            KickTo(m, tx, 2 * wall - tz, S * 45, S * 3);
        }

        // 射门：多半瞄着门柱附近；两成射偏（擦着门柱出去 / 打在门柱上）。扑不扑得住，起脚时按远近和角度定好：
        // 越远越好扑，越贴门柱越难扑，平均下来大概一半能进
        void MatchShoot(SoccerMatch m, int att, double goalX, double toGoal)
        {
            double corner = R01() < 0.6 ? 0.55 + R01() * 0.4 : R01() * 0.5;
            double kz = m.Zc + (R01() < 0.5 ? -1 : 1) * corner * m.GoalHalf;
            bool onTarget = R01() < 0.8;
            if (!onTarget) kz = m.Zc + (kz >= m.Zc ? 1 : -1) * (m.GoalHalf + (2 + R01() * 9) * S);
            double ddx = goalX + att * 4 * S - m.BX, ddz = kz - m.BZ, dd = Math.Max(1, Math.Sqrt(ddx * ddx + ddz * ddz)), sp = S * (230 + R01() * 100);
            m.BVX = ddx / dd * sp; m.BVZ = ddz / dd * sp;
            m.BVH = R01() < 0.3 ? S * (60 + R01() * 50) : S * (8 + R01() * 30);
            double pSave = Clamp(0.45 + 0.35 * toGoal / (120.0 * S) - 0.3 * corner, 0.15, 0.85);
            m.ShotLive = true; m.ShotTeam = m.Team[this]; m.ShotSave = onTarget && R01() < pSave; m.ShotUntil = now + 2;
            Emo("bang", 0.8);
            Dbg(string.Format("shot dist={0:F0} corner={1:F2} onTarget={2} pSave={3:F2} save={4}", toGoal / S, corner, onTarget, pSave, m.ShotSave));
        }

        // 守门员发球：前锋有空当就贴地传过去，被盯着就撞墙传，再不行就大脚开到前场
        string KeeperDistribute(SoccerMatch m, int att)
        {
            PetForm fw = MatchMate(m, false), press = MatchOpp(m, false);
            double r = R01();
            bool pressed = press != null && Math.Abs(press.pitchX - pitchX) < 40 * S && Math.Abs(press.pitchZ - pitchZ) < 40 * S;
            if (pressed && r < 0.7) r = 1;   // 对面前锋扑上来了：别在门口短传，多半直接大脚开远
            if (fw != null)
            {
                double fx = fw.pitchX + att * 10 * S, fz = fw.pitchZ;
                if (r < 0.75 && !LaneBlocked(m, m.BX, m.BZ, fx, fz, 9 * S)) { KickTo(m, fx, fz, S * 30, S * 4); return "pass"; }
                if (r < 0.85) { BankKick(m, fx, fz); return "wallpass"; }
            }
            double kx = m.Mid + att * (20 + R01() * 40) * S, kz = m.Dz * (0.2 + R01() * 0.6);
            double ddx = kx - m.BX, ddz = kz - m.BZ, dd = Math.Max(1, Math.Sqrt(ddx * ddx + ddz * ddz)), sp = S * (190 + R01() * 70);
            m.BVX = ddx / dd * sp; m.BVZ = ddz / dd * sp; m.BVH = S * (110 + R01() * 60);
            return "clear";
        }

        // 被挡住时斜着往没人的那边带一下
        string MatchDodge(SoccerMatch m, int att, PetForm block)
        {
            double side = block.pitchZ > pitchZ ? -1 : 1;
            if (pitchZ + side * 40 * S < 4 * S || pitchZ + side * 40 * S > m.Dz - 4 * S) side = -side;
            KickTo(m, pitchX + att * 34 * S, pitchZ + side * 40 * S, S * 30, S * (4 + R01() * 8));
            return "dodge";
        }

        void FinishKick(SoccerMatch m, string what, bool puff)
        {
            if (m.BH < 0.5 && m.BVH > 0) m.BH = 0.5;
            m.LastKicker = this; m.LastTouchT = now;
            nextKickT = now + 0.35; kickPoseUntil = now + 0.22;
            if (Math.Abs(m.BVX) > 1) facing = m.BVX > 0 ? 1 : -1;
            if (puff) Spawn(Sprite.Puff, Sprite.PuffC, (facing > 0 ? 20 : 2) * S, 15 * S, facing * S * 6, -S * 2, 0.4, 0);
            if (what == "shoot" || what == "longshot") Snd("shot", SND_PLAY, 0.8, false);
            else if (what == "catch") Snd("catch", SND_PLAY, 0.8, false);
            else if (what == "header") Sfx.Play("kick", SndOk(SND_PLAY) ? 0.6 : 0, 5);   // 头球：高一点、轻一点
            else Snd("kick", SND_PLAY, what == "dribble" ? 0.5 : 0.7, false);
            Dbg("match " + what + " team" + m.Team[this]);
        }

        // 场上每一只：按目标跑位，球到脚下就起脚
        void UpdMatchPet(double dt)
        {
            SoccerMatch m = match;
            if (m == null || !m.Team.ContainsKey(this)) { StartIdle(); return; }
            if (plat != IntPtr.Zero || basePet != null) { HopDown(CX < m.Mid ? 1 : -1, BackToMatch); return; }   // 站在窗口上 / 伙伴头上：先跳下来
            // 刚上场（从地上走进来）或者被拎走又放下了：按现在站的地方算回场地坐标
            if (double.IsNaN(pitchX) || Math.Abs(PitchSX(m, pitchX, pitchZ) - CX) > 2 * S || Math.Abs(PitchSY(m, pitchZ) - posY) > 2 * S)
            {
                pitchZ = Clamp((m.Floor - posY) / PK, 0, m.Dz);
                pitchX = m.Mid + (CX - m.Mid) / PitchP(m, pitchZ);   // 在球场外面也不硬拽进来：自己跑进场
                pvx = pvz = 0;
            }
            // 蹦起来（进球庆祝 / 头球 / 守门员扑高球）：只是看起来往上跳，脚下的位置不变
            bool cheer = (m.Phase == 2 && m.Team[this] == m.ScoredBy) || (m.Phase == 3 && (m.Winner < 0 || m.Team[this] == m.Winner));
            if (cheer && hopH == 0 && hopV == 0 && R01() < dt * 2.5) hopV = S * (90 + R01() * 40);
            if (hopH > 0 || hopV > 0) { hopV -= S * 520.0 * dt; hopH += hopV * dt; if (hopH <= 0) { hopH = 0; hopV = 0; } }
            double tx, tz, speed;
            MatchTarget(m, out tx, out tz, out speed);
            double dx = tx - pitchX, dz = tz - pitchZ, d = Math.Sqrt(dx * dx + dz * dz), wvx = 0, wvz = 0;
            if (d > 1.5 * S) { double sp = Math.Min(speed, d * 4); wvx = dx / d * sp; wvz = dz / d * sp; }
            double acc = Math.Min(1, dt * 8);
            pvx += (wvx - pvx) * acc; pvz += (wvz - pvz) * acc;
            // 别跟别的小人挤成一团（屏幕上左右差不到 13 格、上下差不到 5 格就算挤）
            foreach (PetForm f in m.Players)
            {
                if (f == this || double.IsNaN(f.pitchX)) continue;
                double ex = pitchX - f.pitchX, ez = (pitchZ - f.pitchZ) * PK * 2.6, r = Math.Sqrt(ex * ex + ez * ez);
                if (r < 13 * S && r > 0.01) { double push = (13 * S - r) * 5 * dt; pitchX += ex / r * push; pitchZ += ez / r * push / (PK * 2.6); }
            }
            pitchX = Clamp(pitchX + pvx * dt, Math.Min(m.L + 4 * S, pitchX), Math.Max(m.R - 4 * S, pitchX));   // 场外的只能往里走
            pitchZ = Clamp(pitchZ + pvz * dt, 0, m.Dz);
            double spd = Math.Sqrt(pvx * pvx + pvz * pvz);
            if (Math.Abs(pvx) > S * 3) facing = pvx > 0 ? 1 : -1;
            else if (m.BallOn) facing = m.BX >= pitchX ? 1 : -1;
            gvx = spd > 1.5 * S ? facing * spd : 0;   // 走路动画按跑的快慢迈腿（往里、往外跑也迈）
            walkPh += spd * dt / (S * 1.5);
            ballWait = spd < 3 * S;
            SetCX(PitchSX(m, pitchX, pitchZ)); posY = PitchSY(m, pitchZ);
            if (m.BallOn) { attnX = PitchSX(m, m.BX, m.BZ); attnY = PitchSY(m, m.BZ) - m.BH - 2 * S; attnUntil = now + 0.3; }
            TryMatchKick(m);
        }

        // 球到了脚下（或者头顶）该怎么踢：
        // 守门员扑救（抱住 / 托出去），拿到球就看前锋有没有空当再发出去；
        // 前锋离门近就射门，前面没人就往前带，被挡住了就绕过去、撞墙传、回传给守门员或者远射；高球落下来跳起来顶
        void TryMatchKick(SoccerMatch m)
        {
            if (m.Phase != 1 || !m.BallOn) return;
            bool keeper = m.Keeper[this];
            int team = m.Team[this], att = team == 0 ? 1 : -1;
            string what;
            if (m.Holder == this)
            {
                if (now < m.HoldUntil) return;
                m.Holder = null; m.BH = 6 * S;
                what = KeeperDistribute(m, att);
                FinishKick(m, what, true);
                return;
            }
            if (m.Holder != null || now < nextKickT) return;
            if (!keeper && m.LastKicker != null && m.LastKicker != this && now - m.LastTouchT < 0.3) return;   // 刚被别人踢出去：先让球飞一会儿，别你一下我一下顶个没完
            double dx = m.BX - pitchX, dz = m.BZ - pitchZ;
            if (Math.Abs(dx) > (keeper ? 11 : 8) * S || Math.Abs(dz) > (keeper ? 16 : 10) * S) return;
            bool shotAtMe = m.ShotLive && m.ShotTeam != team;
            if (keeper && shotAtMe && !m.ShotSave) return;   // 这一脚判断错了方向，够不着
            bool header = false;
            if (m.BH > (keeper ? m.BarH + 4 * S : 7 * S))
            {
                if (keeper || m.BH > 24 * S) return;                         // 太高了够不着
                if (m.BVH > S * 30 || m.LastKicker == this) return;          // 球还在往上飞 / 自己刚踢的：等它落下来
                header = true;
            }
            m.ShotLive = false;
            double goalX = att > 0 ? m.GR : m.GL, toGoal = (goalX - pitchX) * att;
            if (keeper && (shotAtMe || m.BH > 6 * S || Math.Abs(m.BVX) + Math.Abs(m.BVZ) > S * 150))
            {
                // 扑救：低一点的抱住，高的、刁的托出去（可能弹给对面补射）
                if (m.BH > 4 * S && hopH == 0) hopV = Math.Sqrt(2 * S * 520.0 * Math.Min(m.BH, 20 * S) * 0.8);
                if (m.BH < m.BarH - 4 * S && R01() < 0.55)
                {
                    m.Holder = this; m.HoldUntil = now + 0.8 + R01() * 0.7;
                    m.BVX = m.BVZ = m.BVH = 0; what = "catch";
                }
                else
                {
                    double side = m.BZ >= m.Zc ? 1 : -1;
                    KickTo(m, pitchX + att * (14 + R01() * 26) * S, Clamp(m.BZ + side * (28 + R01() * 26) * S, 4 * S, m.Dz - 4 * S), S * 60, S * (30 + R01() * 40));
                    what = "parry";
                }
            }
            else if (keeper) what = KeeperDistribute(m, att);
            else if (header)
            {
                double kz = m.Zc + (R01() - 0.5) * m.GoalHalf;
                double ddx = goalX - m.BX, ddz = kz - m.BZ, dd = Math.Max(1, Math.Sqrt(ddx * ddx + ddz * ddz)), sp = S * (110 + R01() * 50);
                m.BVX = ddx / dd * sp; m.BVZ = ddz / dd * sp; m.BVH = S * (20 + R01() * 30);
                if (hopH == 0) hopV = Math.Sqrt(2 * S * 520.0 * Math.Max(2 * S, m.BH - 10 * S));
                what = "header";
            }
            else if (toGoal > 0 && toGoal < 120 * S) { MatchShoot(m, att, goalX, toGoal); what = "shoot"; }
            else
            {
                PetForm block = null;
                foreach (PetForm f in m.Players)
                {
                    if (m.Team[f] == team || double.IsNaN(f.pitchX)) continue;
                    double ax = (f.pitchX - pitchX) * att, az = f.pitchZ - pitchZ;
                    if (ax > 0 && ax < 26 * S && Math.Abs(az) < 16 * S) { block = f; break; }
                }
                PetForm gk = MatchMate(m, true);
                double r = R01();
                if (block == null)
                {
                    // 前面没人：往前带一脚，稍微往中路靠
                    KickTo(m, pitchX + att * 50 * S, pitchZ + (m.Zc - pitchZ) * 0.35 + (R01() - 0.5) * 24 * S, S * 25, S * (4 + R01() * 10));
                    what = "dribble";
                }
                else if (r < 0.35) what = MatchDodge(m, att, block);
                else if (r < 0.7) { BankKick(m, pitchX + att * 55 * S, pitchZ); what = "wall"; }   // 撞墙传：往边线踢，弹回来正好越过对手，自己追上去
                else if (r < 0.88 && gk != null && Math.Abs(gk.pitchX - pitchX) < 150 * S && !LaneBlocked(m, m.BX, m.BZ, gk.pitchX, gk.pitchZ, 9 * S)
                         && Math.Abs(block.pitchX - gk.pitchX) > 50 * S)
                {
                    KickTo(m, gk.pitchX + att * 4 * S, gk.pitchZ, S * 25, S * 3);   // 回传给守门员，让它重新组织
                    what = "backpass";
                }
                else if (toGoal > 0 && toGoal < 175 * S) { MatchShoot(m, att, goalX, toGoal); what = "longshot"; }
                else what = MatchDodge(m, att, block);
            }
            FinishKick(m, what, !header && what != "catch");
        }

        // 球：往前滚、往里滚、飞起来落地弹几下；碰到边线弹回来，底线上门里进球、门外弹回来，进了门被网兜住
        void UpdPitchBall(SoccerMatch m, double dt)
        {
            if (m.Holder != null)
            {
                // 守门员抱着球：球跟着它，捧在胸前
                PetForm k = m.Holder;
                if (!All.Contains(k) || k.IsDisposed || k.state != St.Match || double.IsNaN(k.pitchX)) m.Holder = null;
                else
                {
                    m.BX = k.pitchX + (m.Team[k] == 0 ? 1 : -1) * 3 * S; m.BZ = k.pitchZ - 0.5; m.BH = 7 * S + k.hopH;
                    m.BVX = m.BVZ = m.BVH = 0;
                    return;
                }
            }
            int n = 3; double h = dt / n, G = S * 520.0;
            for (int i = 0; i < n; i++)
            {
                double px = m.BX;
                if (m.BH > 0 || m.BVH > 0)
                {
                    m.BVH -= G * h; m.BH += m.BVH * h;
                    if (m.BH <= 0)
                    {
                        m.BH = 0;
                        if (m.BVH < -S * 40)
                        {
                            if (m.BVH < -S * 80) Snd("bounce", SND_PLAY, Clamp(-m.BVH / (S * 250.0), 0.2, 0.7), false);
                            m.BVH = -m.BVH * 0.45; m.BVX *= 0.85; m.BVZ *= 0.85;
                        }
                        else m.BVH = 0;
                    }
                }
                double fr = m.BH <= 0 ? Math.Exp(-1.3 * h) : Math.Exp(-0.15 * h);
                m.BVX *= fr; m.BVZ *= fr;
                m.BX += m.BVX * h; m.BZ += m.BVZ * h;
                // 边线：像室内足球场的挡板一样弹回来（撞墙传就靠它）
                if (m.BZ < 2 * S) { m.BZ = 2 * S; m.BVZ = Math.Abs(m.BVZ) * 0.8; m.ShotLive = false; }
                if (m.BZ > m.Dz - 2 * S) { m.BZ = m.Dz - 2 * S; m.BVZ = -Math.Abs(m.BVZ) * 0.8; m.ShotLive = false; }
                for (int s = 0; s < 2; s++)
                {
                    double gx = s == 0 ? m.GL : m.GR;
                    bool crossed = s == 0 ? (px >= gx && m.BX < gx) : (px <= gx && m.BX > gx);
                    if (!crossed) continue;
                    double off = Math.Abs(m.BZ - m.Zc) - m.GoalHalf;
                    bool mouth = off < -1.5 * S && m.BH < m.BarH - 2 * S;
                    if (mouth) { if (m.Phase == 1) GoalScored(m, s == 0 ? 1 : 0); }
                    else if (off < 2 * S && m.BH < m.BarH)
                    {
                        // 打在门柱上：当的一声弹回场内
                        m.BX = gx + (s == 0 ? 0.5 : -0.5); m.BVX = -m.BVX * 0.6; m.BVZ += (m.BZ >= m.Zc ? 1 : -1) * S * 30;
                        m.ShotLive = false;
                        if (m.LastKicker != null && All.Contains(m.LastKicker)) m.LastKicker.Emo("sweat", 1.0);
                        Snd("post", SND_PLAY);
                        Dbg("post!");
                    }
                    else { m.BX = gx + (s == 0 ? 0.5 : -0.5); m.BVX = -m.BVX * 0.55; m.ShotLive = false; }   // 偏出去打在底线挡板上：弹回来
                }
                if (m.BX < m.GL || m.BX > m.GR)
                {
                    // 进了门：网兜住，很快停下
                    m.BZ = Clamp(m.BZ, m.Zc - m.GoalHalf + 2 * S, m.Zc + m.GoalHalf - 2 * S);
                    if (m.BH > m.BarH - 3 * S) { m.BH = m.BarH - 3 * S; m.BVH = Math.Min(0, m.BVH); }
                    double lo = m.GL - m.NetD + 3 * S, hi = m.GR + m.NetD - 3 * S;
                    if (m.BX < lo) { m.BX = lo; m.BVX = Math.Abs(m.BVX) * 0.15; }
                    if (m.BX > hi) { m.BX = hi; m.BVX = -Math.Abs(m.BVX) * 0.15; }
                    m.BVX *= Math.Exp(-3 * h); m.BVZ *= Math.Exp(-3 * h);
                }
            }
            m.Roll += Math.Sqrt(m.BVX * m.BVX + m.BVZ * m.BVZ) * dt;
        }

        // 把球和影子挪到屏幕上该在的地方
        void DrawPitchBall(SoccerMatch m)
        {
            if (m.BallWin == null || m.BallWin.IsDisposed) return;
            int u = m.U;
            if (!m.BallOn) { m.BallWin.MoveTo(-10000, -10000); m.ShadowWin.MoveTo(-10000, -10000); return; }
            int f = ((int)Math.Floor(m.Roll / (2.5 * u))) & 3;   // 滚起来换四个角的颜色
            if (f != m.BallFrame) { m.BallFrame = f; Art a = Sprite.BallArt(f); m.BallWin.SetArt(a.Rows, a, u); }
            double sx = PitchSX(m, m.BX, m.BZ), sy = PitchSY(m, m.BZ);
            m.BallWin.MoveTo((int)Math.Round(sx - 3 * u), (int)Math.Round(sy - 5.5 * u - m.BH));
            m.ShadowWin.MoveTo((int)Math.Round(sx - 3 * u), (int)Math.Round(sy - 1.5 * u));
        }

        // 近的盖住远的：小人和球按深度排前后（顺序变了才动）
        static void SortPitchDepth(SoccerMatch m)
        {
            List<KeyValuePair<double, IntPtr>> list = new List<KeyValuePair<double, IntPtr>>();
            foreach (PetForm f in m.Players)
                if (!f.IsDisposed && f.IsHandleCreated) list.Add(new KeyValuePair<double, IntPtr>(double.IsNaN(f.pitchX) ? -1 : f.pitchZ, f.Handle));
            if (m.BallOn && m.BallWin != null && m.BallWin.IsHandleCreated) list.Add(new KeyValuePair<double, IntPtr>(m.BZ - 1.5 * m.U, m.BallWin.Handle));
            list.Sort((a, b) => b.Key.CompareTo(a.Key));   // 远的先放（先放上去的在下面）
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<double, IntPtr> kv in list) sb.Append(kv.Value.ToInt64()).Append(',');
            string key = sb.ToString();
            if (key == m.Order) return;
            m.Order = key;
            const uint fl = Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE;
            if (m.ShadowWin != null && m.ShadowWin.IsHandleCreated) Native.SetWindowPos(m.ShadowWin.Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, fl);
            foreach (KeyValuePair<double, IntPtr> kv in list) Native.SetWindowPos(kv.Value, Native.HWND_TOPMOST, 0, 0, 0, 0, fl);
        }

        void GoalScored(SoccerMatch m, int team)
        {
            if (m.Phase != 1) return;
            m.Score[team]++; m.Phase = 2; m.PhaseT0 = now; m.ScoredBy = team;
            m.ShotLive = false; m.Holder = null;
            Snd("goal", SND_PLAY);
            if (m.BoardWin != null) { string[] br = BoardRows(m.Score[0], m.Score[1]); m.BoardWin.SetArt(br, new Art("deco", br, BoardPal), m.BoardU); }
            PetForm k = m.LastKicker;
            bool ownGoal = k != null && m.Team.ContainsKey(k) && m.Team[k] != team;
            foreach (PetForm f in m.Players)
            {
                if (!All.Contains(f)) continue;
                if (m.Team[f] == team) f.Emo("star", 1.6); else f.Emo("sweat", 1.4);
            }
            if (k != null && All.Contains(k) && petTalk) k.Say(ownGoal ? "乌龙球…" : k.Pick(GoalLines), 1.8);
            Dbg("GOAL team" + team + " " + m.Score[0] + ":" + m.Score[1] + (ownGoal ? " (own goal)" : ""));
        }

        // 开球：球从中圈上空掉下来
        void KickOff(SoccerMatch m)
        {
            m.BallOn = true; m.BX = m.Mid; m.BZ = m.Zc; m.BH = 34 * S;
            m.BVX = (R01() - 0.5) * S * 16; m.BVZ = (R01() - 0.5) * S * 16; m.BVH = 0;
            m.Phase = 1; m.PhaseT0 = now; m.LastTouchT = now; m.LastKicker = null;
            m.ShotLive = false; m.Holder = null;
            Snd("whistle", SND_PLAY, 0.8, false);
            foreach (PetForm f in m.Players) if (!m.Keeper[f]) f.Emo("bang", 0.8);
            Dbg("kick off " + m.Score[0] + ":" + m.Score[1]);
        }

        void FinalWhistle(SoccerMatch m)
        {
            m.Phase = 3; m.PhaseT0 = now; m.BallOn = false; m.Holder = null; m.ShotLive = false;
            Snd("final", SND_PLAY, 0.8, false);
            int w = m.Score[0] > m.Score[1] ? 0 : m.Score[1] > m.Score[0] ? 1 : -1;
            m.Winner = w;
            PetForm sayer = null;
            foreach (PetForm f in m.Players)
            {
                if (!All.Contains(f)) continue;
                if (w < 0 || m.Team[f] == w) { f.Emo("star", 1.8); if (sayer == null) sayer = f; }
                else f.Emo("heart", 1.8);   // 输了也开心
            }
            if (sayer != null && petTalk) sayer.Say(w < 0 ? "平局！" : "赢啦！", 2);
            Dbg("final " + m.Score[0] + ":" + m.Score[1]);
        }

        // 收场：拆掉场地；踢完了的话两队互相击掌（半路散场的，站在球场半空的会掉回地上）
        void EndMatch(bool shake)
        {
            SoccerMatch m = match;
            if (m == null) return;
            match = null;
            foreach (ObjWin w in new[] { m.FieldWin, m.BallWin, m.ShadowWin, m.BoardWin })
                if (w != null) { try { w.Close(); w.Dispose(); } catch { } }
            List<PetForm> ps = m.Players.FindAll(f => All.Contains(f) && !f.IsDisposed);
            foreach (PetForm f in ps)
            {
                f.pitchX = double.NaN; f.hopH = f.hopV = 0;
                if (f.state == St.Match) { f.ResetPlans(); f.StartIdle(); }
            }
            if (shake)
            {
                List<PetForm> used = new List<PetForm>();
                foreach (PetForm f in ps)
                {
                    if (m.Team[f] != 0 || !f.Grounded() || f.stay) continue;
                    PetForm o = null; double bd = double.MaxValue;
                    foreach (PetForm g in ps)
                        if (m.Team[g] == 1 && !used.Contains(g) && g.Grounded() && Math.Abs(g.CX - f.CX) < bd) { bd = Math.Abs(g.CX - f.CX); o = g; }
                    if (o == null) continue;
                    used.Add(o);
                    f.ResetPlans(); o.ResetPlans(); o.StartIdle();
                    f.HighFiveWith(o);
                }
            }
            nextMatchAll = now + 600 + R01() * 600;
            Dbg("match end");
        }

        // 主持比赛的那只每帧调用：球、阶段、前后顺序
        void UpdMatch(double dt)
        {
            SoccerMatch m = match;
            foreach (PetForm f in m.Players)
                if (!All.Contains(f) || f.IsDisposed || f.hiddenForFullscreen || f.stay || f.dreamForm != null || f.ccWorking) { EndMatch(false); return; }   // Claude Code 开始干活了：比赛先散了
            double t = now - m.PhaseT0;
            if (m.BallOn) UpdPitchBall(m, dt);
            switch (m.Phase)
            {
                case 0:
                    {
                        bool ready = true;
                        foreach (PetForm f in m.Players)
                        {
                            if (f.state != St.Match || double.IsNaN(f.pitchX)) { ready = false; break; }
                            double hx, hz;
                            f.MatchHome(m, out hx, out hz);
                            if (Math.Abs(f.pitchX - hx) > 4 * S || Math.Abs(f.pitchZ - hz) > 6 * S) ready = false;
                        }
                        if ((ready && t > 1.2) || t > 9) KickOff(m);
                    }
                    break;
                case 1:
                    if (now - m.StartT > MATCH_SECS) { FinalWhistle(m); break; }
                    if (m.ShotLive && now > m.ShotUntil) m.ShotLive = false;
                    if (m.Holder == null && now - m.LastTouchT > 12 && Math.Abs(m.BVX) + Math.Abs(m.BVZ) < S * 5) { Dbg("ball stuck: drop again"); KickOff(m); }   // 卡住了：重新开球
                    break;
                case 2:
                    if (t > 3.0)
                    {
                        m.BallOn = false;
                        if (m.Score[0] >= MATCH_WIN || m.Score[1] >= MATCH_WIN || now - m.StartT > MATCH_SECS) FinalWhistle(m);
                        else { m.Phase = 0; m.PhaseT0 = now; m.ScoredBy = -1; }
                    }
                    break;
                case 3:
                    if (t > 3.0) { m.Phase = 4; m.PhaseT0 = now; }
                    break;
                case 4:
                    {
                        bool done = true;
                        foreach (PetForm f in m.Players) if (f.state == St.Match && !double.IsNaN(f.pitchX) && f.pitchZ > 1.5 * S) done = false;
                        if (done || t > 7) { EndMatch(true); return; }
                    }
                    break;
            }
            DrawPitchBall(m);
            SortPitchDepth(m);
        }

        // 比赛时脚下一圈：中间是影子，外圈是队伍颜色（红队 / 蓝队），一眼看出谁跟谁一队
        void PaintPitchRing(Rectangle clip, int W, int team)
        {
            int cx = offX + 12 * S, cy = offY + FOOT * S, b = Math.Max(1, S / 2);
            double rx = 9.0 * S, ry = 2.3 * S;
            int x0 = cx - (int)Math.Ceiling(rx) - b, x1 = cx + (int)Math.Ceiling(rx) + b, y0 = cy - (int)Math.Ceiling(ry) - b, y1 = cy + (int)Math.Ceiling(ry) + b;
            int ring = TeamRing[team & 1];
            for (int by = y0; by < y1; by += b)
                for (int bx = x0; bx < x1; bx += b)
                {
                    double ex = (bx + b * 0.5 - cx) / rx, ey = (by + b * 0.5 - cy) / ry, e = ex * ex + ey * ey;
                    if (e > 1) continue;
                    int col = e > 0.55 ? ring : RingShade;
                    for (int y = Math.Max(by, clip.Top); y < Math.Min(by + b, clip.Bottom); y++)
                        for (int x = Math.Max(bx, clip.Left); x < Math.Min(bx + b, clip.Right); x++)
                            destBuf[y * W + x] = col;
                }
        }

        // 预览（--pitch out.png）：半俯视球场、四只、球、比分牌画成一张图
        internal static void PitchPreview(string path)
        {
            int s = 4;
            SoccerMatch m = new SoccerMatch();
            m.U = s;
            double width = 250.0 * s, margin = 12.0 * s, depthPx = 60.0 * s;
            m.GL = margin + 10 * s; m.GR = m.GL + width; m.L = m.GL - margin; m.R = m.GR + margin; m.Mid = m.GL + width / 2;
            m.Dz = depthPx / PK; m.Zc = m.Dz / 2; m.GoalHalf = m.Dz * 0.17; m.BarH = 15 * s; m.NetD = 8 * s; m.BoxD = 30 * s; m.BoxHalf = m.Dz * 0.33;
            int imgW = (int)(m.R + 10 * s), imgH = (int)(depthPx + 80 * s);
            m.Floor = imgH - 12 * s; m.Top = m.Floor - depthPx - 2 * s;
            using (Bitmap bmp = new Bitmap(imgW, imgH))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(0x3B, 0x4C, 0x5E));
                using (SolidBrush tb = new SolidBrush(Color.FromArgb(0xE9, 0xE5, 0xDA))) g.FillRectangle(tb, 0, (int)m.Floor, imgW, imgH - (int)m.Floor);
                g.SmoothingMode = SmoothingMode.None;
                string[] fr = PitchRows(m, s);
                Sprite.DrawArt(g, fr, new Art("p", fr, PitchPal), (int)m.L, (int)m.Top, s);
                string[] br = BoardRows(2, 1);
                int bu = Math.Max(2, (int)Math.Round(s * 0.75));
                Sprite.DrawArt(g, br, new Art("b", br, BoardPal), (int)(m.Mid - 7.5 * bu), (int)(m.Top - 9 * bu - s), bu);
                // 四只：x、z、队、饰品、朝向、是不是在踢
                double[][] who = {
                    new double[] { m.GL + 8 * s, m.Zc + 6 * s, 0, 0, 1, 0 },
                    new double[] { m.GR - 8 * s, m.Zc - 10 * s, 1, 2, -1, 0 },
                    new double[] { m.Mid + 40 * s, m.Zc + 30 * s, 0, 1, 1, 1 },
                    new double[] { m.Mid + 62 * s, m.Zc - 26 * s, 1, Sprite.SeasonAcc(), -1, 0 },
                };
                double bx = m.Mid + 52 * s, bz = m.Zc + 26 * s, bh = 6 * s;
                List<double[]> order = new List<double[]>(who);
                order.Sort((a, b) => b[1].CompareTo(a[1]));
                bool ballDrawn = false;
                foreach (double[] w in order)
                {
                    if (!ballDrawn && bz > w[1]) { PreviewBall(g, m, bx, bz, bh, s); ballDrawn = true; }
                    int sx = (int)Math.Round(PitchSX(m, w[0], w[1])), sy = (int)Math.Round(PitchSY(m, w[1]));
                    // 脚下一圈
                    int team = (int)w[2], b = Math.Max(1, s / 2);
                    double rx = 9.0 * s, ry = 2.3 * s;
                    for (int y = sy - 12; y < sy + 12; y += b)
                        for (int x = sx - 40; x < sx + 40; x += b)
                        {
                            double ex = (x + b * 0.5 - sx) / rx, ey = (y + b * 0.5 - sy) / ry, e = ex * ex + ey * ey;
                            if (e > 1) continue;
                            using (SolidBrush rb = new SolidBrush(Color.FromArgb(e > 0.55 ? TeamRing[team] : RingShade))) g.FillRectangle(rb, x, y, b, b);
                        }
                    Pose p = new Pose(); p.Acc = (int)w[3]; p.Facing = (int)w[4]; p.LookX = p.Facing;
                    if (w[5] > 0) { p.Legs = LegPose.Kick; p.ArmL = p.ArmR = ArmPose.Out; p.Eyes = EyePose.Focus; }
                    else { p.Legs = LegPose.WalkA; p.ArmL = ArmPose.Raised; }
                    Sprite.Draw(g, p, sx - 12 * s, sy - Sprite.Ground * s, s);
                }
                if (!ballDrawn) PreviewBall(g, m, bx, bz, bh, s);
                bmp.Save(path);
            }
        }

        static void PreviewBall(Graphics g, SoccerMatch m, double bx, double bz, double bh, int s)
        {
            int sx = (int)Math.Round(PitchSX(m, bx, bz)), sy = (int)Math.Round(PitchSY(m, bz));
            Sprite.DrawArt(g, BallShadowRows, new Art("k", BallShadowRows, ShadowPal), sx - 3 * s, (int)(sy - 1.5 * s), s);
            Art a = Sprite.BallArt(1);
            Sprite.DrawArt(g, a.Rows, a, sx - 3 * s, (int)Math.Round(sy - 5.5 * s - bh), s);
        }

        // 场地、工位这些摆件盖在小人上面了：把小人都提到最前面。
        // 顺序固定（后叫出来的先放、第一只最后放），叠在一起时谁在前面不会变来变去；第一只在最前面，它头上的 Claude 进度气泡不会被挡
        static void RaisePets()
        {
            List<PetForm> list = new List<PetForm>(All);
            list.Sort((a, b) => b.slot.CompareTo(a.slot));
            foreach (PetForm f in list)
                if (f.IsHandleCreated && !f.IsDisposed && !f.hiddenForFullscreen)
                    try { Native.SetWindowPos(f.Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE); } catch { }
        }

        // 定时重新置顶：小人按固定顺序 → 球、纸飞机在小人前面 → 叠高高时上面那只在下面那只前面 → 踢比赛时按远近重排
        static void ReassertTopmost()
        {
            RaisePets();
            foreach (PetForm f in All)
                foreach (PetObj o in f.objs)
                    if (o.Win != null && !o.Win.IsDisposed && o.Win.IsHandleCreated)
                        try { Native.SetWindowPos(o.Win.Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE); } catch { }
            foreach (PetForm f in All) if (f.basePet == null) f.RaiseRiders();
            if (match != null) match.Order = "";   // 比赛：下一帧按远近重新排一遍
        }

        // ================================================================ 一起上班：四只都在、Claude Code 在干活时，在任务栏上摆开一排小工位
        // 每只坐进一个小隔间，对着小电脑噼里啪啦敲；第一只头上照常转圈。干完了大家蹦一下，工位收起来。
        class Office { public PetForm Host; public readonly List<PetForm> Members = new List<PetForm>(); public double[] SeatX; public ObjWin Win; public double EndT = -1; }
        static Office office;
        internal const int CUB_W = 28, CUB_H = 28;   // 一个隔间多宽（含一块隔板）；整排多高（含隔板顶上的绿植）
        internal const string OfficePal = "P=#8F9AA7,p=#B5BEC8,B=#CDD5DE,b=#BDC7D2,F=#9AA4AF,Y=#F6D365,O=#F4A58A,g=#A6D99A,W=#F8F6F0,w=#BDB6A8,K=#4A4540,T=#C27C4E,t=#9E5F37,L=#4F9A45,l=#7BC46A,R=#E5484D,U=#6FA8DC";
        static readonly string[] OfNoteY = { "YYY", "YYY", "YY." }, OfNoteO = { "OOO", "OOO", "OO." }, OfNoteG = { "ggg", "ggg", "gg." };
        static readonly string[] OfPaper = { "WWWWW", "WwwwW", "WWWWW", "WwwwW", "WWWWW", "WwwWW" };
        static readonly string[] OfCalendar = { "RRRRRR", "WWWWWW", "WwWwWw", "WWWWWW", "WwWwWW" };
        static readonly string[] OfClock = { "..KKK..", ".KWWWK.", "KWWKWWK", "KWWKKWK", "KWWWWWK", ".KWWWK.", "..KKK.." };
        static readonly string[] OfChart = { "WWWWWWW", "WWWWWRW", "WWWgWRW", "WUWgWRW", "WUWgWRW", "WWWWWWW" };
        static readonly string[] OfPlant = { ".L.l", "lLLl", ".LL.", "TTTT", ".tt." };
        bool atDesk; double nextDeskFx, deskLookUntil, deskStretchUntil; int deskLookDir;

        static void Stamp(char[][] g, int x, int y, string[] art)
        {
            for (int r = 0; r < art.Length; r++)
                for (int c = 0; c < art[r].Length; c++)
                    if (art[r][c] != '.' && y + r >= 0 && y + r < g.Length && x + c >= 0 && x + c < g[y + r].Length) g[y + r][x + c] = art[r][c];
        }

        // n 个小隔间（正面看）：两格宽的隔板，中间是布面的后挡板，上面贴着便利贴、日程表、挂钟、小图表；
        // 两头的隔板上各放一盆小绿植；最底下一行是地毯。第 i 个隔间的中间在 (16 + i*CUB_W) 格
        internal static string[] OfficeRows(int n)
        {
            int w = n * CUB_W + 4, h = CUB_H;
            char[][] g = new char[h][];
            for (int y = 0; y < h; y++) { g[y] = new char[w]; for (int x = 0; x < w; x++) g[y][x] = '.'; }
            for (int i = 0; i < n; i++)
            {
                int ix = 3 + i * CUB_W;
                for (int y = 7; y < h - 1; y++)
                    for (int x = ix; x < ix + CUB_W - 2; x++)
                        g[y][x] = y == 7 || (x * 3 + y * 5) % 11 == 0 ? 'b' : 'B';   // 布面，一点点纹理
                switch (i % 4)
                {
                    case 0: Stamp(g, ix + 4, 10, OfNoteY); Stamp(g, ix + 8, 11, OfNoteO); Stamp(g, ix + 17, 9, OfPaper); break;
                    case 1: Stamp(g, ix + 4, 9, OfCalendar); Stamp(g, ix + 19, 11, OfNoteG); break;
                    case 2: Stamp(g, ix + 3, 11, OfNoteY); Stamp(g, ix + 15, 8, OfClock); break;
                    default: Stamp(g, ix + 4, 10, OfNoteO); Stamp(g, ix + 15, 9, OfChart); break;
                }
            }
            for (int i = 0; i <= n; i++)
            {
                int px = 1 + i * CUB_W;
                for (int y = 5; y < h - 1; y++) { char c = y == 5 ? 'p' : 'P'; g[y][px] = c; g[y][px + 1] = c; }
            }
            Stamp(g, 0, 0, OfPlant); Stamp(g, w - 4, 0, OfPlant);
            for (int x = 0; x < w; x++) g[h - 1][x] = 'F';   // 地毯
            string[] rows = new string[h];
            for (int y = 0; y < h; y++) rows[y] = new string(g[y]);
            return rows;
        }

        int DeskIdx() { return office == null ? -1 : office.Members.IndexOf(this); }

        // 能不能拉来一起上班：没在「待在这儿别动」、没在梦里、没被拎着、跟第一只在同一块屏幕
        bool CanJoinOffice(PetForm f, Rectangle wa)
        {
            return f != null && All.Contains(f) && !f.IsDisposed && !f.hiddenForFullscreen && !f.stay && f.dreamForm == null && !f.dragging && f.WorkArea() == wa;
        }

        // Claude Code 开始干活了（第一只收到 thinking 时调用）：四只都在就摆开一排工位，大家各就各位
        void StartOffice()
        {
            if (NightNow() && office == null) return;   // 夜里大家睡觉：不摆工位（摆了会把睡着的都叫起来）
            if (office != null)
            {
                if (office.EndT >= 0) { office.EndT = -1; CallToDesks(); }   // 刚要收工又来活了：接着干
                return;
            }
            if (All.Count < MAX_PETS || hiddenForFullscreen || stay || dreamForm != null) return;
            Rectangle wa = WorkArea();
            List<PetForm> ps = new List<PetForm>();
            foreach (PetForm f in All) if (CanJoinOffice(f, wa)) ps.Add(f);
            if (ps.Count < 2 || !ps.Contains(this)) return;
            int n = ps.Count, s = S;
            double width = (n * CUB_W + 4) * s;
            if (width > wa.Width - 4 * s) return;
            if (match != null) match.Host.EndMatch(false);
            double left = Math.Round(Clamp(CX - width / 2, wa.Left + 2.0 * s, wa.Right - 2.0 * s - width));
            ps.Sort((a, b) => a.CX.CompareTo(b.CX));   // 按现在站的位置排座位，走过去不用互相穿过
            Office o = new Office();
            o.Host = this; o.SeatX = new double[n];
            for (int i = 0; i < n; i++) { o.Members.Add(ps[i]); o.SeatX[i] = left + (16 + i * CUB_W) * s; }
            string[] rows = OfficeRows(n);
            o.Win = MakeDeco(rows, OfficePal, s, (int)left, (int)(wa.Bottom - CUB_H * s));
            if (hiddenForFullscreen) o.Win.Hide();
            office = o;
            RaisePets();
            CallToDesks();
            Dbg("office start: " + n + " desks");
        }

        // 叫伙伴们回工位（第一只自己走，不在这里叫）；睡着的叫醒，在半空 / 爬墙的落地以后自己会去
        void CallToDesks()
        {
            foreach (PetForm f in office.Members)
            {
                if (f == office.Host || f.IsDisposed || !All.Contains(f) || f.atDesk) continue;
                if (f.Sleeping()) { f.ResetPlans(); f.Wake(false); continue; }   // 伸完懒腰就去
                if (f.Grounded() && !f.dragging) { f.ResetPlans(); f.StartIdle(); }
            }
        }

        // 去自己的工位（站在窗口上 / 伙伴头上的先跳下来）
        void GoToDesk()
        {
            int i = DeskIdx();
            if (i < 0) { StartIdle(); return; }
            double x = office.SeatX[i];
            if (plat != IntPtr.Zero)
            {
                // 站在窗口上：别走到窗口边上再跳（大窗口要走好久），就地穿过标题栏往下跳，顺势往工位那边飘
                afterLandThen = GoToDesk;
                Jump(Mood.Happy, S * 35, true, Clamp((x - CX) * 0.6, -S * 70, S * 70), true);
                return;
            }
            if (basePet != null) { HopDown(x >= CX ? 1 : -1, GoToDesk); return; }
            Dbg(string.Format("to desk {0} at {1:F0}", i, x / S));
            if (Math.Abs(CX - x) > 2 * S) { WalkTo(x, S * 16, SitAtDesk); return; }
            SitAtDesk();
        }

        void SitAtDesk()
        {
            int i = DeskIdx();
            if (i < 0 || office.EndT >= 0) { if (ccWorking) StartThink(true); else SetState(St.Idle, 1 + R01() * 2); return; }
            if (Math.Abs(CX - office.SeatX[i]) <= 4 * S && plat == IntPtr.Zero && basePet == null) SetCX(office.SeatX[i]);
            facing = 1;   // 杯子都放在右手边，一排看着整齐
            if (ccWorking) StartThink(true); else SetState(St.Work, 1e9);
            atDesk = true;
            nextDeskFx = now + 3 + R01() * 5;
        }

        // 第一只开始干活：有工位就去工位，没有就原地坐下
        void WorkNow()
        {
            if (DeskIdx() >= 0 && office.EndT < 0) { GoToDesk(); return; }
            StartThink(true);
        }

        // 工位上：偶尔歪头看看隔壁在干嘛、伸个懒腰
        void DeskFx()
        {
            if (now < nextDeskFx || office == null) return;
            nextDeskFx = now + 5 + R01() * 7;
            double r = R01();
            int i = DeskIdx(), last = office.Members.Count - 1;
            if (r < 0.4) { deskLookDir = i <= 0 ? 1 : i >= last ? -1 : (R01() < 0.5 ? -1 : 1); deskLookUntil = now + 1.2 + R01() * 0.8; }
            else if (r < 0.6) { deskStretchUntil = now + 1.1; if (bubble == null) Emo(R01() < 0.5 ? "note" : "dots", 1.0); }
        }

        // 第一只每一帧调用：Claude 干完了，等大家蹦完再把工位收起来
        void UpdOffice()
        {
            Office o = office;
            if (!ccWorking && o.EndT < 0) o.EndT = now + 3;
            if (o.EndT >= 0 && now >= o.EndT) EndOffice();
        }

        void EndOffice()
        {
            Office o = office;
            if (o == null) return;
            office = null;
            if (o.Win != null) { try { o.Win.Close(); o.Win.Dispose(); } catch { } }
            Dbg("office end");
        }

        // 主持比赛的那只每一帧调用：看有没有人走了、球出底线、时间到了、进球庆祝完了
        // ================================================================ 交流：聊几句、围观捧场、晕了去安慰、分零食

        // 聊天的台词：A = 先开口的，B = 伙伴；"#xx" = 只冒一个表情；"=xx" = 聊完一起做什么
        static readonly string[][] ChatTalks = {
            new[] { "A:今天写了几行代码？", "B:三行", "B:删了两行", "=giggle" },
            new[] { "A:刚才鼠标嗖一下飞过去了", "B:看到了！", "A:下次一定抓住它", "=look" },
            new[] { "A:You're absolutely right!", "B:我还什么都没说呢", "=giggle" },
            new[] { "A:git push 了吗？", "B:推了", "A:推到哪个分支了？", "B:#sweat", "=giggle" },
            new[] { "A:ultrathink…", "B:想出什么了？", "A:想吃饼干", "=giggle" },
            new[] { "A:我梦到一大块饼干", "B:分我一半！", "A:梦里的，分不了…", "B:#sweat", "=giggle" },
            new[] { "A:要不要叠高高？", "B:好呀！", "=stack" },
            new[] { "A:踢会儿球？", "B:来！", "=ball" },
            new[] { "A:跳个舞吧~", "B:走起！", "=dance" },
            new[] { "A:今天也辛苦啦", "B:你也是！", "A:击个掌~", "=five" },
            new[] { "A:我们提醒一下吧", "B:提醒什么？", "A:该喝口水啦", "B:#idea", "=look" },
            new[] { "A:你会爬墙吗？", "B:当然会", "A:我也会！", "=giggle" },
            new[] { "A:要不要讨点零食？", "B:好主意！", "=beg" },
            new[] { "A:Claude 今天好忙", "B:我们也敲敲键盘？", "A:嗯！", "=work" },
            new[] { "A:这个窗口好高", "B:架根杆子爬上去？", "A:下次吧~", "=giggle" },
            new[] { "A:任务栏下面是什么？", "B:沙子吧", "A:怪不得能挖", "=giggle" },
            new[] { "A:你觉得我们像什么？", "B:像小螃蟹", "A:#heart", "=heart" },
            new[] { "A:我刚才是不是很帅", "B:#q", "A:#sweat", "=giggle" },
        };
        static readonly string[][] ChatEmotes = {   // 关掉冒字时：只比划表情
            new[] { "A:#q", "B:#idea", "A:#star", "=giggle" },
            new[] { "A:#note", "B:#note", "=dance" },
            new[] { "A:#heart", "B:#heart", "=heart" },
            new[] { "A:#dots", "B:#dots", "A:#idea", "=five" },
            new[] { "A:#q", "B:#bang", "=look" },
        };

        string[] PickChat(PetForm f)
        {
            if (!petTalk) return ChatEmotes[rnd.Next(ChatEmotes.Length)];
            int h = DateTime.Now.Hour;
            double r = R01();
            if (r < 0.2)
            {
                if (h >= 23 || h < 5) return new[] { "A:好晚了…", "B:屏幕前的人还没睡", "A:那我们先睡", "=nap" };
                if (h >= 6 && h < 10) return new[] { "A:早上好！", "B:早！今天也加油", "=five" };
                if (h == 11 || h == 12) return new[] { "A:午饭吃什么？", "B:吃 bug！", "A:#sweat", "=giggle" };
                if (h == 17 || h == 18) return new[] { "A:快下班啦", "B:下班去哪玩？", "A:任务栏上散散步", "=giggle" };
            }
            if (r < 0.4)
            {
                // 夸夸伙伴戴的东西
                switch (AccOf(f.slot))
                {
                    case 0: return new[] { "A:你怎么什么都不戴？", "B:我是原版！", "=giggle" };
                    case 1: return new[] { "A:你的眼镜真好看", "B:嘿嘿，看代码更清楚", "=heart" };
                    case 2: return new[] { "A:帽子好帅！", "B:借你戴一下？", "A:#heart", "=giggle" };
                    case 3: return new[] { "A:小花好香", "B:春天到啦", "=heart" };
                    case 4: return new[] { "A:西瓜甜吗？", "B:还没舍得吃", "=giggle" };
                    case 5: return new[] { "A:枫叶好红", "B:秋天来啦~", "=heart" };
                    case 6: return new[] { "A:围巾暖和吗？", "B:暖和！", "A:#heart", "=heart" };
                }
            }
            if (r < 0.5) return ChatEmotes[rnd.Next(ChatEmotes.Length)];
            string[] s = ChatTalks[rnd.Next(ChatTalks.Length)];
            if (s == lastChat) s = ChatTalks[rnd.Next(ChatTalks.Length)];   // 别连着聊同一段
            return s;
        }

        // 伙伴现在有空吗：醒着、闲着、没叠着、跟我站在同一块地面上
        bool FreeToTalk(PetForm f)
        {
            return f != null && f != this && All.Contains(f) && !f.IsDisposed && !f.hiddenForFullscreen && !f.Sleeping()
                && Rest(f.state) && f.state != St.Work && f.basePet == null && f.Rider() == null
                && f.plat == plat && f.WorkArea() == WorkArea() && now >= f.braceUntil;
        }

        // 走到伙伴跟前聊几句
        void StartChat(PetForm f)
        {
            if (f == null || (stay && Math.Abs(f.CX - CX) > 40 * S)) { StartIdle(); return; }
            nextChatAll = now + 70 + R01() * 90;
            chatScript = PickChat(f); lastChat = chatScript;
            int side = f.CX >= CX ? 1 : -1;
            double stand = f.CX - side * 22 * S;
            f.Brace(this, 9);
            if (stay || Math.Abs(stand - CX) < 4 * S) { BeginChat(f); return; }
            WalkTo(stand, S * 12, () => BeginChat(f));
        }

        void BeginChat(PetForm f)
        {
            if (!All.Contains(f) || f.IsDisposed || !f.Grounded() || f.Sleeping() || f.chatWith != null || !Grounded() || Math.Abs(f.CX - CX) > 42 * S)
            { Emo("q", 1.2); StartIdle(); return; }
            ResetPlans(); gvx = 0;
            facing = f.CX >= CX ? 1 : -1;
            chatWith = f; chatLead = true; chatIdx = -1; chatNext = now + 0.3; talkUntil = 0; nodAt = 0;
            SetState(St.Chat, 40);
            f.ResetPlans(); f.gvx = 0; f.braceUntil = 0; f.facing = -facing;
            f.chatWith = this; f.chatLead = false; f.talkUntil = 0; f.nodAt = 0;
            f.SetState(St.Chat, 40);
            Dbg("chat with #" + f.slot + ": " + string.Join(" / ", chatScript));
        }

        // 聊天：先开口的那只管着轮到谁说；另一只看着它、听着听着点个头
        void UpdChat()
        {
            PetForm f = chatWith;
            if (f == null || !All.Contains(f) || f.IsDisposed || f.state != St.Chat || f.chatWith != this || stT >= stDur)
            {
                // 对方被拎走 / 被叫去干别的了：聊不下去了
                chatWith = null; chatLead = false;
                if (Grounded()) { Emo("q", 1.0); StartIdle(); }
                return;
            }
            if (Math.Abs(f.CX - CX) > 2 * S) facing = f.CX >= CX ? 1 : -1;
            attnX = f.CX; attnY = f.posY - 10 * S; attnUntil = now + 0.3;
            if (nodAt > 0 && now >= nodAt) { nodAt = 0; sqV -= 2.5; }
            if (!chatLead || now < chatNext) return;
            chatIdx++;
            if (chatScript == null || chatIdx >= chatScript.Length) { FinishChat(null); return; }
            string line = chatScript[chatIdx];
            if (line.StartsWith("=")) { FinishChat(line.Substring(1)); return; }
            PetForm who = line.StartsWith("B:") ? f : this, other = who == this ? f : this;
            string body = line.Length > 2 ? line.Substring(2) : "";
            double dur;
            other.ClearBubble();
            if (body.StartsWith("#")) { who.Emo(body.Substring(1), 1.5); dur = 1.3; }
            else { dur = Clamp(1.2 + body.Length * 0.13, 1.6, 3.4); who.Say(body, dur + 0.3); who.BabbleFor(body); }
            who.talkUntil = who.now + dur;
            who.sqV -= 1.5;
            other.nodAt = other.now + dur * (0.4 + R01() * 0.3);
            chatNext = now + dur + 0.25;
        }

        // 聊完了：一起笑、一起看你、一起跳舞 / 叠高高 / 踢球 / 击掌……
        void FinishChat(string act)
        {
            PetForm f = chatWith;
            chatWith = null; chatLead = false;
            bool ok = f != null && All.Contains(f) && !f.IsDisposed && f.state == St.Chat;
            if (f != null) { f.chatWith = null; f.chatLead = false; }
            Dbg("chat end: " + (act ?? "-"));
            if (!ok) { StartIdle(); return; }
            switch (act)
            {
                case "giggle":
                    SetState(St.Giggle, 1.2); nextFx = now; blushUntil = now + 1.5;
                    f.SetState(St.Giggle, 1.2); f.nextFx = f.now; f.blushUntil = f.now + 1.5;
                    return;
                case "heart":
                    Emo("heart", 1.6); blushUntil = now + 2.5; StartHappy(0.9);
                    f.Emo("heart", 1.6); f.blushUntil = f.now + 2.5; f.StartHappy(0.9);
                    return;
                case "look": case "beg":
                    LookAtYou(act == "beg" ? "heart" : "q");
                    f.LookAtYou(act == "beg" ? "heart" : "q");
                    return;
                case "dance":
                    f.StartIdle();
                    StartDance(true);
                    if (f.state != St.Dance) f.StartDance(false);
                    return;
                case "stack":
                    {
                        PetForm up = R01() < 0.5 ? this : f, down = up == this ? f : this;
                        down.StartIdle(); up.ResetPlans(); up.lastStackT = -100;
                        if (down.TowerTop().CanBeClimbed(up)) up.StackOnto(down);
                        else { up.Emo("sweat", 1.2); up.StartIdle(); }
                        if (up == f) StartIdle();
                    }
                    return;
                case "ball":
                    f.StartIdle();
                    if (AnyBall() == null) KickAbout(f); else KickTogether();
                    return;
                case "five":
                    f.StartIdle(); HighFiveWith(f);
                    return;
                case "nap":
                    StartNap(0); f.StartNap(0);
                    return;
                case "work":
                    StartWork(); f.StartWork();
                    return;
            }
            StartIdle(); f.StartIdle();
        }

        // 一起转过来看着你（鼠标那儿）
        void LookAtYou(string emo)
        {
            Point c = Cursor.Position;
            facing = c.X >= CX ? 1 : -1;
            attnX = c.X; attnY = c.Y; attnUntil = now + 2.4;
            Emo(emo, 1.6);
            SetState(St.Idle, 2.4);
        }

        // 我开始弹琴 / 看书 / 举重……：旁边闲着的伙伴有时候会过来坐着看
        static bool IsShow(St s) { return s == St.Guitar || s == St.Music || s == St.Read || s == St.Lift || s == St.Castle || s == St.Bubbles || s == St.Plane; }

        void MaybeAudience(St s)
        {
            if (All.Count < 2 || plat != IntPtr.Zero || basePet != null || R01() > 0.45) return;
            PetForm best = null; double bd = 170 * S;
            foreach (PetForm f in All)
            {
                if (f == this || f.stay || !FreeToTalk(f)) continue;
                double d = Math.Abs(f.CX - CX);
                if (d < bd) { bd = d; best = f; }
            }
            if (best != null) best.GoWatch(this, s);
        }

        void GoWatch(PetForm p, St s)
        {
            ResetPlans();
            watchPet = p; watchWhat = s;
            int side = p.CX >= CX ? 1 : -1;
            double stand = p.CX - side * 26 * S;
            Emo(s == St.Guitar || s == St.Music ? "note" : "q", 1.0);
            if (Math.Abs(stand - CX) < 4 * S) BeginWatch(); else WalkTo(stand, S * 11, BeginWatch);
        }

        void BeginWatch()
        {
            PetForm p = watchPet;
            if (p == null || !All.Contains(p) || p.IsDisposed || p.state != watchWhat || !Grounded()) { watchPet = null; StartIdle(); return; }
            ResetPlans(); gvx = 0;
            facing = p.CX >= CX ? 1 : -1;
            SetState(St.Watch, 30);
            nextWatchFx = now + 1.0 + R01();
            if (p.emoteKind == null) p.Emo("heart", 1.0);   // 表演的那只：有人看，开心
            Dbg("watch #" + p.slot + " " + watchWhat);
        }

        // 坐着看：弹琴就跟着晃，举重就喊加油，看书就凑过去好奇；表演完了鼓掌
        void UpdWatch()
        {
            PetForm p = watchPet;
            if (p == null || !All.Contains(p) || p.IsDisposed || p.state != watchWhat || stT >= stDur)
            {
                bool ok = p != null && All.Contains(p) && !p.IsDisposed && !p.Sleeping() && stT < stDur;
                watchPet = null;
                if (ok)
                {
                    StartHappy(1.1); cheerUntil = now + 1.1; Emo(R01() < 0.5 ? "star" : "heart", 1.4);
                    if (p.emoteKind == null) p.Emo("heart", 1.2);
                }
                else StartIdle();
                return;
            }
            attnX = p.CX; attnY = p.posY - 8 * S; attnUntil = now + 0.3;
            if (now < nextWatchFx) return;
            nextWatchFx = now + 1.8 + R01() * 1.6;
            switch (watchWhat)
            {
                case St.Guitar: case St.Music: Emo("note", 1.2); break;
                case St.Read: Emo(R01() < 0.5 ? "q" : "idea", 1.2); break;
                case St.Lift: Emo(R01() < 0.6 ? "bang" : "star", 1.0); cheerUntil = now + 0.6; sqV -= 3; break;
                case St.Castle: Emo(R01() < 0.5 ? "heart" : "star", 1.2); break;
                case St.Bubbles: Emo("note", 1.0); cheerUntil = now + 0.5; break;
                default: Emo("bang", 1.0); break;
            }
        }

        // 我晕了：离得最近、闲着的伙伴跑过来拍拍我（等它来之前多晕一会儿）
        void CallComforter()
        {
            if (All.Count < 2) return;
            PetForm best = null; double bd = 150 * S;
            foreach (PetForm f in All)
            {
                if (f == this || f.stay || !FreeToTalk(f)) continue;
                double d = Math.Abs(f.CX - CX);
                if (d < bd) { bd = d; best = f; }
            }
            if (best == null) return;
            stDur = Math.Max(stDur, Math.Min(7, 1.2 + bd / (S * 17)));
            best.GoComfort(this);
        }

        void GoComfort(PetForm d)
        {
            ResetPlans();
            patPet = d;
            Emo("bang", 0.8);
            int side = d.CX >= CX ? 1 : -1;
            double stand = d.CX - side * 19 * S;
            if (Math.Abs(stand - CX) < 3 * S) StartPat(); else WalkTo(stand, S * 17, StartPat);
        }

        void StartPat()
        {
            PetForm d = patPet;
            if (d == null || !All.Contains(d) || d.IsDisposed || !Grounded() || Math.Abs(d.CX - CX) > 30 * S) { patPet = null; StartIdle(); return; }
            ResetPlans(); gvx = 0;
            facing = d.CX >= CX ? 1 : -1;
            if (d.state != St.Dizzy) { patPet = null; Emo("heart", 1.2); StartWave(1.0); return; }   // 已经缓过来了：挥挥手
            SetState(St.Pat, 1.5); nextFx = now + 0.15;
            Dbg("pat #" + d.slot);
        }

        void UpdPat()
        {
            PetForm d = patPet;
            if (d == null || !All.Contains(d) || d.IsDisposed) { patPet = null; StartIdle(); return; }
            if (now >= nextFx) { nextFx = now + 0.32; d.sqV -= 2.5; d.leanV += facing * 0.8; }   // 拍一下，它晃一晃
            if (stT < stDur) return;
            patPet = null;
            Emo("heart", 1.3);
            if (d.state == St.Dizzy) { d.ResetPlans(); d.Emo("heart", 1.5); d.blushUntil = d.now + 2; d.StartHappy(0.9); }
            StartHappy(0.7);
        }

        // 吃到好吃的：走过去分伙伴一口
        bool TryShareSnack()
        {
            if (stay || basePet != null || Rider() != null) return false;
            PetForm best = null; double bd = 150 * S;
            foreach (PetForm f in All)
            {
                if (f == this || !FreeToTalk(f) || f.fedTimes.Count >= 4) continue;
                double d = Math.Abs(f.CX - CX);
                if (d < bd) { bd = d; best = f; }
            }
            if (best == null) return false;
            lastShare = now;
            PetForm g = best;
            Emo("idea", 1.0);
            g.Brace(this, 8);
            int side = g.CX >= CX ? 1 : -1;
            double stand = g.CX - side * 20 * S;
            if (Math.Abs(stand - CX) < 3 * S) GiveSnack(g); else WalkTo(stand, S * 13, () => GiveSnack(g));
            return true;
        }

        void GiveSnack(PetForm g)
        {
            if (!All.Contains(g) || g.IsDisposed || !g.Grounded() || g.Sleeping() || Math.Abs(g.CX - CX) > 30 * S) { Emo("q", 1.2); StartIdle(); return; }
            facing = g.CX >= CX ? 1 : -1;
            g.braceUntil = 0; g.facing = -facing;
            if (g.FeedNow()) { Emo("heart", 1.4); StartWave(0.9); Dbg("share snack with #" + g.slot); }
            else StartIdle();
        }

        // 右键菜单「聊聊天」/ 信号 chat：叫离得最近的伙伴过来聊几句
        void ChatNow()
        {
            if (!Grounded() || basePet != null || state == St.Chat) return;
            PetForm f = NearestFriend(5000);
            if (f == null || f.basePet != null || !f.Grounded() || f.state == St.Chat) return;
            if (Sleeping()) Wake(false);
            if (f.Sleeping()) f.Wake(false);
            f.ResetPlans(); f.StartIdle();
            ResetPlans();
            StartChat(f);
        }

        // 地面上移动：速度慢慢加上去、慢慢减下来，腿按走过的距离迈步（不会脚底打滑）
        void GroundMove(double target, double dt, double accel)
        {
            gvx += (target - gvx) * Math.Min(1, dt * accel);
            if (target == 0 && Math.Abs(gvx) < 0.2 * S) gvx = 0;
            MoveX(gvx * dt);
            walkPh += Math.Abs(gvx) * dt / (S * 1.5);
        }

        // 当前脚下这块地方，左右能走到哪（中心 x）
        void SurfaceSpan(out double lo, out double hi)
        {
            if (basePet != null)
            {
                // 站在伙伴头上：只能在头顶那一小块地方挪一挪
                lo = basePet.CX - 2 * S; hi = basePet.CX + 2 * S;
                return;
            }
            if (plat != IntPtr.Zero)
            {
                Rectangle vs = SystemInformation.VirtualScreen;
                lo = Math.Max(vs.Left + 11 * S, platL + 5 * S); hi = Math.Min(vs.Right - 11 * S, platR - 5 * S);
            }
            else
            {
                Rectangle wa = WorkArea();
                lo = wa.Left + 11 * S; hi = wa.Right - 11 * S;
            }
            if (hi < lo) { double mid = (lo + hi) / 2; lo = hi = mid; }
        }

        void ClampToSurface()
        {
            double lo, hi, cx = CX;
            SurfaceSpan(out lo, out hi);
            if (cx < lo || cx > hi) { SetCX(Clamp(cx, lo, hi)); gvx = 0; }
        }

        void UpdWalk(double dt)
        {
            if (!hasWalkTarget && CheckAfk()) return;   // 有目的地走过去（爬杆子、跳窗口……）时不半路睡着，到了再说
            if (stay) { ResetPlans(); StartIdle(); return; }
            double speed = walkSpeed > 0 ? walkSpeed : S * 9;
            bool chasing = follow && now - lastCursorMove < 6 && !hasWalkTarget;
            if (hasWalkTarget)
            {
                double dx = walkTarget - CX;
                if (Math.Abs(dx) < 1.5 * S) { Arrive(); return; }
                facing = dx > 0 ? 1 : -1;
                speed = Math.Min(speed, Math.Abs(dx) * 3 + S * 3);   // 快到了就放慢
            }
            else if (chasing)
            {
                double dx = lastCursor.X - CX;
                if (Math.Abs(dx) < 6 * S) { StartIdle(); return; }
                facing = dx > 0 ? 1 : -1;
                speed = S * 14;
            }
            GroundMove(facing * speed, dt, 5);
            double lo, hi, cx = CX;
            SurfaceSpan(out lo, out hi);
            if (cx < lo || cx > hi)
            {
                SetCX(cx < lo ? lo : hi);
                gvx = 0;
                if (hasWalkTarget) { Arrive(); return; }
                if (chasing) { StartIdle(); return; }
                AtEdge(lo, hi);
                return;
            }
            // 随便走着走到离屏幕左右边不到 12 格（地上，或者标题栏一直伸到屏幕边）：一定走过去扒上墙
            if (!hasWalkTarget && !chasing && now >= climbCooldownUntil && Rider() == null)
            {
                Rectangle wa = WorkArea();
                int side = facing;
                double dist = side < 0 ? cx - (wa.Left + 11 * S) : (wa.Right - 11 * S) - cx;
                bool screenEdge = plat == IntPtr.Zero || (side < 0 ? platL <= wa.Left + S : platR >= wa.Right - S);
                if (dist <= 12 * S && screenEdge && SideFree(side))
                {
                    int sd = side;
                    WalkTo(sd < 0 ? wa.Left + 11 * S : wa.Right - 11 * S, Math.Max(speed, S * 9), () => StartClimb(sd));
                    return;
                }
            }
            if (stT >= stDur) StartIdle();
        }

        // 走到边上：在窗口上可能坐下、晃两下、跳下去；在地上就转身接着走
        void AtEdge(double lo, double hi)
        {
            double r = R01();
            // 走到了屏幕的左右边（地上，或者窗口标题栏一直伸到屏幕边）：偶尔转身扒上墙往上爬
            {
                Rectangle wa = WorkArea();
                int side = CX <= (lo + hi) / 2 ? -1 : 1;
                bool atSide = side < 0 ? CX <= wa.Left + 12 * S : CX >= wa.Right - 12 * S;
                if (!stay && atSide && now >= climbCooldownUntil && SideFree(side) && Rider() == null && basePet == null) { StartClimb(side); return; }
            }
            if (basePet != null) { HopDown(facing, null); return; }   // 在伙伴头上走到边了：跳下去
            if (plat != IntPtr.Zero)
            {
                if (r < 0.3) { facing = -facing; leanV -= facing * 1.5; }
                else if (r < 0.5) StartLedge();
                else if (r < 0.7) StartTeeter();
                else if (r < 0.88) HopDown(facing, null);
                else StartLook();
                return;
            }
            facing = CX <= lo ? 1 : -1;
            leanV -= facing * 1.5;
        }

        // ================================================================ 挖沙躲起来（只在地面上，也就是任务栏上沿）
        void StartBurrow()
        {
            if (plat != IntPtr.Zero || basePet != null || !Grounded()) { StartIdle(); return; }
            ResetPlans();
            gvx = 0; sitOff = 0;
            burrowPhase = 0; burrowDig = 2 + R01(); sink = 0; moundLevel = 0; burrowSleep = false; popStartled = false;
            moundX = CX; moundY = posY;
            nextSand = now; nextBurrowFx = now + 3;
            SetState(St.Burrow, 1e9);
        }

        // 菜单「挖沙躲起来」：埋着的时候再点就钻出来
        void ToggleBurrow()
        {
            if (state == St.Burrow) { PopOut(false); return; }
            if (Grounded() && plat == IntPtr.Zero) { StartBurrow(); return; }
            Emo("q", 1.0);
        }

        // 沙子往上、往两边溅（2x2 小格，带重力；位置是小人画布坐标）
        void SandBurst(int n)
        {
            for (int i = 0; i < n; i++)
                Spawn(Sprite.Crumb, R01() < 0.5 ? Sprite.SandA : Sprite.SandB, (5 + R01() * 14) * S, 15 * S,
                    (R01() - 0.5) * S * 30, -S * (8 + R01() * 10), 0.5 + R01() * 0.4, S * 60);
        }

        void UpdBurrow(double dt)
        {
            switch (burrowPhase)
            {
                case 0:   // 挖：一边刨一边往下沉，沙堆慢慢堆起来
                    {
                        double k = Math.Min(1, stT / burrowDig), e = k * k * (3 - 2 * k);
                        sink = 7 * e;
                        moundLevel = 3 * Math.Min(1, k * 1.3);
                        if (now >= nextSand)
                        {
                            nextSand = now + 0.05;
                            int side = R01() < 0.5 ? -1 : 1;
                            Spawn(Sprite.Crumb, R01() < 0.5 ? Sprite.SandA : Sprite.SandB, (side < 0 ? 4 : 18) * S + R01() * 2 * S, 15 * S,
                                side * S * (6 + R01() * 8), -S * (6 + R01() * 6), 0.6, S * 50);
                        }
                        if (k >= 1) { burrowPhase = 1; burrowEnd = now + 8 + R01() * 22; nextBurrowFx = now + 2 + R01() * 3; }
                    }
                    break;
                case 1:   // 埋好了：只露头顶和眼睛
                    {
                        double want = now < blushUntil ? 6 : 7;   // 被摸了往上冒一点，脸红看得见
                        sink += (want - sink) * Math.Min(1, dt * 6);
                        // 你走开太久：就在沙里睡着（闭眼冒 Z），你回来再醒
                        if (!ccWorking && idleSecs > 180) { if (!burrowSleep) { burrowSleep = true; nextBurrowZ = now + 0.8; } }
                        else if (burrowSleep && idleSecs < 1) { burrowSleep = false; burrowEnd = Math.Max(burrowEnd, now + 2 + R01() * 3); }
                        if (burrowSleep)
                        {
                            if (now >= nextBurrowZ) { nextBurrowZ = now + 1.5; Spawn(Sprite.Zed, Sprite.ZC, 18 * S, (3 + sink) * S, S * 3, -S * 5, 2.2, 0); }
                            break;
                        }
                        // 害羞：鼠标从旁边飞快划过，整个缩进沙里
                        double dx = lastCursor.X - CX, dy = lastCursor.Y - (posY - 3 * S);
                        if (cursorSpeed > S * 300 && dx * dx + dy * dy < (18.0 * S) * (18.0 * S))
                        {
                            burrowPhase = 2; phaseT0 = now; sinkFrom = sink; shyUntil = now + 2 + R01() * 2;
                            break;
                        }
                        if (now >= nextBurrowFx)
                        {
                            nextBurrowFx = now + 3 + R01() * 3;
                            Spawn(Sprite.Puff, R01() < 0.5 ? Sprite.SandA : Sprite.SandB, (6 + R01() * 10) * S, 14 * S, (R01() - 0.5) * S * 4, -S * 2, 0.5, 0);
                        }
                        if (now >= burrowEnd) PopOut(false);
                    }
                    break;
                case 2:   // 缩进去，只剩沙堆
                    {
                        double k = Math.Min(1, (now - phaseT0) / 0.25);
                        sink = sinkFrom + (14 - sinkFrom) * k;
                        if (now >= shyUntil) { burrowPhase = 3; phaseT0 = now; }
                    }
                    break;
                case 3:   // 眼睛慢慢探出来
                    {
                        double k = Math.Min(1, (now - phaseT0) / 1.4);
                        sink = 14 - 7 * (1 - (1 - k) * (1 - k));
                        if (k >= 1) { burrowPhase = 1; burrowEnd = Math.Max(burrowEnd, now + 3); }
                    }
                    break;
                case 4:   // 钻出来：一下子冒出来，沙子四溅，小跳一下，落地抖掉沙子
                    {
                        double k = Math.Min(1, (now - phaseT0) / (popStartled ? 0.07 : 0.15));
                        sink = sinkFrom * (1 - k);
                        if (k >= 1)
                        {
                            sink = 0;
                            SandBurst(14);
                            moundFrom = moundLevel; moundT0 = now; moundLevel = 0;
                            bool st = popStartled;
                            ResetPlans();
                            Jump(st ? Mood.Plain : Mood.Happy, S * (st ? 72 : 55), false);
                            afterLandThen = StartShake;
                        }
                    }
                    break;
            }
        }

        // 钻出来（startled = 被点了一下，吓得一下子蹦出来）
        void PopOut(bool startled)
        {
            if (state != St.Burrow || burrowPhase == 4) return;
            popStartled = startled;
            burrowPhase = 4; phaseT0 = now; sinkFrom = sink;
            burrowSleep = false;
            if (startled) { Emo("bang", 1.0); Snd("startle", SND_YOU, 1, true); }
            else Snd("pop", SND_PLAY, 0.8, false);
        }

        // 埋着的时候被别的事打断：直接钻出来（沙子一溅），沙堆 0.5 秒塌掉
        void LeaveBurrowFx()
        {
            if (sink > 0.01) SandBurst(10);
            if (moundLevel > 0) { moundFrom = moundLevel; moundT0 = now; moundLevel = 0; }
            sink = 0;
            burrowSleep = false;
        }

        void StartShake() { SetState(St.Shake, 0.7); nextFx = now; }

        double MoundLevelNow()
        {
            if (state == St.Burrow) return moundLevel;
            double k = (now - moundT0) / 0.5;
            return k >= 0 && k < 1 ? moundFrom * (1 - k) : 0;
        }

        // 沙堆在窗体里的位置（24x3 格，下沿贴着地面）
        Rectangle MoundRect()
        {
            Point fp = FormPos();
            return new Rectangle((int)Math.Round(moundX - fp.X) - 12 * S, (int)Math.Round(moundY - fp.Y) - 3 * S, 24 * S, 3 * S);
        }

        // ================================================================ 爬墙（屏幕左右两边）
        // 在墙上时整个小人转 90°，脚贴着墙：左墙 rot = +π/2，右墙 rot = -π/2。
        // 转过去以后画布的 +x 方向在左墙朝下、在右墙朝上，所以「往上爬」时 facing = wallSide，往下爬时 facing = -wallSide。
        // 坐标：posX 让脚正好贴墙；身子中心（转轴）在 posY - 12 格，上下各占 12 格，所以 posY 就是身子最下沿。
        static double WallRot(int side) { return side < 0 ? Math.PI / 2 : -Math.PI / 2; }
        double WallPosX(Rectangle wa, int side) { return side < 0 ? wa.Left : wa.Right - 24 * S; }

        // 这一边是不是「真正的屏幕边」：外面没有挨着的另一块显示器（整条边都要查）
        bool SideFree(int side)
        {
            Rectangle mb = ScreenArea(), wa = WorkArea();
            int x = side < 0 ? mb.Left - 2 : mb.Right + 1;
            int[] ys = { wa.Top + 1, (wa.Top + wa.Bottom) / 2, wa.Bottom - 2 };
            foreach (int y in ys) if (Native.HasMonitorAt(x, y)) return false;
            return true;
        }

        // 从地上 / 标题栏上：转身扒上墙
        void StartClimb(int side)
        {
            if (stay) { StartIdle(); return; }
            Rectangle wa = WorkArea();
            ResetPlans();
            gvx = 0; sitOff = 0;
            plat = IntPtr.Zero;   // 从标题栏上直接扒到屏幕边上
            basePet = null;
            wallSide = side; onPole = false;
            SetCX(side < 0 ? wa.Left + 11 * S : wa.Right - 11 * S);
            facing = side;        // 朝上
            climbPhase = 0; climbMode = 1; climbV = 0; climbTop = false;
            SetState(St.Climb, 1e9);
            if (R01() < 0.3) Emo("note", 1.0);
        }

        // 扔过去速度不太大：扒住墙
        void GrabWall(int side)
        {
            Rectangle wa = WorkArea();
            double mid = posY - 9 * S;   // 保持身子中心不跳
            ResetPlans();
            velX = velY = 0; gvx = 0; sitOff = 0; mood = Mood.Plain;
            wallSide = side; onPole = false;
            SetState(St.Climb, 1e9);
            rot = WallRot(side);
            climbPhase = 1;
            posX = WallPosX(wa, side);
            posY = Clamp(mid + 12 * S, wa.Top + 24 * S, wa.Bottom);
            climbMode = 0; climbV = 0; climbTop = posY <= wa.Top + 24.5 * S;
            climbUntil = now + 0.7 + R01() * 1.3;
            facing = side;
            sqV -= 5; leanV += 1.5;
            Emo(R01() < 0.5 ? "bang" : "sweat", 0.9);
        }

        void UpdClimb(double dt)
        {
            Rectangle wa = WorkArea();
            double target = WallRot(wallSide);
            Native.RECT pr = new Native.RECT();
            if (onPole && !PoleWindow(out pr)) { Emo("sweat", 1.2); DropOff(); return; }   // 杆子顶着的窗口没了 / 挪走了：从杆子上掉下来
            if (onPole && poleSlant) { UpdSlantClimb(dt, pr); return; }
            if (climbPhase == 0)
            {
                // 转身扒上去：0.25 秒里慢慢转 90°
                double k = Math.Min(1, stT / 0.25);
                rot = target * (1 - (1 - k) * (1 - k));
                if (k >= 1)
                {
                    rot = target; climbPhase = 1;
                    posX = onPole ? PoleWallPosX(wallSide) : WallPosX(wa, wallSide);
                    posY = Clamp(posY, wa.Top + 24 * S, wa.Bottom);
                    climbMode = 1; climbUntil = now + 2 + R01() * 3;
                }
                return;
            }
            if (climbPhase == 2)
            {
                // 爬到底了：转回来站在地上
                double k = Math.Min(1, stT / 0.25);
                rot = target * (1 - k) * (1 - k);
                if (rot == 0) rot = 1e-6 * -wallSide;
                if (k >= 1)
                {
                    facing = -wallSide;
                    posY = wa.Bottom;
                    climbCooldownUntil = now + 6 + R01() * 4;
                    StartIdle();
                    if (R01() < 0.35) Emo("note", 1.2);
                }
                return;
            }
            rot = target;
            posX = onPole ? PoleWallPosX(wallSide) : WallPosX(wa, wallSide);
            if (onPole)
            {
                poleTopNow = pr.Top - 3 * S;   // 窗口上下挪了一点：杆子跟着
                if (posY <= Math.Max(pr.Top + 3 * S, wa.Top + 24 * S)) { PoleMantle(pr.Top); return; }   // 头已经过了标题栏（或者快顶到屏幕顶）：翻上去
            }
            // 沿墙的速度：和地上走路一样慢慢加速、慢慢停下，腿按爬过的距离迈步（爬杆子快一点）
            double cv = onPole ? S * 16 : S * 8;
            double want = climbMode > 0 ? -cv : climbMode < 0 ? cv : 0;
            climbV += (want - climbV) * Math.Min(1, dt * 5);
            if (want == 0 && Math.Abs(climbV) < 0.2 * S) climbV = 0;
            posY += climbV * dt;
            walkPh += Math.Abs(climbV) * dt / (S * 1.5);
            if (climbMode != 0) facing = climbMode > 0 ? wallSide : -wallSide;
            double topY = wa.Top + 24 * S, botY = wa.Bottom;
            if (posY <= topY)
            {
                posY = topY;
                if (climbMode > 0)
                {
                    // 爬到顶了：扒一会儿，再松手掉下去或者蹬墙跳回来
                    climbV = 0; climbMode = 0; climbTop = true;
                    climbUntil = now + 1.5 + R01() * 2;
                    Emo(R01() < 0.5 ? "sweat" : "note", 1.2);
                }
            }
            if (posY >= botY)
            {
                posY = botY;
                if (climbMode < 0) { climbPhase = 2; stT = 0; climbV = 0; return; }
            }
            if (climbMode == 0)
            {
                // 扒着：东张西望、眨眼、偶尔冒个表情
                if (now >= nextClingLook)
                {
                    nextClingLook = now + 0.7 + R01() * 1.0;
                    int[] xs = { -1, 0, 1, 0 }, ys = { -1, 0, 0, 1 };
                    clingLookX = xs[rnd.Next(4)]; clingLookY = ys[rnd.Next(4)];
                    if (R01() < 0.12 && emoteKind == null) { string[] em = { "note", "q", "heart", "dots" }; Emo(em[rnd.Next(em.Length)], 1.3); }
                }
            }
            if (now >= climbUntil) ClimbDecide(wa);
        }

        void ClimbDecide(Rectangle wa)
        {
            if (onPole)
            {
                // 爬杆子：一口气爬一段，停下来喘口气、东张西望，再接着往上
                if (climbMode != 0) { climbMode = 0; climbUntil = now + 0.4 + R01() * 0.8; nextClingLook = now; }
                else { climbMode = 1; climbUntil = now + 1.5 + R01() * 2.5; }
                return;
            }
            if (climbMode != 0)
            {
                // 爬了一段：停下来扒一会儿
                climbMode = 0; climbUntil = now + 1.2 + R01() * 2.5; nextClingLook = now;
                return;
            }
            if (climbTop) { if (R01() < 0.5) DropOff(); else KickOff(false); return; }
            double r = R01();
            if (r < 0.45 && posY > wa.Top + 30 * S) { climbMode = 1; climbUntil = now + 1.5 + R01() * 3; }
            else if (r < 0.75) { climbMode = -1; climbUntil = now + 60; }   // 一路爬回地上
            else KickOff(false);
        }

        // 蹬墙跳回屏幕里（startled = 被点了一下吓得跳开）
        void KickOff(bool startled)
        {
            Rectangle wa = WorkArea();
            int s = wallSide;
            double mid = posY - 12 * S;
            facing = -s;
            ResetPlans();
            climbCooldownUntil = now + 6 + R01() * 4;
            bool pole = onPole;
            SetState(St.Fall, 1e9);   // 先转回来（rot 归零）
            if (pole) SetCX(posX + 12 * S + s * S); else SetCX(s < 0 ? wa.Left + 11 * S : wa.Right - 11 * S);
            posY = Math.Min(wa.Bottom, mid + 9 * S);
            if (startled) { SpawnBang(); sqV += 5; }
            Launch(startled ? Mood.Plain : Mood.Happy, S * (startled ? 55 : 60 + R01() * 30), -s * S * (startled ? 70 : 90 + R01() * 60), false);
        }

        // 在墙顶上松手，掉下去
        void DropOff()
        {
            Rectangle wa = WorkArea();
            int s = wallSide;
            double mid = posY - 12 * S;
            facing = -s;
            ResetPlans();
            climbCooldownUntil = now + 6 + R01() * 4;
            bool pole = onPole;
            SetState(St.Fall, 1e9);
            if (pole) SetCX(posX + 12 * S + s * S); else SetCX(s < 0 ? wa.Left + 11 * S : wa.Right - 11 * S);
            posY = Math.Min(wa.Bottom, mid + 9 * S);
            velX = -s * S * 15; velY = 0;
            StartFall(Mood.Happy);
        }

        void UpdThink()
        {
            if (thinkPersistent)
            {
                if (!ccWorking) { StartIdle(); return; }
                if (now >= nextVerb) ShowThink();
                WorkFx(false);
                if (atDesk) DeskFx();
                return;
            }
            if (CheckAfk()) return;
            if (stT >= stDur) StartIdle();
        }

        // 办公：对着小电脑噼里啪啦敲，偶尔想一想、灵光一闪、遇到 bug 冒汗，咖啡杯冒热气
        void UpdWork()
        {
            if (atDesk)
            {
                // 在工位上陪着干活，一直到 Claude 干完、工位收起来：伸个懒腰下班
                if (office == null || office.EndT >= 0 || DeskIdx() < 0) { atDesk = false; StartStretch(); return; }
                WorkFx(true);
                DeskFx();
                return;
            }
            if (CheckAfk()) return;
            WorkFx(true);
            if (stT >= stDur) StartStretch();
        }

        void WorkFx(bool emotes)
        {
            if (now >= nextSteam)
            {
                nextSteam = now + 1.4 + R01() * 1.8;
                double mx = (facing < 0 ? 2.2 : 20.2) * S;
                Spawn(Sprite.Steam, Sprite.SteamC, mx + (R01() - 0.5) * S, 11 * S, (R01() - 0.5) * S * 1.2, -S * 4.5, 1.1, 0);
                if (R01() < 0.4) Spawn(Sprite.Steam, Sprite.SteamC, mx + S, 10 * S, (R01() - 0.5) * S, -S * 3.5, 0.9, 0);
            }
            if (now >= nextWorkFx)
            {
                nextWorkFx = now + 3 + R01() * 4;
                if (R01() < 0.55)
                {
                    Particle q = Spawn(R01() < 0.5 ? Sprite.CodeTag : Sprite.CodeBrace, Sprite.NoteC, (7 + R01() * 6) * S, 4 * S, (R01() - 0.5) * S * 2, -S * 5, 1.4, 0);
                    if (q != null) q.U = Math.Max(1, S / 2);
                }
            }
            if (emotes && now >= nextWorkEmote && emoteKind == null)
            {
                nextWorkEmote = now + 4 + R01() * 5;
                double r = R01();
                if (r < 0.45) Emo("dots", 1.8 + R01());
                else if (r < 0.75) Emo("idea", 1.2);
                else { Emo("sweat", 1.3); sqV -= 1.5; }
            }
        }

        void UpdSleep()
        {
            if (now >= nextFx) { nextFx = now + 1.5; SpawnZ(); }
            if (now >= noseT0 + noseLen) { PopNose(); sqV += 1; noseT0 = now + 1.2 + R01(); noseLen = 5 + R01() * 4; }
            if (now >= nextTwitch) { nextTwitch = now + 9 + R01() * 10; SleepTwitch(); }
            if (dreamForm == null && now >= nextDream && emoteKind == null) { nextDream = now + 12 + R01() * 14; StartDream(); }
            if (dreamForm != null) return;   // 梦没做完不醒
            // 夜里盖着被子睡的（不管是怎么睡下的）：都算夜里睡，动鼠标不醒
            if (sleepMode != SleepMode.Night && inBed && NightNow() && now >= nightAwakeUntil) sleepMode = SleepMode.Night;
            if (sleepMode == SleepMode.Night)
            {
                // 天亮了：各自隔几秒陆续起床
                if (NightNow()) nightWakeAt = -1;
                else if (nightWakeAt < 0) nightWakeAt = now + R01() * 25;
                else if (now >= nightWakeAt) { nightWakeAt = -1; Wake(false); }
            }
            else if (sleepMode == SleepMode.Afk && idleSecs < 1.0) Wake(false);
            else if (sleepMode == SleepMode.Nap && stT >= napDur)
            {
                if (NightNow() && inBed) sleepMode = SleepMode.Night;
                else if (idleSecs > 180) sleepMode = SleepMode.Afk;
                else Wake(false);
            }
        }

        void UpdRoll()
        {
            double k = Math.Min(1, stT / stDur), e = k < 0.5 ? 2 * k * k : 1 - Math.Pow(-2 * k + 2, 2) / 2;
            rot = rollDir * e * Math.PI * 2;
            if (rot == 0) rot = 1e-6 * rollDir;
            SetCX(rollX0 + rollDir * e * 48 * S);
            if (plat == IntPtr.Zero) ClampToSurface();
            if (stT >= stDur) { rot = 0; if (R01() < 0.4) StartDizzy(1.0); else { StartHappy(0.5); Emo("star", 1); } }
        }

        void UpdTeeter()
        {
            if (stT < stDur) return;
            if (plat != IntPtr.Zero && R01() < 0.3 && !stay)
            {
                // 没站稳，掉下去了
                ignorePlat = plat; plat = IntPtr.Zero; fellOff = true;
                velX = facing * S * 40; velY = 0; StartFall(Mood.Plain);
            }
            else { Emo("sweat", 1.3); facing = -facing; if (stay) StartIdle(); else StartWalk(facing, 1 + R01() * 1.5, S * 9); }
        }

        void UpdPeek()
        {
            Rectangle wa = WorkArea();
            double k = stT < 0.5 ? stT / 0.5 : stT > stDur - 0.5 ? Math.Max(0, (stDur - stT) / 0.5) : 1;
            double inner = peekSide > 0 ? wa.Right - 11 * S : wa.Left + 11 * S, outer = peekSide > 0 ? wa.Right + 3 * S : wa.Left - 3 * S;
            SetCX(inner + (outer - inner) * k);
            if (stT >= stDur) { SetCX(inner); if (R01() < 0.5) Emo("note", 1.2); StartIdle(); }
        }

        // 走到窗口边上再跳下去
        void HopDown(int dir, Action then)
        {
            if (basePet != null)
            {
                // 从伙伴头上跳下去（往两边随便一边）
                int dd = dir != 0 ? dir : (rnd.Next(2) == 0 ? -1 : 1);
                facing = dd; afterLandThen = then;
                Jump(Mood.Happy, S * (35 + R01() * 20), true, dd * S * (70 + R01() * 50), true);
                return;
            }
            if (plat == IntPtr.Zero || stay) { if (then != null) then(); else StartIdle(); return; }
            double cx = CX;
            int d = dir != 0 ? dir : (cx - platL < platR - cx ? -1 : 1);
            Action go = () => { facing = d; afterLandThen = then; Jump(Mood.Happy, S * (40 + R01() * 20), true, d * S * (90 + R01() * 60), true); };
            double edge = d < 0 ? platL + 5 * S : platR - 5 * S;
            if (Math.Abs(edge - cx) < 6 * S) go(); else WalkTo(edge, S * 12, go);
        }

        // ---------------- 跳到别的窗口标题栏上
        void ScanPlats()
        {
            platsT = now;
            plats.Clear(); zRects.Clear();
            int minW = 22 * S, minH = 6 * S;
            Native.EnumWindows((h, l) =>
            {
                if (!IsOccluder(h)) return true;
                Native.RECT r;
                if (!GetFrame(h, out r)) return true;
                Rectangle rr = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                int z = zRects.Count;
                zRects.Add(rr);
                if (Native.IsZoomed(h) || rr.Width < minW || rr.Height < minH) return true;
                string cls = ClassOf(h);
                if (cls == "Progman" || cls == "WorkerW") return true;
                Plat p = new Plat(); p.H = h; p.L = r.Left; p.T = r.Top; p.R = r.Right; p.Z = z;
                plats.Add(p);
                return plats.Count < 40;
            }, IntPtr.Zero);
        }

        bool PlatTopVisible(Plat p, int x)
        {
            Point pt = new Point(x, p.T + 3);
            for (int i = 0; i < p.Z && i < zRects.Count; i++) if (zRects[i].Contains(pt)) return false;
            Rectangle wa = Native.WorkAt(x, p.T + 1);
            return p.T - 15 * S >= wa.Top && p.T < wa.Bottom - 2 * S;
        }

        // 找一个够得着的窗口跳上去
        Plat ClimbTarget(out double tx)
        {
            tx = 0;
            if (now - platsT > 2) ScanPlats();
            double cx = CX, baseY = posY - sitOff;
            Rectangle wa = WorkArea();
            Plat best = null; double bestScore = -1e9;
            foreach (Plat p in plats)
            {
                if (p.H == plat || p.R - p.L < 22 * S) continue;
                double up = baseY - p.T;   // >0：窗口顶边比脚高
                if (up > 95 * S || up < -60 * S || Math.Abs(up) < 5 * S) continue;
                double x = Clamp(cx, p.L + 8 * S, p.R - 8 * S), dx = x - cx;
                if (Math.Abs(dx) > (plat != IntPtr.Zero ? 80 : 400) * S) continue;   // 在地上可以先走过去再跳
                if (plat == IntPtr.Zero && (x < wa.Left + 11 * S || x > wa.Right - 11 * S)) continue;
                if (!PlatTopVisible(p, (int)x)) continue;
                double score = -Math.Abs(dx) / (3.0 * S) - Math.Abs(up) / (2.0 * S) + (up > 0 ? 12 : 0) + R01() * 30;
                if (score > bestScore) { bestScore = score; best = p; tx = x; }
            }
            return best;
        }

        void ClimbTo(Plat p, double tx)
        {
            if (stay) { StartIdle(); return; }
            double cx = CX;
            if (plat == IntPtr.Zero && Math.Abs(tx - cx) > 45 * S)
            {
                // 离得远：先走到窗口底下附近，再重新量一下跳上去
                int dir = tx > cx ? 1 : -1;
                IntPtr h = p.H;
                WalkTo(tx - dir * 28 * S, S * 15, () =>
                {
                    ScanPlats();
                    Plat q = null;
                    foreach (Plat pp in plats) if (pp.H == h) { q = pp; break; }
                    if (q == null || q.R - q.L < 22 * S) { StartIdle(); return; }
                    double up = posY - sitOff - q.T, x2 = Clamp(CX, q.L + 8 * S, q.R - 8 * S);
                    if (up > 95 * S || up < -60 * S || !PlatTopVisible(q, (int)x2)) { Emo("q", 1.2); StartIdle(); return; }
                    ClimbTo(q, x2);
                });
                return;
            }
            double G = S * 520.0, up0 = posY - sitOff - p.T;
            Rectangle wa = Native.WorkAt((int)tx, p.T + 1);
            double apex = Math.Max(up0, 0) + Math.Min(10 * S, Math.Max(2 * S, p.T - wa.Top - 16 * S));
            double vy = Math.Sqrt(2 * G * apex);
            double tFlight = vy / G + Math.Sqrt(2 * Math.Max(0, apex - up0) / G);
            double vx = Clamp((tx - cx) / Math.Max(0.2, tFlight), -S * 420, S * 420);
            facing = vx >= 0 ? 1 : -1;
            hasAim = true; aimVY = vy; aimVX = vx == 0 ? 0.001 : vx; aimIgnore = up0 < 0;
            attnX = tx; attnY = p.T; attnUntil = now + 1.2;
            SetState(St.Aim, 0.35 + R01() * 0.35);
        }

        // ---------------- 伸手够停在头顶上的鼠标，够着了就跳起来抓
        void StartReach() { facing = lastCursor.X >= CX ? 1 : -1; SetState(St.Reach, 0.9 + R01() * 0.6); }

        void ReachJump()
        {
            double h = posY - sitOff - 12 * S - lastCursor.Y;
            if (now - lastCursorMove > 6 || h < 0 || R01() < 0.25) { StartIdle(); return; }   // 够不着就算了
            double v = Math.Sqrt(2 * S * 520.0 * Clamp(h, 6 * S, 60 * S));
            double vx = stay ? 0 : Clamp((lastCursor.X - CX) * 1.6, -S * 120, S * 120);
            Jump(Mood.Reach, v, true, vx, false);
        }

        void CheckReach()
        {
            if (!(Rest(state) || state == St.Walk) || now - lastReach < 15 || cursorOnPet || mouseDown) return;
            double still = now - lastCursorMove;
            if (still < 0.8 || still > 6) return;
            double dx = lastCursor.X - CX, up = posY - sitOff - 16 * S - lastCursor.Y;
            if (Math.Abs(dx) < 16 * S && up > 3 * S && up < 32 * S) { lastReach = now; ResetPlans(); StartReach(); }
        }

        // ================================================================ 物理
        void UpdFall(double dt)
        {
            double G = S * 520.0;
            double prevY = posY;
            velY += G * dt;
            if (velY > S * 700) velY = S * 700;
            posX += velX * dt;
            posY += velY * dt;

            Rectangle vs = SystemInformation.VirtualScreen;
            double cx = CX;
            {
                // 撞到当前屏幕的左右边（外面没有别的显示器）：扔得太狠糊在墙上；速度适中就扒住墙（「待在这儿别动」时不扒，直接滑下来）
                Rectangle wa0 = WorkArea();
                int side = 0;
                if (cx - 11 * S < wa0.Left && SideFree(-1)) side = -1;
                else if (cx + 11 * S > wa0.Right && SideFree(1)) side = 1;
                if (side != 0)
                {
                    SetCX(side < 0 ? wa0.Left + 11 * S : wa0.Right - 11 * S);
                    double av = Math.Abs(velX);
                    if (av > S * 300) { StartSplat(side); return; }
                    if (!stay && av > S * 110 && posY < wa0.Bottom - 4 * S && posY > wa0.Top + 20 * S) { GrabWall(side); return; }
                    velX = -side * av * 0.5; leanV -= side * 2;
                    cx = CX;
                }
            }
            if (cx - 11 * S < vs.Left || cx + 11 * S > vs.Right)
            {
                int side = cx - 11 * S < vs.Left ? -1 : 1;
                SetCX(side < 0 ? vs.Left + 11 * S : vs.Right - 11 * S);
                if (Math.Abs(velX) > S * 300) { StartSplat(side); return; }   // 扔太狠，糊在墙上
                velX = -side * Math.Abs(velX) * 0.5; leanV -= side * 2;
            }
            Rectangle wa = WorkArea();
            double ceil = wa.Top + 15 * S;
            if (posY < ceil) { posY = ceil; if (velY < 0) { velY = -velY * 0.3; sqV -= 3; } }
            if (mood == Mood.Reach && now - lastCursorMove < 3 && HitPet(lastCursor)) { mood = Mood.Happy; SpawnHeart(); Emo("heart", 1.4); }   // 抓到鼠标了

            if (velY > 0)
            {
                int top;
                IntPtr h = FindPlatform(prevY, posY, (int)CX, out top);
                double ptop = double.MaxValue;
                PetForm pb = All.Count > 1 ? FindPetBelow(prevY, posY, out ptop) : null;
                if (pb != null && (h == IntPtr.Zero || ptop <= top) && ptop < wa.Bottom) { TouchDownPet(pb, ptop); return; }   // 落在伙伴头上
                if (h != IntPtr.Zero && top < wa.Bottom) { TouchDown(top, h); return; }
                if (posY >= wa.Bottom) { TouchDown(wa.Bottom, IntPtr.Zero); return; }
            }
        }

        // 落在伙伴头上：踩得它一扁；太猛就从头上弹开
        void TouchDownPet(PetForm b, double top)
        {
            posY = top;
            double v = velY;
            if (v > impact) impact = v;
            sqV -= Clamp(v / (S * 55.0), 1.0, 9.0);
            leanV += velX / (S * 300.0);
            b.sqV -= Clamp(v / (S * 70.0), 1.0, 6.0);
            if (v > S * 210)
            {
                velY = -v * 0.3; velX = velX * 0.5 + (CX >= b.CX ? 1 : -1) * S * 30;
                b.Emo("bang", 0.8);
                ignorePet = b; ignorePetUntil = now + 0.5;
                return;
            }
            velX = 0; velY = 0; gvx = 0;
            ignorePlat = IntPtr.Zero; plat = IntPtr.Zero;
            Snd("boing", SND_PLAY);
            basePet = b; baseLastCX = b.CX; rideSince = now;
            SetCX(b.CX + Clamp(CX - b.CX, -2 * S, 2 * S));
            if (IsHandleCreated) Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            RaiseRiders();
            b.braceUntil = 0;
            int hgt = TowerHeight();
            b.Emo(hgt >= 3 ? "star" : "note", 1.3);
            if (hgt >= 3)
            {
                // 叠到三层以上：大家一起冒星星
                PetForm p = this;
                for (int i = 0; i < MAX_PETS && p != null; i++) { p.SpawnSparkle(); p.SpawnSparkle(); p = p.basePet; }
            }
            SetState(St.Land, 0.12);
        }

        void StartSplat(int side) { Snd("splat", SND_YOU); splatSide = side; velX = velY = 0; sqV += 9; leanV = 0; SpawnBang(); ResetPlans(); SetState(St.Splat, 0.7); }

        void TouchDown(int y, IntPtr h)
        {
            posY = y;
            double v = velY;
            if (v > impact) impact = v;
            sqV -= Clamp(v / (S * 55.0), 1.0, 9.0);   // 啪叽
            leanV += velX / (S * 300.0);
            if (v > S * 110) SpawnDust();
            if (v > S * 150) Snd("land", SND_YOU, Clamp((v - S * 100) / (S * 400.0), 0.3, 1.0), false);   // 被扔下来 / 摔下来才噗一声，自己蹦跶不响
            if (v > S * 150)
            {
                velY = -v * 0.32; velX *= 0.6;   // 弹一下
                return;
            }
            velX = 0; velY = 0;
            ignorePlat = IntPtr.Zero;
            plat = h;
            if (h != IntPtr.Zero)
            {
                Native.RECT r;
                if (GetFrame(h, out r)) { platL = r.Left; platT = r.Top; platR = r.Right; }
            }
            SetState(St.Land, 0.12);
        }

        void AfterLand()
        {
            double imp = impact; impact = 0;
            if (stackJoy)
            {
                // 特意跳到伙伴头上：成功了开心，没站上去冒汗
                stackJoy = false;
                if (basePet != null) { StartHappy(1.0); Emo(TowerHeight() >= 3 ? "star" : "heart", 1.4); return; }
                Emo("sweat", 1.2);
            }
            if (afterLandThen != null) { Action f = afterLandThen; afterLandThen = null; f(); return; }
            if (pendingDizzy) { pendingDizzy = pendingDance = false; SpawnSweat(); Emo("anger", 1.5); StartDizzy(2.2); return; }
            if (pendingDance) { pendingDance = false; Snd("dance", SND_YOU); StartDance(); return; }
            if (hopsLeft > 0) { hopsLeft--; Jump(Mood.Happy, S * (38 + R01() * 14), true); return; }
            if (afterLandSay != null) { string t = afterLandSay; afterLandSay = null; afterLandWave = false; StartWave(2); Say(t, 6); return; }
            if (afterLandWave) { afterLandWave = false; StartWave(1.6); Emo("note", 1.6); return; }
            if (imp > S * 300 && mood == Mood.Plain) { Emo("sweat", 1.5); StartDizzy(2.2); return; }   // 被扔 / 摔下来才会晕，自己跳不会
            if (fellOff) { fellOff = false; Emo("sweat", 1.5); SetState(St.Idle, 1.2); return; }
            if (mood == Mood.Celebrate) { StartHappy(1.6); return; }
            StartIdle();
        }

        // 每帧把小人贴在脚下的地面 / 窗口上；窗口被拖走了就跟着走，被关掉 / 盖住了就掉下来
        void CheckSupport(double dt)
        {
            if (basePet != null)
            {
                // 站在伙伴头上：它挪我跟着挪，头一颤我也一颤；它不稳了（被拎走、跳起来、打滚……）我就掉下去
                sitOff = 0;
                PetForm b = basePet;
                if (!b.CanCarry() || b.Above(this))
                {
                    int away = CX >= b.CX ? 1 : -1;
                    LeavePerch();
                    velX = away * S * (25 + R01() * 25); velY = -S * 35;
                    Emo("bang", 0.8);
                    return;
                }
                double bcx = b.CX;
                posX += bcx - baseLastCX; baseLastCX = bcx;
                if (Math.Abs(CX - bcx) > 7 * S) { LeavePerch(); return; }
                posY = b.HeadTop();
                return;
            }
            double want = state == St.Ledge || (state == St.Sleep && sleepLedge) || (state == St.Guitar && guitarLedge) ? 3 * S : 0;
            sitOff += (want - sitOff) * Math.Min(1, dt * 10);
            if (Math.Abs(sitOff - want) < 0.2) sitOff = want;
            if (plat == IntPtr.Zero)
            {
                Rectangle wa = WorkArea();
                if (state != St.Peek) posX = Clamp(posX, wa.Left - S, wa.Right - 23 * S);
                if (posY - sitOff < wa.Bottom - 1) { velX = 0; velY = 0; StartFall(Mood.Plain); }
                else posY = wa.Bottom + sitOff;
                return;
            }
            if (!RefreshPlatform()) { LeavePerch(); return; }
            double cx = CX;
            if (cx < platL + S || cx > platR - S) { LeavePerch(); return; }
            if (now >= nextOcclCheck)
            {
                nextOcclCheck = now + 0.4;
                if (Occluded(plat, (int)cx, platT + 3)) { LeavePerch(); return; }
            }
            posY = platT + sitOff;
        }

        void LeavePerch()
        {
            if (plat != IntPtr.Zero) ignorePlat = plat;
            plat = IntPtr.Zero; sitOff = 0; fellOff = true;
            hasWalkTarget = false; walkThen = null;
            velX = 0; velY = 0;
            StartFall(Mood.Plain);
        }

        // ---------------- 站在别的窗口上
        static bool GetFrame(IntPtr h, out Native.RECT r)
        {
            if (Native.DwmGetWindowAttribute(h, 9, out r, Marshal.SizeOf(typeof(Native.RECT))) == 0 && r.Right > r.Left) return true;
            return Native.GetWindowRect(h, out r);
        }
        static bool IsCloaked(IntPtr h) { int c; return Native.DwmGetWindowAttribute(h, 14, out c, 4) == 0 && c != 0; }
        static string ClassOf(IntPtr h) { StringBuilder sb = new StringBuilder(128); Native.GetClassName(h, sb, 128); return sb.ToString(); }

        bool IsOccluder(IntPtr h)
        {
            if (h == Handle || !Native.IsWindowVisible(h) || Native.IsIconic(h)) return false;
            int ex = Native.GetWindowLong(h, -20);
            if ((ex & 0x20) != 0 || (ex & 0x80) != 0) return false;
            if (Native.GetWindowTextLength(h) == 0) return false;
            if (IsCloaked(h)) return false;
            uint pid; Native.GetWindowThreadProcessId(h, out pid);
            return pid != myPid;
        }

        IntPtr FindPlatform(double prevFeet, double newFeet, int cx, out int topY)
        {
            IntPtr best = IntPtr.Zero; int bestTop = int.MaxValue;
            List<Rectangle> above = new List<Rectangle>();
            int need = 15 * S;
            IntPtr skip = ignorePlat;
            Native.EnumWindows((h, l) =>
            {
                if (!IsOccluder(h)) return true;
                Native.RECT r;
                if (!GetFrame(h, out r)) return true;
                Rectangle rr = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                if (h != skip && !Native.IsZoomed(h) && rr.Width >= 20 * S && rr.Height >= 6 * S
                    && cx >= r.Left + 2 * S && cx <= r.Right - 2 * S
                    && r.Top >= prevFeet - 1 && r.Top <= newFeet && r.Top < bestTop)
                {
                    string cls = ClassOf(h);
                    if (cls != "Progman" && cls != "WorkerW")
                    {
                        Rectangle wa = Native.WorkAt(cx, r.Top + 1);
                        if (r.Top - need >= wa.Top && r.Top < wa.Bottom - 2 * S)
                        {
                            Point pt = new Point(cx, r.Top + 3);
                            bool hidden = false;
                            foreach (Rectangle a in above) if (a.Contains(pt)) { hidden = true; break; }
                            if (!hidden) { best = h; bestTop = r.Top; }
                        }
                    }
                }
                above.Add(rr);
                return true;
            }, IntPtr.Zero);
            topY = bestTop;
            return best;
        }

        bool Occluded(IntPtr target, int x, int y)
        {
            bool occl = false;
            Point pt = new Point(x, y);
            Native.EnumWindows((h, l) =>
            {
                if (h == target) return false;
                if (IsOccluder(h))
                {
                    Native.RECT r;
                    if (GetFrame(h, out r) && Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom).Contains(pt)) { occl = true; return false; }
                }
                return true;
            }, IntPtr.Zero);
            return occl;
        }

        bool RefreshPlatform()
        {
            IntPtr h = plat;
            if (!Native.IsWindow(h) || !Native.IsWindowVisible(h) || Native.IsIconic(h) || Native.IsZoomed(h) || IsCloaked(h)) return false;
            Native.RECT r;
            if (!GetFrame(h, out r)) return false;
            if (r.Left != platL || r.Top != platT)
            {
                int dyMove = r.Top - platT;
                posX += r.Left - platL;   // 窗口被拖动：跟着一起走
                posY = r.Top + sitOff;
                if (dyMove != 0) sqV += Clamp(-dyMove / (S * 4.0), -3, 3);
                leanV += Clamp(-(r.Left - platL) / (S * 6.0), -2, 2);
            }
            platL = r.Left; platT = r.Top; platR = r.Right;
            Rectangle wa = WorkArea();
            return r.Top - 15 * S >= wa.Top - 2 && r.Top <= wa.Bottom;
        }

        void CheckFullscreen()
        {
            bool fs = false;
            try
            {
                IntPtr fg = Native.GetForegroundWindow();
                if (fg != IntPtr.Zero && fg != Handle && !Native.IsZoomed(fg))
                {
                    string cls = ClassOf(fg);
                    if (cls != "Progman" && cls != "WorkerW" && cls != "Shell_TrayWnd" && cls != "Shell_SecondaryTrayWnd")
                    {
                        Native.RECT r;
                        if (Native.GetWindowRect(fg, out r))
                        {
                            Rectangle b = Native.WindowMonitorBounds(fg);
                            if (!b.IsEmpty && r.Left <= b.Left && r.Top <= b.Top && r.Right >= b.Right && r.Bottom >= b.Bottom)
                                fs = ScreenArea() == b;
                        }
                    }
                }
            }
            catch { }
            if (fs != hiddenForFullscreen)
            {
                hiddenForFullscreen = fs;
                if (fs) { HideBar(); Hide(); } else { Show(); dirty = true; }
                if (fs && match != null && (InMatch() || match.Host == this)) EndMatch(false);   // 全屏了：拆掉球场
                foreach (PetObj o in objs) { try { if (fs) o.Win.Hide(); else o.Win.Show(); } catch { } }
                if (poleWin != null) { try { if (fs) poleWin.Hide(); else ShowPole(); } catch { } }
                if (office != null && office.Host == this && office.Win != null) { try { if (fs) office.Win.Hide(); else { office.Win.Show(); RaisePets(); } } catch { } }
            }
        }

        // ================================================================ 眼睛 / 鼠标
        void TrackCursor(double dt)
        {
            Point c = Cursor.Position;
            double d = Math.Sqrt((double)(c.X - lastCursor.X) * (c.X - lastCursor.X) + (double)(c.Y - lastCursor.Y) * (c.Y - lastCursor.Y));
            double sp = dt > 0 ? d / dt : 0;
            cursorSpeed = cursorSpeed * 0.6 + sp * 0.4;
            if (d > 0)
            {
                // 你离开很久后回来：跟你打个招呼
                if (now - lastCursorMove > 90 && Rest(state) && R01() < 0.5) { facing = c.X >= CX ? 1 : -1; StartWave(1.4); Emo("note", 1.4); }
                lastCursor = c; lastCursorMove = now;
            }
            cursorOnPet = !dragging && !mouseDown && HitPet(c);
            // 鼠标从身边飞快划过：吓一跳
            if (cursorSpeed > S * 500 && (Rest(state) || state == St.Walk) && now - lastStartle > 25 && !mouseDown)
            {
                double dx = c.X - CX, dy = c.Y - (posY - 9 * S);
                if (dx * dx + dy * dy < (12.0 * S) * (12.0 * S)) { lastStartle = now; if (R01() < 0.5) { ResetPlans(); Startle(); } }
            }
            // 摸摸：要在它身上来回蹭（方向来回换两次以上）才算；只是从身上划过去不算
            if (cursorOnPet && cursorSpeed < S * 450)
            {
                if (hoverValid && (c.X != lastHover.X || c.Y != lastHover.Y))
                {
                    int mdx = c.X - lastHover.X, mdy = c.Y - lastHover.Y;
                    if (now - lastPetMove > 0.6) { petDist = 0; rubFlips.Clear(); rubDir = rubDirY = 0; }
                    petDist += Math.Abs(mdx) + Math.Abs(mdy);
                    lastPetMove = now;
                    if (Math.Abs(mdx) >= 2) { int dd = Math.Sign(mdx); if (rubDir != 0 && dd != rubDir) rubFlips.Add(now); rubDir = dd; }
                    if (Math.Abs(mdy) >= 2) { int dd = Math.Sign(mdy); if (rubDirY != 0 && dd != rubDirY) rubFlips.Add(now); rubDirY = dd; }
                    rubFlips.RemoveAll(ft => now - ft >= 1.2);
                    leanV += mdx / (S * 60.0);   // 被蹭得晃一晃
                    if (rubFlips.Count >= 2 && petDist > 14 * S) { petDist = 0; rubFlips.Clear(); OnPetted(); }
                }
                lastHover = c; hoverValid = true;
            }
            else { hoverValid = false; petDist = 0; rubFlips.Clear(); rubDir = rubDirY = 0; }
        }

        void UpdateEyes()
        {
            if (now >= nextBlink)
            {
                blinkUntil = now + 0.13;
                nextBlink = R01() < 0.2 ? now + 0.32 : now + 2.2 + R01() * 4.5;
            }
            bool has = false; double tx = 0, ty = 0;
            if (now < attnUntil) { has = true; tx = attnX; ty = attnY; }
            else if (All.Count > 1)
            {
                // 伙伴被拎起来 / 正在往下掉：一直盯着它看
                foreach (PetForm f in All)
                    if (f != this && (f.state == St.Drag || (f.state == St.Fall && f.velY > S * 60)) && Math.Abs(f.CX - CX) < 200 * S)
                    { has = true; tx = f.CX; ty = f.posY - 9 * S; break; }
            }
            if (!has && now - lastCursorMove < 4) { has = true; tx = lastCursor.X; ty = lastCursor.Y; }
            if (has)
            {
                double dx = tx - CX, dy = ty - (posY - 10 * S + sink * S);
                lookX = dx > 8 * S ? 1 : (dx < -8 * S ? -1 : 0);
                lookY = dy < -8 * S ? -1 : (dy > 8 * S ? 1 : 0);
            }
            else if (now >= nextGlance)
            {
                nextGlance = now + 1.5 + R01() * 3;
                int r = rnd.Next(6);
                lookX = r == 0 ? -1 : (r == 1 ? 1 : 0);
                lookY = r == 2 ? -1 : 0;
            }
        }

        int Ph(double per) { return ((int)Math.Floor(now / per)) & 1; }

        // 四帧走路：迈左脚 → 并拢 → 迈右脚 → 并拢；几乎没在动就站着
        LegPose WalkLegs()
        {
            if (Math.Abs(gvx) < 1.5 * S) return LegPose.Stand;
            int f = ((int)Math.Floor(walkPh)) & 3;
            return f == 0 ? LegPose.WalkA : (f == 2 ? LegPose.WalkB : LegPose.Stand);
        }

        void BuildPose()
        {
            Pose p = new Pose();
            p.Facing = facing;
            p.LookX = lookX; p.LookY = lookY;
            p.Eyes = now < blinkUntil ? EyePose.Blink : EyePose.Open;
            propKind = null; propFrame = 0;
            switch (state)
            {
                case St.Walk:
                    p.Legs = WalkLegs(); p.LookX = facing; p.LookY = 0;
                    if (bedBound) p.Eyes = (stT % 2.2) < 0.45 ? EyePose.Closed : EyePose.Focus;   // 去睡觉的路上：眼睛快睁不开了
                    break;
                case St.Chase: case St.Match:
                    if (ballWait && Math.Abs(gvx) < S * 3)
                    {
                        // 等伙伴传球：叉开腿、张着手准备接，盯着球（时不时挥一下手：传给我！）
                        p.Legs = LegPose.Spread;
                        p.ArmL = p.ArmR = ArmPose.Out;
                        if (Ph(0.7) == 0 && chaseObj != null && Math.Abs(chaseObj.VX) < S * 5) { if (facing > 0) p.ArmR = ArmPose.Up; else p.ArmL = ArmPose.Up; }
                    }
                    else
                    {
                        p.Legs = WalkLegs();
                        bool a = (((int)Math.Floor(walkPh)) & 2) == 0;
                        p.ArmL = a ? ArmPose.Raised : ArmPose.Normal; p.ArmR = a ? ArmPose.Normal : ArmPose.Raised;
                    }
                    if (state == St.Match && match != null && match.Holder == this) { p.ArmL = p.ArmR = ArmPose.Raised; p.Legs = LegPose.Stand; }   // 守门员抱着球
                    else if (state == St.Match && hopH > 2 * S) { p.ArmL = p.ArmR = ArmPose.Up; p.Legs = LegPose.DangleA; }   // 蹦起来：手举高
                    else if (state == St.Match && now < kickPoseUntil) { p.Legs = LegPose.Kick; p.ArmL = p.ArmR = ArmPose.Out; }   // 起脚
                    break;
                case St.Think: case St.Work:
                    {
                        // 坐在小电脑后面噼里啪啦打字，眼睛盯着屏幕
                        bool a = (((int)Math.Floor(now / 0.1)) & 1) == 0;
                        p.BodyDy = 2;
                        p.ArmL = a ? ArmPose.Raised : ArmPose.Normal;
                        p.ArmR = a ? ArmPose.Normal : ArmPose.Raised;
                        p.Eyes = now < blinkUntil ? EyePose.Blink : EyePose.Focus;
                        p.LookX = 0; p.LookY = 1;
                        if (atDesk && now < deskStretchUntil) { p.ArmL = p.ArmR = ArmPose.Up; p.Eyes = EyePose.Happy; p.LookY = 0; }        // 工位上伸个懒腰
                        else if (atDesk && now < deskLookUntil) { p.ArmL = p.ArmR = ArmPose.Normal; p.LookX = deskLookDir; p.LookY = 0; }   // 歪头看看隔壁
                    }
                    break;
                case St.Sit: p.BodyDy = 2; p.ArmL = p.ArmR = ArmPose.Down; break;
                case St.Ledge:
                    p.Legs = LegPose.Hang; p.ArmL = p.ArmR = ArmPose.Down;
                    p.LegDx = (int)Math.Round(Math.Sin(now * 2.6) * 1.3); p.LegDx2 = -p.LegDx; p.LegPh = Ph(0.5);
                    break;
                case St.Lie: p.BodyDy = 3; p.Legs = LegPose.Tucked; p.ArmL = p.ArmR = ArmPose.Out; break;
                case St.Look:
                    {
                        int lx, ly; CurLook(out lx, out ly);
                        p.LookX = lx; p.LookY = ly;
                        if (ly < 0) p.ArmR = ArmPose.Raised;
                    }
                    break;
                case St.Ponder: p.ArmR = ArmPose.Raised; p.LookX = 1; p.LookY = -1; break;
                case St.Scratch: p.ArmR = Ph(0.09) == 0 ? ArmPose.Up : ArmPose.UpOut; p.Eyes = EyePose.Squint; break;
                case St.Spin: p.ArmL = p.ArmR = ArmPose.Out; p.Eyes = flip < 0 ? EyePose.None : EyePose.Happy; p.LookX = p.LookY = 0; break;
                case St.Roll: p.Eyes = EyePose.Squint; p.ArmL = p.ArmR = ArmPose.Up; p.Legs = LegPose.Tucked; p.LookX = p.LookY = 0; break;
                case St.Exercise:
                    {
                        bool up = (((int)Math.Floor(stT / 0.3)) & 1) == 0;
                        p.ArmL = p.ArmR = up ? ArmPose.Up : ArmPose.Down; p.Legs = up ? LegPose.Spread : LegPose.Stand;
                        p.Eyes = up ? EyePose.Happy : EyePose.Open; p.LookX = p.LookY = 0;
                    }
                    break;
                case St.Teeter:
                    {
                        int f = ((int)Math.Floor(now / 0.08)) & 3;
                        p.ArmL = Flail[f]; p.ArmR = Flail[(f + 2) & 3]; p.Eyes = EyePose.Wide; p.LookX = facing; p.LookY = 1;
                    }
                    break;
                case St.Peek: p.LookX = -peekSide; p.LookY = 0; break;
                case St.Chat:
                    if (now < talkUntil)
                    {
                        // 说话：朝着对方那只手比划，一抬一放
                        ArmPose ap = Ph(0.22) == 0 ? ArmPose.Raised : ArmPose.Out;
                        if (facing > 0) p.ArmR = ap; else p.ArmL = ap;
                    }
                    else p.ArmL = p.ArmR = ArmPose.Down;   // 听着
                    break;
                case St.Watch:
                    p.BodyDy = 2;   // 坐着看
                    if (now < cheerUntil) { p.ArmL = p.ArmR = Ph(0.15) == 0 ? ArmPose.Up : ArmPose.UpOut; p.Eyes = EyePose.Happy; }
                    else if (watchWhat == St.Guitar || watchWhat == St.Music) p.ArmL = p.ArmR = ArmPose.Out;
                    else p.ArmL = p.ArmR = ArmPose.Down;
                    break;
                case St.Pat:
                    {
                        // 伸手拍拍伙伴的头
                        ArmPose ap = Ph(0.16) == 0 ? ArmPose.Up : ArmPose.UpOut;
                        if (facing > 0) p.ArmR = ap; else p.ArmL = ap;
                        p.Eyes = EyePose.Happy;
                    }
                    break;
                case St.Kick:
                    if (stT < kickAt - 0.1) { p.Legs = LegPose.Stand; p.ArmL = p.ArmR = ArmPose.Out; p.LookX = facing; p.LookY = 1; }   // 停住球，低头看一眼
                    else { p.Legs = LegPose.Kick; p.ArmL = p.ArmR = ArmPose.Out; p.Eyes = EyePose.Happy; }
                    break;
                case St.Burrow:
                    if (burrowPhase == 0)
                    {
                        // 两只手轮流往下刨，眯着眼
                        bool a = Ph(0.08) == 0;
                        p.ArmL = a ? ArmPose.DownOut : ArmPose.Down; p.ArmR = a ? ArmPose.Down : ArmPose.DownOut;
                        p.Eyes = Ph(0.3) == 0 ? EyePose.Squint : EyePose.Focus; p.LookX = 0; p.LookY = 1; p.Legs = LegPose.Spread;
                    }
                    else if (burrowPhase == 4) { p.ArmL = p.ArmR = ArmPose.Up; p.Eyes = popStartled ? EyePose.Wide : EyePose.Happy; p.LookX = p.LookY = 0; }
                    else
                    {
                        // 埋在沙里：只露眼睛，眨眼、东张西望（跟着鼠标看）；睡着了就闭眼；被摸了眯眼笑
                        p.Legs = LegPose.Tucked;
                        if (burrowSleep) p.Eyes = EyePose.Closed;
                        else if (now < blushUntil) p.Eyes = EyePose.Happy;
                    }
                    break;
                case St.Shake:
                    p.ArmL = p.ArmR = Ph(0.06) == 0 ? ArmPose.Out : ArmPose.UpOut;
                    p.Eyes = EyePose.Squint; p.LookX = p.LookY = 0;
                    break;
                case St.Climb:
                    if (climbPhase == 1 && Math.Abs(climbV) > 1.5 * S)
                    {
                        // 沿着墙「走」：四帧迈步，手一前一后扒
                        int f = ((int)Math.Floor(walkPh)) & 3;
                        p.Legs = f == 0 ? LegPose.WalkA : (f == 2 ? LegPose.WalkB : LegPose.Stand);
                        bool a = (((int)Math.Floor(walkPh)) & 2) == 0;
                        p.ArmL = a ? ArmPose.Raised : ArmPose.Normal; p.ArmR = a ? ArmPose.Normal : ArmPose.Raised;
                        p.LookX = facing; p.LookY = 0;
                    }
                    else if (climbPhase == 1)
                    {
                        // 扒着不动：手抓紧，眼睛东看西看（lookY = -1 是看向屏幕里面）
                        p.ArmL = p.ArmR = ArmPose.Raised;
                        p.LookX = clingLookX; p.LookY = clingLookY;
                        if (climbTop && Ph(0.5) == 0) p.Eyes = now < blinkUntil ? EyePose.Blink : EyePose.Wide;
                    }
                    else { p.ArmL = p.ArmR = ArmPose.Up; p.LookX = 0; p.LookY = 0; }
                    break;
                case St.Reach: p.ArmL = p.ArmR = ArmPose.Up; p.LookY = -1; p.Legs = Ph(0.12) == 0 ? LegPose.Stand : LegPose.WalkA; break;
                case St.Aim: p.ArmL = p.ArmR = ArmPose.Down; p.Eyes = EyePose.Focus; break;
                case St.Splat: p.ArmL = p.ArmR = ArmPose.Up; p.Legs = LegPose.Spread; p.Eyes = EyePose.Squint; p.LookX = p.LookY = 0; break;
                case St.Refuse: p.Eyes = EyePose.Squint; p.ArmL = p.ArmR = ArmPose.Down; p.LookX = p.LookY = 0; break;
                case St.Giggle:
                    {
                        bool a = Ph(0.1) == 0;
                        p.ArmL = a ? ArmPose.Up : ArmPose.Raised; p.ArmR = a ? ArmPose.Raised : ArmPose.Up;
                        p.Eyes = EyePose.Happy; p.Blush = true; p.LookX = p.LookY = 0;
                    }
                    break;
                case St.Sleep:
                    if (sleepLedge)
                    {
                        // 坐在边上睡，腿慢慢晃
                        p.Legs = LegPose.Hang; p.LegDx = (int)Math.Round(Math.Sin(now * 1.1) * 0.8); p.LegDx2 = -p.LegDx; p.ArmL = p.ArmR = ArmPose.Down;
                    }
                    else { p.Legs = LegPose.Tucked; p.BodyDy = 3; }
                    p.Eyes = now < blushUntil ? EyePose.Happy : EyePose.Closed;   // 睡着被摸：嘴角上扬
                    if (now < twitchUntil) p.ArmL = p.ArmR = ArmPose.Out;
                    p.LookX = p.LookY = 0;
                    break;
                case St.Doze:
                    {
                        double k = stT / stDur;
                        p.BodyDy = 2; p.ArmL = p.ArmR = ArmPose.Down; p.LookX = p.LookY = 0;
                        p.Eyes = now < jerkUntil ? EyePose.Wide : now < blushUntil ? EyePose.Happy : k < 0.45 ? EyePose.Focus : EyePose.Closed;
                    }
                    break;
                case St.Drag:
                    {
                        // 被拎起来：往上提手被甩到下面，往下压手举过头；一直扑腾，使劲晃就手脚乱蹬
                        bool intense = shake > S * 70;
                        int f = ((int)Math.Floor(armPh)) & 3;
                        if (dragVY < -S * 140) p.ArmL = p.ArmR = ArmPose.DownOut;
                        else if (dragVY > S * 140) p.ArmL = p.ArmR = ArmPose.Up;
                        else if (intense) { p.ArmL = Flail[f]; p.ArmR = Flail[(f + 2) & 3]; }
                        else
                        {
                            ArmPose[] l = { ArmPose.UpOut, ArmPose.Out, ArmPose.UpOut, ArmPose.Up }, r = { ArmPose.Out, ArmPose.UpOut, ArmPose.Up, ArmPose.UpOut };
                            p.ArmL = l[f]; p.ArmR = r[f];
                        }
                        // 两组腿交替往前蹬（剪刀腿），再叠上被甩出去的方向
                        double kick = Math.Sin(kickPh * Math.PI * 2) * (intense ? 1.6 : 1.2);
                        p.Legs = LegPose.Hang;
                        p.LegDx = (int)Clamp(Math.Round(legA * 0.7 + kick), -2, 2);
                        p.LegDx2 = (int)Clamp(Math.Round(legB * 0.7 - kick), -2, 2);
                        p.LegPh = ((int)Math.Floor(kickPh * 2)) & 1;
                        p.Eyes = intense ? EyePose.Squint : stT > 2.5 ? EyePose.Happy : EyePose.Wide;
                        if (stT > 2.5 && !intense) p.Blush = true;
                        p.LookX = p.LookY = 0;
                    }
                    break;
                case St.Fall:
                    p.LookX = p.LookY = 0;
                    if (mood != Mood.Plain) { p.Eyes = EyePose.Happy; p.Blush = true; p.ArmL = p.ArmR = ArmPose.Up; p.Legs = LegPose.DangleA; }
                    else
                    {
                        p.Eyes = EyePose.Wide;
                        int f = ((int)Math.Floor(armPh)) & 3;
                        p.ArmL = Flail[f]; p.ArmR = Flail[(f + 2) & 3];
                        double k = Math.Sin(kickPh * Math.PI * 2);   // 掉下去的时候也在蹬腿
                        p.Legs = LegPose.Hang;
                        p.LegDx = (int)Clamp(Math.Round(legA + k), -2, 2); p.LegDx2 = (int)Clamp(Math.Round(legB - k), -2, 2);
                        p.LegPh = ((int)Math.Floor(legPh)) & 1;
                    }
                    break;
                case St.Crouch:
                    p.Eyes = mood != Mood.Plain ? EyePose.Happy : EyePose.Squint;
                    p.ArmL = p.ArmR = ArmPose.Down; p.LookX = 0; p.LookY = 0;
                    break;
                case St.Land:
                    if (mood != Mood.Plain) p.Eyes = EyePose.Happy;
                    break;
                case St.Happy:
                    p.Eyes = EyePose.Happy; p.Blush = true; p.LookX = 0; p.LookY = 0;
                    if (mood == Mood.Celebrate) p.ArmL = p.ArmR = Ph(0.2) == 0 ? ArmPose.Up : ArmPose.Raised;
                    else p.ArmL = p.ArmR = ArmPose.Raised;
                    break;
                case St.Dance:
                    {
                        bool odd = Ph(0.22) == 1;
                        p.ArmL = odd ? ArmPose.Up : ArmPose.Normal;
                        p.ArmR = odd ? ArmPose.Normal : ArmPose.Up;
                        p.Legs = odd ? LegPose.WalkA : LegPose.WalkB;
                        p.Facing = odd ? 1 : -1;
                        p.LookX = odd ? 1 : -1; p.LookY = 0;
                        p.Eyes = EyePose.Happy;
                    }
                    break;
                case St.Wave:
                    if (facing < 0) p.ArmL = Ph(0.22) == 0 ? ArmPose.Up : ArmPose.UpOut; else p.ArmR = Ph(0.22) == 0 ? ArmPose.Up : ArmPose.UpOut;
                    p.Eyes = EyePose.Happy; p.Blush = true;
                    break;
                case St.Alert:
                    {
                        bool up = Ph(0.18) == 0;
                        p.ArmL = up ? ArmPose.Up : ArmPose.Raised;
                        p.ArmR = up ? ArmPose.Raised : ArmPose.Up;
                        p.Eyes = EyePose.Wide; p.LookX = 0; p.LookY = 0;
                    }
                    break;
                case St.Dizzy: p.Eyes = EyePose.Dizzy; p.ArmL = p.ArmR = ArmPose.Down; p.LookX = 0; p.LookY = 0; break;
                case St.Yawn: p.ArmL = p.ArmR = ArmPose.Up; p.Eyes = EyePose.Closed; p.LookX = 0; p.LookY = 0; break;
                case St.Stretch: p.ArmL = p.ArmR = ArmPose.Up; p.Eyes = EyePose.Happy; p.LookX = 0; p.LookY = 0; break;
                case St.Sneeze:
                    p.LookX = 0; p.LookY = 0;
                    if (stT < 0.5) p.Eyes = EyePose.Squint;
                    else if (stT < 0.75) p.Eyes = EyePose.Wide;
                    break;
                case St.Eat:
                    {
                        bool a = Ph(0.12) == 0;
                        p.ArmL = a ? ArmPose.Raised : ArmPose.Normal;
                        p.ArmR = a ? ArmPose.Normal : ArmPose.Raised;
                        p.Eyes = snack != null ? EyePose.Heart : EyePose.Happy; p.Blush = true; p.LookX = 0; p.LookY = 0;
                    }
                    break;
                case St.Read:   // 坐着看书，眼睛往下看；时不时翻一页、偷笑
                    p.BodyDy = 2; p.Legs = LegPose.Tucked; p.ArmL = p.ArmR = ArmPose.Raised; p.LookX = 0; p.LookY = 1;
                    if (now < readGiggleUntil) { p.Eyes = EyePose.Happy; p.Blush = true; }
                    propKind = "book"; propFrame = now < pageFlipUntil ? 1 : 0;
                    break;
                case St.Guitar:   // 抱着尤克里里，一只手按弦、一只手扫弦；坐在边上时两条腿垂下来，跟着拍子一摇一摆
                    {
                        bool a = Ph(0.18) == 0;
                        p.LookX = p.LookY = 0;
                        if (guitarLedge)
                        {
                            double sw = Math.Sin(now * Math.PI * 2 / 0.9) * 1.6;   // 0.9 秒一个来回，跟身子左右晃的节奏一样
                            p.Legs = LegPose.Hang; p.LegPh = Ph(0.45);
                            p.LegDx = (int)Clamp(Math.Round(sw), -2, 2); p.LegDx2 = (int)Clamp(Math.Round(-sw * 0.8), -2, 2);
                        }
                        else { p.BodyDy = 2; p.Legs = LegPose.Tucked; }
                        p.Eyes = Ph(1.3) == 0 ? EyePose.Happy : EyePose.Closed;
                        if (facing >= 0) { p.ArmR = ArmPose.Raised; p.ArmL = a ? ArmPose.Raised : ArmPose.Normal; }
                        else { p.ArmL = ArmPose.Raised; p.ArmR = a ? ArmPose.Raised : ArmPose.Normal; }
                        propKind = "uke";
                    }
                    break;
                case St.Sweep:   // 拿着扫帚边走边扫
                    p.Legs = WalkLegs(); p.ArmL = p.ArmR = ArmPose.Raised; p.LookX = facing; p.LookY = 1;
                    propKind = "broom"; propFrame = Ph(0.22);
                    break;
                case St.Lift:   // 举重：放下 → 举过头顶，一下一下来
                    {
                        bool up = LiftUp();
                        p.ArmL = p.ArmR = up ? ArmPose.Up : ArmPose.Raised; p.BodyDy = up ? 0 : 1;
                        p.Legs = up ? LegPose.Stand : LegPose.Spread; p.LookX = p.LookY = 0;
                        p.Eyes = up ? EyePose.Squint : EyePose.Focus;
                        propKind = "barbell"; propFrame = up ? 1 : 0;
                    }
                    break;
                case St.Castle:   // 蹲着拍沙子堆沙堡，堆好了美滋滋地看
                    {
                        bool building = stT < 4.5, pat = Ph(0.16) == 0;
                        p.BodyDy = 2; p.Legs = LegPose.Tucked; p.LookX = facing; p.LookY = 1;
                        ArmPose work = building ? (pat ? ArmPose.Out : ArmPose.Down) : ArmPose.Raised;
                        if (facing >= 0) { p.ArmR = work; p.ArmL = ArmPose.Normal; } else { p.ArmL = work; p.ArmR = ArmPose.Normal; }
                        p.Eyes = building ? EyePose.Focus : (castleStage >= 4 ? EyePose.Wide : EyePose.Happy);
                        if (!building && castleStage < 4) p.Blush = true;
                        propKind = "castle"; propFrame = castleStage;
                    }
                    break;
                case St.Bubbles:   // 像真螃蟹一样吐泡泡，眼睛跟着泡泡往上看
                    p.Eyes = Ph(0.6) == 0 ? EyePose.Happy : EyePose.Open; p.LookX = facing; p.LookY = -1;
                    p.ArmL = p.ArmR = Ph(0.9) == 0 ? ArmPose.Normal : ArmPose.Raised;
                    break;
                case St.Music:   // 戴着耳机跟着节奏点头、跺脚
                    {
                        bool on = ((int)Math.Floor(stT / 0.42) & 1) == 0;
                        p.BodyDy = on ? 0 : 1; p.Legs = on ? LegPose.Stand : (Ph(0.84) == 0 ? LegPose.WalkA : LegPose.WalkB);
                        p.ArmL = p.ArmR = on ? ArmPose.Raised : ArmPose.Normal; p.LookX = p.LookY = 0;
                        p.Eyes = Ph(1.7) == 0 ? EyePose.Closed : EyePose.Happy;
                        propKind = "phones";
                    }
                    break;
                case St.Plane:   // 叠纸飞机 → 扔出去 → 目送它飞走
                    if (stT < 1.0)
                    {
                        bool a = Ph(0.15) == 0;
                        p.ArmL = a ? ArmPose.Raised : ArmPose.Normal; p.ArmR = a ? ArmPose.Normal : ArmPose.Raised;
                        p.LookX = facing; p.LookY = 1; p.Eyes = EyePose.Focus;
                        propKind = "paper"; propFrame = Ph(0.3);
                    }
                    else if (stT < 1.3)
                    {
                        if (facing >= 0) p.ArmR = ArmPose.Up; else p.ArmL = ArmPose.Up;
                        p.LookX = facing; p.LookY = -1; p.Eyes = EyePose.Wide; p.Legs = LegPose.WalkA;
                    }
                    else
                    {
                        PetObj pl = FindObj("plane");
                        p.LookX = pl != null ? (pl.X >= CX ? 1 : -1) : facing; p.LookY = pl != null && pl.Y < posY - 16 * S ? -1 : 0;
                        p.Eyes = EyePose.Happy;
                        if (pl != null && Math.Abs(pl.X - CX) > 12 * S) facing = pl.X >= CX ? 1 : -1;
                    }
                    break;
                case St.Pole:   // 站在杆子旁边，一只手把杆子往上推，眼睛跟着往上看
                    if (facing >= 0) { p.ArmR = ArmPose.Up; p.ArmL = ArmPose.Raised; } else { p.ArmL = ArmPose.Up; p.ArmR = ArmPose.Raised; }
                    p.LookX = facing; p.LookY = -1; p.Eyes = EyePose.Focus; p.Legs = LegPose.Spread;
                    break;
            }
            p.Acc = AccOf(slot);   // 第二只戴眼镜，第三只戴帽子，第四只跟着季节换
            // 小被子：躺下后一行一行拉到下巴；起床伸懒腰时一行一行掀下去。每只一种颜色
            if (inBed && state == St.Sleep) { p.BlanketH = Math.Max(1, Math.Min(5, 1 + (int)((now - bedT) / 0.12))); p.BlanketC = slot & 3; }
            else if (state == St.Stretch && now - bedOffT < 0.4) { p.BlanketH = 5 - (int)((now - bedOffT) / 0.08); p.BlanketC = slot & 3; }
            if (state == St.HighFive)
            {
                // 击掌：朝着伙伴那边的手先举到一边，碰上的一下举直
                ArmPose a = stT < 0.3 ? ArmPose.UpOut : ArmPose.Up;
                if (facing >= 0) { p.ArmR = a; p.ArmL = ArmPose.Normal; } else { p.ArmL = a; p.ArmR = ArmPose.Normal; }
                p.Eyes = stT < 0.3 ? EyePose.Wide : EyePose.Happy; p.Blush = stT >= 0.3; p.LookX = facing; p.LookY = 0;
            }
            else if (state == St.Idle && Rider() != null)
            {
                // 头上站着伙伴：两只手举起来托着，抬头看它
                p.ArmL = p.ArmR = ArmPose.Up; p.LookX = 0; p.LookY = -1;
                if (p.Eyes == EyePose.Open && Ph(1.6) == 0) p.Eyes = EyePose.Happy;
            }
            else if (state == St.Idle && now < braceUntil && braceFor != null)
            {
                // 等伙伴过来：转过去看着它，手张开
                p.ArmL = p.ArmR = ArmPose.Raised; p.LookX = braceFor.CX >= CX ? 1 : -1; p.LookY = braceFor.posY < posY - 6 * S ? -1 : 0;
            }
            // 鼠标停在它身上不动：害羞
            if (cursorOnPet && now - lastCursorMove > 1.2 && now - lastCursorMove < 10 && Rest(state) && state != St.Work)
            { p.Blush = true; if (p.Eyes == EyePose.Open) p.Eyes = EyePose.Happy; }
            if (dragHover) { p.Eyes = EyePose.Wide; p.ArmL = p.ArmR = ArmPose.Raised; p.LookX = 0; p.LookY = 0; }
            if (now < blushUntil) p.Blush = true;
            // 坐下 / 站起 / 转身时身子软软地弹一下，而不是一下子换姿势
            if (p.BodyDy != lastBodyDy) { sqV += p.BodyDy > lastBodyDy ? -3.5 : 3.5; lastBodyDy = p.BodyDy; }
            if (p.Facing != lastFacing) { if (state != St.Dance) { sqV -= 1.2; leanV += p.Facing * 0.8; } lastFacing = p.Facing; }
            curBodyDy = p.BodyDy;
            if (!p.Same(lastPose)) { lastPose = p; spriteDirty = true; }
        }

        // ================================================================ 粒子 / 表情 / 气泡
        Particle Spawn(string[] shape, Color c, double x, double y, double vx, double vy, double life, double gravity)
        {
            if (parts.Count >= 60) return null;
            Particle q = new Particle();
            q.Shape = shape; q.Color = c; q.X = x; q.Y = y; q.VX = vx; q.VY = vy; q.Life = life; q.Gravity = gravity;
            parts.Add(q);
            return q;
        }
        void SpawnHeart() { Spawn(Sprite.Heart, Sprite.HeartC, (9 + rnd.Next(5)) * S, 1 * S, (R01() - 0.5) * S * 4, -S * 9, 1.1, 0); }
        void SpawnZ() { Spawn(Sprite.Zed, Sprite.ZC, 18 * S, 5 * S, S * 3, -S * 5, 2.2, 0); }
        void SpawnNote() { bool l = rnd.Next(2) == 0; Spawn(Sprite.Note, Sprite.NoteC, (l ? 0 : 21) * S, 5 * S, (l ? -1 : 1) * S * 3, -S * 7, 1.3, 0); }
        void SpawnBang() { Spawn(Sprite.Bang, Sprite.BangC, 21 * S, 0, 0, -S * 2, 1.0, 0); }
        void SpawnSweat() { Spawn(Sprite.Drop, Sprite.DropC, 20 * S, 3 * S, S * 3, -S * 2, 0.9, S * 30); }
        void SpawnSparkle() { Spawn(Sprite.Star, Sprite.StarC, rnd.Next(0, 22) * S, rnd.Next(-2, 6) * S, (R01() - 0.5) * S * 6, -S * 6, 1.0, 0); }
        void SpawnCrumb() { Spawn(Sprite.Crumb, Sprite.CrumbC, (10 + rnd.Next(5)) * S, 11 * S, (R01() - 0.5) * S * 10, -S * 5, 0.7, S * 60); }
        void SpawnDust()
        {
            Spawn(Sprite.Puff, Sprite.PuffC, 1 * S, 15 * S, -S * 7, -S * 1.5, 0.45, 0);
            Spawn(Sprite.Puff, Sprite.PuffC, 20 * S, 15 * S, S * 7, -S * 1.5, 0.45, 0);
        }
        void SpawnOrbitStar()
        {
            double a = now * 6;
            Spawn(Sprite.Star, Sprite.StarC, 11 * S + Math.Cos(a) * 9 * S, 1 * S + Math.Sin(a) * 1.5 * S, 0, 0, 0.36, 0);
        }

        void UpdateParticles(double dt)
        {
            for (int i = parts.Count - 1; i >= 0; i--)
            {
                Particle q = parts[i];
                q.Life -= dt; q.VY += q.Gravity * dt; q.X += q.VX * dt; q.Y += q.VY * dt;
                if (q.Life <= 0) parts.RemoveAt(i);
            }
        }

        // 头顶的小表情泡泡（平时的反应都用它，不用文字）
        void Emo(string kind, double secs) { emoteKind = kind; emoteIcon = null; emoteBorn = now; emoteUntil = now + secs; }
        void ClearEmote() { emoteKind = null; emoteIcon = null; dreamHit = Rectangle.Empty; }

        void UpdEmoteGeom()
        {
            if (emoteKind == "dream" && bubble == null && state == St.Sleep)
            {
                int ax, hy;
                EmoteAnchor(out ax, out hy);
                dreamHit = Sprite.DreamHitRect(now - emoteBorn, ax, hy, S, facing >= 0 ? 1 : -1);
                if (OverDreamBubble(Cursor.Position)) emoteUntil = Math.Max(emoteUntil, now + 1.5);   // 鼠标放在梦泡泡上：泡泡不消失
            }
            else dreamHit = Rectangle.Empty;
        }

        Point ToLocal(Point screen) { return IsHandleCreated ? PointToClient(screen) : new Point(-10000, -10000); }

        bool OverDreamBubble(Point screen)
        {
            return !dreamHit.IsEmpty && emoteKind == "dream" && state == St.Sleep && dreamHit.Contains(ToLocal(screen));
        }

        void UpdCursorShape()
        {
            Point lc = ToLocal(Cursor.Position);
            bool hand = (barOn && BarIndexAt(lc) >= 0) || (!dreamHit.IsEmpty && dreamHit.Contains(lc));
            Cursor want = hand ? Cursors.Hand : Cursors.Default;
            if (want != curCursor) { curCursor = want; Cursor = want; }
        }

        void Say(string text, double secs) { SayRaw(text, false, secs); }
        void SayRaw(string text, bool spin, double secs) { bubble = text; bubbleSpin = spin; bubbleUntil = now + secs; dirty = true; }
        void ClearBubble() { bubble = null; bubbleSpin = false; dirty = true; }

        void UpdateBubble()
        {
            if (bubble != null && now >= bubbleUntil) ClearBubble();
            if (now >= nextChatter)
            {
                nextChatter = now + 240 + R01() * 240;
                if (chatty && pomoMode != 1 && bubble == null && Rest(state)) { string line = RandomLine(); Say(line, 4); BabbleFor(line); }
            }
        }

        string RandomLine()
        {
            int h = DateTime.Now.Hour;
            if (R01() < 0.25)
            {
                if (h >= 23 || h < 5) return "很晚啦，早点休息吧";
                if (h == 11 || h == 12) return "午饭时间到！";
                if (h == 17) return "快下班啦~";
            }
            return Pick(IdleLines);
        }

        // ================================================================ 绘制
        protected override void OnPaintBackground(PaintEventArgs e) { }   // OnPaint 会铺满整个重绘区域

        protected override void OnPaint(PaintEventArgs e)
        {
            if (!profiling) { PaintAll(e); return; }
            double t0 = clock.Elapsed.TotalMilliseconds;
            PaintAll(e);
            profPaint += clock.Elapsed.TotalMilliseconds - t0; profPaints++;
            profPx += (long)e.ClipRectangle.Width * e.ClipRectangle.Height;
        }

        void PaintAll(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            Pose p = lastPose ?? new Pose();
            long pk = p.Key();
            int[] cellPix;
            if (!poseCache.TryGetValue(pk, out cellPix))
            {
                if (poseCache.Count > 600) poseCache.Clear();
                cellPix = Sprite.CellPixels(p); poseCache[pk] = cellPix;
            }

            int W = ClientSize.Width, H = ClientSize.Height;
            if (destBmp == null || destBmp.Width != W || destBmp.Height != H)
            {
                if (destBmp != null) destBmp.Dispose();
                destBmp = new Bitmap(W, H, PixelFormat.Format32bppPArgb);   // 只有全透明/全不透明像素，ARGB 与预乘值相同
                destBuf = new int[W * H];
            }
            Sprite.Affine xf = CurXf();
            Rectangle clip = Rectangle.Intersect(e.ClipRectangle, new Rectangle(0, 0, W, H));
            if (clip.Width > 0 && clip.Height > 0)
            {
                // 先铺透明色键，再画身体（和小电脑），最后整块直接拷贝（不做 alpha 混合，背景也不用另外擦）
                int key = Key.ToArgb();
                for (int y = clip.Top; y < clip.Bottom; y++)
                {
                    int row = y * W;
                    for (int x = clip.Left; x < clip.Right; x++) destBuf[row + x] = key;
                }
                // 埋在沙里：地面线以下的部分不画（像沉进沙里 / 躲到任务栏后面）
                if (state == St.Match && match != null && match.Team.ContainsKey(this)) PaintPitchRing(clip, W, match.Team[this]);   // 球场上：脚下的影子 + 队伍颜色的圈
                Rectangle sclip = state == St.Burrow ? Rectangle.Intersect(clip, new Rectangle(0, 0, W, offY + FOOT * S)) : clip;
                if (sclip.Width > 0 && sclip.Height > 0) Sprite.RasterXf(cellPix, CW, CH, destBuf, W, H, xf, sclip);
                double ml = MoundLevelNow();
                if (ml > 0) { Rectangle mr = MoundRect(); Sprite.DrawMound(destBuf, W, H, clip, mr.X, mr.Y, S, ml); }
                if (HasLaptop()) Sprite.DrawLaptop(destBuf, W, H, clip, offX, offY, S, facing, key, 0.3 + 0.05 * Math.Sin(now * 3.1));
                BitmapData bd = destBmp.LockBits(clip, ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
                for (int y = 0; y < clip.Height; y++)
                    Marshal.Copy(destBuf, (clip.Top + y) * W + clip.Left, new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride), clip.Width);
                destBmp.UnlockBits(bd);
                g.CompositingMode = CompositingMode.SourceCopy;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.CompositingQuality = CompositingQuality.HighSpeed;
                g.DrawImage(destBmp, clip, clip.X, clip.Y, clip.Width, clip.Height, GraphicsUnit.Pixel);
                g.CompositingMode = CompositingMode.SourceOver;
                g.PixelOffsetMode = PixelOffsetMode.Default;
            }

            if (noseOn) Sprite.DrawNose(g, xf, noseX, noseY, noseR, S);
            if (snack != null && state == St.Eat)
            {
                int sx, sy;
                SnackPos(out sx, out sy);
                Sprite.DrawArt(g, Sprite.SnackRows(snack, snackBite), snack, sx, sy, S);
            }
            {
                Art pa; string[] pr; int ppx, ppy;
                if (propKind != null && Sprite.Prop(propKind, propFrame, facing, curBodyDy, out pa, out pr, out ppx, out ppy))
                    Sprite.DrawArt(g, pr, pa, offX + ppx * S, offY + ppy * S, S);
            }

            int u0 = Sprite.Half(S);
            foreach (Particle q in parts)
            {
                if (q.Life < 0.25 && (((int)(q.Life * 24)) & 1) == 0) continue;   // 快消失时闪烁
                Sprite.Glyph(g, q.Shape, Sprite.BrushOf(q.Color), offX + (int)q.X, offY + (int)q.Y, q.U > 0 ? q.U : u0);
            }

            int minX, maxX, minY;
            ClampRange(out minX, out maxX, out minY);
            int pu = Math.Max(2, S / 2);

            if (pomoMode != 0)
            {
                if (lastBadgeRect.IsEmpty || clip.IntersectsWith(Rectangle.Inflate(lastBadgeRect, 2 * S, S)))
                {
                    int secs = Math.Max(0, (int)Math.Ceiling((pomoEnd - DateTime.Now).TotalSeconds));
                    string t = (pomoMode == 2 ? "休 " : "") + (secs / 60).ToString("D2") + ":" + (secs % 60).ToString("D2");
                    int bx = offX + 23 * S;
                    bool right = bx + 30 * pu > maxX;
                    lastBadgeRect = Sprite.DrawBadge(g, t, badgeFont, pu, right ? offX + S : bx, offY + 5 * S, right);
                }
            }
            else lastBadgeRect = Rectangle.Empty;

            // 头顶的小表情（有文字气泡时先不画，免得叠在一起）
            if (emoteKind != null && bubble == null)
            {
                int ax, hy;
                EmoteAnchor(out ax, out hy);
                Sprite.DrawEmote(g, emoteKind, emoteIcon, now - emoteBorn, emoteUntil - now, ax, hy, S, facing >= 0 ? 1 : -1);
            }

            if (barOn)
            {
                Sprite.DrawBar(g, barRect, barBtns, BarIcons, barHot, barPress, stay ? BAR_STAY : -1, uiS);
            }

            if (bubble != null)
            {
                // 气泡（框 + 文字）缓存成图，只有文字变了才重新排版；转圈每帧单独画
                string text = bubbleSpin ? ThinkText() : bubble;
                int bottomY = barOn ? barRect.Top - pu : offY + 3 * S;
                int anchorX = offX + 12 * S;
                if (state == St.Climb && rot != 0)
                {
                    // 在墙上身子是横着的：气泡冒在身子上方，对准身子中间
                    bottomY = offY - 8 * S;
                    anchorX = offX + 12 * S + wallSide * (onPole && poleSlant ? 3 : 6) * S;
                }
                string ck = text + "|" + bubbleSpin + "|" + minX + "|" + maxX + "|" + minY + "|" + bottomY + "|" + anchorX + "|" + S;
                int topH = ClientSize.Height;   // 贴着屏幕顶时气泡会被挪下来压住头顶：整块窗口都允许画，别被裁掉
                if (bubbleCache == null || bubbleCache.Width != W || bubbleCache.Height != topH || ck != bubbleCacheKey)
                {
                    if (bubbleCache == null || bubbleCache.Width != W || bubbleCache.Height != topH)
                    {
                        if (bubbleCache != null) bubbleCache.Dispose();
                        bubbleCache = new Bitmap(W, topH, PixelFormat.Format32bppPArgb);
                    }
                    bubbleCacheKey = ck;
                    using (Graphics bg = Graphics.FromImage(bubbleCache))
                    {
                        bg.Clear(Color.Transparent);
                        bg.SmoothingMode = SmoothingMode.None;
                        lastBubbleRect = Sprite.DrawBubble(bg, text, bubbleSpin, -1, bubbleFont, pu, anchorX, bottomY,
                            minX, maxX, W - 4 * pu, minY, out spinAt);
                    }
                }
                Rectangle r = Rectangle.Intersect(clip, Rectangle.Intersect(lastBubbleRect, new Rectangle(0, 0, W, topH)));
                if (r.Width > 0 && r.Height > 0)
                {
                    g.CompositingMode = CompositingMode.SourceOver;
                    g.DrawImage(bubbleCache, r, r.X, r.Y, r.Width, r.Height, GraphicsUnit.Pixel);
                }
                if (bubbleSpin)
                {
                    int frame = Sprite.SpinSeq[((int)(now / 0.11)) % Sprite.SpinSeq.Length];
                    Sprite.Glyph(g, Sprite.Spin[frame], Sprite.BrushOf(Sprite.BodyC), spinAt.X, spinAt.Y, pu);
                }
            }

            // 按钮的小提示最后画，盖在最上面
            if (barOn && barHot >= 0 && barPress < 0 && barHot < barBtns.Length)
                Sprite.DrawBarTip(g, BarTip(barHot), tipFont, Math.Max(2, uiS / 2), barRect, barBtns[barHot], minX, maxX, minY);
        }

        // ================================================================ 鼠标
        // 鼠标是不是在它身上（屏幕坐标；还在晃 / 打滚时也算得准）
        bool HitPet(Point screen)
        {
            if (hiddenForFullscreen || !IsHandleCreated) return false;
            Point lc = PointToClient(screen);
            if (state == St.Burrow)
            {
                // 埋着：只有露在地面上的部分（眼睛、头顶）和沙堆算
                if (MoundLevelNow() > 0 && MoundRect().Contains(lc)) return true;
                if (lc.Y >= offY + FOOT * S) return false;
            }
            double x = lc.X + 0.5, y = lc.Y + 0.5;
            double Ax = offX + 12 * S, Ay = offY + anchorY - hop + sink * S;
            if (rot != 0 && state == St.Roll) { double dx = x - Ax, dy = y - (Ay - 8 * S); return dx * dx + dy * dy < 100.0 * S * S; }
            // 一般情况：把点按当前变形（果冻 + 钟摆 + 在墙上转的 90°）反算回小人的格子
            Sprite.Affine m = Sprite.MakeXf(S, Ax, Ay, anchorY, sq, lean, Math.Max(0.35, Math.Abs(flip)), rot, swing, pivX);
            double det = m.A * m.D - m.B * m.C;
            if (Math.Abs(det) < 1e-9) return false;
            double du = x - m.E, dv = y - m.F;
            double u = (m.D * du - m.C * dv) / det, v = (-m.B * du + m.A * dv) / det;
            return u >= 1 && u <= 23 && v >= 3 && v <= 17.6;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (barOn)
            {
                int i = BarIndexAt(e.Location);
                if (i >= 0) { barPress = i; dirty = true; return; }
                Rectangle br = barRect; br.Inflate(2, 2);
                if (br.Contains(e.Location)) return;
            }
            if (OverDreamBubble(Cursor.Position)) { OpenDream(); return; }   // 钻进梦里
            mouseDown = true; dragging = false;
            downPt = Cursor.Position;
            downT = clock.Elapsed.TotalSeconds;
            grabDX = downPt.X - CX; grabDY = downPt.Y - posY;
            trail.Clear();
            trail.Add(new double[] { downT, downPt.X, downPt.Y });
            sqV -= 2;   // 按下去软软的
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!mouseDown) return;
            Point c = Cursor.Position;
            if (!dragging && (Math.Abs(c.X - downPt.X) > 4 || Math.Abs(c.Y - downPt.Y) > 4)) BeginDrag();
            if (dragging) { FollowDrag(); Location = FormPos(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right)
            {
                // 右键：先冒出快捷按钮；按钮条开着时再右键一下（或者现在冒不出来，比如在墙上、在空中）就打开完整菜单
                if (barOn || !Grounded() || dragging) { HideBar(); ShowMenu(Cursor.Position); }
                else ShowBar(4);
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            if (barPress >= 0)
            {
                int i = barPress;
                barPress = -1; dirty = true;
                if (BarIndexAt(e.Location) == i) BarAction(i);
                return;
            }
            if (!mouseDown) return;
            mouseDown = false;
            if (dragging) EndDrag(); else OnClickPet();
        }

        void BeginDrag()
        {
            NightTouched();   // 夜里把它拎起来了：陪你玩一会儿再睡
            if (state == St.Climb)
            {
                // 从墙上拎下来：身子转回正的，抓在身子中间
                grabDX = 0; grabDY = -8 * S;
            }
            if (state == St.Burrow)
            {
                // 从沙里直接拔出来：抓着脑袋，沙子一溅（离开 Burrow 状态时会溅沙、沙堆塌掉）
                grabDX = 0; grabDY = -12 * S;
            }
            dragging = true;
            Snd("grab", SND_YOU, 1, true);
            HideBar();
            ResetPlans();
            plat = IntPtr.Zero; sitOff = 0;
            if (basePet != null) { ignorePet = basePet; ignorePetUntil = now + 0.5; basePet = null; }   // 从伙伴头上拎起来
            braceUntil = 0;
            velX = velY = 0; gvx = 0;
            sqV += 4; shake = 0; shakeAcc = 0; legAV += 6;
            SetState(St.Drag, 1e9);
            if (R01() < 0.4) Emo("bang", 0.8);
        }

        void FollowDrag()
        {
            Point c = Cursor.Position;
            double t = clock.Elapsed.TotalSeconds;
            posX = c.X - grabDX - 12 * S; posY = c.Y - grabDY;
            double[] last = trail.Count > 0 ? trail[trail.Count - 1] : null;
            if (last == null || last[1] != c.X || last[2] != c.Y) trail.Add(new double[] { t, c.X, c.Y });
            while (trail.Count > 2 && t - trail[0][0] > 0.09) trail.RemoveAt(0);
        }

        void EndDrag()
        {
            dragging = false;
            double vx = 0, vy = 0;
            if (trail.Count >= 2)
            {
                double[] a = trail[0], b = trail[trail.Count - 1];
                double span = b[0] - a[0];
                if (span > 0.01 && clock.Elapsed.TotalSeconds - b[0] < 0.08) { vx = (b[1] - a[1]) / span; vy = (b[2] - a[2]) / span; }
            }
            double max = S * 500;
            velX = Clamp(vx, -max, max);
            velY = Clamp(vy, -max, max);
            // 在屏幕左右边附近松手（不在最底下）：直接扒住墙。往墙上扔得太狠照样糊墙，往外扔就正常飞出去
            if (!stay)
            {
                Rectangle wa = WorkArea();
                double cx = CX;
                int side = cx - wa.Left <= 18 * S ? -1 : (wa.Right - cx <= 18 * S ? 1 : 0);
                if (side != 0 && posY < wa.Bottom - 4 * S && SideFree(side))
                {
                    bool toward = velX * side > 0;
                    double av = Math.Abs(velX);
                    if (!(toward && av > S * 300) && !(!toward && av > S * 150)) { pendingDizzy = false; GrabWall(side); return; }
                }
            }
            if (shakeAcc > 1.2) pendingDizzy = true;   // 被晃晕了
            StartFall(Mood.Plain);
        }

        void OnClickPet()
        {
            NightTouched();
            double t = now;
            clicks.Add(t);
            clicks.RemoveAll(x => t - x > 2.5);
            energy = Math.Min(1, energy + 0.05);
            if (Sleeping()) { Wake(true); return; }   // 点醒：吓一跳
            if (state == St.Climb) { Snd("startle", SND_YOU, 1, true); KickOff(true); return; }   // 在墙上被点：吓得从墙上跳开
            if (state == St.Pole) { Snd("boop", SND_YOU, 1, true); sqV -= 2; Emo(R01() < 0.5 ? "note" : "heart", 1.0); return; }   // 爬杆子时被点：抱紧杆子，冲你乐一下
            if (state == St.Burrow) { PopOut(true); return; }   // 埋着被点：吓得一下子蹦出来
            if (state == St.Alert)
            {
                alertSticky = false;
                Say("收到~", 1.2);
                Snd("boop", SND_YOU, 1, true);
                sqV -= 3;
                StartIdle();
                return;
            }
            bool air = !Grounded();
            if (clicks.Count >= 6)
            {
                clicks.Clear();
                if (air) pendingDizzy = true; else { ResetPlans(); SpawnSweat(); Emo("anger", 1.5); StartDizzy(2.2); }
                return;
            }
            bool dbl = t - lastClickT < 0.4;
            lastClickT = t;
            if (air) { if (dbl) pendingDance = true; else Snd("boop", SND_YOU, 0.8, true); return; }
            ResetPlans();
            if (dbl) { Snd("dance", SND_YOU); StartDance(); return; }
            Snd("boop", SND_YOU, 1, true);
            SpawnHeart();
            Jump(Mood.Happy, S * 95, true);
        }

        void OnPetted()
        {
            SpawnHeart();
            sqV += 1.5;
            energy = Math.Min(1, energy + 0.03);
            if (Sleeping()) { blushUntil = now + 2.5; return; }   // 睡梦里笑一下，接着睡
            pets.Add(now);
            pets.RemoveAll(x => now - x >= 2.5);
            if (pets.Count >= 4 && Grounded() && state != St.Giggle && state != St.Eat && state != St.Think && state != St.Burrow)
            {
                // 挠痒痒：笑得直晃
                pets.Clear(); ResetPlans(); SetState(St.Giggle, 1.6); nextFx = now; blushUntil = now + 2.5;
                return;
            }
            if (Rest(state) || state == St.Walk || state == St.Happy || state == St.Wave) { ResetPlans(); mood = Mood.Plain; StartHappy(1.4); }
            else blushUntil = now + 1.5;
        }

        // ================================================================ 快捷按钮条
        string BarTip(int i)
        {
            switch (i)
            {
                case BAR_FEED: return "喂一口";
                case BAR_BALL: return AnyBall() != null ? "收起球" : "扔个球";
                case BAR_SLEEP: return Sleeping() ? "叫醒它" : "睡一会儿";
                case BAR_DREAM: return "做个梦（小游戏）";
                case BAR_STAY: return stay ? "随便走走" : "待在这儿别动";
                default: return "更多…";
            }
        }

        int BarIndexAt(Point local)
        {
            if (!barOn) return -1;
            for (int i = 0; i < barBtns.Length; i++) if (barBtns[i].Contains(local)) return i;
            return -1;
        }

        bool InBar(Point screen)
        {
            if (!barOn) return false;
            Rectangle r = barRect;
            r.Inflate(4, 4); r.Height += 2;
            return r.Contains(ToLocal(screen));
        }

        void LayoutBar()
        {
            int mn, mx, my;
            ClampRange(out mn, out mx, out my);
            Rectangle r; Rectangle[] btns;
            // 埋在沙里时按钮条挂在露出来的眼睛上方
            Sprite.BarLayout(uiS, BarIcons.Length, offX + 12 * S, offY - 2 * S + (state == St.Burrow ? 7 * S : 0), mn, mx, my, out r, out btns);
            if (r != barRect) { barRect = r; barBtns = btns; dirty = true; }
        }

        void ShowBar(double secs)
        {
            if (dreamForm != null || hiddenForFullscreen || menu.Visible || dragging) return;
            barKeepUntil = Math.Max(barKeepUntil, now + secs);
            if (barOn) return;
            barOn = true; barHot = -1; barPress = -1;
            barRect = Rectangle.Empty;
            LayoutBar();
            dirty = true;
        }

        void HideBar() { if (!barOn) return; barOn = false; barHot = barPress = -1; dirty = true; }

        // 右键点它：头顶冒出快捷按钮（鼠标只是路过 / 停在身上不会冒）；它也乖乖站住等你。
        // 鼠标移开 2.5 秒、或者点了别的地方，按钮条就收起来
        void UpdBar()
        {
            if (!barOn) return;
            Point c = Cursor.Position;
            if (cursorOnPet || InBar(c)) barKeepUntil = Math.Max(barKeepUntil, now + 2.5);
            else if ((Control.MouseButtons & (MouseButtons.Left | MouseButtons.Right | MouseButtons.Middle)) != 0) { HideBar(); return; }
            if (now > barKeepUntil || dreamForm != null || menu.Visible || mouseDown || !Grounded()) { HideBar(); return; }
            if (state == St.Walk && !hasWalkTarget) StartIdle();
            else if (state == St.Idle) stT = Math.Min(stT, stDur - 0.3);
            LayoutBar();
            int hot = BarIndexAt(ToLocal(c));
            if (hot != barHot) { barHot = hot; dirty = true; }
        }

        void BarAction(int i)
        {
            barKeepUntil = now + 1.5;
            switch (i)
            {
                case BAR_FEED: FeedNow(); break;                 // 按钮条留着，可以接着喂
                case BAR_BALL: HideBar(); ToggleBall(); break;
                case BAR_SLEEP:
                    HideBar();
                    if (Sleeping()) Wake(false); else if (Grounded()) GoToBed(SleepMode.Nap, 90 + R01() * 60);
                    break;
                case BAR_DREAM: HideBar(); OpenDream(); break;
                case BAR_STAY: SetStay(!stay); dirty = true; break;
                case BAR_MORE:
                    {
                        Point at = PointToScreen(new Point(barRect.Left, barRect.Bottom));
                        HideBar();
                        ShowMenu(at);
                    }
                    break;
            }
        }

        // 待在这儿别动：不再自己走来走去（原地的小动作、点它摸它拎它、喂它、Claude Code 信号照常）
        void SetStay(bool on)
        {
            if (stay == on) return;
            stay = on;
            SaveSettings();
            if (on)
            {
                ResetPlans();
                gvx = 0;
                if (state == St.Walk || state == St.Chase || state == St.Aim || state == St.Peek || state == St.Roll || state == St.Teeter) StartIdle();
                if (state == St.Climb) DropOff();   // 正在墙上：松手滑下来，落在哪就待在哪
                Emo("note", 1.4);
            }
            else if (Grounded() && !Sleeping() && state != St.Think) { ResetPlans(); Jump(Mood.Happy, S * 40, true); }
        }

        // ================================================================ 喂一口
        // 零食直接递到它嘴边，马上开吃；吃的时候再点就排队，一口接一口
        bool FeedNow()
        {
            if (!Grounded()) return false;
            fedTimes.RemoveAll(x => now - x >= 300);
            if (state == St.Eat && snack != null) { feedQueue = Math.Min(3, feedQueue + 1); return true; }
            if (fedTimes.Count >= 6) { ResetPlans(); SetState(St.Refuse, 0.9); Emo("sweat", 1.3); return false; }   // 吃不下了，摇头
            if (Sleeping()) sqV += 6;     // 睡着也闻得到，一下子醒了
            ResetPlans();
            Art s = Sprite.Snacks[rnd.Next(Sprite.Snacks.Length)];
            snack = null;
            SetState(St.Eat, 1.3);
            snack = s; snackBite = 0;
            nextFx = now; sqV -= 3;
            return true;
        }

        void UpdEat()
        {
            if (now >= nextFx) { nextFx = now + 0.14; SpawnCrumb(); }
            if (snack != null) { int b = Math.Min(2, (int)Math.Floor(stT / 0.5)); if (b != snackBite) { snackBite = b; spriteDirty = true; } }
            if (stT >= stDur) FinishEat();
        }

        void FinishEat()
        {
            bool had = snack != null;
            snack = null; mood = Mood.Plain;
            if (had)
            {
                fedTimes.RemoveAll(x => now - x >= 300);
                fedTimes.Add(now);
                energy = Math.Min(1, energy + 0.15);
                SpawnHeart();
                if (feedQueue > 0) { feedQueue--; int q = feedQueue; if (FeedNow()) { feedQueue = q; return; } }   // 还有排队的：接着吃
                feedQueue = 0;
                if (fedTimes.Count >= 4) { blushUntil = now + 4; Emo("heart", 2); StartLie(4); return; }   // 吃饱了，摊一会儿
                if (All.Count > 1 && now - lastShare > 60 && R01() < 0.4 && TryShareSnack()) return;   // 分伙伴一口
                Emo("heart", 1.4);
            }
            StartHappy(0.9);
        }

        // ================================================================ 拖文件给它吃
        static bool CanEat(IDataObject d)
        {
            return d != null && (d.GetDataPresent(DataFormats.FileDrop) || d.GetDataPresent(DataFormats.UnicodeText) || d.GetDataPresent(DataFormats.Text));
        }

        protected override void OnDragEnter(DragEventArgs e)
        {
            base.OnDragEnter(e);
            if (!CanEat(e.Data)) { e.Effect = DragDropEffects.None; return; }
            e.Effect = DragDropEffects.Copy;
            if (!dragHover) { dragHover = true; sqV += 3; if (Grounded()) Emo("q", 1.2); }
        }

        protected override void OnDragOver(DragEventArgs e)
        {
            base.OnDragOver(e);
            e.Effect = CanEat(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        protected override void OnDragLeave(EventArgs e) { base.OnDragLeave(e); dragHover = false; }

        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);
            dragHover = false;
            try
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    string[] fs = (string[])e.Data.GetData(DataFormats.FileDrop);
                    if (fs == null || fs.Length == 0) return;
                    List<string> q = new List<string>();
                    foreach (string f in fs) q.Add(f.IndexOf(' ') >= 0 ? "\"" + f + "\"" : f);
                    Clipboard.SetDataObject(string.Join(" ", q.ToArray()), true, 5, 100);
                    Eat(fs.Length == 1 ? "吃掉了 " + Path.GetFileName(fs[0]) + "，路径已复制！" : "吃掉 " + fs.Length + " 个文件，路径都复制好了！");
                }
                else
                {
                    string t = e.Data.GetData(DataFormats.UnicodeText) as string;
                    if (t == null) t = e.Data.GetData(DataFormats.Text) as string;
                    if (t == null) return;
                    int lines = t.Split('\n').Length, chars = 0;
                    foreach (char c in t) if (!char.IsWhiteSpace(c)) chars++;
                    Eat("嚼嚼…这段有 " + chars + " 个字、" + lines + " 行");
                }
            }
            catch { Say("呃…没吃下去", 2); }
        }

        void Eat(string text)
        {
            Say(text, 4);
            sqV -= 4;
            Emo("heart", 1.2);
            if (Grounded()) { ResetPlans(); SetState(St.Eat, 1.1); nextFx = now; }
        }

        // ================================================================ 梦境小游戏
        void OpenDream()
        {
            if (dreamForm != null) { try { dreamForm.Activate(); } catch { } return; }
            if (hiddenForFullscreen) return;
            menu.Close(); HideBar(); ResetPlans();
            if (mouseDown || dragging) { mouseDown = false; if (dragging) EndDrag(); }
            if (state != St.Sleep && Grounded()) { sleepLedge = false; StartSleep(SleepMode.Nap); }
            sleepMode = SleepMode.Nap; napDur = 1e9;   // 梦没做完不醒
            Art theme = lastDream ?? Sprite.Dreams[rnd.Next(Sprite.Dreams.Length)];
            lastDream = theme;
            ClearEmote();
            try
            {
                dreamForm = new DreamForm(theme, dpiScale, CloseDream);
                dreamForm.Show();
            }
            catch { dreamForm = null; }
        }

        void CloseDream(string result)
        {
            dreamForm = null;
            SaveSettings();   // 记住小游戏里音乐开没开
            if (state != St.Sleep) return;
            if (result == "lose") { Wake(true); Emo("sweat", 1.6); }                     // 被 bug 吓醒
            else if (result == "win") { StartStretch(); wakeLook = false; Emo("heart", 2.2); SpawnHeart(); energy = Math.Min(1, energy + 0.3); }   // 睡饱了
            else napDur = stT + 15 + R01() * 20;                                          // 不玩了：让它接着睡一会儿
        }

        // ================================================================ 更多待机动作
        double pageFlipUntil, nextPage, readGiggleUntil; int castleStage; bool castleAdmired, planeThrown;
        static readonly string[] BubS = { ".X.", "X.X", ".X." }, BubL = { ".XX.", "X..X", "X..X", ".XX." };
        static readonly Color BubC = Color.FromArgb(0x8E, 0xC9, 0xEE);

        void StartRead() { SetState(St.Read, 9 + R01() * 10); nextPage = now + 2 + R01() * 2; }
        void StartGuitar()
        {
            // 坐在边上弹，两条腿垂下来晃：窗口标题栏上随便坐；任务栏上要底下还有地方（任务栏没藏起来）才往下坐；站在伙伴头上的照旧盘腿
            guitarLedge = basePet == null && (plat != IntPtr.Zero || ScreenArea().Bottom - WorkArea().Bottom >= 3 * S);
            SetState(St.Guitar, 5 + R01() * 4); nextFx = now + 0.3;
        }
        void StartMusic() { SetState(St.Music, 6 + R01() * 6); nextFx = now + 0.5; }
        void StartBubbles() { SetState(St.Bubbles, 3.5 + R01() * 3); nextFx = now + 0.25; }
        void StartLift() { SetState(St.Lift, 1.4 * (3 + rnd.Next(3))); }
        void StartSweep() { if (stay) { StartIdle(); return; } facing = rnd.Next(2) == 0 ? -1 : 1; SetState(St.Sweep, 5 + R01() * 4); nextFx = now; }
        void StartCastle() { if (plat != IntPtr.Zero || basePet != null) { StartIdle(); return; } castleStage = 0; castleAdmired = false; SetState(St.Castle, 7.6); nextFx = now; }
        void StartPlane() { planeThrown = false; SetState(St.Plane, 4.2); }

        bool LiftUp() { return (((int)Math.Floor(stT / 0.7)) & 1) == 1; }   // 0.7 秒放下、0.7 秒举起

        void SpawnBubble()
        {
            int side = facing >= 0 ? 1 : -1;
            bool big = R01() < 0.35;
            Spawn(big ? BubL : BubS, BubC, (12 + side * 4 + (R01() - 0.5) * 2) * S, (8 + R01() * 2) * S,
                side * S * (1.5 + R01() * 4), -S * (3 + R01() * 3), 1.3 + R01() * 1.2, -S * 1.5);
        }

        void SpawnSand(double cellX, double cellY)
        {
            Spawn(Sprite.Crumb, R01() < 0.5 ? Sprite.SandA : Sprite.SandB, (cellX + R01() * 5) * S, (cellY + R01() * 3) * S,
                (R01() - 0.5) * S * 10, -S * (2 + R01() * 4), 0.6, S * 60);
        }

        void UpdFun()
        {
            switch (state)
            {
                case St.Read:
                    if (CheckAfk()) return;
                    if (now >= nextPage)
                    {
                        nextPage = now + 2.2 + R01() * 2.5; pageFlipUntil = now + 0.35;
                        if (R01() < 0.2) { readGiggleUntil = now + 1.3; Emo(R01() < 0.5 ? "idea" : "heart", 1.3); }
                    }
                    break;
                case St.Guitar:
                    if (CheckAfk()) return;
                    if (now >= nextFx)
                    {
                        nextFx = now + 0.45 + R01() * 0.35; SpawnNote(); leanV += (Ph(0.9) == 0 ? 1 : -1) * 0.8;
                        if (SndOk(SND_PLAY)) Sfx.Pluck(UkeNotes[rnd.Next(UkeNotes.Length)] + VoicePitch(), 0.7);   // 弹尤克里里：五声音阶里随便拨一个，怎么弹都好听
                    }
                    break;
                case St.Music:
                    if (CheckAfk()) return;
                    if (now >= nextFx) { nextFx = now + 0.9 + R01() * 0.8; SpawnNote(); }
                    break;
                case St.Bubbles:
                    if (now >= nextFx) { nextFx = now + (R01() < 0.2 ? 0.6 : 0.12 + R01() * 0.2); SpawnBubble(); }
                    break;
                case St.Lift:
                    if (stT >= stDur) { SpawnSweat(); Emo(R01() < 0.5 ? "sweat" : "star", 1.4); energy = Math.Max(0.1, energy - 0.08); StartIdle(); }
                    return;
                case St.Castle:
                    {
                        // 0..4.5 秒一点点堆高，4.5..6.4 秒美滋滋地看，然后塌掉
                        int stage = stT < 1.5 ? 1 : stT < 3 ? 2 : stT < 6.4 ? 3 : stT < 7.0 ? 4 : 5;
                        if (stage != castleStage) { castleStage = stage; sqV -= 1.5; }
                        double sx = facing >= 0 ? 23 : -6;
                        if ((stT < 4.5 || stage >= 4) && now >= nextFx) { nextFx = now + (stage >= 4 ? 0.06 : 0.16); SpawnSand(sx, 12); }
                        if (stT >= 4.5 && !castleAdmired) { castleAdmired = true; Emo("heart", 1.6); SpawnSparkle(); }
                        if (stage == 4 && emoteKind != "bang" && stT < 6.5) Emo("bang", 0.6);
                        if (stT >= stDur) { castleStage = 0; if (R01() < 0.5) Emo("sweat", 1.2); StartIdle(); }
                    }
                    return;
                case St.Plane:
                    if (!planeThrown && stT >= 1.0) { planeThrown = true; ThrowPlane(); sqV -= 2; }
                    if (planeThrown)
                    {
                        PetObj pl = FindObj("plane");
                        if (pl != null) { attnX = pl.X; attnY = pl.Y; attnUntil = now + 0.3; }
                    }
                    if (stT >= stDur) { if (R01() < 0.6) { StartHappy(0.8); Emo("note", 1.2); } else StartIdle(); }
                    return;
            }
            if (stT >= stDur) StartIdle();
        }

        // 扫地：慢慢往前挪着扫，扫到头就转身，扫帚前面扬起一点灰
        void UpdSweep(double dt)
        {
            if (stay) { StartIdle(); return; }
            double speed = S * (Ph(0.44) == 0 ? 6 : 2);
            GroundMove(facing * speed, dt, 6);
            double lo, hi, cx = CX;
            SurfaceSpan(out lo, out hi);
            if (cx < lo || cx > hi) { SetCX(Clamp(cx, lo, hi)); gvx = 0; facing = -facing; }
            if (now >= nextFx)
            {
                nextFx = now + 0.22 + R01() * 0.12;
                int side = facing >= 0 ? 1 : -1;
                Spawn(Sprite.Puff, Sprite.PuffC, (side > 0 ? 22 + R01() * 2 : -3 + R01() * 2) * S, 14 * S, side * S * (4 + R01() * 4), -S * (1 + R01() * 2), 0.5, 0);
            }
            if (stT >= stDur) { gvx = 0; if (R01() < 0.4) Emo("note", 1.2); StartIdle(); }
        }

        // 纸飞机：一个小小的透明窗口（鼠标穿得过去），先往上冲一点，再忽高忽低地滑下来，落地停一会儿收掉
        void ThrowPlane()
        {
            PetObj old = FindObj("plane");
            if (old != null) RemoveObj(old);
            Snd("plane", SND_PLAY);
            int side = facing >= 0 ? 1 : -1;
            PetObj o = new PetObj();
            o.Kind = "plane"; o.Born = o.Touched = now;
            o.X = CX + side * 10 * S; o.Y = posY - 13 * S;
            o.VX = side * S * (60 + R01() * 30); o.VY = -S * (50 + R01() * 30);
            o.Win = new ObjWin(true);
            objs.Add(o);
            DrawObj(o);
            o.Win.Show();
            try { Native.SetWindowPos(o.Win.Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE); } catch { }
        }

        void UpdPlaneObj(PetObj o, double dt)
        {
            double age = now - o.Born;
            if (age > 45) { RemoveObj(o); return; }
            if (o.Rest) { if (now - o.Touched > 6) RemoveObj(o); return; }   // 落地停一会儿就收起来
            Rectangle wa = Native.WorkAt((int)o.X, (int)o.Y - S);
            // 速度越快升力越大；上下方向有阻力，所以是慢慢飘下来，再叠一点忽上忽下
            o.VY += (S * 60 - Math.Abs(o.VX) * 0.55) * dt + Math.Sin(age * 2.6) * S * 45 * dt;
            o.VY *= Math.Max(0, 1 - 1.4 * dt);
            o.VX *= Math.Max(0, 1 - 0.04 * dt);
            o.X += o.VX * dt; o.Y += o.VY * dt;
            int my = (int)o.Y - 2 * S;
            if (o.X - 4 * S < wa.Left && o.VX < 0 && !Native.HasMonitorAt(wa.Left - 2, my)) { o.X = wa.Left + 4 * S; o.VX = -o.VX * 0.25; }
            if (o.X + 4 * S > wa.Right && o.VX > 0 && !Native.HasMonitorAt(wa.Right + 1, my)) { o.X = wa.Right - 4 * S; o.VX = -o.VX * 0.25; }
            if (o.Y - 4 * S < wa.Top && o.VY < 0) { o.Y = wa.Top + 4 * S; o.VY = 0; }
            if (o.Y >= wa.Bottom) { o.Y = wa.Bottom; o.VX = o.VY = 0; o.Rest = true; o.Touched = now; }
        }

        // ================================================================ 架木杆爬到别的窗口上
        // polePhase 0 = 把杆子竖起来往上推，顶到窗口标题栏；1 = 顺着杆子往上爬；2 = 跨上标题栏，杆子从下往上收走
        // 杆子是一个单独的小窗口（鼠标穿得过去），放在小人身后
        // onPole = 正在 St.Climb 里扒着杆子（不是屏幕边）
        ObjWin poleWin; IntPtr poleH; int polePhase, poleOx; bool onPole;
        double poleX, poleBot, poleTopNow, poleRetractT = -1, poleClimbPh, poleCooldownUntil = 30;
        string poleArtKey; string[] poleRows;
        // 斜杆（像梯子一样斜靠在窗口上角）：poleX / poleBot 是杆子底，wallSide = +1 窗口在右边（靠它的左上角）/ -1 窗口在左边
        // polePhase 3 = 竖起来以后松手，让杆子往窗口那边倒，啪地靠在窗角上
        bool poleSlant, poleClacked;
        double poleAngNow = Math.PI / 2, poleLen, poleS, poleTipT, slantFromX, slantFromY;

        // 找一个比较高、跳不上去也没关系的窗口：标题栏顶边要露在外面
        // slant ≠ 0：斜着靠（杆子底在窗口外面的地上，tx 是站着推杆子的地方）；0：窗口两边都没地方，竖着立在窗口底下
        Plat PoleTarget(out double tx, out int slant)
        {
            tx = 0; slant = 0;
            if (now - platsT > 2) ScanPlats();
            Rectangle wa = WorkArea();
            double cx = CX;
            Plat best = null; double bestScore = -1e9;
            foreach (Plat p in plats)
            {
                if (p.R - p.L < 28 * S) continue;
                double up = posY - p.T;
                if (up < 30 * S || p.T < wa.Top + 16 * S) continue;   // 太矮的直接跳上去就行；紧贴着屏幕顶的翻不上去
                double x = 0; int sl = 0;
                int first = cx < (p.L + p.R) / 2.0 ? 1 : -1;   // 离哪个角近先试哪边
                for (int k = 0; k < 2 && sl == 0; k++)
                {
                    int sd = k == 0 ? first : -first;
                    double bx;
                    if (PlanSlant(p, sd, out bx) && Math.Abs(bx - sd * 12 * S - cx) <= 300 * S) { sl = sd; x = bx - sd * 12 * S; }
                }
                if (sl == 0)
                {
                    x = Clamp(cx, p.L + 10 * S, p.R - 10 * S);
                    if (x < wa.Left + 11 * S || x > wa.Right - 11 * S || Math.Abs(x - cx) > 300 * S) continue;
                    if (!wa.Contains((int)x, p.T + 1) || !PlatTopVisible(p, (int)x)) continue;
                }
                double score = (up > 95 * S ? 25 : 0) + (sl != 0 ? 15 : 0) - Math.Abs(x - cx) / (4.0 * S) + R01() * 30;
                if (score > bestScore) { bestScore = score; best = p; tx = x; slant = sl; }
            }
            return best;
        }

        // 斜着靠的方案：杆子底落在窗口外边的地上，顶搭在窗口上角（side = +1：窗口在右边，搭它的左上角）。
        // 62° 最好看；地方不够就立陡一点（最陡 79°），还放不下就算了
        bool PlanSlant(Plat q, int side, out double bx)
        {
            bx = 0;
            Rectangle wa = WorkArea();
            double floorY = posY, cornerX = side > 0 ? q.L : q.R;
            if (floorY - q.T < 30 * S || q.T < wa.Top + 16 * S || q.R - q.L < 30 * S) return false;
            if (cornerX <= wa.Left || cornerX >= wa.Right || !wa.Contains((int)cornerX, q.T + 1)) return false;
            if (!PlatTopVisible(q, (int)(cornerX + side * 4 * S)) || !PlatTopVisible(q, (int)(cornerX + side * 14 * S))) return false;   // 窗角和翻上去落脚的地方没被别的窗口挡着
            double[] tries = { 1.08, 1.2, 1.3, 1.38 };
            foreach (double a in tries)
            {
                double px = cornerX - side * Math.Sin(a) * S, py = q.T - Math.Cos(a) * S;   // 杆子中线从窗角外上方一格经过
                double b = px - side * (floorY - py) / Math.Tan(a);
                double stand = b - side * 12 * S;
                if (Math.Min(b, stand) < wa.Left + 12 * S || Math.Max(b, stand) > wa.Right - 12 * S) continue;
                bx = b; return true;
            }
            return false;
        }

        // 斜杆现在的样子：杆子中线贴着窗角外面一格经过；算出角度（从水平量）和杆子底到窗角的长度。窗口挪得太远就靠不住了
        bool SlantGeom(Native.RECT r, out double ang, out double reach)
        {
            int sd = wallSide;
            double cx = sd > 0 ? r.Left : r.Right;
            double a = Math.Atan2(poleBot - r.Top, sd * (cx - poleX));
            double px = cx - sd * Math.Sin(a) * S, py = r.Top - Math.Cos(a) * S;
            double dx = sd * (px - poleX), dy = poleBot - py;
            ang = Math.Atan2(dy, dx); reach = Math.Sqrt(dx * dx + dy * dy);
            return dx > 4 * S && dy > 20 * S && ang > 0.75 && ang < 1.5;
        }

        // 在斜杆上爬到 s（从杆子底沿着杆子量）时小人的位置：脚踩在杆子上面那一侧，身子跟着杆子斜过来
        void SlantPos(double s, out double ax, out double ay)
        {
            int sd = wallSide;
            double a = poleAngNow, si = Math.Sin(a), co = Math.Cos(a);
            double h = (8 + 4 * si) * S;                          // 转了以后身子中心离脚多远（和 MakeXf 里的转轴一样）
            double fx = poleX + sd * co * s - sd * si * S;        // 脚：杆子中线往上那一侧挪一格
            double fy = poleBot - si * s - co * S;
            double pcx = fx - sd * si * h, pcy = fy - co * h;    // 身子中心
            ax = pcx; ay = pcy + h;
        }

        bool wantPole;   // 收到 --signal pole：找机会去爬
        bool TryWantPole()
        {
            if (!wantPole || stay) return false;
            if (plat == IntPtr.Zero && basePet == null) { wantPole = false; PoleNow(); return true; }
            // 站在窗口上：就地往下跳，穿过脚下的标题栏落到地上（走到窗口边再跳，边上要是贴着屏幕边会扒到墙上）
            afterLandThen = () => { if (!TryWantPole()) StartIdle(); };
            Jump(Mood.Happy, S * 35, true, 0, true);
            return true;
        }

        void PoleNow()
        {
            double tx = 0; int slant = 0;
            Plat p = plat == IntPtr.Zero ? PoleTarget(out tx, out slant) : null;
            if (p == null) { Emo("q", 1.2); StartIdle(); return; }
            PoleTo(p, tx, slant);
        }

        void PoleTo(Plat p, double tx, int slant)
        {
            if (stay || plat != IntPtr.Zero) { StartIdle(); return; }
            IntPtr h = p.H;
            WalkTo(tx, S * 12, () => BeginPole(h, slant, false));
        }

        // 走到窗口旁边了：再量一下。斜着靠：在身前立起杆子，竖直了再往窗口那边倒；竖着立：在身边（朝标题栏中间那一侧）立起杆子
        void BeginPole(IntPtr h, int slant, bool retried)
        {
            ScanPlats();
            Plat q = null;
            foreach (Plat pp in plats) if (pp.H == h) { q = pp; break; }
            if (slant != 0)
            {
                double bx;
                if (q == null || plat != IntPtr.Zero || stay || !PlanSlant(q, slant, out bx)) { Emo("q", 1.2); StartIdle(); return; }
                double stand = bx - slant * 12 * S;
                if (Math.Abs(CX - stand) > 4 * S)
                {
                    // 窗口挪了一点：再走两步
                    if (retried) { Emo("q", 1.2); StartIdle(); return; }
                    WalkTo(stand, S * 12, () => BeginPole(h, slant, true));
                    return;
                }
                SetCX(stand);
                RemovePole();
                poleH = h; polePhase = 0; poleRetractT = -1; onPole = false;
                poleSlant = true; poleClacked = false; poleAngNow = Math.PI / 2; poleLen = 0;
                poleX = bx; poleBot = posY; poleTopNow = posY - 8 * S;   // 先从手里冒出一截
                wallSide = slant; facing = slant;
                gvx = 0;
                SetState(St.Pole, 1e9);
                Emo("idea", 1.0);
                ShowPole();
                return;
            }
            poleSlant = false;
            double cx = CX;
            if (q == null || plat != IntPtr.Zero || stay || cx < q.L + 6 * S || cx > q.R - 6 * S || posY - q.T < 20 * S || !PlatTopVisible(q, (int)cx))
            { Emo("q", 1.2); StartIdle(); return; }
            if (q.T < WorkArea().Top + 16 * S) { Emo("q", 1.2); StartIdle(); return; }
            int side = cx < (q.L + q.R) / 2.0 ? 1 : -1;   // 杆子立在靠标题栏中间那边，爬上去翻身时身子正好在标题栏上
            double px = cx + side * 12 * S;
            if (px < q.L + 4 * S || px > q.R - 4 * S || !PlatTopVisible(q, (int)px)) { Emo("q", 1.2); StartIdle(); return; }
            RemovePole();
            poleH = h; polePhase = 0; poleRetractT = -1; poleClimbPh = 0; onPole = false;
            poleX = px; poleBot = posY; poleTopNow = posY - 8 * S;   // 先从手里冒出一截
            wallSide = side; facing = side;
            gvx = 0;
            SetState(St.Pole, 1e9);
            Emo("idea", 1.0);
            ShowPole();
        }

        // 爬杆子时脚贴着杆子的那条边（和爬屏幕边一样，杆子就是一面很窄的墙）
        double PoleWallPosX(int side) { return side < 0 ? poleX + S : poleX - S - 24 * S; }

        // 爬到标题栏上面了（或者快顶到屏幕顶了）：翻身跳上去，杆子从下往上收走
        void PoleMantle(int barTop)
        {
            int s = wallSide;
            double mid = posY - 12 * S;
            Rectangle wa = WorkArea();
            double foot0 = mid + 9 * S, apex = Math.Max(wa.Top + 15.5 * S, barTop - 3 * S);   // 跳到脚比标题栏高一点，落下来正好踩上去
            double jv = Math.Sqrt(2 * S * 520.0 * Math.Max(S, foot0 - apex)) + S * 520.0 * 0.025;   // 一帧一帧算会矮几个像素：多给点劲，免得差一点没上去
            onPole = false;
            polePhase = 2; poleRetractT = now;
            poleCooldownUntil = now + 45 + R01() * 45;
            ResetPlans();
            facing = s;
            SetState(St.Fall, 1e9);   // 先转回来（rot 归零）
            SetCX(posX + 12 * S + s * S);
            posY = mid + 9 * S;
            ignorePlat = IntPtr.Zero;
            afterLandThen = () => { Emo("note", 1.3); StartHappy(0.9); };
            Launch(Mood.Happy, jv, s * S * 12, false);
        }

        // 杆子顶着的那个窗口现在在哪（被关掉 / 最小化 / 挪开了就返回 false）
        bool PoleWindow(out Native.RECT r)
        {
            r = new Native.RECT();
            IntPtr h = poleH;
            if (h == IntPtr.Zero || !Native.IsWindow(h) || !Native.IsWindowVisible(h) || Native.IsIconic(h) || Native.IsZoomed(h) || IsCloaked(h)) return false;
            if (!GetFrame(h, out r)) return false;
            if (poleSlant) { double a, reach; return SlantGeom(r, out a, out reach); }
            return poleX >= r.Left + 4 * S && poleX <= r.Right - 4 * S && r.Top < poleBot - 12 * S;
        }

        // 站在地上把杆子往上推，顶到标题栏后转身扒到杆子上（接下来就是 St.Climb，和爬屏幕边一样）
        void UpdPoleClimb(double dt)
        {
            Native.RECT r;
            if (!PoleWindow(out r)) { Emo("q", 1.2); StartIdle(); return; }   // 窗口没了 / 挪走了：收起杆子
            if (poleSlant) { UpdSlantRaise(dt, r); return; }
            double goal = r.Top - 3 * S;   // 杆子顶头比标题栏高出一点
            double rest = poleTopNow - goal;
            poleTopNow -= Math.Max(S * 60, rest * 5) * dt;   // 越推越慢
            if (poleTopNow > goal) return;
            poleTopNow = goal; sqV -= 3;
            // 顶住了：转身扒上杆子，往上爬
            ResetPlans();
            gvx = 0; sitOff = 0;
            onPole = true; polePhase = 1;
            facing = wallSide;
            climbPhase = 0; climbMode = 1; climbV = 0; climbTop = false;
            SetState(St.Climb, 1e9);
        }

        // 斜杆：先竖着往上推到够长，松手让它往窗口那边倒（越倒越快），啪地靠在窗角上弹一下，然后扒上去
        void UpdSlantRaise(double dt, Native.RECT r)
        {
            double ang, reach;
            SlantGeom(r, out ang, out reach);
            if (polePhase == 0)
            {
                poleLen = reach + 5 * S;   // 杆子顶头比窗角多出一截
                double goal = poleBot - poleLen, rest = poleTopNow - goal;
                poleTopNow -= Math.Max(S * 60, rest * 5) * dt;   // 越推越慢
                if (poleTopNow > goal) return;
                poleTopNow = goal; sqV -= 2;
                polePhase = 3; poleTipT = now; poleAngNow = Math.PI / 2;
                return;
            }
            const double T1 = 0.55;
            double t = now - poleTipT;
            if (t < T1)
            {
                double k = t / T1;
                poleAngNow = Math.PI / 2 - (Math.PI / 2 - ang) * k * k;
                return;
            }
            if (!poleClacked)
            {
                poleClacked = true;
                Snd("clack", SND_PLAY);
                Dbg(string.Format("pole clack side={0} ang={1:F0} reach={2:F0} base={3:F0}", wallSide, ang * 180 / Math.PI, reach / S, poleX / S));
                sqV -= 3; leanV += wallSide * 1.2;
                Emo("note", 1.0);
            }
            double u = (t - T1) / 0.3;
            if (u < 1) { poleAngNow = ang + 0.045 * Math.Sin(Math.PI * u) * (1 - u); return; }   // 靠上去弹一下
            poleAngNow = ang;
            // 靠稳了：扒上杆子，往上爬
            ResetPlans();
            gvx = 0; sitOff = 0;
            onPole = true; polePhase = 1;
            facing = wallSide;
            poleS = 11 * S; slantFromX = CX; slantFromY = posY;
            climbPhase = 0; climbMode = 1; climbV = 0; climbTop = false;
            SetState(St.Climb, 1e9);
        }

        // 斜杆上：身子跟着杆子斜过来，脚踩在杆子上面那一侧，一段一段往上爬；爬到窗角跳上标题栏
        void UpdSlantClimb(double dt, Native.RECT r)
        {
            double ang, reach;
            SlantGeom(r, out ang, out reach);
            poleAngNow = ang; poleLen = reach + 5 * S;   // 窗口挪了一点：杆子跟着
            double target = -wallSide * ang, ax, ay;
            if (climbPhase == 0)
            {
                // 从地上爬上杆子：一边转过来一边挪到杆子上
                double k = Math.Min(1, stT / 0.3), e = 1 - (1 - k) * (1 - k);
                rot = target * e;
                if (rot == 0) rot = -1e-6 * wallSide;
                SlantPos(poleS, out ax, out ay);
                SetCX(slantFromX + (ax - slantFromX) * e);
                posY = slantFromY + (ay - slantFromY) * e - Math.Sin(Math.PI * k) * 3 * S;
                if (k >= 1) { climbPhase = 1; climbMode = 1; climbUntil = now + 1.5 + R01() * 2; }
                return;
            }
            rot = target;
            double cv = S * 16;
            double want = climbMode > 0 ? -cv : 0;   // climbV < 0 = 往上爬（和爬墙一样，走路动画、身子倾斜都照着它）
            climbV += (want - climbV) * Math.Min(1, dt * 5);
            if (want == 0 && Math.Abs(climbV) < 0.2 * S) climbV = 0;
            poleS = Math.Max(11 * S, poleS - climbV * dt);
            walkPh += Math.Abs(climbV) * dt / (S * 1.5);
            if (climbMode != 0) facing = wallSide;
            SlantPos(poleS, out ax, out ay);
            SetCX(ax); posY = ay;
            if (poleS >= reach - 10 * S) { SlantMantle(r); return; }
            if (climbMode == 0 && now >= nextClingLook)
            {
                // 歇口气：东张西望，偶尔冒个表情
                nextClingLook = now + 0.7 + R01() * 1.0;
                int[] xs = { -1, 0, 1, 0 }, ys = { -1, 0, 0, 1 };
                clingLookX = xs[rnd.Next(4)]; clingLookY = ys[rnd.Next(4)];
                if (R01() < 0.12 && emoteKind == null) { string[] em = { "note", "q", "heart", "dots" }; Emo(em[rnd.Next(em.Length)], 1.3); }
            }
            if (now >= climbUntil) ClimbDecide(WorkArea());
        }

        // 爬到窗角了：转回来，往窗口里面一跳，落在标题栏上；杆子从下往上收走
        void SlantMantle(Native.RECT r)
        {
            int s = wallSide;
            double pcx = CX, pcy = posY - (8 + 4 * Math.Abs(Math.Sin(rot))) * S;   // 身子中心
            onPole = false;
            polePhase = 2; poleRetractT = now;
            poleCooldownUntil = now + 45 + R01() * 45;
            ResetPlans();
            facing = s;
            SetState(St.Fall, 1e9);   // 先转回来（rot 归零）
            SetCX(pcx); posY = pcy + 9 * S;
            Rectangle wa = WorkArea();
            double G = S * 520.0;
            // 跳到脚比标题栏高一点，落下来正好踩上去。一帧一帧算会比理论上矮几个像素（窗口贴近屏幕顶时余量本来就小，会落空），
            // 所以多给一点劲；顶到屏幕顶的「天花板」也没关系，会被挡住再落下来
            double apex = Math.Max(wa.Top + 15 * S, r.Top - 4 * S);
            double vy = Math.Sqrt(2 * G * Math.Max(S, posY - apex)) + G * 0.025;
            double tF = vy / G + Math.Sqrt(2 * Math.Max(0, r.Top - apex) / G);
            double landX = (s > 0 ? r.Left : r.Right) + s * 14 * S;
            Dbg(string.Format("pole mantle bar={0} land={1:F0}", r.Top / S, landX / S));
            ignorePlat = IntPtr.Zero;
            afterLandThen = () => { Emo("note", 1.3); StartHappy(0.9); };
            Launch(Mood.Happy, vy, (landX - CX) / Math.Max(0.2, tF), false);
        }

        void ShowPole()
        {
            if (poleWin == null || poleWin.IsDisposed) { poleWin = new ObjWin(true); poleArtKey = null; }
            double bx, by, tx, ty;
            PoleEnds(out bx, out by, out tx, out ty);
            DrawPole(bx, by, tx, ty);
            if (!poleWin.Visible && !hiddenForFullscreen) poleWin.Show();
            // 放在小人身后
            try { Native.SetWindowPos(poleWin.Handle, Handle, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE); } catch { }
        }

        // 杆子现在两头在哪：竖着推的时候顶头在 poleTopNow；斜杆倒下去 / 靠着时按角度和长度算
        void PoleEnds(out double bx, out double by, out double tx, out double ty)
        {
            bx = tx = poleX; by = poleBot; ty = poleTopNow;
            if (poleSlant && polePhase != 0)
            {
                tx = poleX + wallSide * Math.Cos(poleAngNow) * poleLen;
                ty = poleBot - Math.Sin(poleAngNow) * poleLen;
            }
        }

        // 画杆子：杆子底中线 (bx, by)，顶 (tx, ty)；样子变了才重画
        void DrawPole(double bx, double by, double tx, double ty)
        {
            if (poleWin == null || poleWin.IsDisposed) return;
            int u = S;
            double dxc = Math.Round((tx - bx) / u * 4) / 4, dyc = Math.Max(1, Math.Round((by - ty) / u));
            string key = dxc.ToString("F2") + "|" + dyc + "|" + u;
            if (key != poleArtKey || poleRows == null)
            {
                poleArtKey = key;
                poleRows = Sprite.SlantPoleRows(dxc, dyc, out poleOx);
                poleWin.SetArt(poleRows, Sprite.PoleArt, u);
            }
            poleWin.MoveTo((int)Math.Round(bx) + poleOx * u, (int)Math.Round(by) - poleRows.Length * u);
        }

        // 每帧更新杆子的样子：爬的时候跟着长；被打断就缩回去；爬上去了从下往上收走
        void UpdPole(double dt)
        {
            if (poleWin == null) return;
            bool active = state == St.Pole || (state == St.Climb && onPole);
            if (!active && poleRetractT < 0) poleRetractT = now;   // 被拎走 / 掉下来 / 有别的事：收杆子
            double bx, by, tx, ty;
            PoleEnds(out bx, out by, out tx, out ty);
            if (poleRetractT >= 0)
            {
                double k = Math.Min(1, (now - poleRetractT) / 0.45);
                if (polePhase == 2) { bx += (tx - bx) * k; by += (ty - by) * k; }   // 爬上去了：从下往上收
                else { tx += (bx - tx) * k; ty += (by - ty) * k; }                  // 半路不爬了：缩回地上
                if (k >= 1) { RemovePole(); return; }
            }
            DrawPole(bx, by, tx, ty);
        }

        void RemovePole()
        {
            poleRetractT = -1; polePhase = 0; poleH = IntPtr.Zero;
            poleArtKey = null; poleRows = null;
            if (poleWin != null) { try { poleWin.Close(); poleWin.Dispose(); } catch { } poleWin = null; }
        }

        // ================================================================ 小物件（球）
        // 球：一个小小的置顶窗口，落在任务栏上面；可以拿起来扔，点一下会弹起来，右键点它收起来。
        // 它会追着球跑、踢着玩、跳起来顶头球；球砸到它身上会被弹开。十分钟没人碰就自己收起来。
        // 蝴蝶先不做（SpawnFly 留空）
        PetObj FindObj(string kind) { foreach (PetObj o in objs) if (o.Kind == kind && !o.Gone) return o; return null; }
        void SpawnFly() { }

        void FillPlayRow()
        {
            // 陪它玩：只放常点的几个（想事情、办公这些它自己会做，不占地方）
            playRow.ClearChips();
            playRow.AddChip("喂一口", () => FeedNow());
            playRow.AddChip(AnyBall() != null ? "收起球" : "扔个球", () => ToggleBall());
            playRow.AddChip("跳舞", () => { if (Grounded()) { ResetPlans(); StartDance(); } });
            playRow.AddChip("说句话", () => Say(RandomLine(), 4.5));
            playRow.AddChip(state == St.Burrow ? "钻出来" : "挖沙", () => ToggleBurrow());
            playRow.Invalidate();
            // 一起玩：好几只时才有，单独一排
            friendRow.ClearChips();
            friendRow.AddChip("叠高高", () => { if (Grounded() && basePet == null && !stay) { PetForm f = NearestFriend(5000); if (f != null) { ResetPlans(); StackOnto(f); } } });
            friendRow.AddChip("击掌", () => { if (Grounded() && basePet == null && !stay) { PetForm f = NearestFriend(5000); if (f != null && f.basePet == null) { ResetPlans(); HighFiveWith(f); } } });
            friendRow.AddChip("踢球", () => KickTogether());
            friendRow.AddChip("聊天", () => ChatNow());
            friendRow.AddChip(match != null ? "结束比赛" : "比赛", () => MatchNow());
            friendRow.AddChip("一起睡", () => BedTimeAll());
            friendRow.Visible = All.Count > 1;
            friendRow.Invalidate();
        }

        // 现在有空陪球玩吗（睡觉、吃东西、干活、埋在沙里、待在原地时不追）
        bool CanPlay()
        {
            return Grounded() && !Sleeping() && !stay && !ccWorking && dreamForm == null
                && state != St.Eat && state != St.Burrow && state != St.Shake && state != St.Alert && state != St.Think && state != St.Work;
        }

        void ToggleBall()
        {
            if (match != null) { match.Host.EndMatch(false); return; }   // 比赛中点「收起球」：不踢了
            PetObj old = AnyBall();
            if (old != null) { PetForm ow = BallOwner(old); if (ow != null) ow.RemoveObj(old); return; }
            if (hiddenForFullscreen) return;
            PetObj b = ThrowBall(0);
            if (CanPlay()) { ResetPlans(); Emo("bang", 0.9); StartChase(b, 3 + rnd.Next(4)); }
            InviteToBall(b);   // 伙伴们也跑过来一起踢
        }

        // 屏幕上的球（不管是哪只扔出来的，大家都能踢）
        static PetObj AnyBall()
        {
            foreach (PetForm f in All) foreach (PetObj o in f.objs) if (o.Kind == "ball" && !o.Gone) return o;
            return null;
        }

        static PetForm BallOwner(PetObj b) { foreach (PetForm f in All) if (f.objs.Contains(b)) return f; return null; }

        // 扔出一个球；dir = 0：往屏幕中间那边扔
        PetObj ThrowBall(int dir)
        {
            Rectangle wa = WorkArea();
            if (dir == 0) dir = CX > (wa.Left + wa.Right) / 2.0 ? -1 : 1;
            return NewBall(CX + dir * 12 * S, Math.Max(wa.Top + 12 * S, posY - 26 * S), dir * S * (40 + R01() * 30), -S * 100);
        }

        // 在 (x, y) 放一个球（y 是球的底边），带初速度；球挂在我身上，由我的计时器算物理
        PetObj NewBall(double x, double y, double vx, double vy)
        {
            PetObj b = new PetObj();
            b.Kind = "ball"; b.Born = b.Touched = now;
            b.X = x; b.Y = y; b.PrevX = x;
            b.VX = vx; b.VY = vy;
            b.Win = new ObjWin(false);
            b.Win.Cursor = Cursors.Hand;
            PetObj me = b;
            b.Win.MouseDown += (s, e) => GrabObj(me, e.Button);
            objs.Add(b);
            DrawObj(b);
            b.Win.Show();
            try { Native.SetWindowPos(b.Win.Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE); } catch { }
            return b;
        }

        // ================================================================ 好几只一起踢球：传来传去、头球、停球、踢完击掌

        // 现在能被叫去踢球吗：有空、在同一块屏幕上、没叠在别人头上 / 没驮着别人
        bool FreeForBall(Rectangle wa)
        {
            return !IsDisposed && All.Contains(this) && !hiddenForFullscreen && CanPlay() && basePet == null && Rider() == null
                && WorkArea() == wa && now >= braceUntil;
        }

        // 叫上有空的伙伴一起踢；叫上了就定好这一轮传几脚
        int InviteToBall(PetObj b)
        {
            if (b == null || b.Gone) return 0;
            Rectangle wa = WorkArea();
            int n = 0;
            foreach (PetForm f in All.ToArray())
            {
                if (f == this) continue;
                if (f.chaseObj == b && (f.state == St.Chase || f.state == St.Kick)) { n++; continue; }
                if (!f.FreeForBall(wa)) continue;
                f.ResetPlans(); f.Emo("bang", 0.9); f.StartChase(b, 2 + f.rnd.Next(3));
                if (f.chaseObj == b) n++;
            }
            if (n > 0 && b.Passes <= 0) b.Passes = 4 + rnd.Next(4) + 2 * n;
            Dbg("ball: invited " + n + ", passes=" + b.Passes);
            return n;
        }

        // 右键菜单「一起踢球」：没球就扔一个，有球就叫大家过去
        void KickTogether()
        {
            if (hiddenForFullscreen || match != null) return;
            PetObj b = AnyBall();
            if (b == null) { ToggleBall(); return; }
            if (b.Held) return;
            if (CanPlay()) { ResetPlans(); Emo("bang", 0.9); StartChase(b, 3 + rnd.Next(3)); }
            InviteToBall(b);
        }

        // 自己拿个球出来，往伙伴那边一扔，叫它一起踢
        void KickAbout(PetForm f)
        {
            if (AnyBall() != null || hiddenForFullscreen || f == null) { StartIdle(); return; }
            nextKickAbout = now + 150 + R01() * 200;
            PetObj b = ThrowBall(f.CX >= CX ? 1 : -1);
            b.Auto = true;
            ResetPlans(); Emo("note", 1.0); StartChase(b, 3 + rnd.Next(3));
            InviteToBall(b);
        }

        // 也在追这个球的伙伴（离球最近的那只）
        PetForm BallMate(PetObj o)
        {
            PetForm best = null; double bd = double.MaxValue;
            foreach (PetForm f in All)
            {
                if (f == this || f.IsDisposed || f.chaseObj != o) continue;
                if (f.state != St.Chase && f.state != St.Kick && f.state != St.Fall && f.state != St.Crouch && f.state != St.Land) continue;
                double d = Math.Abs(f.CX - o.X);
                if (d < bd) { bd = d; best = f; }
            }
            return best;
        }

        // 要不要让伙伴先去踢：刚传给别人的球让它去接；别的时候谁离球近谁去（不抢）
        bool ShouldWait(PetObj o, PetForm mate)
        {
            if (now - o.KickT < 3.5 && o.PassTo != null && All.Contains(o.PassTo) && o.PassTo.chaseObj == o) return o.PassTo != this;
            double dm = Math.Abs(mate.CX - o.X), di = Math.Abs(CX - o.X);
            if (Math.Abs(dm - di) < 2 * S) return slot > mate.slot;
            return dm < di;
        }

        // 等伙伴把球传过来：盯着球，离伙伴太近就退几步，拉开传球的距离
        void WaitForPass(PetObj o, PetForm mate, double dt)
        {
            ballWait = true;
            if (Math.Abs(o.X - CX) > 2 * S) facing = o.X >= CX ? 1 : -1;
            double gap = CX - mate.CX, ag = Math.Abs(gap), target = 0;
            int away = gap >= 0 ? 1 : -1;
            if (ag < 44 * S) target = away * S * 34;
            else if (ag > 130 * S) target = -away * S * 20;
            GroundMove(target, dt, 8);
            double lo, hi, c2 = CX;
            SurfaceSpan(out lo, out hi);
            if (c2 < lo || c2 > hi) { SetCX(Clamp(c2, lo, hi)); gvx = 0; }
        }

        // 传给谁：正在一起踢的、或者旁边有空的伙伴（好几只就随便挑一只）
        PetForm PassTarget(PetObj o)
        {
            Rectangle wa = WorkArea();
            List<PetForm> cands = new List<PetForm>();
            foreach (PetForm f in All)
            {
                if (f == this || f.IsDisposed || f.hiddenForFullscreen) continue;
                bool inGame = f.chaseObj == o && (f.state == St.Chase || f.state == St.Kick) && f.WorkArea() == wa;
                if (inGame || (f.FreeForBall(wa) && f.plat == IntPtr.Zero)) cands.Add(f);
            }
            return cands.Count == 0 ? null : cands[rnd.Next(cands.Count)];
        }

        // 把球传给伙伴：大多贴着地滚过去，有时候吊个高球；球在身后那边就先挑起来从头顶上过去
        // 返回 false：这一脚只是把球挑到另一边，还没传出去
        bool PassBall(PetObj o, PetForm mate, int dir)
        {
            int to = mate.CX >= CX ? 1 : -1;
            double gap = Math.Abs(mate.CX - CX);
            o.LastKicker = this; o.KickT = now; o.Touched = now; o.Rest = false;
            kicksLeft = Math.Max(kicksLeft, 2);
            if (gap < 34 * S)
            {
                // 跟伙伴贴得太近（球会被两只夹住）：先把球往另一边带开，拉开距离再传
                int away = -to;
                if (dir == away) { o.VX = away * S * (50 + R01() * 20); o.VY = -S * (20 + R01() * 20); }
                else { o.VX = away * S * (52 + R01() * 10); o.VY = -S * (155 + R01() * 20); o.NoHit = this; o.NoHitUntil = now + 0.75; }
                Spawn(Sprite.Puff, Sprite.PuffC, (dir > 0 ? 20 : 2) * S, 15 * S, 0, -S * 4, 0.4, 0);
                Dbg("ball: too close to #" + mate.slot + ", take it away");
                return false;
            }
            if (to != dir)
            {
                o.VX = to * S * (52 + R01() * 10); o.VY = -S * (155 + R01() * 20);
                o.NoHit = this; o.NoHitUntil = now + 0.75;   // 从自己身上飞过去，别撞上
                Spawn(Sprite.Puff, Sprite.PuffC, (dir > 0 ? 20 : 2) * S, 15 * S, 0, -S * 4, 0.4, 0);
                Dbg("ball: flick over head");
                return false;
            }
            if (R01() < 0.3)
            {
                o.VX = to * Clamp(0.95 * (gap - 20 * S), S * 30, S * 200); o.VY = -S * (105 + R01() * 30);   // 吊过去
            }
            else
            {
                o.VX = to * Clamp(2.4 * (gap - 20 * S), S * 45, S * 300); o.VY = -S * (15 + R01() * 25);     // 贴地滚过去
            }
            CountPass(o, mate);
            Spawn(Sprite.Puff, Sprite.PuffC, (dir > 0 ? 20 : 2) * S, 15 * S, dir * S * 6, -S * 2, 0.4, 0);
            Dbg("ball: pass to #" + mate.slot + " gap=" + (gap / S).ToString("F0") + " left=" + o.Passes);
            CallForBall(mate, o);   // 传出去了还得等它传回来（kicksLeft 上面留够了）
            return true;
        }

        // 记一脚传球（一下子连着碰好几下只算一脚）
        void CountPass(PetObj o, PetForm mate)
        {
            if (now - o.PassT > 0.3) o.Passes--;
            o.PassT = now; o.PassTo = mate;
        }

        // 球传过来了：伙伴转过来准备接
        void CallForBall(PetForm mate, PetObj o)
        {
            if (!mate.Grounded() || mate.state == St.Kick) return;
            bool inGame = mate.chaseObj == o && mate.state == St.Chase;
            if (!inGame) { mate.ResetPlans(); mate.Emo("bang", 0.8); }
            mate.StartChase(o, Math.Max(mate.kicksLeft, 2));
        }

        // 一轮传完了：接到最后一脚的停住球，再去跟传球的伙伴击个掌
        void TrapBall(PetObj o)
        {
            PetForm mate = o.LastKicker;
            o.VX *= 0.15; o.VY = -S * 30;
            o.Passes = -1; o.LastKicker = this; o.KickT = now; o.Touched = now; o.Rest = false;
            kicksLeft = 0;
            Dbg("ball: trap, rally over" + (mate != null ? ", high five #" + mate.slot : ""));
            if (mate != null && mate != this && All.Contains(mate)) { fiveAfter = mate; Emo("star", 1.0); }
        }

        void RemoveObj(PetObj o)
        {
            o.Gone = true; o.Held = false;
            if (heldObj == o) heldObj = null;
            if (chaseObj == o) chaseObj = null;
            objs.Remove(o);
            if (o.Win != null) { try { o.Win.Close(); o.Win.Dispose(); } catch { } o.Win = null; }
        }

        void DrawObj(PetObj o)
        {
            if (o.Win == null || o.Win.IsDisposed) return;
            int u = S;   // 一格和小人一样大
            if (o.Kind == "plane")
            {
                int k = o.VX < 0 ? 1 : 0;   // 往左飞就左右翻过来
                if (k != o.Frame || u != o.LastU) { o.Frame = k; o.LastU = u; o.Win.SetArt(k == 1 ? Sprite.FlipRows(Sprite.PlaneRows) : Sprite.PlaneRows, Sprite.PlaneArt, u); }
                o.Win.MoveTo((int)Math.Round(o.X - 3.5 * u), (int)Math.Round(o.Y - 4 * u));
                return;
            }
            int f = ((int)Math.Floor(o.Spin)) & 3;   // 滚起来换四个角的颜色
            if (f != o.Frame || u != o.LastU) { o.Frame = f; o.LastU = u; Art a = Sprite.BallArt(f); o.Win.SetArt(a.Rows, a, u); }
            int w = 6 * u;
            o.Win.MoveTo((int)Math.Round(o.X - w / 2.0), (int)Math.Round(o.Y - w));
        }

        bool ObjsMoving()
        {
            foreach (PetObj o in objs) if (o.Held || !o.Rest) return true;
            return false;
        }

        // 小人这一帧实际移动的速度（走、追、掉、被拎着都算），球撞上去时用
        double petVX, petVY, lastPetCX = double.NaN, lastPetY;

        void UpdObjs(double dt)
        {
            double cxNow = CX;
            if (dt > 0 && !double.IsNaN(lastPetCX))
            {
                petVX = Clamp((cxNow - lastPetCX) / dt, -S * 700, S * 700);
                petVY = Clamp((posY - lastPetY) / dt, -S * 700, S * 700);
            }
            lastPetCX = cxNow; lastPetY = posY;
            UpdPole(dt);
            for (int i = objs.Count - 1; i >= 0; i--)
            {
                PetObj o = objs[i];
                if (o.Kind == "ball") UpdBall(o, dt);
                else if (o.Kind == "plane") UpdPlaneObj(o, dt);
                if (!o.Gone) DrawObj(o);
            }
        }

        void UpdBall(PetObj o, double dt)
        {
            if (o.Held) return;
            // 球飞得快时一帧拆成几小步算，免得一下子穿进身子里
            double fast = Math.Max(Math.Abs(o.VX), Math.Abs(o.VY)) * dt;
            int n = Math.Min(6, Math.Max(1, (int)Math.Ceiling(fast / (1.5 * S))));
            double h = dt / n;
            bool onFloor = false;
            for (int i = 0; i < n && !o.Gone; i++)
            {
                onFloor = StepBall(o, h);
                if (o.Kind == "ball") { foreach (PetForm p in All) if (!o.Gone && !p.IsDisposed) p.Collide(o, onFloor); }   // 每一只都能碰到球
                else Collide(o, onFloor);
            }
            if (o.Gone) return;
            o.Spin -= o.VX * dt / (4.7 * S);
            o.Rest = o.VX == 0 && o.VY == 0 && onFloor;
            if (o.Rest && now - o.Touched > (o.Auto ? 25 : 600)) RemoveObj(o);   // 十分钟没人玩：收起来（自己拿出来的球，踢完放一会儿就收）
        }

        // 球走一小步；返回：是不是贴在地上（任务栏上面）
        bool StepBall(PetObj o, double dt)
        {
            double r = 3 * S;
            Rectangle wa = Native.WorkAt((int)o.X, (int)(o.Y - r));
            o.VY = Math.Min(o.VY + S * 520 * dt, S * 900);
            o.PrevX = o.X;
            o.X += o.VX * dt; o.Y += o.VY * dt;
            int my = (int)(o.Y - r);
            // 屏幕左右边会弹回来（那边接着别的显示器就滚过去）
            if (o.X - r < wa.Left && !Native.HasMonitorAt(wa.Left - 2, my)) { o.X = wa.Left + r; o.VX = Math.Abs(o.VX) * 0.7; }
            if (o.X + r > wa.Right && !Native.HasMonitorAt(wa.Right + 1, my)) { o.X = wa.Right - r; o.VX = -Math.Abs(o.VX) * 0.7; }
            if (o.Y - 2 * r < wa.Top && o.VY < 0) { o.Y = wa.Top + 2 * r; o.VY = -o.VY * 0.5; }
            if (o.Y >= wa.Bottom)
            {
                o.Y = wa.Bottom;   // 落在任务栏上面
                if (o.VY > S * 40)
                {
                    if (o.Kind == "ball" && o.VY > S * 90) Snd("bounce", SND_PLAY, Clamp(o.VY / (S * 350.0), 0.25, 0.9), false);
                    o.VY = -o.VY * 0.62; o.VX *= 0.92;
                }
                else { o.VY = 0; o.VX *= Math.Max(0, 1 - 3.2 * dt); if (Math.Abs(o.VX) < S * 2) o.VX = 0; }
                return o.VY == 0;
            }
            return false;
        }

        // 小人身子的碰撞框（屏幕坐标）：左右各 11.5 格，脚底到头顶（坐下、趴下时矮一点，跳起来跟着往上）
        bool PetBox(out double l, out double t, out double r, out double b)
        {
            double cx = CX, foot = posY - hop + sink * S;
            l = cx - 11.5 * S; r = cx + 11.5 * S; b = foot; t = foot - (14 - curBodyDy) * S;
            return !(hiddenForFullscreen || state == St.Climb || state == St.Burrow || state == St.Pole || dreamForm != null || t >= b - S);
        }

        // 球碰到小人：每一帧都把球推到身子外面（不会穿模）；撞得狠了它才有反应（被弹一下、追过去、被砸醒）
        // 小人跳起来顶到球 = 头球；正在追这个球就先用身子停住球、再踢出去
        void Collide(PetObj o, bool onFloor)
        {
            double l, t, r, b;
            if (o.Gone || o.Held || !PetBox(out l, out t, out r, out b)) return;
            if (o.NoHit == this && now < o.NoHitUntil) return;   // 刚从自己头顶上挑过去的球
            double br = 3 * S, bx = o.X, by = o.Y - br, cx = CX;
            double qx = Clamp(bx, l, r), qy = Clamp(by, t, b);
            double dx = bx - qx, dy = by - qy, d2 = dx * dx + dy * dy;
            if (d2 >= br * br) return;
            double nx, ny, pen;
            if (d2 > 1e-6) { double d = Math.Sqrt(d2); nx = dx / d; ny = dy / d; pen = br - d; }
            else
            {
                // 球心已经在身子里：从最近的那一边推出去（球贴着地就不往下推）
                double pl = bx - l, pr = r - bx, pt = by - t, pb = onFloor ? 1e9 : b - by;
                double m = Math.Min(Math.Min(pl, pr), Math.Min(pt, pb));
                if (m == pt) { nx = 0; ny = -1; } else if (m == pl) { nx = -1; ny = 0; } else if (m == pr) { nx = 1; ny = 0; } else { nx = 0; ny = 1; }
                pen = m + br;
            }
            if (ny > 0.3 && onFloor)   // 小人从上面压到地上的球：往旁边挤
            {
                nx = bx >= cx ? 1 : -1; ny = 0;
                pen = (11.5 * S + br) - Math.Abs(bx - cx);
            }
            bool myChase = (state == St.Chase || state == St.Match) && chaseObj == o && Grounded();
            double nbx = bx + nx * pen, nby = by + ny * pen;
            // 球被挤在小人和屏幕边中间、塞不下：从头顶上挤出去
            Rectangle wa = Native.WorkAt((int)bx, (int)by);
            if ((nbx - br < wa.Left - 1 && !Native.HasMonitorAt(wa.Left - 2, (int)by)) || (nbx + br > wa.Right + 1 && !Native.HasMonitorAt(wa.Right + 1, (int)by)))
            {
                nx = 0; ny = -1; nbx = bx; nby = t - br;
                if (myChase) myChase = false;
            }
            o.X = nbx; o.Y = nby + br;
            double pvx = petVX, pvy = petVY;
            double rel = (o.VX - pvx) * nx + (o.VY - pvy) * ny;
            if (myChase && now - lastBonk >= 0.2)
            {
                o.VX = pvx * 0.3; if (o.VY < 0) o.VY *= 0.3;
                lastBonk = now; o.Touched = now; o.Rest = false;
                StartKick(o);
                return;
            }
            if (rel < 0) { o.VX -= 1.6 * rel * nx; o.VY -= 1.6 * rel * ny; }
            o.Touched = now; o.Rest = false;
            if (now - lastBonk < 0.2) return;   // 下面这些反应别一下子来好几次
            bool air = state == St.Fall;
            if (air && velY < 0 && ny < -0.3)   // 头球！
            {
                o.VY = Math.Min(o.VY, velY * 1.1 - S * 80); o.VX += (bx - cx) * 2.5; SpawnSparkle(); lastBonk = now;
                o.LastKicker = this; o.KickT = now;
                PetForm mate = chaseObj == o && o.Passes > 0 ? PassTarget(o) : null;
                if (mate != null)   // 一起踢的时候：顶给伙伴
                {
                    int to = mate.CX >= CX ? 1 : -1;
                    o.VX = to * Clamp(0.9 * (Math.Abs(mate.CX - CX) - 20 * S), S * 35, S * 190);
                    CountPass(o, mate); kicksLeft = Math.Max(kicksLeft, 2);
                    Dbg("ball: header pass to #" + mate.slot + " left=" + o.Passes);
                    CallForBall(mate, o);
                }
                return;
            }
            if (Math.Abs(rel) > S * 120)
            {
                lastBonk = now;
                if (!Grounded()) return;
                if (Sleeping()) { Wake(true); return; }   // 睡着被球砸醒
                sqV -= 3; leanV -= (bx >= cx ? 1 : -1) * 2;
                if (state != St.Chase && state != St.Kick && state != St.Match && !InMatch() && CanPlay()) { ResetPlans(); SpawnBang(); StartChase(o, 2 + rnd.Next(3)); }
            }
        }

        // kicks < 0：接着上次剩下的脚数
        void StartChase(PetObj o, int kicks)
        {
            if (o == null || o.Gone) return;
            if (!CanPlay() && state != St.Kick && state != St.Fall && state != St.Land) { attnX = o.X; attnY = o.Y - 2 * S; attnUntil = now + 1.5; return; }
            if (stay || ccWorking) { StartIdle(); return; }   // 刚落地时发现不能玩了：正常站好（干活时会回到小电脑前）
            chaseObj = o; if (kicks >= 0) kicksLeft = kicks;
            hasWalkTarget = false; walkThen = null;
            o.Touched = now;
            SetState(St.Chase, 25);
        }

        void UpdChase(double dt)
        {
            PetObj o = chaseObj;
            if (o == null || o.Gone) { chaseObj = null; StartIdle(); return; }
            if (stT >= stDur || stay) { chaseObj = null; StartSit(); return; }
            attnX = o.X; attnY = o.Y - 2 * S; attnUntil = now + 0.4;
            ballWait = false;
            if (o.Held) { GroundMove(0, dt, 8); return; }   // 你拿着呢：站住盯着看
            if (plat != IntPtr.Zero || basePet != null) { PetObj oo = o; HopDown(o.X >= CX ? 1 : -1, () => StartChase(oo, -1)); return; }   // 站在窗口上 / 伙伴头上：先跳下去
            if (o.Kind == "ball")
            {
                PetForm mate = BallMate(o);
                if (mate != null && ShouldWait(o, mate)) { WaitForPass(o, mate, dt); return; }   // 让伙伴先踢，我等着接
            }
            double cx = CX, dx = o.X - cx, adx = Math.Abs(dx);
            if (adx > 2 * S) facing = dx > 0 ? 1 : -1;
            Rectangle wa = WorkArea();
            bool low = o.Y > wa.Bottom - 8 * S;
            if (o.Kind == "ball" && All.Count > 1 && low && adx > 16 * S && Math.Abs(o.VX) > S * 25 && (o.VX > 0) == (dx < 0))
            {
                // 球正朝我滚过来：站好等它到脚边，别迎上去（不然两只一下就挤到一块儿了）
                ballWait = true; GroundMove(0, dt, 8);
                return;
            }
            if (adx < 11 * S && low) { StartKick(o); return; }
            if (adx < 14 * S && !low && o.VY > -S * 40 && o.Y < posY - 12 * S && o.Y > posY - 40 * S)
            {
                PetObj oo = o;
                afterLandThen = () => { if (!oo.Gone && kicksLeft > 0) StartChase(oo, -1); else StartIdle(); };
                Jump(Mood.Happy, S * 100, false);   // 跳起来顶头球
                return;
            }
            GroundMove(adx > 3 * S ? facing * Math.Min(S * 42, adx * 4) : 0, dt, 8);
            double lo, hi, c2 = CX;
            SurfaceSpan(out lo, out hi);
            if (c2 < lo || c2 > hi) { SetCX(Clamp(c2, lo, hi)); gvx = 0; }
        }

        double kickAt = 0.1;   // 进入踢球状态多久后脚碰到球
        void StartKick(PetObj o)
        {
            chaseObj = o; facing = o.X >= CX ? 1 : -1; kicked = false;
            kickAt = 0.1;
            // 伙伴传过来的球：先停一下、看一眼，再传回去（不然像打乒乓球）
            if (o.Kind == "ball" && All.Count > 1 && o.PassTo == this && now - o.PassT < 4) kickAt = 0.35 + R01() * 0.3;
            SetState(St.Kick, kickAt + 0.22);
        }

        void UpdKick()
        {
            PetObj o = chaseObj;
            if (!kicked && stT >= kickAt)
            {
                kicked = true;
                // 球贴在身子边上时离中心 14.5 格（身子连手臂左右各 11.5 格 + 球半径 3 格），留点余量
                if (o != null && !o.Gone && !o.Held && Math.Abs(o.X - CX) < 18 * S)
                {
                    Snd("kick", SND_PLAY, 0.75, false);
                    int dir = o.X >= CX ? 1 : -1;
                    PetForm mate = o.Kind == "ball" && o.Passes > 0 && !o.MatchBall ? PassTarget(o) : null;
                    if (mate != null) PassBall(o, mate, dir);   // 一起踢：传给伙伴
                    else if (o.Kind == "ball" && o.Passes == 0 && o.LastKicker != null && o.LastKicker != this && now - o.KickT < 8) TrapBall(o);   // 最后一脚：停住球
                    else
                    {
                        bool big = R01() < 0.25;   // 大多是轻轻带一下，偶尔来一脚大的
                        o.VX = dir * S * (big ? 80 + R01() * 50 : 30 + R01() * 40);
                        o.VY = -S * (big ? 90 + R01() * 60 : 30 + R01() * 50);
                        o.Touched = now; o.Rest = false; o.LastKicker = this; o.KickT = now;
                        Spawn(Sprite.Puff, Sprite.PuffC, (dir > 0 ? 20 : 2) * S, 15 * S, dir * S * 6, -S * 2, 0.4, 0);
                        kicksLeft--;
                    }
                    energy = Math.Min(1, energy + 0.02);
                }
            }
            if (stT >= stDur && InMatch()) { SetState(St.Match, 1e9); return; }   // 比赛里：踢完接着跑
            if (stT >= stDur)
            {
                PetForm five = fiveAfter; fiveAfter = null;
                if (five != null && All.Contains(five) && five.Grounded() && !five.Sleeping() && five.basePet == null && !stay)
                {
                    // 传完一轮：两只都停下来，走过去击个掌
                    chaseObj = null;
                    if (five.chaseObj == o) { five.ResetPlans(); five.SetState(St.Idle, 1); }
                    HighFiveWith(five);
                }
                else if (o != null && !o.Gone && kicksLeft > 0) StartChase(o, -1);
                else { chaseObj = null; if (R01() < 0.5) { StartHappy(0.9); Emo("note", 1.2); } else StartSit(); }
            }
        }

        // 用鼠标拿球：左键按住拖着走、松手扔出去；只点一下球会弹起来；右键收起来
        void GrabObj(PetObj o, MouseButtons b)
        {
            if (o.Gone) return;
            if (b == MouseButtons.Right) { RemoveObj(o); return; }
            if (b != MouseButtons.Left) return;
            heldObj = o; o.Held = true; o.VX = o.VY = 0; o.Rest = false; o.Touched = now;
            Point c = Cursor.Position;
            objGrabDX = c.X - o.X; objGrabDY = c.Y - o.Y;
            o.TX = c.X; o.TY = c.Y; o.NextT = now;   // 记下按下去的位置和时间：分辨「点一下」还是「拖」
            objTrail.Clear();
            objTrail.Add(new double[] { clock.Elapsed.TotalSeconds, c.X, c.Y });
            HideBar();
        }

        void UpdHeldObj()
        {
            PetObj o = heldObj;
            if (o == null || o.Gone) { heldObj = null; return; }
            Point c = Cursor.Position;
            double t = clock.Elapsed.TotalSeconds;
            o.X = c.X - objGrabDX; o.Y = c.Y - objGrabDY;
            double[] last = objTrail.Count > 0 ? objTrail[objTrail.Count - 1] : null;
            if (last == null || last[1] != c.X || last[2] != c.Y) objTrail.Add(new double[] { t, c.X, c.Y });
            if ((Control.MouseButtons & MouseButtons.Left) == 0) ReleaseObj(o);
            else while (objTrail.Count > 2 && t - objTrail[0][0] > 0.09) objTrail.RemoveAt(0);
        }

        void ReleaseObj(PetObj o)
        {
            heldObj = null; o.Held = false; o.Touched = now; o.Rest = false;
            double t = clock.Elapsed.TotalSeconds, vx = 0, vy = 0;
            Point c = Cursor.Position;
            bool tap = Math.Abs(c.X - o.TX) + Math.Abs(c.Y - o.TY) < 5 && now - o.NextT < 0.4;
            if (objTrail.Count >= 2)
            {
                double[] a = objTrail[0], z = objTrail[objTrail.Count - 1];
                double span = z[0] - a[0];
                if (span > 0.01 && t - z[0] < 0.08) { vx = (z[1] - a[1]) / span; vy = (z[2] - a[2]) / span; }
            }
            double max = S * 700;
            o.VX = Clamp(vx, -max, max); o.VY = Clamp(vy, -max, max);
            if (tap)
            {
                o.VY = -S * (110 + R01() * 40); o.VX = (R01() - 0.5) * S * 30;   // 只是点了一下：球弹起来
            }
            if (Math.Sqrt(o.VX * o.VX + o.VY * o.VY) > S * 100 && CanPlay()) { ResetPlans(); Emo("bang", 0.8); StartChase(o, 2 + rnd.Next(4)); }
        }

        // ================================================================ 菜单 / 设置
        // 菜单项：统一留出上下空隙，看起来不那么像系统菜单
        ToolStripMenuItem Mi(string text, EventHandler click)
        {
            ToolStripMenuItem it = new ToolStripMenuItem(text, null, click);
            it.Padding = new Padding(0, (int)Math.Round(3 * dpiScale), 0, (int)Math.Round(3 * dpiScale));
            return it;
        }

        void BuildMenu()
        {
            // Clawd 自己的样子：奶油色底、深色像素描边、暖色高亮、橙色勾；托盘图标用的也是这个菜单
            int pu = ClawdUi.Px(dpiScale), d2 = (int)Math.Round(2 * dpiScale), d4 = (int)Math.Round(4 * dpiScale);
            menu.Renderer = new ClawdMenuRenderer(dpiScale);
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = true;
            menu.Font = new Font("Microsoft YaHei UI", 9.5f);
            menu.BackColor = ClawdUi.Bg;
            menu.ForeColor = ClawdUi.Text;
            menu.Padding = new Padding(pu + d2, pu + d4, pu + d2, pu + d4);
            menu.SizeChanged += (s, e) => { if (menu.Width > 0 && menu.Height > 0) menu.Region = ClawdUi.StepRegion(menu.Size, pu); };
            menu.Closing += (s, e) =>
            {
                // 点在一排小按钮的空白处：先别关
                if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked && ChipRowItem.KeepOpenNow()) e.Cancel = true;
            };

            // 顶层只放常用的；番茄钟 / 提醒、各种开关收进两个子菜单
            playRow = new ChipRowItem("陪它玩", dpiScale);
            friendRow = new ChipRowItem("一起玩", dpiScale);
            FillPlayRow();
            menu.Items.Add(playRow);
            menu.Items.Add(friendRow);
            miSleep = Mi("睡一会儿", (s, e) =>
            {
                if (Sleeping()) Wake(false);
                else if (Grounded()) GoToBed(SleepMode.Nap, 90 + R01() * 60);
            });
            menu.Items.Add(miSleep);
            miDream = Mi("做个梦（小游戏）", (s, e) => OpenDream());
            menu.Items.Add(miDream);
            miStopWork = Mi("别忙了（清掉 Claude 会话状态）", (s, e) =>
            {
                sessions.Clear(); ccWorking = false;
                if (state == St.Think) StartIdle();
            });
            menu.Items.Add(miStopWork);
            menu.Items.Add(new ToolStripSeparator());

            miStay = Mi("待在这儿别动", (s, e) => SetStay(!stay));
            menu.Items.Add(miStay);

            // 子菜单：番茄钟和提醒
            miTimers = Mi("番茄钟和提醒", null);
            StyleSub(miTimers);
            pomoRow = new ChipRowItem("番茄钟", dpiScale);   // 按钮在打开菜单时按现在的状态重排
            miTimers.DropDownItems.Add(pomoRow);
            miRemind = Mi("提醒我…", (s, e) => AskReminder());
            miTimers.DropDownItems.Add(miRemind);
            miClearRemind = Mi("清空提醒", (s, e) => { reminders.Clear(); Say("提醒都清掉了", 2); });
            miTimers.DropDownItems.Add(miClearRemind);
            menu.Items.Add(miTimers);

            // 子菜单：设置
            miSettings = Mi("设置", null);
            StyleSub(miSettings);
            miFollow = Mi("跟着鼠标走", (s, e) => { follow = !follow; SaveSettings(); });
            miChatty = Mi("偶尔自言自语", (s, e) => { chatty = !chatty; SaveSettings(); });
            miHealth = Mi("健康提醒（久坐 / 电量 / CPU）", (s, e) => { health = !health; SaveSettings(); });
            sizeRow = new ChipRowItem("大小", dpiScale);
            for (int i = 0; i < SizeNames.Length; i++)
            {
                int idx = i;
                sizeRow.AddChip(SizeNames[i], () =>
                {
                    foreach (PetForm f in All.ToArray()) { f.sizeIdx = idx; f.ApplySize(true); f.Location = f.FormPos(); f.sqV += 5; }   // 几只一起变
                    SaveSettings();
                });
            }
            sizeRow.IsOn = i => i == sizeIdx;   // 现在的大小画成橙色
            miSettings.DropDownItems.Add(sizeRow);
            miSettings.DropDownItems.Add(miFollow);
            miSettings.DropDownItems.Add(miChatty);
            miTalk = Mi("它们聊天时冒字（关掉就只比划表情）", (s, e) => { petTalk = !petTalk; SaveSettings(); });
            miSettings.DropDownItems.Add(miTalk);
            miSettings.DropDownItems.Add(miHealth);
            // 晚上自动睡觉：开关 + 几点睡 / 几点起（关掉、或者改了时间现在不算夜里了，睡着的会陆续起来）
            miNight = Mi("晚上自动睡觉", (s, e) => { nightSleep = !nightSleep; SaveSettings(); RefreshNightItems(); });
            bedRow = new ChipRowItem("几点睡", dpiScale);
            for (int i = 0; i < BedHours.Length; i++) { int h = BedHours[i]; bedRow.AddChip(h + ":00", () => { bedHour = h; SaveSettings(); RefreshNightItems(); }); }
            bedRow.IsOn = i => BedHours[i] == bedHour;
            wakeRow = new ChipRowItem("几点起", dpiScale);
            for (int i = 0; i < WakeHours.Length; i++) { int h = WakeHours[i]; wakeRow.AddChip(h + ":00", () => { wakeHour = h; SaveSettings(); RefreshNightItems(); }); }
            wakeRow.IsOn = i => WakeHours[i] == wakeHour;
            miSettings.DropDownItems.Add(miNight);
            miSettings.DropDownItems.Add(bedRow);
            miSettings.DropDownItems.Add(wakeRow);
            // 音效：一个总开关（勾上全开、去掉全关）；开着的时候下面多一排挑音量：轻 / 中 / 响（点一下就「啵」一声，听听有多响）
            miSfx = Mi("音效", (s, e) => { Sfx.On = !Sfx.On; SaveSettings(); if (Sfx.On) Sfx.Play("boop", 1, VoicePitch()); });
            sfxRow = new ChipRowItem("音量", dpiScale);
            for (int i = 0; i < Sfx.VolNames.Length; i++)
            {
                int lv = i + 1;
                sfxRow.AddChip(Sfx.VolNames[i], () => { Sfx.Level = lv; SaveSettings(); sfxRow.Invalidate(); Sfx.Play("boop", 1, VoicePitch()); });
            }
            sfxRow.IsOn = i => i + 1 == Sfx.Level;
            miSettings.DropDownItems.Add(miSfx);
            miSettings.DropDownItems.Add(sfxRow);
            miAuto = Mi("开机自动启动", (s, e) => SetAutoStart(!GetAutoStart()));
            miSettings.DropDownItems.Add(miAuto);
            miSettings.DropDownItems.Add(new ToolStripSeparator());
            miSettings.DropDownItems.Add(Mi("叫到鼠标这儿来", (s, e) => Summon()));
            menu.Items.Add(miSettings);
            menu.Items.Add(new ToolStripSeparator());
            miAddPet = Mi("再叫一只 Clawd（会一起玩、叠高高）", (s, e) => AddPet(true));
            menu.Items.Add(miAddPet);
            miDismiss = Mi("送走这只", (s, e) => Dismiss());
            menu.Items.Add(miDismiss);
            menu.Items.Add(Mi("退出", (s, e) => ExitApp()));
            menu.Opening += (s, e) => RefreshMenu();
        }

        ToolStripMenuItem miNight, miSfx; ChipRowItem bedRow, wakeRow, sfxRow;

        void RefreshNightItems()
        {
            if (miNight == null) return;
            miNight.Checked = nightSleep;
            miNight.Text = nightSleep ? "晚上自动睡觉（" + bedHour + ":00 睡，" + wakeHour + ":00 起）" : "晚上自动睡觉";
            bedRow.Visible = wakeRow.Visible = nightSleep;
            bedRow.Invalidate(); wakeRow.Invalidate();
        }

        // 打开菜单前：按现在的状态改文字、勾选、显示哪些项
        void RefreshMenu()
        {
            {
                HideBar();
                ChipRowItem.KeepOpenT = -10000;
                miSleep.Text = Sleeping() ? "叫醒它" : "睡一会儿";
                miDream.Text = state == St.Sleep ? "钻进它的梦里玩" : "做个梦（小游戏）";
                miStopWork.Visible = ccWorking;
                FillPlayRow();
                pomoRow.ClearChips();
                pomoRow.AddChip("专注 25", () => StartPomo(1, 25, true));
                pomoRow.AddChip("专注 50", () => StartPomo(1, 50, true));
                pomoRow.AddChip("休息 5", () => StartPomo(2, 5, true));
                if (pomoMode != 0) pomoRow.AddChip("停止", () => { pomoMode = 0; dirty = true; Say("好，计时停了", 2); });
                pomoRow.Invalidate();
                miRemind.Text = reminders.Count > 0 ? "提醒我…（已有 " + reminders.Count + " 个）" : "提醒我…";
                miClearRemind.Visible = reminders.Count > 0;
                miClearRemind.Text = "清空提醒（" + reminders.Count + " 个）";
                // 子菜单收起来了：正在计时 / 有提醒的话在外面标一下
                string tm = pomoMode == 1 ? "专注中" : pomoMode == 2 ? "休息中" : "";
                if (reminders.Count > 0) tm += (tm.Length > 0 ? "，" : "") + reminders.Count + " 个提醒";
                miTimers.Text = tm.Length > 0 ? "番茄钟和提醒（" + tm + "）" : "番茄钟和提醒";
                miTimers.DropDown.PerformLayout();
                miStay.Checked = stay;
                miFollow.Checked = follow;
                miFollow.Enabled = !stay;
                miChatty.Checked = chatty;
                miHealth.Checked = health;
                RefreshNightItems();
                miSfx.Checked = Sfx.On;
                sfxRow.Visible = Sfx.On;   // 音效关着就不显示音量那一排
                sfxRow.Invalidate();
                NightTouched();   // 夜里你来找它（开菜单）：先别回去睡
                miAuto.Checked = GetAutoStart();
                miAddPet.Visible = All.Count < MAX_PETS;
                miAddPet.Text = All.Count <= 1 ? "再叫一只 Clawd（会一起玩、叠高高）" : "再叫一只 Clawd（现在 " + All.Count + " 只）";
                miTalk.Visible = All.Count > 1;
                miTalk.Checked = petTalk;
                miDismiss.Visible = !Main;
                miDismiss.Text = "送走这只（" + AccName(AccOf(slot)) + "）";
                sizeRow.Invalidate();
                miSettings.DropDown.PerformLayout();
                menu.PerformLayout();   // 番茄钟那一排按钮个数可能变了：重新量宽度
            }
        }

        // 调试用（CLAWD_DEBUG=1 时的 --signal menushot）：不弹出来（弹出来会抢键盘），直接把右键菜单和两个子菜单画成图，看排版
        void MenuShot()
        {
            try
            {
                RefreshMenu();
                ShotStrip(menu, "menu.png");
                ShotStrip(miTimers.DropDown, "menu-timers.png");
                ShotStrip(miSettings.DropDown, "menu-settings.png");
                Dbg("menushot saved");
            }
            catch (Exception ex) { Dbg("menushot failed: " + ex.Message); }
        }

        void BarShot()
        {
            try
            {
                ShowBar(6);
                if (!barOn) { Dbg("barshot: bar not shown"); return; }
                LayoutBar(); barHot = BAR_DREAM;
                using (Bitmap b = new Bitmap(ClientSize.Width, ClientSize.Height))
                {
                    DrawToBitmap(b, new Rectangle(Point.Empty, ClientSize));
                    b.Save(Path.Combine(Signal.Dir(), "bar.png"));
                }
                Dbg("barshot saved form=" + ClientSize.Width + "x" + ClientSize.Height + " S=" + S + " uiS=" + uiS + " bar=" + barRect + " off=" + offX + "," + offY);
            }
            catch (Exception ex) { Dbg("barshot failed: " + ex.Message); }
        }

        void ShotStrip(ToolStripDropDown dd, string name)
        {
            Size sz = dd.GetPreferredSize(Size.Empty);
            dd.Size = sz;
            dd.PerformLayout();
            using (Bitmap b = new Bitmap(sz.Width, sz.Height))
            {
                dd.DrawToBitmap(b, new Rectangle(Point.Empty, sz));
                b.Save(Path.Combine(Signal.Dir(), name));
            }
        }

        // 子菜单也用 Clawd 自己的样子（不然弹出来是系统默认的灰菜单）
        void StyleSub(ToolStripMenuItem mi)
        {
            int pu = ClawdUi.Px(dpiScale);
            ToolStripDropDownMenu dd = (ToolStripDropDownMenu)mi.DropDown;
            dd.Renderer = menu.Renderer;
            dd.ShowImageMargin = false;
            dd.ShowCheckMargin = true;
            dd.Font = menu.Font;
            dd.BackColor = ClawdUi.Bg;
            dd.ForeColor = ClawdUi.Text;
            dd.Padding = menu.Padding;
            dd.SizeChanged += (s, e) => { if (dd.Width > 0 && dd.Height > 0) dd.Region = ClawdUi.StepRegion(dd.Size, pu); };
            dd.Closing += (s, e) => { if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked && ChipRowItem.KeepOpenNow()) e.Cancel = true; };
        }

        void StartPomo(int mode, int minutes, bool announce)
        {
            pomoMode = mode;
            pomoEnd = DateTime.Now.AddMinutes(minutes);
            lastPomoSecs = -1;
            dirty = true;
            if (!announce) return;
            if (mode == 1)
            {
                Say("开始专注 " + minutes + " 分钟！我不吵你~", 3);
                if (Grounded()) { ResetPlans(); Jump(Mood.Happy, S * 70, true); }
            }
            else Say("休息 " + minutes + " 分钟，起来走走~", 3);
        }

        void AskReminder()
        {
            using (ReminderDialog d = new ReminderDialog(dpiScale))
            {
                if (d.ShowDialog() != DialogResult.OK) return;
                int mins = (int)d.Minutes.Value;
                string text = d.Message.Text.Trim();
                if (text.Length == 0) text = "时间到啦！";
                Reminder r = new Reminder();
                r.Due = DateTime.Now.AddMinutes(mins);
                r.Text = text;
                reminders.Add(r);
                Say("好的，" + mins + " 分钟后提醒你~", 2.5);
                if (Grounded()) { ResetPlans(); Jump(Mood.Happy, S * 60, true); }
            }
        }

        void ShowMenu(Point at)
        {
            Native.SetForegroundWindow(Handle);
            menu.Show(at);
        }

        // 退出：所有 Clawd 一起走
        void ExitApp()
        {
            SavePositions(true);
            foreach (PetForm f in All.ToArray()) f.Cleanup();
            foreach (PetForm f in All.ToArray()) { try { f.tray.Visible = false; f.tray.Dispose(); } catch { } }
            Application.Exit();
        }

        // 收拾这一只的东西（梦、球、木杆）
        void Cleanup()
        {
            if (match != null && (match.Host == this || InMatch())) EndMatch(false);   // 在踢比赛：拆掉球场
            timer.Stop();
            if (dreamForm != null) { try { DreamForm d = dreamForm; dreamForm = null; d.Close(); } catch { } }
            foreach (PetObj o in objs) { o.Gone = true; try { o.Win.Close(); } catch { } }   // Gone：别的 Clawd 就不会再追这个球
            objs.Clear();
            RemovePole();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            tray.Visible = false;
            All.Remove(this);
            base.OnFormClosed(e);
        }

        bool GetAutoStart()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue("ClawdPet") != null;
            }
            catch { return false; }
        }

        void SetAutoStart(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue("ClawdPet", "\"" + Application.ExecutablePath + "\"");
                    else k.DeleteValue("ClawdPet", false);
                }
                Say(on ? "以后开机我就来陪你~" : "好的，不自动启动了", 2.5);
            }
            catch (Exception ex) { Say("设置失败：" + ex.Message, 3); }
        }

        string SettingsFile() { return Path.Combine(Signal.Dir(), "settings.ini"); }

        // ---------------- 记住每只站在哪（每 20 秒、退出时存一次；程序被直接关掉也只丢最后几秒）
        static bool restoring = true;          // 刚打开：按上次的位置摆
        static double nextPosSave = 20;
        static string lastPosText;
        static Dictionary<int, Point> savedPos;

        static string PosFile() { return Path.Combine(Signal.Dir(), "positions.ini"); }

        static bool SavedPos(int slot, out Point p)
        {
            p = Point.Empty;
            if (savedPos == null)
            {
                savedPos = new Dictionary<int, Point>();
                try
                {
                    if (File.Exists(PosFile()))
                        foreach (string line in File.ReadAllLines(PosFile()))
                        {
                            string[] kv = line.Split('='), xy = kv.Length == 2 ? kv[1].Split(',') : new string[0];
                            int s, x, y;
                            if (xy.Length == 2 && int.TryParse(kv[0], out s) && int.TryParse(xy[0], out x) && int.TryParse(xy[1], out y)) savedPos[s] = new Point(x, y);
                        }
                }
                catch { }
            }
            if (!savedPos.TryGetValue(slot, out p)) return false;
            return Native.HasMonitorAt(p.X, p.Y - 2);   // 那块屏幕还在才用
        }

        // 存每只脚底的位置（屏幕坐标）；没怎么动就不写文件
        static void SavePositions(bool force)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                foreach (PetForm f in All)
                    if (!f.IsDisposed) sb.Append(f.slot).Append('=').Append((int)Math.Round(f.CX / 8) * 8).Append(',').Append((int)Math.Round(f.posY)).Append("\r\n");
                string text = sb.ToString();
                if (!force && text == lastPosText) return;
                lastPosText = text;
                File.WriteAllText(PosFile(), text);
            }
            catch { }
        }

        void LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsFile())) return;
                foreach (string line in File.ReadAllLines(SettingsFile()))
                {
                    int i = line.IndexOf('=');
                    if (i < 0) continue;
                    string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                    int n;
                    if (k == "size" && int.TryParse(v, out n) && n >= 0 && n < SizeCells.Length) sizeIdx = n;
                    else if (k == "follow") follow = v == "1";
                    else if (k == "chatty") chatty = v == "1";     // 老版本的 quiet= 不再使用：默认就不自言自语
                    else if (k == "stay") stay = v == "1";
                    else if (k == "health") health = v != "0";
                    else if (k == "greeted") greeted = v == "1";
                    else if (k == "pets" && int.TryParse(v, out n) && n >= 1 && n <= MAX_PETS) savedPets = n;
                    else if (k == "talk") petTalk = v != "0";
                    else if (k == "music") DreamForm.MusicOn = v != "0";
                    else if (k == "night") nightSleep = v != "0";
                    else if (k == "bedtime" && int.TryParse(v, out n) && Array.IndexOf(BedHours, n) >= 0) bedHour = n;
                    else if (k == "waketime" && int.TryParse(v, out n) && Array.IndexOf(WakeHours, n) >= 0) wakeHour = n;
                    else if (k == "sfx" && int.TryParse(v, out n) && n >= 0 && n <= Sfx.VolNames.Length) { if (n == 0) Sfx.On = false; else Sfx.Level = n; }   // 旧版的「关」是 sfx=0
                    else if (k == "sfxon") Sfx.On = v == "1";
                }
            }
            catch { }
        }

        // 设置文件只有一份：跟着 / 待着 / 自言自语这些按第一只的存，再记下现在有几只
        void SaveSettings()
        {
            try
            {
                PetForm m = All.Count > 0 ? All[0] : this;
                int pets = Math.Max(1, All.Count);
                File.WriteAllText(SettingsFile(), "size=" + sizeIdx + "\r\nfollow=" + (m.follow ? 1 : 0) + "\r\nchatty=" + (m.chatty ? 1 : 0)
                    + "\r\nstay=" + (m.stay ? 1 : 0) + "\r\nhealth=" + (m.health ? 1 : 0) + "\r\ngreeted=" + (m.greeted ? 1 : 0)
                    + "\r\npets=" + pets + "\r\ntalk=" + (petTalk ? 1 : 0) + "\r\nmusic=" + (DreamForm.MusicOn ? 1 : 0)
                    + "\r\nnight=" + (nightSleep ? 1 : 0) + "\r\nbedtime=" + bedHour + "\r\nwaketime=" + wakeHour + "\r\nsfx=" + Sfx.Level + "\r\nsfxon=" + (Sfx.On ? 1 : 0) + "\r\n");
            }
            catch { }
        }

        // ================================================================ Claude Code 信号
        DateTime lastQueueStamp;
        double nextQueueScan;

        void PollSignals()
        {
            // 先看目录修改时间，没变就不枚举文件；不过每 2 秒还是老老实实扫一遍
            // （信号是先写 .tmp 再改名的：要是刚好在这两步中间看了一眼，又碰上两步落在同一个时钟刻度里，目录时间就不会再变，那个信号会一直躺着）
            try
            {
                DateTime stamp = Directory.GetLastWriteTimeUtc(queueDir);
                if (stamp == lastQueueStamp && now < nextQueueScan) return;
                lastQueueStamp = stamp;
                nextQueueScan = now + 2;
            }
            catch { return; }
            string[] files;
            try { files = Directory.GetFiles(queueDir, "*.sig"); } catch { return; }
            if (files.Length == 0) return;
            Array.Sort(files, StringComparer.Ordinal);
            foreach (string f in files)
            {
                string body;
                try { body = File.ReadAllText(f, Encoding.UTF8); File.Delete(f); } catch { continue; }
                string[] p = body.Split('\n');
                HandleSignal(p[0].Trim(), p.Length > 1 ? p[1].Trim() : "", p.Length > 2 ? p[2].Trim() : "", p.Length > 3 ? p[3].Trim() : "");
            }
        }

        string NoticeText(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return Pick(AttentionLines);
            Match m = Regex.Match(msg, "permission to use (.+)$", RegexOptions.IgnoreCase);
            if (m.Success) return "Claude 想用 " + m.Groups[1].Value.Trim().TrimEnd('.') + "，等你批准~";
            if (msg.IndexOf("permission", StringComparison.OrdinalIgnoreCase) >= 0) return "Claude 需要你批准一下~";
            if (msg.IndexOf("waiting for your input", StringComparison.OrdinalIgnoreCase) >= 0) return "Claude 在等你回复~";
            return msg.Length > 40 ? msg.Substring(0, 40) + "…" : msg;
        }

        void HandleSignal(string cmd, string sid, string project, string msg)
        {
            if (sid.Length == 0) sid = "default";
            switch (cmd)
            {
                case "thinking": case "working": case "start": case "prompt":
                    {
                        CcSession s;
                        if (!sessions.TryGetValue(sid, out s)) { s = new CcSession(); s.Start = now; sessions[sid] = s; }
                        s.Last = now;
                        if (project.Length > 0) s.Project = project;
                        ccWorking = true;
                        if (NightNow() && Sleeping()) break;   // 夜里睡着呢：不叫醒（醒来以后还在干活的话，就接着去敲电脑）
                        StartOffice();   // 四只都在：摆开一排小工位，大家一起上班
                        // 坐到小电脑后面敲键盘，头上是 Claude 风格的转圈气泡，一直到 done（站在伙伴头上的话先跳下来；有工位就去工位）
                        if (basePet != null && !Sleeping()) { ResetPlans(); HopDown(0, WorkNow); }
                        else if (Sleeping() || (Grounded() && !(state == St.Think && thinkPersistent && (atDesk || DeskIdx() < 0)) && state != St.Alert)) { ResetPlans(); WorkNow(); }
                        else if (state == St.Climb) KickOff(false);   // 在墙上：先跳下来，落地后坐到小电脑后面
                    }
                    break;
                case "done": case "stop": case "finished":
                    {
                        CcSession s;
                        double dur = -1;
                        string proj = project;
                        if (sessions.TryGetValue(sid, out s))
                        {
                            dur = now - s.Start;
                            if (proj.Length == 0) proj = s.Project;
                            sessions.Remove(sid);
                        }
                        ccWorking = sessions.Count > 0;
                        string text = (proj.Length > 0 ? proj + " " : "") + Pick(DoneLines);
                        if (dur >= 10) text += "（用时 " + FormatDur(dur) + "）";
                        if (NightNow() && Sleeping()) { blushUntil = now + 2.5; Emo("note", 1.4); break; }   // 夜里睡着呢：在梦里笑一下，不起来
                        Snd("done", SND_NEWS);
                        if (state == St.Climb) KickOff(false);   // 在墙上：先跳下来，气泡跟着
                        if (Grounded()) { ResetPlans(); Celebrate(dur >= 0, text); } else Say(text, 3.5);
                        // 伙伴们也跟着蹦一下
                        foreach (PetForm f in All)
                            if (f != this && f.Grounded() && !f.Sleeping() && f.basePet == null && f.Rider() == null)
                            { f.ResetPlans(); f.Emo("star", 1.2); f.Jump(Mood.Happy, f.S * (55 + f.R01() * 25), true); }
                    }
                    break;
                case "addpet":   // ClawdPet.exe --signal addpet：再叫一只
                    AddPet(true);
                    break;
                case "down":     // 站在窗口上 / 伙伴头上的都跳下来（穿过脚下的标题栏落到地上）
                    foreach (PetForm f in All.ToArray())
                        if ((f.plat != IntPtr.Zero || f.basePet != null) && f.Grounded() && !f.stay)
                        {
                            if (f.Sleeping()) f.Wake(false);
                            f.ResetPlans();
                            if (f.basePet != null) f.HopDown(0, null); else f.Jump(Mood.Happy, f.S * 35, true, 0, true);
                        }
                    break;
                case "stack": case "five":   // 让离第一只最近的伙伴跳到它头上 / 过来击掌（测试用）
                    {
                        PetForm f = NearestFriend(5000);
                        if (f == null) { AddPet(true); break; }
                        if (!f.Grounded() || f.basePet != null || f.stay) break;
                        if (f.Sleeping()) f.Wake(false);
                        f.ResetPlans();
                        if (cmd == "stack") { lastStackT = -100; f.StackOnto(this); } else f.HighFiveWith(this);
                    }
                    break;
                case "attention": case "notify": case "notification": case "permission":
                    {
                        string text = NoticeText(msg);
                        if (project.Length > 0) text = project + "：" + text;
                        if (NightNow() && Sleeping()) { Say(text, 6); break; }   // 夜里睡着呢：说梦话把提醒冒出来，不起来
                        Snd("alert", SND_NEWS);
                        if (state == St.Climb) KickOff(false);
                        if (Grounded()) { ResetPlans(); StartAlert(text, 8, false); } else Say(text, 6);
                    }
                    break;
                case "end":
                    sessions.Remove(sid);
                    ccWorking = sessions.Count > 0;
                    break;
                case "hello": case "summon":
                    Summon();
                    break;
                case "ball":   // ClawdPet.exe --signal ball：扔个球 / 收起球
                    ToggleBall();
                    break;
                case "soccer": case "kickabout":   // ClawdPet.exe --signal soccer：大家一起踢球（睡着的也叫起来）
                    foreach (PetForm f in All) if (f.Sleeping() && !f.stay) f.Wake(false);
                    KickTogether();
                    break;
                case "chat":   // ClawdPet.exe --signal chat：叫离得最近的伙伴过来聊几句
                    ChatNow();
                    break;
                case "match":  // ClawdPet.exe --signal match：摆开小足球场踢比赛（在踢就结束）
                    MatchNow();
                    break;
                case "dismiss":   // ClawdPet.exe --signal dismiss：送走最后叫出来的那只
                    {
                        PetForm last = null;
                        foreach (PetForm f in All) if (!f.Main) last = f;
                        if (last != null) last.Dismiss();
                    }
                    break;
                case "menushot":   // 调试用：把右键菜单画成图（只在 CLAWD_DEBUG=1 时）
                    if (debugLog) MenuShot();
                    break;
                case "barshot":    // 调试用：冒出按钮条、当鼠标停在「做个梦」上，把整个窗口画成图（只在 CLAWD_DEBUG=1 时）
                    if (debugLog) BarShot();
                    break;
                case "dizzy":  // 测试用：第一只晕一下，看伙伴会不会跑过来安慰
                    if (Grounded()) { if (Sleeping()) Wake(false); ResetPlans(); SpawnSweat(); StartDizzy(2.2); }
                    break;
                case "bed":    // ClawdPet.exe --signal bed：都去右下角排队盖被子睡觉（菜单「一起玩 → 一起睡」）
                    BedTimeAll();
                    break;
                case "pole":   // 找个窗口架木杆爬上去（站在窗口上就先跳下来；正忙着就记着，忙完再去）
                    if (stay) { Emo("q", 1.2); break; }
                    if (Sleeping()) Wake(false);
                    wantPole = true;
                    if (Grounded()) { ResetPlans(); TryWantPole(); }
                    break;
                // 马上做某个待机动作
                case "read": case "guitar": case "music": case "bubbles": case "lift": case "sweep": case "castle": case "plane":
                    if (!Grounded() || Sleeping()) break;
                    ResetPlans();
                    if (cmd == "read") StartRead(); else if (cmd == "guitar") StartGuitar(); else if (cmd == "music") StartMusic();
                    else if (cmd == "bubbles") StartBubbles(); else if (cmd == "lift") StartLift(); else if (cmd == "sweep") StartSweep();
                    else if (cmd == "castle") StartCastle(); else StartPlane();
                    break;
            }
        }
    }
}
