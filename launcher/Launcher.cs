// Rumble Roses XX (PC recomp) - graphical launcher.
// Contains NO game data: it only runs setup.ps1 and jugar.bat that sit next to it.
// Build: launcher\build.bat (uses the C# compiler that ships with Windows; no SDK needed).
using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

[assembly: AssemblyTitle("Rumble Roses XX Launcher")]
[assembly: AssemblyProduct("Rumble Roses XX PC recomp launcher")]
[assembly: AssemblyDescription("Runs setup.ps1 and jugar.bat. Contains no game data.")]
[assembly: AssemblyVersion("0.1.0.0")]

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

class MainForm : Form
{
    static readonly bool Es = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es";
    static string L(string en, string es) { return Es ? es : en; }

    readonly string root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
    string SetupPs1 { get { return Path.Combine(root, "setup.ps1"); } }
    string JugarBat { get { return Path.Combine(root, "jugar.bat"); } }
    string GameExe { get { return Path.Combine(root, "build\\dev-release-opt\\rrxx.exe"); } }
    string LogsDir { get { return Path.Combine(root, "logs"); } }
    string GameDataXex { get { return Path.GetFullPath(Path.Combine(root, "..\\rrxx_data\\extraido\\default.xex")); } }

    Label title, info, stepLabel;
    TextBox isoBox, logBox;
    Button browse, start, play, cancel, openLogs, rerun;
    ProgressBar bar;
    Process proc;
    bool running, userCancelled;
    string lastError;
    int exitCode;

    public MainForm()
    {
        Text = "Rumble Roses XX - PC recomp";
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        ClientSize = new Size(640, 470);
        Font = new Font("Segoe UI", 9f);

        title = new Label { Text = "Rumble Roses XX - PC recomp (alpha)", Font = new Font("Segoe UI", 13f, FontStyle.Bold), Left = 16, Top = 12, Width = 608, Height = 28 };
        info = new Label { Left = 16, Top = 44, Width = 608, Height = 54 };

        var isoLbl = new Label { Text = L("Your game disc image (.iso):", "Imagen de tu disco del juego (.iso):"), Left = 16, Top = 104, Width = 608, Height = 18 };
        isoBox = new TextBox { Left = 16, Top = 124, Width = 508, ReadOnly = false };
        browse = new Button { Text = L("Browse...", "Elegir..."), Left = 532, Top = 122, Width = 92, Height = 26 };
        browse.Click += delegate { BrowseIso(); };

        stepLabel = new Label { Left = 16, Top = 164, Width = 608, Height = 20, Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
        bar = new ProgressBar { Left = 16, Top = 188, Width = 608, Height = 22, Minimum = 0, Maximum = 100 };
        logBox = new TextBox { Left = 16, Top = 220, Width = 608, Height = 170, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 8.5f), BackColor = SystemColors.Window };

        start = new Button { Text = L("Install / Build", "Instalar / Compilar"), Left = 16, Top = 402, Width = 140, Height = 34 };
        start.Click += delegate { StartSetup(); };
        play = new Button { Text = "Play", Left = 164, Top = 402, Width = 140, Height = 34, Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
        play.Click += delegate { Play(); };
        cancel = new Button { Text = L("Cancel", "Cancelar"), Left = 312, Top = 402, Width = 100, Height = 34, Enabled = false };
        cancel.Click += delegate { CancelSetup(); };
        rerun = new Button { Text = L("Repair / re-run setup", "Reparar / repetir setup"), Left = 420, Top = 402, Width = 204, Height = 34 };
        rerun.Click += delegate { StartSetup(); };
        openLogs = new Button { Text = L("Open logs folder", "Abrir carpeta de logs"), Left = 16, Top = 440, Width = 160, Height = 24 };
        openLogs.Click += delegate { OpenLogs(); };

        Controls.AddRange(new Control[] { title, info, isoLbl, isoBox, browse, stepLabel, bar, logBox, start, play, cancel, rerun, openLogs });
        AcceptButton = null;
        FormClosing += OnClosing;
        Load += delegate { Initialize(); };
    }

    bool SetupDone { get { return File.Exists(GameExe) && File.Exists(JugarBat); } }
    bool GameExtracted { get { return File.Exists(GameDataXex); } }

    void Initialize()
    {
        if (!File.Exists(SetupPs1))
        {
            info.Text = L("setup.ps1 was not found next to this program.\r\nPut rrxx-launcher.exe inside the project folder (the one you get with: git clone https://github.com/JesusTaylo/rumble-roses-xx-recomp.git C:\\rrxx\\rrxx-recomp).",
                          "No se encontro setup.ps1 junto a este programa.\r\nColoca rrxx-launcher.exe dentro de la carpeta del proyecto (la que obtienes con: git clone https://github.com/JesusTaylo/rumble-roses-xx-recomp.git C:\\rrxx\\rrxx-recomp).");
            info.ForeColor = Color.Firebrick;
            SetBusy(false, false);
            start.Enabled = false; rerun.Enabled = false; browse.Enabled = false; isoBox.Enabled = false; play.Enabled = false;
            return;
        }
        RefreshState();
    }

    void RefreshState()
    {
        if (SetupDone)
        {
            info.Text = L("Setup is complete. Press Play to start the game.", "El setup ya esta completo. Pulsa Play para jugar.");
            info.ForeColor = Color.DarkGreen;
            stepLabel.Text = L("Ready.", "Listo.");
            bar.Value = 100;
        }
        else
        {
            info.Text = L("First time: choose the .iso of YOUR copy of the game and press Install. It extracts the disc, builds the SDK and recompiles the game on this PC. "
                          + "This takes about 30-90 minutes the first time (about 20 on a fast PC that already has the build tools; mostly compiling) and needs ~25 GB of free disk. Nothing is uploaded; no game files are included in this program.",
                          "Primera vez: elige la .iso de TU copia del juego y pulsa Instalar. Extrae el disco, compila el SDK y recompila el juego en este PC. "
                          + "Tarda de 30 a 90 minutos la primera vez (unos 20 en un PC rapido que ya tiene las herramientas; sobre todo compilando) y necesita ~25 GB libres. No se sube nada; este programa no incluye archivos del juego.");
            info.ForeColor = SystemColors.ControlText;
            stepLabel.Text = L("Waiting.", "En espera.");
            bar.Value = 0;
        }
        SetBusy(false, SetupDone);
    }

    void SetBusy(bool busy, bool ready)
    {
        running = busy;
        bool needIso = !GameExtracted;
        isoBox.Enabled = !busy && !ready;
        browse.Enabled = !busy && !ready;
        start.Visible = !ready;
        start.Enabled = !busy;
        play.Visible = ready;
        play.Enabled = ready && !busy;
        rerun.Visible = ready;
        rerun.Enabled = !busy;
        cancel.Enabled = busy;
        if (ready && !busy) { isoBox.Enabled = true; browse.Enabled = true; }
        if (!needIso && !busy && !ready) { isoBox.Text = L("(game files already extracted - ISO not needed)", "(archivos del juego ya extraidos - no hace falta la ISO)"); isoBox.Enabled = false; browse.Enabled = false; }
    }

    void BrowseIso()
    {
        using (var d = new OpenFileDialog())
        {
            d.Title = L("Select your Rumble Roses XX ISO", "Selecciona la ISO de Rumble Roses XX");
            d.Filter = L("Disc images (*.iso)|*.iso|All files (*.*)|*.*", "Imagenes de disco (*.iso)|*.iso|Todos los archivos (*.*)|*.*");
            if (d.ShowDialog(this) == DialogResult.OK) isoBox.Text = d.FileName;
        }
    }

    void StartSetup()
    {
        if (running) return;
        string iso = isoBox.Text.Trim().Trim('"');
        bool needIso = !GameExtracted;
        if (needIso)
        {
            if (iso.Length == 0 || !File.Exists(iso))
            {
                MessageBox.Show(this, L("Choose the ISO of your game first.", "Elige primero la ISO de tu juego."), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (new FileInfo(iso).Length < 1024L * 1024 * 1024)
            {
                MessageBox.Show(this, L("That file is too small to be a game disc image.", "Ese archivo es demasiado pequeno para ser una imagen de disco."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }
        try
        {
            var di = new DriveInfo(root.Substring(0, 1));
            if (!SetupDone && di.AvailableFreeSpace < 25L * 1024 * 1024 * 1024)
            {
                var r = MessageBox.Show(this,
                    string.Format(L("Only {0:N0} GB free on drive {1}; about 25 GB are needed. Continue anyway?", "Solo hay {0:N0} GB libres en el disco {1}; se necesitan unos 25 GB. Continuar de todos modos?"),
                                  di.AvailableFreeSpace / (1024.0 * 1024 * 1024), di.Name),
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes) return;
            }
        }
        catch { }
        if (root.IndexOf(' ') >= 0 || root.IndexOf("\\Documents", StringComparison.OrdinalIgnoreCase) >= 0 || root.IndexOf("\\Documentos", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            var r = MessageBox.Show(this,
                L("This folder path has spaces or is under Documents. Builds often fail there (Windows protected folders). Move the project to a short path like C:\\rrxx\\rrxx-recomp. Continue anyway?",
                  "La ruta de esta carpeta tiene espacios o esta en Documentos. Ahi las compilaciones suelen fallar (carpetas protegidas de Windows). Mueve el proyecto a una ruta corta como C:\\rrxx\\rrxx-recomp. Continuar de todos modos?"),
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r != DialogResult.Yes) return;
        }

        logBox.Clear();
        lastError = null; userCancelled = false; exitCode = 0;
        bar.Value = 0;
        stepLabel.Text = L("Starting...", "Iniciando...");
        info.ForeColor = SystemColors.ControlText;
        info.Text = L("Running setup. Windows may ask for administrator permission to install build tools (Git, Visual Studio Build Tools, LLVM, CMake...). Do not close this window.",
                      "Ejecutando el setup. Windows puede pedir permiso de administrador para instalar herramientas (Git, Visual Studio Build Tools, LLVM, CMake...). No cierres esta ventana.");

        var psi = new ProcessStartInfo("powershell.exe");
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
            SetBusy(true, false);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, L("Could not start PowerShell: ", "No se pudo iniciar PowerShell: ") + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetBusy(false, SetupDone);
        }
    }

    static readonly Regex StepRe = new Regex(@"^\[(\d)/6\]\s*(.*)$");
    static readonly Regex Spinner = new Regex(@"^[\s\-\\|/█▒░]*$");

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
                stepLabel.Text = string.Format(L("Step {0}/6: {1}", "Paso {0}/6: {1}"), n, m.Groups[2].Value);
                bar.Value = Math.Max(0, Math.Min(100, (n - 1) * 100 / 6));
            }
            if (t.StartsWith("ERROR:") || t.StartsWith("Setup stopped") || t.StartsWith("Setup detenido")) lastError = t;
            if (logBox.TextLength > 60000) logBox.Text = logBox.Text.Substring(30000);
            logBox.AppendText(line + "\r\n");
        }
    }

    void OnExited()
    {
        try { proc.WaitForExit(); exitCode = proc.ExitCode; } catch { exitCode = -1; }
        running = false;
        if (userCancelled)
        {
            stepLabel.Text = L("Cancelled.", "Cancelado.");
            info.Text = L("Setup cancelled. Run it again any time: finished steps are skipped.", "Setup cancelado. Puedes repetirlo cuando quieras: los pasos terminados se saltan.");
            RefreshAfter();
            return;
        }
        if (exitCode == 0 && SetupDone)
        {
            bar.Value = 100;
            stepLabel.Text = L("Done!", "Listo!");
            info.ForeColor = Color.DarkGreen;
            info.Text = L("Setup finished. Press Play to start the game.", "Setup terminado. Pulsa Play para jugar.");
            RefreshAfter();
            return;
        }
        info.ForeColor = Color.Firebrick;
        stepLabel.Text = L("Setup failed.", "El setup fallo.");
        string why = lastError != null ? lastError : L("exit code " + exitCode, "codigo de salida " + exitCode);
        info.Text = L("Something went wrong: ", "Algo salio mal: ") + why + "\r\n"
                  + L("Full log: ", "Log completo: ") + Path.Combine(LogsDir, "setup.log") + L("  (finished steps are skipped if you try again).", "  (los pasos terminados se saltan si reintentas).");
        SetBusy(false, false);
        // Keep the error visible; RefreshState would overwrite the message.
        MessageBox.Show(this,
            L("Setup failed.\n\nReason: ", "El setup fallo.\n\nMotivo: ") + why + "\n\n"
            + L("Details are in:\n", "Los detalles estan en:\n") + LogsDir + "\n"
            + L("(setup.log, build_sdk.log, build.log, codegen.log).\n\nYou can press Install again: it continues where it stopped. If it keeps failing, open an issue and attach logs\\setup.log (it contains no game data).",
                "(setup.log, build_sdk.log, build.log, codegen.log).\n\nPuedes pulsar Instalar otra vez: continua donde se quedo. Si sigue fallando, abre un issue y adjunta logs\\setup.log (no contiene datos del juego)."),
            Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    void RefreshAfter()
    {
        bool ready = SetupDone;
        if (!ready) { SetBusy(false, false); } else { SetBusy(false, true); }
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
            var k = Process.Start(new ProcessStartInfo("taskkill", "/PID " + proc.Id + " /T /F") { CreateNoWindow = true, UseShellExecute = false });
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
        if (!SetupDone)
        {
            MessageBox.Show(this, L("The game is not built yet. Run the install first.", "El juego aun no esta compilado. Ejecuta primero la instalacion."), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            RefreshState();
            return;
        }
        try
        {
            var psi = new ProcessStartInfo(JugarBat) { WorkingDirectory = root, UseShellExecute = true };
            Process.Start(psi);
            stepLabel.Text = L("Game launched. If it closes or crashes, see logs\\ultimo_crash.txt.", "Juego iniciado. Si se cierra o falla, mira logs\\ultimo_crash.txt.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, L("Could not start jugar.bat: ", "No se pudo iniciar jugar.bat: ") + ex.Message + "\n\n" + L("Logs: ", "Logs: ") + LogsDir, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    void OpenLogs()
    {
        try
        {
            Directory.CreateDirectory(LogsDir);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + LogsDir + "\"") { UseShellExecute = true });
        }
        catch { }
    }
}
