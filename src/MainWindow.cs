using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Ui = Wpf.Ui.Controls;

namespace GDDowngrade
{
    class DepotDownloadException : Exception
    {
        public DepotDownloadException(string message) : base(message) { }
    }

    /// <summary>Ventana mínima: texto de lo que está haciendo, barra de progreso y el botón de downgrade.</summary>
    class MainWindow : Ui.FluentWindow
    {
        enum Tone { Normal, Ok, Warn, Bad }

        static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GDDowngrade", "registro.txt");

        readonly TextBlock status;
        readonly ProgressBar progress;
        readonly Ui.Button button;
        GameInstall game = new GameInstall();
        CancellationTokenSource cts;
        bool busy, allowClose;

        public MainWindow()
        {
            Title = "GD Downgrade";
            Width = 320;
            MinWidth = 0;  // el estilo de WPF UI trae un tamaño mínimo que no deja achicar la ventana
            MinHeight = 0;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowBackdropType = Ui.WindowBackdropType.Mica;
            ExtendsContentIntoTitleBar = true;

            status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13 };
            progress = new ProgressBar { Maximum = 100, Margin = new Thickness(0, 10, 0, 14) };
            button = new Ui.Button { HorizontalAlignment = HorizontalAlignment.Stretch };
            button.Click += OnButtonClick;

            var body = new StackPanel { Margin = new Thickness(16, 0, 16, 16) };
            body.Children.Add(status);
            body.Children.Add(progress);
            body.Children.Add(button);

            var root = new StackPanel();
            root.Children.Add(new Ui.TitleBar { Title = Title, ShowMaximize = false, CanMaximize = false, ShowHelp = false });
            root.Children.Add(body);
            Content = root;

            // Aplica el tema también al fondo Mica y a la barra de título, no solo a los controles
            Loaded += (s, e) => Wpf.Ui.Appearance.ApplicationThemeManager.Apply(WindowsTheme(), Ui.WindowBackdropType.Mica, true);
            RefreshInfo();
        }

        /// <summary>Tema claro u oscuro según la opción "modo de las apps" de Windows.</summary>
        public static Wpf.Ui.Appearance.ApplicationTheme WindowsTheme()
        {
            object light = Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
            return light is int && (int)light == 0 ? Wpf.Ui.Appearance.ApplicationTheme.Dark : Wpf.Ui.Appearance.ApplicationTheme.Light;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (busy && !allowClose)
            {
                e.Cancel = true;
                ConfirmExit();
                return;
            }
            if (cts != null) cts.Cancel();
            base.OnClosing(e);
        }

        async void ConfirmExit()
        {
            if (!await AskAsync("Salir", "Hay una operación en curso. ¿Salir de todas formas?", "Salir", "Cancelar")) return;
            allowClose = true;
            Close();
        }

        // ---------- Estado ----------

        void SetStatus(string text, Tone tone)
        {
            status.Text = text;
            string brush = tone == Tone.Ok ? "SystemFillColorSuccessBrush"
                : tone == Tone.Warn ? "SystemFillColorCautionBrush"
                : tone == Tone.Bad ? "SystemFillColorCriticalBrush"
                : "TextFillColorPrimaryBrush";
            status.SetResourceReference(TextBlock.ForegroundProperty, brush);
        }

        /// <summary>-2 deja la barra indeterminada; 0..100 es el porcentaje.</summary>
        void SetProgress(int value)
        {
            progress.IsIndeterminate = value == -2;
            if (value >= 0) progress.Value = Math.Min(100, value);
        }

        void SetBusy(bool on, bool canCancel)
        {
            busy = on;
            bool cancel = on && canCancel;
            button.Content = cancel ? "Cancelar" : "Downgrade a " + Gd.Version;
            button.Appearance = cancel ? Ui.ControlAppearance.Secondary : Ui.ControlAppearance.Primary;
            button.IsEnabled = on ? canCancel : game.SteamFound && game.GameFound;
        }

        void RefreshInfo()
        {
            try { game = GameInstall.Detect(); }
            catch (Exception ex) { game = new GameInstall { Error = ex.Message }; }
            if (busy) return;
            bool? isTarget = game.GameFound ? VersionCheck.IsTargetVersion(game) : null;
            bool locked = game.GameFound && game.ManifestLocked;
            SetProgress(isTarget == true && locked ? 100 : 0);
            if (!game.SteamFound || !game.GameFound) SetStatus(game.Error ?? "No encontré Geometry Dash.", Tone.Bad);
            else if (isTarget == true && locked) SetStatus("Ya tienes " + Gd.Version + " y Steam no lo actualizará.", Tone.Ok);
            else if (isTarget == true) SetStatus("Tienes " + Gd.Version + ", pero Steam puede actualizarlo.", Tone.Warn);
            else SetStatus("Listo para hacer el downgrade.", Tone.Normal);
            SetBusy(false, false);
        }

        static void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                File.AppendAllText(LogPath, "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + message + Environment.NewLine);
            }
            catch (Exception) { }
        }

        void BringAppToFront()
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Topmost = true;
            Topmost = false;
            Activate();
        }

        async Task<bool> AskAsync(string title, string text, string yes, string no)
        {
            var box = new Ui.MessageBox { Title = title, Content = text, PrimaryButtonText = yes, CloseButtonText = no, Owner = this };
            return await box.ShowDialogAsync(true, CancellationToken.None) == Ui.MessageBoxResult.Primary;
        }

        // ---------- Downgrade ----------

        async void OnButtonClick(object sender, RoutedEventArgs e)
        {
            if (busy)
            {
                if (cts != null)
                {
                    cts.Cancel();
                    Log("Cancelando");
                }
                return;
            }

            RefreshInfo();
            if (!game.SteamFound || !game.GameFound) return;

            cts = new CancellationTokenSource();
            CancellationToken ct = cts.Token;
            SetBusy(true, true);
            bool done = false, offerAppAdmin = false, offerSteamAdmin = false;
            string error = null;
            try
            {
                Log("Empezando el downgrade a " + Gd.Version);
                await WaitForGameClosedAsync(ct);
                CheckWriteAccess();

                SetStatus("Respaldando tu partida...", Tone.Normal);
                SetProgress(-2);
                string backup = FileOps.BackupSaves();
                Log(backup != null ? "Partida respaldada en " + backup : "No había datos guardados que respaldar");

                string depotDir = await DownloadDepotAsync(ct);

                SetBusy(true, false); // a mitad de la copia ya no se puede cancelar
                SetStatus("Copiando archivos a la carpeta del juego...", Tone.Normal);
                SetProgress(0);
                string gameDir = game.GameDir;
                var copyProgress = new Progress<int>(p => SetProgress(p));
                int copied = await Task.Run(() => FileOps.CopyDepot(depotDir, gameDir, copyProgress));
                VersionCheck.Remember(Path.Combine(depotDir, "GeometryDash.exe"));
                Log(copied + " archivos copiados a " + gameDir);

                SetStatus("Bloqueando las actualizaciones...", Tone.Normal);
                game.SetManifestLocked(true);
                Log(Path.GetFileName(game.ManifestPath) + " marcado como solo lectura");
                done = true;
            }
            catch (OperationCanceledException)
            {
                Log("Cancelado");
            }
            catch (DepotDownloadException ex)
            {
                Log("Steam informó un error: " + ex.Message);
                error = ex.Message;
                offerSteamAdmin = true;
            }
            catch (UnauthorizedAccessException ex)
            {
                Log("Error de permisos: " + ex.Message);
                offerAppAdmin = true;
            }
            catch (Exception ex)
            {
                Log("Error: " + ex);
                error = ex.Message + (ex is IOException ? " Si Geometry Dash está abierto, ciérralo y vuelve a intentarlo." : "");
            }
            finally
            {
                cts.Dispose();
                cts = null;
                busy = false;
                RefreshInfo();
            }

            if (done)
            {
                Log("Downgrade completado");
                SetStatus("Listo: Geometry Dash está en " + Gd.Version + ".", Tone.Ok);
                SetProgress(100);
                BringAppToFront();
            }
            else if (offerSteamAdmin)
            {
                SetStatus("Steam no pudo descargar " + Gd.Version + ".", Tone.Bad);
                BringAppToFront();
                if (await AskAsync("Error de Steam", "Steam dijo: " + error
                        + "\n\nSi es un error de permisos, hay que abrir Steam como administrador. ¿Lo reinicio así?", "Sí", "No"))
                    await RestartSteamAsAdminAsync();
            }
            else if (offerAppAdmin)
            {
                SetStatus("No tengo permiso para modificar los archivos del juego.", Tone.Bad);
                await AskRestartAsAdminAsync();
            }
            else if (error != null) SetStatus(error, Tone.Bad);
        }

        async Task WaitForGameClosedAsync(CancellationToken ct)
        {
            if (Process.GetProcessesByName("GeometryDash").Length == 0) return;
            SetStatus("Cierra Geometry Dash para continuar...", Tone.Warn);
            SetProgress(-2);
            while (Process.GetProcessesByName("GeometryDash").Length > 0) await Task.Delay(1000, ct);
        }

        void CheckWriteAccess()
        {
            string probe = Path.Combine(game.GameDir, ".gddowngrade-test");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            File.SetAttributes(game.ManifestPath, File.GetAttributes(game.ManifestPath));
        }

        async Task<string> DownloadDepotAsync(CancellationToken ct)
        {
            // Si ya hay una descarga completa (Steam cuenta archivos + carpetas), se reutiliza
            int expected = SteamLog.LastDepotFileCount(game) ?? Gd.DefaultDepotFileCount;
            if (File.Exists(Path.Combine(game.DepotDir, "GeometryDash.exe")) && FileOps.CountEntries(game.DepotDir) >= expected)
            {
                Log("Usando la descarga que ya existía en " + game.DepotDir);
                return game.DepotDir;
            }

            var tail = new LogTail(game.ConsoleLog);
            if (!SteamConsole.IsSteamRunning())
            {
                Log("Steam no estaba abierto; iniciándolo");
                SetStatus("Abriendo Steam (inicia sesión si te lo pide)...", Tone.Normal);
                OpenUrl("steam://open/console");
                await WaitForSteamWindowAsync(ct);
                await Task.Delay(5000, ct); // que Steam termine de cargar antes de escribir
            }

            SetStatus("Enviando el comando a la consola de Steam...", Tone.Normal);
            bool sent = await SendCommandAsync(ct);
            SetStatus("Esperando a Steam...", Tone.Normal);

            DateTime waitStart = DateTime.Now;
            DateTime lastSizeCheck = DateTime.MinValue;
            bool started = false, hintShown = false;
            long totalBytes = 0;
            while (true)
            {
                foreach (string line in tail.ReadNewLines())
                {
                    Match m = SteamLog.Downloading.Match(line);
                    if (m.Success)
                    {
                        started = true;
                        totalBytes = SteamLog.ParseSize(m.Groups[2].Value, m.Groups[3].Value);
                        Log("Steam está descargando " + m.Groups[1].Value + " archivos (" + m.Groups[2].Value + " " + m.Groups[3].Value + ")");
                        SetStatus("Descargando " + Gd.Version + "...", Tone.Normal);
                        continue;
                    }
                    m = SteamLog.Complete.Match(line);
                    if (m.Success && m.Groups[2].Value == Gd.ManifestId)
                    {
                        Log("Descarga completa: " + m.Groups[1].Value);
                        return m.Groups[1].Value;
                    }
                    if (SteamLog.Failed.IsMatch(line)) throw new DepotDownloadException(SteamLog.StripTimestamp(line));
                }

                if (!started && !hintShown && (!sent || (DateTime.Now - waitStart).TotalSeconds > 15))
                {
                    hintShown = true;
                    Log("Pidiendo al usuario que pegue el comando a mano");
                    SetStatus("Pega el comando en la consola de Steam (Ctrl+V) y pulsa Enter. Ya está copiado.", Tone.Warn);
                    BringAppToFront();
                }

                if (started && totalBytes > 0 && (DateTime.Now - lastSizeCheck).TotalSeconds >= 1)
                {
                    lastSizeCheck = DateTime.Now;
                    string dir = game.DepotDir;
                    long size = await Task.Run(() => FileOps.DirSize(dir));
                    SetProgress((int)Math.Min(99, size * 100 / totalBytes));
                    SetStatus(string.Format("Descargando {0}... {1} / {2} MB", Gd.Version, size >> 20, totalBytes >> 20), Tone.Normal);
                }
                await Task.Delay(500, ct);
            }
        }

        /// <summary>
        /// Abre la consola de Steam, pega el comando y pulsa Enter hasta que el log de Steam confirme que lo
        /// recibió. Solo envía teclas mientras la ventana en primer plano sea la ventana principal de Steam
        /// (nunca un chat); si no, devuelve false y el usuario lo pega a mano.
        /// </summary>
        async Task<bool> SendCommandAsync(CancellationToken ct)
        {
            CopyCommandToClipboard();
            Native.AllowSetForegroundWindow(Native.ASFW_ANY);
            OpenUrl("steam://open/console");

            IntPtr steam = IntPtr.Zero;
            for (int i = 0; i < 40 && steam == IntPtr.Zero; i++)
            {
                await Task.Delay(250, ct);
                IntPtr fg = Native.GetForegroundWindow();
                if (SteamConsole.IsSteamMainWindow(fg)) steam = fg;
            }
            if (steam == IntPtr.Zero)
            {
                steam = SteamConsole.FindMainWindow();
                if (steam == IntPtr.Zero)
                {
                    Log("No encontré la ventana principal de Steam");
                    return false;
                }
                if (Native.IsIconic(steam)) Native.ShowWindow(steam, Native.SW_RESTORE);
                Native.SetForegroundWindow(steam);
                await Task.Delay(500, ct);
            }

            await Task.Delay(2000, ct); // la página de la consola tarda un poco en cargar
            if (!SteamConsole.IsSteamMainWindow(Native.GetForegroundWindow()))
            {
                Log("Steam no quedó en primer plano, así que no escribí nada");
                return false;
            }
            SteamConsole.SendSelectAllAndPaste();
            // Steam tarda un momento en procesar el pegado (y en mostrar el autocompletado) antes de aceptar el Enter
            await Task.Delay(1500, ct);

            const int attempts = 3;
            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                if (!SteamConsole.IsSteamMainWindow(Native.GetForegroundWindow()))
                {
                    Log("Steam dejó de estar en primer plano, así que no pulsé Enter");
                    return false;
                }
                var confirmation = new LogTail(game.ConsoleLog);
                SteamConsole.SendEnter();
                if (await WaitForCommandAcceptedAsync(confirmation, ct))
                {
                    Log("Comando enviado a la consola de Steam: " + Gd.DownloadCommand);
                    return true;
                }
                if (attempt < attempts) Log("Steam no reaccionó al Enter; lo pulso otra vez");
            }
            Log("Steam no aceptó el comando automáticamente");
            return false;
        }

        /// <summary>
        /// Espera hasta 8 s a que el log de Steam muestre que ejecutó el comando. Usa su propio LogTail
        /// para no consumir las líneas que después lee el bucle de descarga.
        /// </summary>
        static async Task<bool> WaitForCommandAcceptedAsync(LogTail tail, CancellationToken ct)
        {
            for (int i = 0; i < 16; i++)
            {
                await Task.Delay(500, ct);
                foreach (string line in tail.ReadNewLines())
                {
                    if (SteamLog.Downloading.IsMatch(line) || SteamLog.Complete.IsMatch(line)
                        || SteamLog.Failed.IsMatch(line) || SteamLog.CommandExecuted.IsMatch(line))
                        return true;
                }
            }
            return false;
        }

        static async Task WaitForSteamWindowAsync(CancellationToken ct)
        {
            DateTime start = DateTime.Now;
            while (SteamConsole.FindMainWindow() == IntPtr.Zero)
            {
                if ((DateTime.Now - start).TotalMinutes > 3)
                    throw new Exception("Steam no terminó de abrirse. Ábrelo, inicia sesión y vuelve a intentarlo.");
                await Task.Delay(1000, ct);
            }
        }

        async Task AskRestartAsAdminAsync()
        {
            if (IsAdmin()) return;
            if (!await AskAsync("Permisos", "No tengo permiso para modificar los archivos del juego. ¿Abro la app como administrador?", "Sí", "No"))
                return;
            try
            {
                Process.Start(new ProcessStartInfo(System.Reflection.Assembly.GetEntryAssembly().Location) { UseShellExecute = true, Verb = "runas" });
                allowClose = true;
                Close();
            }
            catch (Win32Exception) { Log("Se canceló abrir la app como administrador"); }
        }

        async Task RestartSteamAsAdminAsync()
        {
            SetBusy(true, false);
            SetProgress(-2);
            string result = "Cuando Steam cargue, pulsa Downgrade otra vez.";
            try
            {
                Log("Cerrando Steam para abrirlo como administrador");
                SetStatus("Cerrando Steam...", Tone.Normal);
                OpenUrl("steam://exit");
                DateTime start = DateTime.Now;
                while (SteamConsole.IsSteamRunning() && (DateTime.Now - start).TotalSeconds < 60)
                    await Task.Delay(1000);
                if (SteamConsole.IsSteamRunning()) result = "Steam no se cerró. Ciérralo tú y ábrelo como administrador.";
                else
                {
                    Process.Start(new ProcessStartInfo(game.SteamExe) { UseShellExecute = true, Verb = "runas" });
                    Log("Steam abierto como administrador");
                }
            }
            catch (Win32Exception)
            {
                Log("Se canceló abrir Steam como administrador");
                result = "No se abrió Steam como administrador.";
            }
            finally
            {
                busy = false;
                RefreshInfo();
            }
            SetStatus(result, Tone.Warn);
        }

        // ---------- Utilidades ----------

        static void OpenUrl(string url)
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }

        static void CopyCommandToClipboard()
        {
            try { Clipboard.SetDataObject(Gd.DownloadCommand, true); }
            catch (Exception) { }
        }

        static bool IsAdmin()
        {
            using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
