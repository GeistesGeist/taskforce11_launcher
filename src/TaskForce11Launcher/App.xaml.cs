using System.Windows;
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
            MessageBox.Show(
                $"Unerwarteter Fehler: {args.ExceptionObject}",
                "Task Force 11 Launcher",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
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
            await new UpdateService(settings.GithubRepoUrl).CheckAndApplyAsync();
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }
}
