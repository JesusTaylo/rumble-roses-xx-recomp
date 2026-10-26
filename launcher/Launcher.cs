// Rumble Roses XX (PC recomp) - graphical launcher, game-menu style.
// Contains NO game data or game code: it runs setup.ps1 / jugar.bat that sit next to it and edits rrxx.toml.
// Embedded: splash.png (fan artwork background, (c) Konami / Yuke's) and the OFL fonts Saira Condensed.
// Build: launcher\build.bat (uses the C# 5 compiler that ships with Windows; no SDK needed).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

[assembly: AssemblyTitle("Rumble Roses XX Launcher")]
[assembly: AssemblyProduct("Rumble Roses XX PC recomp launcher")]
[assembly: AssemblyDescription("Fan-made, non-profit launcher. Runs setup.ps1 and jugar.bat. Contains no game data.")]
[assembly: AssemblyVersion("0.2.0.0")]

static class Program
{
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    [STAThread]
    static void Main()
    {
        try { SetProcessDPIAware(); } catch { }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}

// ---------------------------------------------------------------------------------------------
// XInput (gamepad) - polled, no dependencies besides the OS DLL.
// ---------------------------------------------------------------------------------------------
static class Pad
{
    [StructLayout(LayoutKind.Sequential)]
    struct State { public uint Packet; public ushort Buttons; public byte LT, RT; public short LX, LY, RX, RY; }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")] static extern uint Get14(uint i, out State s);
    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")] static extern uint Get910(uint i, out State s);
    static int mode = 0; // 0 = try 1.4, 1 = try 9.1.0, 2 = unavailable

    public const int Up = 1, Down = 2, Left = 4, Right = 8, A = 16, B = 32, Count = 6;

    public static bool Connected;

    static bool Query(uint i, out State s)
    {
        s = new State();
        while (mode < 2)
        {
            try { return (mode == 0 ? Get14(i, out s) : Get910(i, out s)) == 0; }
            catch (DllNotFoundException) { mode++; }
            catch (EntryPointNotFoundException) { mode++; }
        }
        return false;
    }

    // Returns a bit mask of the Pad.* virtual buttons currently held (any controller 0-3).
    public static int Poll()
    {
        int m = 0; bool any = false;
        for (uint i = 0; i < 4; i++)
        {
            State s;
            if (!Query(i, out s)) continue;
            any = true;
            int b = s.Buttons;
            if ((b & 0x0001) != 0 || s.LY > 16000) m |= Up;
            if ((b & 0x0002) != 0 || s.LY < -16000) m |= Down;
            if ((b & 0x0004) != 0 || s.LX < -16000) m |= Left;
            if ((b & 0x0008) != 0 || s.LX > 16000) m |= Right;
            if ((b & 0x1000) != 0) m |= A;
            if ((b & 0x2000) != 0) m |= B;
        }
        Connected = any;
        return m;
    }
}

// ---------------------------------------------------------------------------------------------
// Minimal line-preserving TOML editor (flat "key = value" files, as rrxx.toml is).
// ---------------------------------------------------------------------------------------------
class TomlFile
{
    public string Path;
    List<string> lines = new List<string>();
    static readonly Regex Kv = new Regex("^\\s*([A-Za-z0-9_.]+)\\s*=\\s*(?:\"([^\"]*)\"|([^#\\s]*))", RegexOptions.Compiled);
    bool addedHeader;

    public TomlFile(string path)
    {
        Path = path;
        try { if (File.Exists(path)) lines.AddRange(File.ReadAllLines(path, new UTF8Encoding(false))); } catch { }
        foreach (string l in lines) if (l.Contains("Added by rrxx-launcher")) addedHeader = true;
    }

    // Returns the unquoted value, or null if the key is absent.
    public string Get(string key)
    {
        foreach (string l in lines)
        {
            Match m = Kv.Match(l);
            if (m.Success && m.Groups[1].Value == key) return m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
        }
        return null;
    }

    public void Set(string key, string raw)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            Match m = Kv.Match(lines[i]);
            if (m.Success && m.Groups[1].Value == key) { lines[i] = key + " = " + raw; return; }
        }
        if (!addedHeader)
        {
            if (lines.Count > 0 && lines[lines.Count - 1].Trim().Length > 0) lines.Add("");
            lines.Add("# Added by rrxx-launcher");
            addedHeader = true;
        }
        lines.Add(key + " = " + raw);
    }

    public void Save()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
        File.WriteAllText(Path, string.Join("\r\n", lines.ToArray()) + "\r\n", new UTF8Encoding(false));
    }
}

enum Kind { Button, Choice, Toggle, Key }
enum Page { Main, Controls, Settings, Install }

class Row
{
    public string Label, Desc;
    public Kind Kind;
    public Func<string> Value;
    public Action Activate;
    public Action<int> Change;
    public bool Big;
    public float Anim;
}

// One editable cvar of rrxx.toml shown as a choice / toggle.
class Opt
{
    public string Key, Title, Desc;
    public string[] Labels, Values;
    public bool Quote;           // write the value as a quoted string
    public int Index, Initial, Default;
    public bool InFile;
    public bool Bool { get { return Values.Length == 2 && Values[0] == "false"; } }
}

// One keyboard binding (cvar keybind_*).
class Bind
{
    public string Key, Label, Default, Value;
    public bool InFile;
}

class Glyphs
{
    public GraphicsPath Path;
    public RectangleF Bounds;
    public LinearGradientBrush Hot, Cold;
}

class MainForm : Form
{
    static readonly bool Es = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es";
    static string L(string en, string es) { return Es ? es : en; }

    const float VW = 1280f, VH = 720f;   // virtual canvas; everything is laid out here and scaled to the window
    static readonly RectangleF P = new RectangleF(VW * 0.01f, VH * 0.29f, VW * 0.36f, VH * 0.66f);
    const float Pad_ = 26f;

    readonly string root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
    string SetupPs1 { get { return System.IO.Path.Combine(root, "setup.ps1"); } }
    string JugarBat { get { return System.IO.Path.Combine(root, "jugar.bat"); } }
    string GameExe { get { return System.IO.Path.Combine(root, "build\\dev-release-opt\\rrxx.exe"); } }
    string LogsDir { get { return System.IO.Path.Combine(root, "logs"); } }
    string GameDataXex { get { return System.IO.Path.GetFullPath(System.IO.Path.Combine(root, "..\\rrxx_data\\extraido\\default.xex")); } }
    bool SetupDone { get { return File.Exists(GameExe) && File.Exists(JugarBat); } }
    bool GameExtracted { get { return File.Exists(GameDataXex); } }

    static string StateFile
    {
        get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "rrxx-launcher\\state.ini"); }
    }

    // ---- art ----
    static PrivateFontCollection fonts = new PrivateFontCollection();
    static FontFamily famHeavy, famBody;
    Bitmap splash, bgCache;
    int bgW, bgH;
    readonly Dictionary<string, Glyphs> glyphCache = new Dictionary<string, Glyphs>();
    readonly Dictionary<string, Font> fontCache = new Dictionary<string, Font>();

    // ---- ui state ----
    Page page = Page.Main;
    List<Row> rows = new List<Row>();
    int sel;
    float scroll, scrollTarget;
    float pulse;
    string toast; bool toastBad; DateTime toastUntil;
    Timer timer;
    int padPrev, padRepeatMs;
    Bind capturing;
    bool controlsDirty, settingsDirty;
    string configNote;

    // ---- settings / controls model ----
    TomlFile toml;
    List<Opt> opts = new List<Opt>();
    List<Bind> binds = new List<Bind>();
    Opt mnkOpt;

    // ---- install ----
    Process proc;
    bool running, userCancelled, finishedOk, finishedBad;
    string lastError, stepText = "";
    float progress, progressShown;
    int exitCode;
    List<string> logLines = new List<string>();

    public MainForm()
    {
        Text = "Rumble Roses XX - PC recomp";
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        BackColor = Color.Black;
        KeyPreview = true;

        int cw = 1280, ch = 720;
        Rectangle wa = Screen.PrimaryScreen.WorkingArea;
        int ncW = 16, ncH = 39;
        if (cw + ncW > wa.Width || ch + ncH > wa.Height)
        {
            double k = Math.Min((wa.Width - ncW) / 1280.0, (wa.Height - ncH) / 720.0);
            cw = (int)(1280 * k); ch = (int)(720 * k);
        }
        ClientSize = new Size(cw, ch);
        MinimumSize = new Size(640 + (Width - ClientSize.Width), 360 + (Height - ClientSize.Height));

        LoadAssets();
        LoadModel();
        GoPage(Page.Main);

        timer = new Timer { Interval = 16 };
        timer.Tick += delegate { Tick(); };
        timer.Start();
        FormClosing += OnClosing;
        MouseMove += OnMouseMove2;
        MouseDown += OnMouseDown2;
        MouseWheel += delegate(object s, MouseEventArgs e) { if (CanScroll()) { sel = Math.Max(0, Math.Min(rows.Count - 1, sel - Math.Sign(e.Delta))); } };
    }

    // ===========================================================================================
    // assets
    // ===========================================================================================
    static byte[] ReadResource(string name)
    {
        using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
        {
            if (s == null) return null;
            byte[] b = new byte[s.Length];
            int n = 0; while (n < b.Length) { int r = s.Read(b, n, b.Length - n); if (r <= 0) break; n += r; }
            return b;
        }
    }

    void LoadAssets()
    {
        // Splash: a splash.png next to the exe wins over the embedded one.
        try
        {
            string ext = System.IO.Path.Combine(root, "splash.png");
            if (File.Exists(ext))
                using (Image im = Image.FromFile(ext)) splash = new Bitmap(im);
        }
        catch { splash = null; }
        if (splash == null)
        {
            try { byte[] b = ReadResource("splash.png"); if (b != null) using (MemoryStream ms = new MemoryStream(b)) using (Image im = Image.FromStream(ms)) splash = new Bitmap(im); }
            catch { }
        }
        if (famHeavy == null)
        {
            foreach (string r in new string[] { "font.heavy.ttf", "font.body.ttf" })
            {
                byte[] b = ReadResource(r);
                if (b == null) continue;
                IntPtr p = Marshal.AllocCoTaskMem(b.Length);   // must outlive the font collection
                Marshal.Copy(b, 0, p, b.Length);
                fonts.AddMemoryFont(p, b.Length);
            }
            foreach (FontFamily f in fonts.Families)
            {
                if (f.Name.IndexOf("ExtraBold", StringComparison.OrdinalIgnoreCase) >= 0) famHeavy = f;
                else if (f.Name.IndexOf("SemiBold", StringComparison.OrdinalIgnoreCase) >= 0) famBody = f;
            }
            if (famHeavy == null) famHeavy = famBody != null ? famBody : new FontFamily("Segoe UI");
            if (famBody == null) famBody = famHeavy;
        }
    }

    Font BodyFont(float px)
    {
        string k = "b" + px.ToString(CultureInfo.InvariantCulture);
        Font f;
        if (!fontCache.TryGetValue(k, out f)) { f = new Font(famBody, px, FontStyle.Regular, GraphicsUnit.Pixel); fontCache[k] = f; }
        return f;
    }

    void RebuildBackground()
    {
        if (bgCache != null) bgCache.Dispose();
        bgW = Math.Max(1, ClientSize.Width); bgH = Math.Max(1, ClientSize.Height);
        bgCache = new Bitmap(bgW, bgH, PixelFormat.Format32bppPArgb);
        using (Graphics g = Graphics.FromImage(bgCache))
        {
            g.Clear(Color.FromArgb(10, 8, 24));
            if (splash != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                double sc = Math.Max((double)bgW / splash.Width, (double)bgH / splash.Height);
                int w = (int)Math.Ceiling(splash.Width * sc), h = (int)Math.Ceiling(splash.Height * sc);
                g.DrawImage(splash, (bgW - w) / 2, 0, w, h);   // cover, anchored to the top so the logo is never cut
            }
        }
    }

    // Italic, gradient-filled glyph outlines (cached per text/size).
    Glyphs GetGlyphs(string text, float size)
    {
        string k = size.ToString(CultureInfo.InvariantCulture) + "|" + text;
        Glyphs gl;
        if (glyphCache.TryGetValue(k, out gl)) return gl;
        gl = new Glyphs();
        GraphicsPath path = new GraphicsPath();
        StringFormat sf = new StringFormat(StringFormat.GenericTypographic);
        path.AddString(text, famHeavy, (int)FontStyle.Regular, size, new PointF(0, 0), sf);
        float lineH = LineHeight(size);
        if (path.PointCount > 0)
        {
            Matrix m = new Matrix(1f, 0f, -0.21f, 1f, 0.21f * lineH * 0.58f, 0f);   // synthetic italic
            path.Transform(m);
        }
        gl.Path = path;
        gl.Bounds = path.PointCount > 0 ? path.GetBounds() : new RectangleF(0, 0, 1, lineH);
        RectangleF br = new RectangleF(gl.Bounds.X, gl.Bounds.Y - 1, Math.Max(1, gl.Bounds.Width), Math.Max(2, gl.Bounds.Height + 2));
        gl.Hot = Metal(br, true);
        gl.Cold = Metal(br, false);
        glyphCache[k] = gl;
        return gl;
    }

    static LinearGradientBrush Metal(RectangleF r, bool hot)
    {
        LinearGradientBrush b = new LinearGradientBrush(r, Color.White, Color.White, 90f);
        ColorBlend cb = new ColorBlend(5);
        cb.Positions = new float[] { 0f, 0.42f, 0.5f, 0.58f, 1f };
        if (hot)
            cb.Colors = new Color[] { Color.FromArgb(255, 255, 255), Color.FromArgb(205, 215, 245), Color.FromArgb(120, 110, 190), Color.FromArgb(185, 200, 255), Color.FromArgb(150, 130, 255) };
        else
            cb.Colors = new Color[] { Color.FromArgb(232, 234, 246), Color.FromArgb(170, 178, 205), Color.FromArgb(88, 92, 132), Color.FromArgb(140, 150, 195), Color.FromArgb(112, 108, 178) };
        b.InterpolationColors = cb;
        return b;
    }

    float LineHeight(float size)
    {
        return size * famHeavy.GetLineSpacing(FontStyle.Regular) / (float)famHeavy.GetEmHeight(FontStyle.Regular);
    }

    // Draws gradient text with neon glow. (x, cy) = left edge and vertical centre in virtual units.
    void DrawGlyphs(Graphics g, string text, float size, float x, float cy, bool hot, float glow)
    {
        Glyphs gl = GetGlyphs(text, size);
        GraphicsState st = g.Save();
        g.TranslateTransform(x, cy - LineHeight(size) / 2f);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (gl.Path.PointCount > 0)
        {
            float a = hot ? glow : glow * 0.35f;
            using (Pen p = new Pen(Color.FromArgb((int)(26 * a), 150, 70, 255), 11f) { LineJoin = LineJoin.Round }) g.DrawPath(p, gl.Path);
            using (Pen p = new Pen(Color.FromArgb((int)(52 * a), 120, 90, 255), 6.5f) { LineJoin = LineJoin.Round }) g.DrawPath(p, gl.Path);
            using (Pen p = new Pen(Color.FromArgb((int)(110 * a), 70, 170, 255), 3f) { LineJoin = LineJoin.Round }) g.DrawPath(p, gl.Path);
            g.FillPath(hot ? gl.Hot : gl.Cold, gl.Path);
            using (Pen p = new Pen(Color.FromArgb(hot ? 150 : 90, 20, 12, 60), 1f) { LineJoin = LineJoin.Round }) g.DrawPath(p, gl.Path);
        }
        g.Restore(st);
    }

    float GlyphWidth(string text, float size) { return GetGlyphs(text, size).Bounds.Right; }

    // ===========================================================================================
    // model: rrxx.toml
    // ===========================================================================================
    string ConfigPath()
    {
        string built = System.IO.Path.Combine(root, "build\\dev-release-opt\\rrxx.toml");
        if (File.Exists(built)) return built;
        return System.IO.Path.Combine(root, "config\\rrxx.toml");
    }

    static readonly string[,] BindDefs = {
        {"keybind_a", "A", "Semicolon,Space"}, {"keybind_b", "B", "Quote,Backspace"},
        {"keybind_x", "X", "L"}, {"keybind_y", "Y", "P"},
        {"keybind_left_shoulder", "LB", "1"}, {"keybind_right_shoulder", "RB", "3"},
        {"keybind_left_trigger", "LT", "Q,I"}, {"keybind_right_trigger", "RT", "E,O"},
        {"keybind_lstick_up", "Left stick up", "W"}, {"keybind_lstick_down", "Left stick down", "S"},
        {"keybind_lstick_left", "Left stick left", "A"}, {"keybind_lstick_right", "Left stick right", "D"},
        {"keybind_lstick_press", "Left stick click", "F"},
        {"keybind_rstick_up", "Right stick up", "Up"}, {"keybind_rstick_down", "Right stick down", "Down"},
        {"keybind_rstick_left", "Right stick left", "Left"}, {"keybind_rstick_right", "Right stick right", "Right"},
        {"keybind_rstick_press", "Right stick click", "K"},
        {"keybind_dpad_up", "D-pad up", "Shift+Up"}, {"keybind_dpad_down", "D-pad down", "Shift+Down"},
        {"keybind_dpad_left", "D-pad left", "Shift+Left"}, {"keybind_dpad_right", "D-pad right", "Shift+Right"},
        {"keybind_back", "Back", "Z,Tab"}, {"keybind_start", "Start", "X,Return"}
    };

    Opt MakeOpt(string key, string title, string desc, string[] labels, string[] values, int def, bool quote)
    {
        Opt o = new Opt { Key = key, Title = title, Desc = desc, Labels = labels, Values = values, Default = def, Quote = quote };
        string v = toml.Get(key);
        o.InFile = v != null;
        o.Index = def;
        if (v != null)
            for (int i = 0; i < values.Length; i++)
                if (string.Equals(values[i], v, StringComparison.OrdinalIgnoreCase)) o.Index = i;
        o.Initial = o.Index;
        return o;
    }

    void LoadModel()
    {
        toml = new TomlFile(ConfigPath());
        configNote = ConfigPath();
        opts.Clear(); binds.Clear();
        string on = L("On", "Si"), off = L("Off", "No");
        opts.Add(MakeOpt("render_target_path_d3d12", L("Render path", "Ruta de render"),
            L("RTV: fast host render targets (recommended, tested on Intel UHD). ROV: exact pixel interlock, slower. Auto: let the SDK choose by GPU.",
              "RTV: render targets del host, rapido (recomendado, probado en Intel UHD). ROV: interlock exacto de pixeles, mas lento. Auto: el SDK elige segun la GPU."),
            new string[] { "RTV", "ROV", "Auto" }, new string[] { "rtv", "rov", "" }, 0, true));
        opts.Add(MakeOpt("resolution_scale", L("Resolution scale", "Escala de resolucion"),
            L("Internal resolution multiplier: 1x = 720p, 2x = 1440p, 3x = 2160p. Sharper but needs a stronger GPU. Takes effect on next start.",
              "Multiplicador de resolucion interna: 1x = 720p, 2x = 1440p, 3x = 2160p. Mas nitido pero pide mas GPU. Se aplica al reiniciar el juego."),
            new string[] { "1x (720p)", "2x (1440p)", "3x (2160p)" }, new string[] { "1", "2", "3" }, 0, false));
        opts.Add(MakeOpt("fullscreen", L("Display mode", "Modo de pantalla"),
            L("Start the game in fullscreen or in a window.", "Inicia el juego en pantalla completa o en ventana."),
            new string[] { L("Fullscreen", "Completa"), L("Windowed", "Ventana") }, new string[] { "true", "false" }, 0, false));
        opts.Add(MakeOpt("vsync", "VSync",
            L("Syncs frames to the monitor refresh. Turn it off to measure speed (may tear).", "Sincroniza con el refresco del monitor. Desactivalo para medir velocidad (puede haber tearing)."),
            new string[] { on, off }, new string[] { "true", "false" }, 0, false));
        opts.Add(MakeOpt("d3d12_upload_page_guard_margin", L("Upload pool margin", "Margen del upload pool"),
            L("Keeps 64 KB unused at the end of each GPU upload page. Prevents a GPU crash (TDR) seen on some customisation screens. Leave it On.",
              "Deja 64 KB sin usar al final de cada pagina de subida a la GPU. Evita un cuelgue de GPU (TDR) en la personalizacion. Dejalo en Si."),
            new string[] { on, off }, new string[] { "true", "false" }, 0, false));
        opts.Add(MakeOpt("gpu_log_fps", L("FPS in log", "FPS en el log"),
            L("Writes the frame rate to the game log every N seconds (for bug reports).", "Escribe los FPS en el log del juego cada N segundos (para reportes)."),
            new string[] { L("Off", "No"), "1 s", "5 s", "10 s", "30 s" }, new string[] { "0", "1", "5", "10", "30" }, 2, false));
        mnkOpt = MakeOpt("mnk_mode", L("Keyboard control", "Control por teclado"),
            L("Enables keyboard-as-controller. Without it only a gamepad works. Applies on next game start.",
              "Activa el teclado como mando. Sin esto solo funciona un mando. Se aplica al iniciar el juego."),
            new string[] { on, off }, new string[] { "true", "false" }, 1, false);
        for (int i = 0; i < BindDefs.GetLength(0); i++)
        {
            Bind b = new Bind { Key = BindDefs[i, 0], Label = BindDefs[i, 1], Default = BindDefs[i, 2] };
            string v = toml.Get(b.Key);
            b.InFile = v != null;
            b.Value = v != null ? v : b.Default;
            binds.Add(b);
        }
        controlsDirty = settingsDirty = false;
    }

    bool SaveModel()
    {
        try
        {
            LoadMerge();
            toml.Save();
            controlsDirty = settingsDirty = false;
            foreach (Opt o in opts) { o.Initial = o.Index; }
            return true;
        }
        catch (Exception ex) { Toast(L("Could not save: ", "No se pudo guardar: ") + ex.Message, true); return false; }
    }

    void LoadMerge()
    {
        // Re-read so edits made elsewhere in unrelated keys are preserved, then apply ours.
        TomlFile fresh = new TomlFile(toml.Path);
        toml = fresh;
        foreach (Opt o in opts) if (o.InFile || o.Index != o.Initial || o.Index != o.Default || settingsDirty) WriteOpt(o);
        if (mnkOpt.InFile || mnkOpt.Index != mnkOpt.Initial || controlsDirty) WriteOpt(mnkOpt);
        foreach (Bind b in binds) if (b.InFile || b.Value != b.Default) { toml.Set(b.Key, "\"" + b.Value + "\""); b.InFile = true; }
        mnkOpt.InFile = true;
    }

    void WriteOpt(Opt o)
    {
        string v = o.Values[o.Index];
        toml.Set(o.Key, o.Quote ? "\"" + v + "\"" : v);
        o.InFile = true;
    }

    // ===========================================================================================
    // pages
    // ===========================================================================================
    void GoPage(Page p)
    {
        page = p; sel = 0; scroll = scrollTarget = 0; capturing = null;
        rows = new List<Row>();
        switch (p)
        {
            case Page.Main: BuildMain(); break;
            case Page.Controls: BuildControls(); break;
            case Page.Settings: BuildSettings(); break;
            case Page.Install: BuildInstall(); break;
        }
        foreach (Row r in rows) r.Anim = 0;
        if (rows.Count > 0) rows[0].Anim = 0;
    }

    void BuildMain()
    {
        rows.Add(new Row { Label = "Start Game", Big = true, Activate = StartGame,
            Desc = SetupDone ? L("Launch the game.", "Inicia el juego.")
                             : L("First run: pick your game ISO and the launcher installs everything. Takes 30-90 minutes.",
                                 "Primera vez: elige la ISO de tu juego y el launcher lo instala todo. Tarda de 30 a 90 minutos.") });
        rows.Add(new Row { Label = "Controls", Big = true, Activate = delegate { GoPage(Page.Controls); },
            Desc = L("Gamepad and keyboard mapping. Keyboard keys can be changed.", "Mapeo de mando y teclado. Las teclas se pueden cambiar.") });
        rows.Add(new Row { Label = "Settings", Big = true, Activate = delegate { GoPage(Page.Settings); },
            Desc = L("Graphics and performance options (edits rrxx.toml).", "Opciones de graficos y rendimiento (edita rrxx.toml).") });
        rows.Add(new Row { Label = "Exit", Big = true, Activate = delegate { Close(); }, Desc = L("Close the launcher.", "Cierra el launcher.") });
    }

    void BuildSettings()
    {
        foreach (Opt o in opts)
        {
            Opt oo = o;
            rows.Add(new Row { Label = o.Title, Kind = o.Bool ? Kind.Toggle : Kind.Choice, Desc = o.Desc,
                Value = delegate { return oo.Labels[oo.Index]; },
                Change = delegate(int d) { oo.Index = (oo.Index + d + oo.Values.Length) % oo.Values.Length; settingsDirty = true; },
                Activate = delegate { oo.Index = (oo.Index + 1) % oo.Values.Length; settingsDirty = true; } });
        }
        rows.Add(new Row { Label = L("Save", "Guardar"), Activate = delegate { if (SaveModel()) Toast(L("Saved to ", "Guardado en ") + toml.Path, false); },
            Desc = L("Writes the options to rrxx.toml. Restart the game to apply them.", "Escribe las opciones en rrxx.toml. Reinicia el juego para aplicarlas.") });
        rows.Add(new Row { Label = L("Back", "Volver"), Activate = delegate { LoadModel(); GoPage(Page.Main); },
            Desc = L("Back to the menu (unsaved changes are discarded).", "Vuelve al menu (los cambios sin guardar se descartan).") });
    }

    void BuildControls()
    {
        Opt k = mnkOpt;
        rows.Add(new Row { Label = k.Title, Kind = Kind.Toggle, Desc = k.Desc,
            Value = delegate { return k.Labels[k.Index]; },
            Change = delegate(int d) { k.Index = (k.Index + 1) % 2; controlsDirty = true; },
            Activate = delegate { k.Index = (k.Index + 1) % 2; controlsDirty = true; } });
        foreach (Bind b in binds)
        {
            Bind bb = b;
            rows.Add(new Row { Label = b.Label, Kind = Kind.Key,
                Desc = L("Enter: press a new key. Del: restore default (" + b.Default.Replace(",", " / ") + "). Shift+key combinations are supported.",
                         "Enter: pulsa una tecla nueva. Supr: restaurar (" + b.Default.Replace(",", " / ") + "). Se admiten combinaciones con Shift."),
                Value = delegate { return capturing == bb ? L("press a key...", "pulsa una tecla...") : bb.Value.Replace(",", " / "); },
                Activate = delegate { capturing = bb; } });
        }
        rows.Add(new Row { Label = L("Restore defaults", "Restaurar valores"), Activate = delegate { foreach (Bind b2 in binds) b2.Value = b2.Default; controlsDirty = true; },
            Desc = L("Resets every key to the default layout.", "Devuelve todas las teclas a la configuracion por defecto.") });
        rows.Add(new Row { Label = L("Save", "Guardar"), Activate = delegate { if (SaveModel()) Toast(L("Saved to ", "Guardado en ") + toml.Path, false); },
            Desc = L("Writes the keys to rrxx.toml. Restart the game to apply them.", "Escribe las teclas en rrxx.toml. Reinicia el juego para aplicarlas.") });
        rows.Add(new Row { Label = L("Back", "Volver"), Activate = delegate { LoadModel(); GoPage(Page.Main); },
            Desc = L("Back to the menu (unsaved changes are discarded).", "Vuelve al menu (los cambios sin guardar se descartan).") });
    }

    void BuildInstall()
    {
        rows.Add(new Row { Label = "", Activate = InstallButton });
    }

    string InstallButtonLabel()
    {
        if (running) return L("Cancel", "Cancelar");
        if (finishedBad) return L("Back (or run again)", "Volver (o reintenta)");
        return L("Back", "Volver");
    }

    void InstallButton()
    {
        if (running) { CancelSetup(); return; }
        GoPage(Page.Main);
    }

    void Toast(string msg, bool bad) { toast = msg; toastBad = bad; toastUntil = DateTime.Now.AddSeconds(6); }

    // ===========================================================================================
    // layout helpers
    // ===========================================================================================
    float RowH(Row r) { return r.Big ? 74f : 38f; }

    RectangleF ListArea()
    {
        float x = P.X + Pad_, w = P.Width - 2 * Pad_;
        if (page == Page.Install) return new RectangleF(x, P.Bottom - 70f, w, 48f);
        float top = P.Y + (page == Page.Main ? 22f : 88f);
        float bottom = P.Bottom - (page == Page.Main ? 76f : 82f);
        return new RectangleF(x, top, w, bottom - top);
    }

    float ContentH() { float h = 0; foreach (Row r in rows) h += RowH(r); return h; }

    RectangleF RowRect(int i)
    {
        RectangleF a = ListArea();
        float y = a.Y - scroll;
        for (int j = 0; j < i; j++) y += RowH(rows[j]);
        return new RectangleF(a.X, y, a.Width, RowH(rows[i]));
    }

    bool CanScroll() { return page != Page.Install; }

    PointF ToVirtual(Point p) { return new PointF(p.X * VW / Math.Max(1, ClientSize.Width), p.Y * VH / Math.Max(1, ClientSize.Height)); }

    int HitRow(PointF v)
    {
        RectangleF a = ListArea();
        if (!a.Contains(v)) return -1;
        for (int i = 0; i < rows.Count; i++) { RectangleF r = RowRect(i); if (r.Contains(v)) return i; }
        return -1;
    }

    // ===========================================================================================
    // input
    // ===========================================================================================
    void OnMouseMove2(object s, MouseEventArgs e)
    {
        if (capturing != null) return;
        int i = HitRow(ToVirtual(e.Location));
        if (i >= 0 && i != sel) sel = i;
        Cursor = i >= 0 ? Cursors.Hand : Cursors.Default;
    }

    void OnMouseDown2(object s, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        PointF v = ToVirtual(e.Location);
        if (capturing != null) { capturing = null; return; }
        int i = HitRow(v);
        if (i < 0) return;
        sel = i;
        Row r = rows[i];
        if ((r.Kind == Kind.Choice || r.Kind == Kind.Toggle) && r.Change != null)
        {
            RectangleF rr = RowRect(i);
            r.Change(v.X < rr.X + rr.Width * 0.62f ? -1 : 1);
        }
        else Activate(r);
    }

    void Activate(Row r) { if (r.Activate != null) r.Activate(); }

    void MoveSel(int d)
    {
        if (rows.Count == 0) return;
        sel = Math.Max(0, Math.Min(rows.Count - 1, sel + d));
    }

    void ChangeSel(int d)
    {
        if (sel < 0 || sel >= rows.Count) return;
        Row r = rows[sel];
        if (r.Change != null) r.Change(d);
    }

    void Back()
    {
        if (capturing != null) { capturing = null; return; }
        if (page == Page.Main) return;
        if (page == Page.Install) { if (!running) GoPage(Page.Main); return; }
        LoadModel(); GoPage(Page.Main);
    }

    static string KeyName(Keys k)
    {
        if (k >= Keys.A && k <= Keys.Z) return ((char)k).ToString();
        if (k >= Keys.D0 && k <= Keys.D9) return ((char)k).ToString();
        if (k >= Keys.F1 && k <= Keys.F24) return "F" + (k - Keys.F1 + 1);
        if (k >= Keys.NumPad0 && k <= Keys.NumPad9) return "Numpad" + (k - Keys.NumPad0);
        switch (k)
        {
            case Keys.Oem1: return "Semicolon";
            case Keys.Oem2: return "Slash";
            case Keys.Oem3: return "Backtick";
            case Keys.Oem4: return "LBracket";
            case Keys.Oem5: return "Backslash";
            case Keys.Oem6: return "RBracket";
            case Keys.Oem7: return "Quote";
            case Keys.Oemcomma: return "Comma";
            case Keys.OemPeriod: return "Period";
            case Keys.OemMinus: return "Minus";
            case Keys.Oemplus: return "Plus";
            case Keys.Return: return "Return";
            case Keys.Space: return "Space";
            case Keys.Tab: return "Tab";
            case Keys.Back: return "Backspace";
            case Keys.Insert: return "Insert";
            case Keys.Home: return "Home";
            case Keys.End: return "End";
            case Keys.PageUp: return "PageUp";
            case Keys.PageDown: return "PageDown";
            case Keys.Left: return "Left";
            case Keys.Right: return "Right";
            case Keys.Up: return "Up";
            case Keys.Down: return "Down";
            case Keys.Add: return "NumpadPlus";
            case Keys.Subtract: return "NumpadMinus";
            case Keys.Multiply: return "NumpadStar";
            case Keys.Divide: return "NumpadSlash";
            case Keys.Pause: return "Pause";
        }
        return null;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        Keys key = keyData & Keys.KeyCode;
        if (capturing != null)
        {
            if (key == Keys.ShiftKey || key == Keys.ControlKey || key == Keys.Menu) return true;
            if (key == Keys.Escape) { capturing = null; return true; }
            if (key == Keys.Delete) { capturing.Value = capturing.Default; controlsDirty = true; capturing = null; return true; }
            string n = KeyName(key);
            if (n != null)
            {
                string mod = "";
                if ((keyData & Keys.Control) != 0) mod += "Ctrl+";
                if ((keyData & Keys.Alt) != 0) mod += "Alt+";
                if ((keyData & Keys.Shift) != 0) mod += "Shift+";
                capturing.Value = mod + n;
                controlsDirty = true;
                capturing = null;
            }
            return true;
        }
        switch (key)
        {
            case Keys.Up: MoveSel(-1); return true;
            case Keys.Down: MoveSel(1); return true;
            case Keys.Left: ChangeSel(-1); return true;
            case Keys.Right: ChangeSel(1); return true;
            case Keys.Return: case Keys.Space:
                if (sel >= 0 && sel < rows.Count) Activate(rows[sel]);
                return true;
            case Keys.Escape: Back(); return true;
            case Keys.Home: sel = 0; return true;
            case Keys.End: sel = rows.Count - 1; return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    void PollPad()
    {
        int m = Pad.Poll();
        bool active = Form.ActiveForm == this;   // never steal pad input while the game window has focus
        if (!active) { padPrev = m; padRepeatMs = 0; return; }
        int pressed = m & ~padPrev;
        int held = m & padPrev & (Pad.Up | Pad.Down | Pad.Left | Pad.Right);
        if (held != 0) { padRepeatMs += 16; if (padRepeatMs > 420) { pressed |= held; padRepeatMs = 300; } }
        else padRepeatMs = 0;
        padPrev = m;
        if (pressed == 0) return;
        if (capturing != null) { if ((pressed & Pad.B) != 0) capturing = null; return; }
        if ((pressed & Pad.Up) != 0) MoveSel(-1);
        if ((pressed & Pad.Down) != 0) MoveSel(1);
        if ((pressed & Pad.Left) != 0) ChangeSel(-1);
        if ((pressed & Pad.Right) != 0) ChangeSel(1);
        if ((pressed & Pad.A) != 0 && sel >= 0 && sel < rows.Count) Activate(rows[sel]);
        if ((pressed & Pad.B) != 0) Back();
    }

    void Tick()
    {
        if (WindowState == FormWindowState.Minimized) return;
        PollPad();
        pulse += 0.045f;
        for (int i = 0; i < rows.Count; i++)
        {
            float t = i == sel ? 1f : 0f;
            rows[i].Anim += (t - rows[i].Anim) * 0.28f;
        }
        // keep the selected row visible
        if (rows.Count > 0 && sel < rows.Count)
        {
            RectangleF a = ListArea();
            float top = 0; for (int j = 0; j < sel; j++) top += RowH(rows[j]);
            float h = RowH(rows[sel]);
            if (top - scrollTarget < 0) scrollTarget = top;
            if (top + h - scrollTarget > a.Height) scrollTarget = top + h - a.Height;
            float maxS = Math.Max(0, ContentH() - a.Height);
            scrollTarget = Math.Max(0, Math.Min(maxS, scrollTarget));
        }
        scroll += (scrollTarget - scroll) * 0.3f;
        progressShown += (progress - progressShown) * 0.12f;
        if (Form.ActiveForm == this || (int)(pulse * 20) % 2 == 0) Invalidate();
    }

    // ===========================================================================================
    // painting
    // ===========================================================================================
    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        if (bgCache == null || bgW != ClientSize.Width || bgH != ClientSize.Height) RebuildBackground();
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImageUnscaled(bgCache, 0, 0);
        g.CompositingMode = CompositingMode.SourceOver;
        g.ScaleTransform(ClientSize.Width / VW, ClientSize.Height / VH);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        PaintPanel(g);
        PaintRows(g);
        PaintFooter(g);
    }

    GraphicsPath PanelPath(RectangleF r, float c)
    {
        GraphicsPath p = new GraphicsPath();
        p.AddPolygon(new PointF[] {
            new PointF(r.Left, r.Top), new PointF(r.Right - c, r.Top), new PointF(r.Right, r.Top + c),
            new PointF(r.Right, r.Bottom), new PointF(r.Left + c, r.Bottom), new PointF(r.Left, r.Bottom - c) });
        return p;
    }

    void PaintPanel(Graphics g)
    {
        float glow = 0.75f + 0.25f * (float)Math.Sin(pulse);
        using (GraphicsPath pp = PanelPath(P, 22f))
        {
            using (LinearGradientBrush b = new LinearGradientBrush(P, Color.FromArgb(172, 14, 10, 40), Color.FromArgb(196, 5, 4, 18), 90f))
                g.FillPath(b, pp);
            using (LinearGradientBrush sheen = new LinearGradientBrush(new RectangleF(P.X, P.Y, P.Width, 60), Color.FromArgb(34, 140, 120, 255), Color.FromArgb(0, 140, 120, 255), 90f))
            { GraphicsState st = g.Save(); g.SetClip(pp); g.FillRectangle(sheen, P.X, P.Y, P.Width, 60); g.Restore(st); }
            using (LinearGradientBrush nb = new LinearGradientBrush(P, Color.FromArgb(255, 170, 90, 255), Color.FromArgb(255, 70, 190, 255), LinearGradientMode.ForwardDiagonal))
            {
                using (Pen p = new Pen(Color.FromArgb((int)(28 * glow), 120, 110, 255), 9f) { LineJoin = LineJoin.Round }) g.DrawPath(p, pp);
                using (Pen p = new Pen(Color.FromArgb((int)(60 * glow), 120, 110, 255), 4.5f) { LineJoin = LineJoin.Round }) g.DrawPath(p, pp);
                using (Pen p = new Pen(nb, 1.6f) { LineJoin = LineJoin.Round }) g.DrawPath(p, pp);
            }
        }
        if (page != Page.Main)
        {
            string title = page == Page.Controls ? "CONTROLS" : page == Page.Settings ? "SETTINGS" : L("INSTALLING", "INSTALANDO");
            if ((page == Page.Controls && controlsDirty) || (page == Page.Settings && settingsDirty)) title += " *";
            DrawGlyphs(g, title, 34f, P.X + Pad_, P.Y + 34f, true, glow);
            using (Pen p = new Pen(Color.FromArgb(120, 130, 120, 255), 1f)) g.DrawLine(p, P.X + Pad_, P.Y + 62f, P.Right - Pad_, P.Y + 62f);
            string sub = null; Color sc = Color.FromArgb(190, 200, 215);
            if (page == Page.Controls)
            {
                sub = Pad.Connected ? L("Gamepad detected - uses the native Xbox layout (not remappable).", "Mando detectado - usa el mapeo nativo de Xbox (no reasignable).")
                                    : L("No gamepad detected. Plug one in, or use the keyboard keys below.", "No hay mando. Conecta uno o usa las teclas de abajo.");
                if (Pad.Connected) sc = Color.FromArgb(150, 235, 190);
            }
            else if (page == Page.Settings) sub = L("Edits ", "Edita ") + ShortPath(toml.Path);
            if (sub != null) DrawBody(g, sub, 14f, sc, P.X + Pad_, P.Y + 68f, P.Width - 2 * Pad_, 18f, StringAlignment.Near);
        }
    }

    string ShortPath(string p)
    {
        if (p.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return p.Substring(root.Length).TrimStart('\\');
        return p;
    }

    void DrawBody(Graphics g, string s, float px, Color c, float x, float y, float w, float h, StringAlignment al)
    {
        using (StringFormat sf = new StringFormat { Alignment = al, LineAlignment = StringAlignment.Near, Trimming = StringTrimming.EllipsisCharacter })
        {
            RectangleF r = new RectangleF(x, y, w, h);
            RectangleF rs = new RectangleF(x + 1, y + 1, w, h);
            using (Brush sh = new SolidBrush(Color.FromArgb(200, 0, 0, 0))) g.DrawString(s, BodyFont(px), sh, rs, sf);
            using (Brush b = new SolidBrush(c)) g.DrawString(s, BodyFont(px), b, r, sf);
        }
    }

    void PaintRows(Graphics g)
    {
        float glow = 0.75f + 0.25f * (float)Math.Sin(pulse);
        RectangleF area = ListArea();
        if (page == Page.Install) PaintInstall(g);
        GraphicsState st = g.Save();
        g.SetClip(new RectangleF(P.X + 2, area.Y - 4, P.Width - 4, area.Height + 8));
        for (int i = 0; i < rows.Count; i++)
        {
            Row r = rows[i];
            RectangleF rc = RowRect(i);
            if (rc.Bottom < area.Y - 40 || rc.Top > area.Bottom + 40) continue;
            float t = r.Anim;
            bool hot = i == sel;
            float size = r.Big ? 42f : 24f;
            string label = page == Page.Install ? InstallButtonLabel() : r.Label;
            float shift = (r.Big ? 18f : 10f) * t;
            float cy = rc.Y + rc.Height / 2f;

            if (t > 0.02f)
            {
                RectangleF bar = new RectangleF(rc.X - 8f, rc.Y + 3f, rc.Width + 8f, rc.Height - 6f);
                using (LinearGradientBrush b = new LinearGradientBrush(bar, Color.FromArgb((int)(120 * t), 130, 80, 255), Color.FromArgb(0, 70, 140, 255), 0f))
                    g.FillRectangle(b, bar);
                using (LinearGradientBrush ab = new LinearGradientBrush(new RectangleF(bar.X, bar.Y, 4, bar.Height), Color.FromArgb((int)(255 * t), 190, 120, 255), Color.FromArgb((int)(255 * t), 70, 190, 255), 90f))
                    g.FillRectangle(ab, bar.X, bar.Y, 4f, bar.Height);
            }

            DrawGlyphs(g, label, size, rc.X + shift, cy, hot, glow * (0.4f + 0.6f * t));

            if ((r.Kind == Kind.Choice || r.Kind == Kind.Toggle || r.Kind == Kind.Key) && r.Value != null)
            {
                string v = r.Value();
                float vs = 20f;
                float vw = GlyphWidth(v, vs);
                float right = rc.Right - 4f;
                bool cap = capturing != null && r.Kind == Kind.Key && hot;
                if (r.Kind != Kind.Key && hot)
                {
                    DrawArrow(g, right - vw - 26f, cy, -1);
                    DrawArrow(g, right - 6f, cy, 1);
                    right -= 22f;
                }
                if (cap && ((int)(pulse * 8) % 2 == 0)) v = " ";
                DrawGlyphs(g, v, vs, right - GlyphWidth(v == " " ? "press a key..." : v, vs), cy, hot, glow * t);
            }
        }
        g.Restore(st);
        // scroll hints
        float maxS = Math.Max(0, ContentH() - area.Height);
        if (maxS > 1f)
        {
            float tr = area.Height - 6f, th = Math.Max(24f, tr * area.Height / ContentH());
            float ty = area.Y + 3f + (tr - th) * (scroll / maxS);
            using (Brush b = new SolidBrush(Color.FromArgb(120, 150, 130, 255))) g.FillRectangle(b, P.Right - 14f, ty, 3f, th);
        }
    }

    void DrawArrow(Graphics g, float x, float cy, int dir)
    {
        PointF[] pts = dir < 0 ? new PointF[] { new PointF(x + 6, cy - 7), new PointF(x + 6, cy + 7), new PointF(x - 3, cy) }
                               : new PointF[] { new PointF(x - 6, cy - 7), new PointF(x - 6, cy + 7), new PointF(x + 3, cy) };
        using (Brush b = new SolidBrush(Color.FromArgb(230, 120, 200, 255))) g.FillPolygon(b, pts);
    }

    void PaintInstall(Graphics g)
    {
        float x = P.X + Pad_, w = P.Width - 2 * Pad_;
        float y = P.Y + 76f;
        Color c = finishedBad ? Color.FromArgb(255, 130, 120) : finishedOk ? Color.FromArgb(150, 235, 190) : Color.FromArgb(225, 230, 245);
        DrawBody(g, stepText, 17f, c, x, y, w, 24f, StringAlignment.Near);
        y += 30f;
        RectangleF bar = new RectangleF(x, y, w, 16f);
        using (GraphicsPath bp = RoundRect(bar, 8f))
        {
            using (Brush b = new SolidBrush(Color.FromArgb(150, 5, 5, 20))) g.FillPath(b, bp);
            float fw = Math.Max(0f, Math.Min(1f, progressShown / 100f)) * bar.Width;
            if (fw > 4f)
            {
                RectangleF fr = new RectangleF(bar.X, bar.Y, fw, bar.Height);
                using (GraphicsPath fp = RoundRect(fr, 8f))
                {
                    using (Pen p = new Pen(Color.FromArgb(60, 110, 120, 255), 7f)) g.DrawPath(p, fp);
                    using (LinearGradientBrush b = new LinearGradientBrush(fr, Color.FromArgb(255, 160, 80, 255), Color.FromArgb(255, 70, 190, 255), 0f)) g.FillPath(b, fp);
                }
            }
            using (Pen p = new Pen(Color.FromArgb(170, 130, 120, 255), 1f)) g.DrawPath(p, bp);
        }
        y += 28f;
        float lh = 17f;
        int fit = (int)((P.Bottom - 84f - y) / lh);
        int start = Math.Max(0, logLines.Count - fit);
        for (int i = start; i < logLines.Count; i++)
            DrawBody(g, logLines[i], 13f, Color.FromArgb(175, 185, 205), x, y + (i - start) * lh, w, lh, StringAlignment.Near);
    }

    static GraphicsPath RoundRect(RectangleF r, float rad)
    {
        float d = rad * 2;
        GraphicsPath p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    void PaintFooter(Graphics g)
    {
        // description / toast box inside the panel
        float x = P.X + Pad_, w = P.Width - 2 * Pad_;
        if (page != Page.Install)
        {
            float dh = page == Page.Main ? 58f : 64f;
            float dy = P.Bottom - dh - 14f;
            using (Pen p = new Pen(Color.FromArgb(90, 130, 120, 255), 1f)) g.DrawLine(p, x, dy - 6f, P.Right - Pad_, dy - 6f);
            string txt = null; Color c = Color.FromArgb(205, 212, 232);
            if (toast != null && DateTime.Now < toastUntil) { txt = toast; c = toastBad ? Color.FromArgb(255, 135, 125) : Color.FromArgb(150, 235, 190); }
            else if (sel >= 0 && sel < rows.Count && rows[sel].Desc != null) txt = rows[sel].Desc;
            if (txt != null) DrawBody(g, txt, 14f, c, x, dy, w, dh, StringAlignment.Near);
        }
        else if (toast != null && DateTime.Now < toastUntil)
            DrawBody(g, toast, 14f, toastBad ? Color.FromArgb(255, 135, 125) : Color.FromArgb(150, 235, 190), x, P.Bottom - 20f, w, 16f, StringAlignment.Near);

        // credits band
        using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(0, VH - 34f, VW, 34f), Color.FromArgb(0, 0, 0, 0), Color.FromArgb(190, 0, 0, 0), 90f))
            g.FillRectangle(b, 0, VH - 34f, VW, 34f);
        DrawBody(g, L("Fan-made, non-profit project. Rumble Roses XX and its artwork are (c) Konami / Yuke's. No affiliation with Konami or Yuke's. No game files are included.",
                      "Proyecto de fans sin fines de lucro. Rumble Roses XX y su arte son (c) Konami / Yuke's. Sin afiliacion con Konami ni Yuke's. No incluye archivos del juego."),
                 12.5f, Color.FromArgb(215, 220, 235), 14f, VH - 20f, VW - 200f, 16f, StringAlignment.Near);
        DrawBody(g, "PC recomp alpha  v0.2", 12.5f, Color.FromArgb(170, 180, 205), VW - 200f, VH - 20f, 186f, 16f, StringAlignment.Far);
    }

    // ===========================================================================================
    // aspect ratio lock
    // ===========================================================================================
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0214 && WindowState == FormWindowState.Normal)   // WM_SIZING
        {
            RECT r = (RECT)Marshal.PtrToStructure(m.LParam, typeof(RECT));
            int edge = m.WParam.ToInt32();
            int ncW = Width - ClientSize.Width, ncH = Height - ClientSize.Height;
            int cw = r.Right - r.Left - ncW, ch = r.Bottom - r.Top - ncH;
            if (edge == 3 || edge == 6)   // top / bottom: height drives width
            {
                cw = (int)Math.Round(ch * 16.0 / 9.0);
                r.Right = r.Left + cw + ncW;
            }
            else
            {
                ch = (int)Math.Round(cw * 9.0 / 16.0);
                if (edge == 4 || edge == 5) r.Top = r.Bottom - (ch + ncH); else r.Bottom = r.Top + ch + ncH;
            }
            Marshal.StructureToPtr(r, m.LParam, false);
        }
        base.WndProc(ref m);
    }

    // ===========================================================================================
    // actions: start game / install
    // ===========================================================================================
    void StartGame()
    {
        if (running) { GoPage(Page.Install); return; }
        if (!File.Exists(SetupPs1) && !SetupDone)
        {
            Toast(L("setup.ps1 not found next to this program. Put the launcher inside the project folder.",
                    "No se encontro setup.ps1 junto al programa. Coloca el launcher dentro de la carpeta del proyecto."), true);
            return;
        }
        if (SetupDone) { Play(); return; }
        BeginInstall();
    }

    string LoadIsoMemory()
    {
        try { if (File.Exists(StateFile)) foreach (string l in File.ReadAllLines(StateFile)) if (l.StartsWith("iso=")) return l.Substring(4); } catch { }
        return "";
    }

    void SaveIsoMemory(string iso)
    {
        try { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(StateFile)); File.WriteAllText(StateFile, "iso=" + iso + "\r\n"); } catch { }
    }

    string AskIso()
    {
        using (OpenFileDialog d = new OpenFileDialog())
        {
            d.Title = L("Select your Rumble Roses XX ISO", "Selecciona la ISO de Rumble Roses XX");
            d.Filter = L("Disc images (*.iso)|*.iso|All files (*.*)|*.*", "Imagenes de disco (*.iso)|*.iso|Todos los archivos (*.*)|*.*");
            string mem = LoadIsoMemory();
            if (mem.Length > 0 && File.Exists(mem)) { d.InitialDirectory = System.IO.Path.GetDirectoryName(mem); d.FileName = mem; }
            if (d.ShowDialog(this) == DialogResult.OK) return d.FileName;
        }
        return null;
    }

    void BeginInstall()
    {
        string iso = null;
        bool needIso = !GameExtracted;
        if (needIso)
        {
            iso = AskIso();
            if (iso == null) return;
            if (!File.Exists(iso)) { Toast(L("That file does not exist.", "Ese archivo no existe."), true); return; }
            if (new FileInfo(iso).Length < 1024L * 1024 * 1024) { Toast(L("That file is too small to be a game disc image.", "Ese archivo es demasiado pequeno para ser una imagen de disco."), true); return; }
            SaveIsoMemory(iso);
        }
        try
        {
            DriveInfo di = new DriveInfo(root.Substring(0, 1));
            if (di.AvailableFreeSpace < 25L * 1024 * 1024 * 1024)
            {
                DialogResult r = MessageBox.Show(this,
                    string.Format(L("Only {0:N0} GB free on drive {1}; about 25 GB are needed. Continue anyway?", "Solo hay {0:N0} GB libres en el disco {1}; se necesitan unos 25 GB. Continuar de todos modos?"),
                                  di.AvailableFreeSpace / (1024.0 * 1024 * 1024), di.Name), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes) return;
            }
        }
        catch { }
        if (root.IndexOf(' ') >= 0 || root.IndexOf("\\Documents", StringComparison.OrdinalIgnoreCase) >= 0 || root.IndexOf("\\Documentos", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            DialogResult r = MessageBox.Show(this,
                L("This folder path has spaces or is under Documents. Builds often fail there (Windows protected folders). Move the project to a short path like C:\\rrxx\\rrxx-recomp. Continue anyway?",
                  "La ruta de esta carpeta tiene espacios o esta en Documentos. Ahi las compilaciones suelen fallar (carpetas protegidas de Windows). Mueve el proyecto a una ruta corta como C:\\rrxx\\rrxx-recomp. Continuar de todos modos?"),
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r != DialogResult.Yes) return;
        }

        logLines.Clear();
        lastError = null; userCancelled = false; finishedOk = finishedBad = false; exitCode = 0;
        progress = progressShown = 0;
        stepText = L("Starting... Windows may ask for administrator permission to install build tools.",
                     "Iniciando... Windows puede pedir permiso de administrador para instalar herramientas.");
        GoPage(Page.Install);

        ProcessStartInfo psi = new ProcessStartInfo("powershell.exe");
        string args = "-NoProfile -ExecutionPolicy Bypass -File \"" + SetupPs1 + "\" -Yes -NoLaunch";
        if (needIso) args += " \"" + iso + "\"";
        psi.Arguments = args;
        psi.WorkingDirectory = root;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.RedirectStandardInput = true;   // never wait on a console prompt: EOF instead
        try
        {
            Encoding enc = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
            psi.StandardOutputEncoding = enc; psi.StandardErrorEncoding = enc;
        }
        catch { }
        try
        {
            proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            proc.OutputDataReceived += OnData;
            proc.ErrorDataReceived += OnData;
            proc.Exited += delegate { try { BeginInvoke(new Action(OnExited)); } catch { } };
            proc.Start();
            proc.StandardInput.Close();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            running = true;
        }
        catch (Exception ex)
        {
            finishedBad = true;
            stepText = L("Could not start PowerShell: ", "No se pudo iniciar PowerShell: ") + ex.Message;
        }
    }

    static readonly Regex StepRe = new Regex(@"^\[(\d)/6\]\s*(.*)$");
    static readonly Regex Spinner = new Regex(@"^[\s\-\\|/â–ˆâ–’â–‘]*$");

    void OnData(object sender, DataReceivedEventArgs e)
    {
        if (e.Data == null) return;
        string data = e.Data;
        try { BeginInvoke(new Action(delegate { HandleText(data); })); } catch { }
    }

    void HandleText(string data)
    {
        foreach (string seg in data.Split('\r'))
        {
            string line = seg.TrimEnd();
            if (line.Length == 0 || Spinner.IsMatch(line)) continue;
            string t = line.Trim();
            Match m = StepRe.Match(t);
            if (m.Success)
            {
                int n = int.Parse(m.Groups[1].Value);
                stepText = string.Format(L("Step {0}/6: {1}", "Paso {0}/6: {1}"), n, m.Groups[2].Value);
                progress = Math.Max(0, Math.Min(100, (n - 1) * 100 / 6));
            }
            if (t.StartsWith("ERROR:") || t.StartsWith("Setup stopped") || t.StartsWith("Setup detenido")) lastError = t;
            logLines.Add(t);
            if (logLines.Count > 400) logLines.RemoveRange(0, 200);
        }
    }

    void OnExited()
    {
        try { proc.WaitForExit(); exitCode = proc.ExitCode; } catch { exitCode = -1; }
        running = false;
        if (userCancelled)
        {
            stepText = L("Cancelled. Run it again any time: finished steps are skipped.", "Cancelado. Puedes repetirlo cuando quieras: los pasos terminados se saltan.");
            return;
        }
        if (exitCode == 0 && SetupDone)
        {
            progress = 100; finishedOk = true;
            stepText = L("Done! Go back and press Start Game.", "Listo! Vuelve y pulsa Start Game.");
            Toast(L("Installation finished.", "Instalacion terminada."), false);
            return;
        }
        finishedBad = true;
        string why = lastError != null ? lastError : L("exit code " + exitCode, "codigo de salida " + exitCode);
        stepText = L("Setup failed: ", "El setup fallo: ") + why;
        logLines.Add(L("Full log: ", "Log completo: ") + System.IO.Path.Combine(LogsDir, "setup.log"));
        logLines.Add(L("Press Start Game again: finished steps are skipped.", "Pulsa Start Game otra vez: los pasos terminados se saltan."));
    }

    void CancelSetup()
    {
        if (!running || proc == null) return;
        if (MessageBox.Show(this, L("Stop the setup? You can resume later.", "Detener el setup? Podras reanudarlo despues."), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        KillTree();
    }

    void KillTree()
    {
        userCancelled = true;
        try
        {
            Process k = Process.Start(new ProcessStartInfo("taskkill", "/PID " + proc.Id + " /T /F") { CreateNoWindow = true, UseShellExecute = false });
            k.WaitForExit(5000);
        }
        catch { try { proc.Kill(); } catch { } }
    }

    void OnClosing(object sender, FormClosingEventArgs e)
    {
        if (!running) return;
        if (MessageBox.Show(this, L("Setup is still running. Stop it and exit? (You can resume later.)", "El setup sigue en marcha. Detenerlo y salir? (Podras reanudarlo despues.)"), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            e.Cancel = true;
            return;
        }
        KillTree();
    }

    void Play()
    {
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo(JugarBat) { WorkingDirectory = root, UseShellExecute = true };
            Process.Start(psi);
            Toast(L("Game launched. If it closes or crashes, see logs\\ultimo_crash.txt.", "Juego iniciado. Si se cierra o falla, mira logs\\ultimo_crash.txt."), false);
        }
        catch (Exception ex)
        {
            Toast(L("Could not start jugar.bat: ", "No se pudo iniciar jugar.bat: ") + ex.Message, true);
        }
    }
}
