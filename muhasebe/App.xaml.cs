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
        Hub hub;
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
            hub = new Hub(store);
            // Merkezi sunucu oturumdan bağımsızdır: kimse giriş yapmamışken de diğer cihazlara hizmet verir.
            if (store.Setting("mode") == "hub") await hub.StartAsync();
            await animation;
        }
        catch (Exception error)
        {
            store?.Dispose();
            launch?.Close();
            MessageBox.Show(error.Message, "Aden başlatılamadı");
            Shutdown(1);
            return;
        }

        try
        {
            while (true)
            {
                var login = new LoginWindow(store);
                MainWindow = login;
                login.Show();
                launch?.Close();
                launch = null;
                var session = await login.Result;
                if (session is null) break;
                var main = new MainWindow(store, hub, session);
                MainWindow = main;
                var closed = new TaskCompletionSource();
                main.Closed += (_, _) => closed.TrySetResult();
                main.Show();
                login.Close();
                await closed.Task;
                if (!main.LoggedOut) break;
            }
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "Aden");
        }
        finally
        {
            await hub.DisposeAsync();
            store.Dispose();
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        databaseLock?.Dispose();
        base.OnExit(e);
    }
}




