using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace GDDowngrade
{
    static class Gd
    {
        public const string AppId = "322170";
        public const string DepotId = "322171";
        public const string ManifestId = "3816559102876907245";
        public const string Version = "2.2081";
        public const string DownloadCommand = "download_depot " + AppId + " " + DepotId + " " + ManifestId;
        public const int DefaultDepotFileCount = 9012;
    }

    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            // --diag <archivo>: escribe un informe de detección sin abrir la ventana
            if (args.Length == 2 && args[0] == "--diag")
            {
                File.WriteAllText(args[1], Diagnostics.Report());
                return 0;
            }

            // WPF UI y sus dependencias van dentro del .exe y se cargan desde ahí
            AppDomain.CurrentDomain.AssemblyResolve += EmbeddedAssemblies.Resolve;
            return Run();
        }

        // Separado de Main para que WPF UI no se cargue antes de registrar AssemblyResolve
        [MethodImpl(MethodImplOptions.NoInlining)]
        static int Run()
        {
            var app = new System.Windows.Application();
            app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary { Theme = MainWindow.WindowsTheme() });
            app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
            return app.Run(new MainWindow());
        }
    }

    static class EmbeddedAssemblies
    {
        static readonly Dictionary<string, Assembly> loaded = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);

        public static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            string name = new AssemblyName(args.Name).Name;
            lock (loaded)
            {
                Assembly asm;
                if (loaded.TryGetValue(name, out asm)) return asm;
                using (Stream s = typeof(Program).Assembly.GetManifestResourceStream("lib." + name + ".dll"))
                {
                    if (s == null) return null;
                    var bytes = new byte[s.Length];
                    int read = 0;
                    while (read < bytes.Length)
                    {
                        int n = s.Read(bytes, read, bytes.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    asm = Assembly.Load(bytes);
                }
                loaded[name] = asm;
                return asm;
            }
        }
    }
}
