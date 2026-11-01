using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace GDDowngrade
{
    /// <summary>Rutas de Steam y de Geometry Dash detectadas en este equipo.</summary>
    class GameInstall
    {
        public string SteamPath;
        public string SteamExe;
        public string ConsoleLog;
        public string DepotDir;
        public List<string> Libraries = new List<string>();
        public string ManifestPath;
        public string GameDir;
        public string BuildId;
        public string Error;

        public bool SteamFound { get { return SteamPath != null; } }
        public bool GameFound { get { return ManifestPath != null && GameDir != null && Directory.Exists(GameDir); } }
        public string GameExe { get { return GameDir == null ? null : Path.Combine(GameDir, "GeometryDash.exe"); } }

        public bool ManifestLocked
        {
            get
            {
                return ManifestPath != null && File.Exists(ManifestPath)
                    && (File.GetAttributes(ManifestPath) & FileAttributes.ReadOnly) != 0;
            }
        }

        public void SetManifestLocked(bool locked)
        {
            FileAttributes a = File.GetAttributes(ManifestPath);
            File.SetAttributes(ManifestPath, locked ? a | FileAttributes.ReadOnly : a & ~FileAttributes.ReadOnly);
        }

        public static GameInstall Detect()
        {
            var g = new GameInstall();
            g.SteamPath = FindSteamPath();
            if (g.SteamPath == null)
            {
                g.Error = "No se encontró Steam instalado en este equipo.";
                return g;
            }
            g.SteamExe = Path.Combine(g.SteamPath, "steam.exe");
            g.ConsoleLog = Path.Combine(g.SteamPath, @"logs\console_log.txt");
            g.DepotDir = Path.Combine(g.SteamPath, @"steamapps\content\app_" + Gd.AppId + @"\depot_" + Gd.DepotId);
            g.Libraries = ReadLibraries(g.SteamPath);

            foreach (string lib in g.Libraries)
            {
                string manifest = Path.Combine(lib, @"steamapps\appmanifest_" + Gd.AppId + ".acf");
                if (!File.Exists(manifest)) continue;
                string text = FileOps.ReadShared(manifest);
                g.ManifestPath = manifest;
                g.GameDir = Path.Combine(lib, "steamapps", "common", Vdf.Value(text, "installdir") ?? "Geometry Dash");
                g.BuildId = Vdf.Value(text, "buildid");
                break;
            }
            if (g.ManifestPath == null)
                g.Error = "Geometry Dash no está instalado en ninguna biblioteca de Steam.";
            else if (!Directory.Exists(g.GameDir))
                g.Error = "No existe la carpeta del juego: " + g.GameDir;
            return g;
        }

        static string FindSteamPath()
        {
            var candidates = new List<string>();
            AddRegistryValue(candidates, Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");
            AddRegistryValue(candidates, Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
            AddRegistryValue(candidates, Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath");
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));

            foreach (string c in candidates)
            {
                try
                {
                    string p = Path.GetFullPath(c.Replace('/', '\\')).TrimEnd('\\');
                    if (File.Exists(Path.Combine(p, "steam.exe"))) return FileOps.ProperCase(p);
                }
                catch (Exception) { }
            }
            return null;
        }

        static void AddRegistryValue(List<string> list, RegistryKey root, string subKey, string name)
        {
            try
            {
                using (RegistryKey k = root.OpenSubKey(subKey))
                {
                    string v = k == null ? null : k.GetValue(name) as string;
                    if (!string.IsNullOrEmpty(v)) list.Add(v);
                }
            }
            catch (Exception) { }
        }

        static List<string> ReadLibraries(string steamPath)
        {
            var libs = new List<string> { steamPath };
            string vdf = Path.Combine(steamPath, @"steamapps\libraryfolders.vdf");
            if (!File.Exists(vdf)) return libs;
            foreach (string p in Vdf.Values(FileOps.ReadShared(vdf), "path"))
            {
                string lib = p.TrimEnd('\\');
                if (!libs.Any(l => string.Equals(l, lib, StringComparison.OrdinalIgnoreCase))) libs.Add(lib);
            }
            return libs;
        }
    }

    /// <summary>Lectura mínima de archivos .vdf/.acf de Steam (pares "clave" "valor").</summary>
    static class Vdf
    {
        static Regex KeyRegex(string key)
        {
            return new Regex("\"" + Regex.Escape(key) + @"""\s+""((?:[^""\\]|\\.)*)""", RegexOptions.IgnoreCase);
        }

        static string Unescape(string s) { return Regex.Replace(s, @"\\(.)", "$1"); }

        public static string Value(string text, string key)
        {
            Match m = KeyRegex(key).Match(text);
            return m.Success ? Unescape(m.Groups[1].Value) : null;
        }

        public static IEnumerable<string> Values(string text, string key)
        {
            return KeyRegex(key).Matches(text).Cast<Match>().Select(m => Unescape(m.Groups[1].Value));
        }
    }

    /// <summary>Patrones que Steam escribe en logs\console_log.txt al usar download_depot.</summary>
    static class SteamLog
    {
        // [2026-10-15 21:04:14] Downloading depot 322171 (9012 files, 257 MB) ...
        public static readonly Regex Downloading = new Regex(@"Downloading depot " + Gd.DepotId + @" \((\d+) files, ([\d.,]+) (\w+)\)");
        // [2026-10-15 21:04:31] Depot download complete : "C:\...\depot_322171" (manifest 3816559102876907245)
        public static readonly Regex Complete = new Regex(@"Depot download complete : ""(.+?)"" \(manifest (\d+)\)");
        public static readonly Regex Failed = new Regex(@"Depot download failed|failed to download depot|download_depot.*\b(fail|failed|error)\b", RegexOptions.IgnoreCase);
        // [2026-10-24 15:44:33] STARTUP SlowJSMethodCall Console.ExecCommand took 1.6ms
        // No siempre aparece, así que solo sirve como confirmación extra de que el Enter llegó.
        public static readonly Regex CommandExecuted = new Regex(@"Console\.ExecCommand\b");

        public static string StripTimestamp(string line)
        {
            return Regex.Replace(line, @"^\[[^\]]*\]\s*", "").Trim();
        }

        public static long ParseSize(string number, string unit)
        {
            double n;
            if (!double.TryParse(number.Replace(",", ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out n)) return 0;
            switch (unit.ToUpperInvariant())
            {
                case "KB": return (long)(n * 1024);
                case "MB": return (long)(n * 1024 * 1024);
                case "GB": return (long)(n * 1024 * 1024 * 1024);
                default: return (long)n;
            }
        }

        /// <summary>Cantidad de archivos del depot según la última descarga registrada, si la hay.</summary>
        public static int? LastDepotFileCount(GameInstall g)
        {
            int? count = null;
            foreach (string log in new[] { Path.ChangeExtension(g.ConsoleLog, ".previous.txt"), g.ConsoleLog })
            {
                if (!File.Exists(log)) continue;
                foreach (Match m in Downloading.Matches(FileOps.ReadShared(log)))
                    count = int.Parse(m.Groups[1].Value);
            }
            return count;
        }
    }

    /// <summary>Lee solo las líneas nuevas que se van agregando a un log.</summary>
    class LogTail
    {
        readonly string path;
        long offset;

        public LogTail(string path)
        {
            this.path = path;
            try { offset = File.Exists(path) ? new FileInfo(path).Length : 0; }
            catch (IOException) { offset = 0; }
        }

        public List<string> ReadNewLines()
        {
            var lines = new List<string>();
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (fs.Length < offset) offset = 0; // Steam rotó el log
                    if (fs.Length == offset) return lines;
                    fs.Seek(offset, SeekOrigin.Begin);
                    var buf = new byte[fs.Length - offset];
                    int read = 0;
                    while (read < buf.Length)
                    {
                        int n = fs.Read(buf, read, buf.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    if (read == 0) return lines;
                    // Solo consumimos hasta el último salto de línea completo
                    int lastNewline = Array.LastIndexOf(buf, (byte)'\n', read - 1);
                    if (lastNewline < 0) return lines;
                    offset += lastNewline + 1;
                    string text = Encoding.UTF8.GetString(buf, 0, lastNewline + 1);
                    foreach (string l in text.Split('\n'))
                    {
                        string t = l.TrimEnd('\r');
                        if (t.Length > 0) lines.Add(t);
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return lines;
        }
    }

    /// <summary>Abre la consola de Steam y le escribe el comando como si fuera el usuario.</summary>
    static class SteamConsole
    {
        static readonly string[] SteamProcesses = { "steamwebhelper", "steam" };

        public static bool IsSteamRunning()
        {
            return Process.GetProcessesByName("steam").Length > 0;
        }

        public static bool IsSteamMainWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero || !Native.IsWindowVisible(hWnd)) return false;
            var title = new StringBuilder(256);
            Native.GetWindowText(hWnd, title, title.Capacity);
            if (title.ToString() != "Steam") return false;
            uint pid;
            Native.GetWindowThreadProcessId(hWnd, out pid);
            try
            {
                using (Process p = Process.GetProcessById((int)pid))
                    return SteamProcesses.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase);
            }
            catch (ArgumentException) { return false; }
        }

        public static IntPtr FindMainWindow()
        {
            IntPtr best = IntPtr.Zero;
            long bestArea = -1;
            Native.EnumWindows((h, l) =>
            {
                if (IsSteamMainWindow(h))
                {
                    Native.RECT r;
                    Native.GetWindowRect(h, out r);
                    long area = (long)(r.Right - r.Left) * (r.Bottom - r.Top);
                    if (area > bestArea) { bestArea = area; best = h; }
                }
                return true;
            }, IntPtr.Zero);
            return best;
        }

        static Native.INPUT KeyInput(byte vk, bool up)
        {
            var input = new Native.INPUT { type = Native.INPUT_KEYBOARD };
            input.u.ki.wVk = vk;
            input.u.ki.wScan = (ushort)Native.MapVirtualKey(vk, Native.MAPVK_VK_TO_VSC);
            input.u.ki.dwFlags = up ? Native.KEYEVENTF_KEYUP : 0;
            return input;
        }

        /// <summary>
        /// Pulsa las teclas juntas y las suelta en orden inverso, con su scan code como un teclado real.
        /// Va en un solo SendInput para que no se mezcle con lo que el usuario esté tecleando.
        /// </summary>
        static void Press(params byte[] keys)
        {
            var inputs = new Native.INPUT[keys.Length * 2];
            for (int i = 0; i < keys.Length; i++)
            {
                inputs[i] = KeyInput(keys[i], false);
                inputs[inputs.Length - 1 - i] = KeyInput(keys[i], true);
            }
            Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Native.INPUT)));
        }

        /// <summary>Ctrl+A y Ctrl+V: reemplaza lo que haya en la caja de la consola por el comando.</summary>
        public static void SendSelectAllAndPaste()
        {
            Press(Native.VK_CONTROL, Native.VK_A);
            Press(Native.VK_CONTROL, Native.VK_V);
        }

        public static void SendEnter()
        {
            Press(Native.VK_RETURN);
        }
    }

    static class FileOps
    {
        public static string ReadShared(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var r = new StreamReader(fs, Encoding.UTF8))
                return r.ReadToEnd();
        }

        /// <summary>Devuelve la ruta con las mayúsculas reales del disco (el registro de Steam la guarda en minúsculas).</summary>
        public static string ProperCase(string path)
        {
            try
            {
                var di = new DirectoryInfo(path);
                if (di.Parent == null) return di.FullName.ToUpperInvariant();
                string parent = ProperCase(di.Parent.FullName);
                DirectoryInfo[] match = new DirectoryInfo(parent).GetDirectories(di.Name);
                return match.Length > 0 ? match[0].FullName : Path.Combine(parent, di.Name);
            }
            catch (Exception) { return path; }
        }

        /// <summary>Archivos + carpetas: así cuenta Steam los "files" de un depot (9006 archivos + 6 carpetas = 9012).</summary>
        public static int CountEntries(string dir)
        {
            return Directory.Exists(dir) ? Directory.EnumerateFileSystemEntries(dir, "*", SearchOption.AllDirectories).Count() : 0;
        }

        public static long DirSize(string dir)
        {
            if (!Directory.Exists(dir)) return 0;
            long size = 0;
            try
            {
                foreach (FileInfo f in new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    try { size += f.Length; } catch (IOException) { }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return size;
        }

        /// <summary>Copia todo el contenido del depot a la carpeta del juego, reemplazando archivos.</summary>
        public static int CopyDepot(string depotDir, string gameDir, IProgress<int> progress)
        {
            var root = new DirectoryInfo(depotDir);
            FileInfo[] files = root.GetFiles("*", SearchOption.AllDirectories);
            if (files.Length == 0) throw new IOException("La carpeta descargada está vacía: " + depotDir);

            string prefix = root.FullName.TrimEnd('\\') + "\\";
            long total = files.Sum(f => f.Length);
            long done = 0;
            int lastPct = -1;
            foreach (FileInfo f in files)
            {
                string rel = f.FullName.Substring(prefix.Length);
                string target = Path.Combine(gameDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                if (File.Exists(target))
                {
                    FileAttributes a = File.GetAttributes(target);
                    if ((a & FileAttributes.ReadOnly) != 0) File.SetAttributes(target, a & ~FileAttributes.ReadOnly);
                }
                f.CopyTo(target, true);
                if (new FileInfo(target).Length != f.Length)
                    throw new IOException("La copia de " + rel + " quedó incompleta.");

                done += f.Length;
                int pct = total == 0 ? 100 : (int)(done * 100 / total);
                if (pct != lastPct) { lastPct = pct; progress.Report(pct); }
            }
            return files.Length;
        }

        public static string BackupSaves()
        {
            string src = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GeometryDash");
            if (!Directory.Exists(src)) return null;
            string[] files = Directory.GetFiles(src, "*.dat");
            if (files.Length == 0) return null;
            string dest = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "GD Downgrade", "Respaldos", DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
            Directory.CreateDirectory(dest);
            foreach (string f in files) File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), true);
            return dest;
        }
    }

    /// <summary>Compara GeometryDash.exe con el de 2.2081 para saber qué versión está instalada.</summary>
    static class VersionCheck
    {
        static string RefFile
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GDDowngrade", "gd-" + Gd.Version + ".sha1");
            }
        }

        public static string Sha1(string file)
        {
            using (SHA1 sha = SHA1.Create())
            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "");
        }

        /// <summary>Guarda el hash del ejecutable de 2.2081 para reconocerlo aunque se borre la descarga.</summary>
        public static void Remember(string targetExe)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(RefFile));
                File.WriteAllText(RefFile, Sha1(targetExe));
            }
            catch (Exception) { }
        }

        /// <summary>true = 2.2081, false = otra versión, null = no hay con qué comparar.</summary>
        public static bool? IsTargetVersion(GameInstall g)
        {
            try
            {
                if (g.GameExe == null || !File.Exists(g.GameExe)) return null;
                string reference = null;
                if (File.Exists(RefFile)) reference = File.ReadAllText(RefFile).Trim();
                else
                {
                    string depotExe = Path.Combine(g.DepotDir, "GeometryDash.exe");
                    if (File.Exists(depotExe)) reference = Sha1(depotExe);
                }
                if (string.IsNullOrEmpty(reference)) return null;
                return string.Equals(Sha1(g.GameExe), reference, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return null; }
        }
    }

    static class Diagnostics
    {
        public static string Report()
        {
            var sb = new StringBuilder();
            GameInstall g = GameInstall.Detect();
            sb.AppendLine("SteamPath: " + g.SteamPath);
            sb.AppendLine("SteamExe: " + g.SteamExe);
            sb.AppendLine("Libraries: " + string.Join(" | ", g.Libraries));
            sb.AppendLine("ManifestPath: " + g.ManifestPath);
            sb.AppendLine("GameDir: " + g.GameDir);
            sb.AppendLine("GameFound: " + g.GameFound);
            sb.AppendLine("BuildId: " + g.BuildId);
            sb.AppendLine("ManifestLocked: " + (g.ManifestPath != null ? g.ManifestLocked.ToString() : "-"));
            sb.AppendLine("Error: " + g.Error);
            sb.AppendLine("DepotDir: " + g.DepotDir + " (archivos + carpetas: " + FileOps.CountEntries(g.DepotDir) + ")");
            sb.AppendLine("ExpectedDepotFiles: " + SteamLog.LastDepotFileCount(g));
            bool? target = VersionCheck.IsTargetVersion(g);
            sb.AppendLine("IsTargetVersion: " + (target.HasValue ? target.Value.ToString() : "desconocido"));
            sb.AppendLine("SteamRunning: " + SteamConsole.IsSteamRunning());
            sb.AppendLine("SteamMainWindow: " + SteamConsole.FindMainWindow());
            sb.AppendLine("GDRunning: " + (Process.GetProcessesByName("GeometryDash").Length > 0));
            sb.AppendLine("--- eventos de download_depot en console_log ---");
            foreach (string log in new[] { Path.ChangeExtension(g.ConsoleLog, ".previous.txt"), g.ConsoleLog })
            {
                if (!File.Exists(log)) continue;
                foreach (string line in FileOps.ReadShared(log).Split('\n'))
                {
                    Match m;
                    if ((m = SteamLog.Downloading.Match(line)).Success)
                        sb.AppendLine("  inicio: " + m.Groups[1].Value + " archivos, " + SteamLog.ParseSize(m.Groups[2].Value, m.Groups[3].Value) + " bytes");
                    else if ((m = SteamLog.Complete.Match(line)).Success)
                        sb.AppendLine("  completo: " + m.Groups[1].Value + " manifest=" + m.Groups[2].Value);
                    else if (SteamLog.Failed.IsMatch(line))
                        sb.AppendLine("  fallo: " + SteamLog.StripTimestamp(line));
                }
            }
            return sb.ToString();
        }
    }
}
