using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows;

namespace VRAMOptimizerApp {
    public class App : Application { }

    public static class Program {
        [STAThread]
        public static void Main() {
            try {
                bool isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
                if (!isAdmin) {
                    try {
                        ProcessStartInfo psi = new ProcessStartInfo {
                            FileName = Process.GetCurrentProcess().MainModule.FileName,
                            UseShellExecute = true,
                            Verb = "runas"
                        };
                        Process.Start(psi);
                    } catch {
                        MessageBox.Show("Administrator privileges are required to configure WDDM graphics memory settings.",
                            "Elevation Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    return;
                }

                App app = new App();
                VRAMOptimizerWindow mainWindow = new VRAMOptimizerWindow();
                app.Run(mainWindow.Window);
            } catch (Exception ex) {
                MessageBox.Show("Fatal Error on Startup:\n\n" + ex.ToString(), "VRAM Optimizer Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
