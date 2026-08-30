using System.Windows;
// RenderOptions liegt in System.Windows.Media, RenderMode dagegen in
// System.Windows.Interop - fuer die Zeile unten braucht es beide.
using System.Windows.Interop;
using System.Windows.Media;
using TaskForce11Launcher.Services;
using Velopack;

namespace TaskForce11Launcher;

public partial class App : Application
{
    public App()
    {
        // Muss vor jeder anderen Startlogik laufen - hier klinken sich die Installations-
        // und Update-Haken von Velopack ein.
        VelopackApp.Build().Run();

        // WPF rendert hardwarebeschleunigt ueber eine Direct3D9Ex-Swapchain - also genau
        // die Art Oberflaeche, in die sich das Steam-Overlay bei jedem Prozess mit
        // laufender Steam-Sitzung einhaengt (ein offizielles Opt-out gibt es nicht). Ohne
        // Hardwarepfad existiert erst gar keine Swapchain, an die es sich haengen koennte.
        // Eine Launcher-Oberflaeche ist guenstig genug, dass Software-Rendering hier nicht
        // auffaellt.
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            // Der Handler kann auf einem beliebigen Thread anschlagen, ein Fenster laesst
            // sich aber nur auf dem UI-Thread oeffnen - deshalb der Umweg ueber den
            // Dispatcher.
            Dispatcher.Invoke(() => MessageWindow.Inform(
                "UNERWARTETER FEHLER",
                args.ExceptionObject.ToString() ?? "Unbekannter Fehler."));
        };

        // Updates vor allem anderen pruefen und einspielen - vor allem, bevor
        // MainViewModel steam_api64.dll in diesen Prozess laedt. Update.exe muss zum
        // Einspielen kurzzeitig jede Datei unter "current/" allein besitzen (es sichert
        // den ganzen Ordner und entpackt ihn neu); haelt der eigene Prozess - oder ein
        // Virenscanner, der auf ihn reagiert - dabei noch ein Handle, scheitert das
        // Einspielen an einer Windows-Sharing-Verletzung, und zwar bei jedem Neustart
        // aufs Neue. Die Pruefung ganz vorne, ohne offenes Fenster und ohne sonst etwas
        // Laufendes, hat die besten Chancen durchzukommen - und das Fenster geht dann nur
        // einmal auf, gleich in der aktuellen Fassung, statt aufzugehen und im Neustart
        // wieder zu verschwinden.
        var settings = new SettingsService().Load();
        if (!string.IsNullOrWhiteSpace(settings.GithubRepoUrl))
        {
            // Standardmaessig beendet WPF die Anwendung, sobald das letzte Fenster
            // schliesst. Waehrend der Update-Pruefung ist das Splash-Fenster aber das
            // einzige - sein Schliessen wuerde den Launcher beenden, bevor das
            // Hauptfenster ueberhaupt existiert.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var splash = new UpdateWindow();
            splash.Show();

            bool applied;
            try
            {
                // Progress<T> stellt die Meldungen auf den Thread zu, auf dem es erzeugt
                // wurde - hier also den UI-Thread, das Fenster darf direkt angefasst werden.
                applied = await new UpdateService(settings.GithubRepoUrl)
                    .CheckAndApplyAsync(new Progress<UpdateStatus>(splash.Report));
            }
            finally
            {
                splash.Close();
            }

            // Wird ein Update eingespielt, wartet der Updater jetzt darauf, dass dieser
            // Prozess sich beendet - erst dann tauscht er die Dateien aus und startet
            // neu. Also kein Hauptfenster mehr aufziehen, sondern zuegig beenden.
            if (applied)
            {
                Shutdown();
                return;
            }

            ShutdownMode = ShutdownMode.OnLastWindowClose;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }
}
