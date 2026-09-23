using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Penban.Maui.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
	/// <summary>
	/// Initializes the singleton application object.  This is the first line of authored code
	/// executed, and as such is the logical equivalent of main() or WinMain().
	/// </summary>
	public App()
	{
		this.InitializeComponent();

		// Temporary: the Windows crashes end in Microsoft.UI.Xaml.dll without a managed stack, so
		// every way an exception can escape is written to a file instead.
		AppDomain.CurrentDomain.UnhandledException += (_, e) =>
			CrashLog.Write("AppDomain", e.ExceptionObject as Exception);
		TaskScheduler.UnobservedTaskException += (_, e) =>
			CrashLog.Write("UnobservedTask", e.Exception);
		UnhandledException += (_, e) =>
			CrashLog.Write("Xaml", e.Exception);
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}

