using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace Il2CppDumper
{
    public partial class App : Application
    {
        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);
        private const int ATTACH_PARENT_PROCESS = -1;

        protected override void OnStartup(StartupEventArgs e)
        {
            if (e.Args.Length > 0)
            {
                AttachConsole(ATTACH_PARENT_PROCESS);
                var exitCode = CliDumper.Run(e.Args);
                Environment.Exit(exitCode);
            }
            base.OnStartup(e);
        }
    }
}
