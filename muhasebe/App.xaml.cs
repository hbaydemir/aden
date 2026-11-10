using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;

namespace Aden;

public partial class App : Application
{
    private Mutex? databaseLock;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        LaunchWindow? launch = null;
        Store? store = null;
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Aden", "aden.db");
            if (e.Args.Length > 0)
            {
                if (e.Args.Length != 2 || e.Args[0] != "--db")
                    throw new ArgumentException("Kullanım: Aden.Desktop --db <dosya>");
                path = e.Args[1];
            }
            path = Path.GetFullPath(path);
            var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())));
            databaseLock = new Mutex(false, $@"Local\Aden-{identity}", out var created);
            if (!created) throw new InvalidOperationException("Bu veritabanı başka bir Aden penceresinde açık.");
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            launch = new LaunchWindow();
            launch.Show();
            var animation = SystemParameters.ClientAreaAnimation ? Task.Delay(1150) : Task.CompletedTask;
            store = await Task.Run(() => new Store(path));
            await animation;
            MainWindow = new MainWindow(store);
            store = null;
            MainWindow.Show();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            launch.Close();
        }
        catch (Exception error)
        {
            store?.Dispose();
            launch?.Close();
            MessageBox.Show(error.Message, "Aden başlatılamadı");
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        databaseLock?.Dispose();
        base.OnExit(e);
    }
}




