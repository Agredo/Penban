using CommunityToolkit.Maui;
using AgredoApplication.MVVM.Services.Abstractions.Navigation;
using AgredoApplication.MVVM.Services.Maui.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Penban.Data;
using Penban.Maui.Services;
using Penban.Maui.Views;
using Penban.Maui.Views.Pages;
#if IOS
using System.Diagnostics;
using Microsoft.Maui.LifecycleEvents;
#endif
using Penban.Maui.Views.Services;
using Penban.Maui.Views.Widget;
using Penban.Recognition;
using Penban.Recognition.Onnx;
using Penban.Services;
using Penban.Services.Abstractions;
using Penban.ViewModels;
using SkiaSharp.Views.Maui.Controls.Hosting;
using Syncfusion.Maui.Core.Hosting;
using IPreferences = Penban.Services.Abstractions.IPreferences;
using ISecureStorage = Penban.Services.Abstractions.ISecureStorage;
using Syncfusion.Licensing;

namespace Penban.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                fonts.AddFont("FluentSystemIcons-Regular.ttf", "FluentIcons");
            });

        builder.UseMauiCommunityToolkit();
        builder.UseSkiaSharp();
        builder.ConfigureSyncfusionCore();
        SyncfusionLicenseProvider.RegisterLicense("Ngo9BigBOggjHTQxAR8/V1JAaF5cX2pCd1p/TH5YfUNzdUVEY1ZUTXxaS1ZhSXxVdkJgWH9bcnNVT2ZfUkx9XEY=");

#if IOS
        // The home screen widget reaches the app through its lifecycle rather than through a page:
        // the board a tap on it asks for (penban://board/<id>, read in Platforms/iOS/SceneDelegate.cs).
        // The moment the app is left is the other half of it, and it is when the database file is
        // given back and the text recognition is held back - deliberately nothing more, see LeftTheApp.
        //
        // Asked for of the scene and of the process alike, because which of the two gets told is
        // iOS's business: since the scene manifest (see Platforms/iOS/Info.plist) the scene is what
        // the app is started as, while the process events keep coming as well. Both do the same
        // thing, and both are safe to see twice - the second look at the request finds it taken, and
        // holding back what is already held back is nothing to do twice.
        builder.ConfigureLifecycleEvents(events => events.AddiOS(ios => ios
            .OnActivated(_ => ComeBack())
            .WillEnterForeground(_ => ComeBack())
            .DidEnterBackground(_ => LeftTheApp())
            .SceneOnActivated(_ => ComeBack())
            .SceneWillEnterForeground(_ => ComeBack())
            .SceneDidEnterBackground(_ => LeftTheApp())));
#endif

#if DEBUG
        builder.Logging.SetMinimumLevel(LogLevel.Debug);
#endif

        RegisterServices(builder.Services);
        RegisterPages(builder.Services);

        return builder.Build();
    }

    private static void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton(_ =>
        {
            var dbPath = Path.Combine(FileSystem.AppDataDirectory, "penban.db");
            return new LiteDbContext(dbPath);
        });

        services.AddSingleton<IBoardRepository, LiteDbBoardRepository>();
        services.AddSingleton<ICardRepository, LiteDbCardRepository>();
        services.AddSingleton<IProjectRepository, LiteDbProjectRepository>();
        services.AddSingleton<IBoardService, BoardService>();
        services.AddSingleton<ICardService, CardService>();
        services.AddSingleton<IProjectService, ProjectService>();
        services.AddSingleton<ISyncService, LocalOnlySyncService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();

        services.AddSingleton<IPreferences, MauiPreferences>();
        services.AddSingleton<ISecureStorage, MauiSecureStorage>();
        services.AddSingleton<IDialogService, MauiDialogService>();

        services.AddSingleton<IFileShareService, MauiFileShareService>();
        services.AddSingleton<IDataTransferService, DataTransferService>();
        services.AddSingleton<TransferCoordinator>();

        // How a page opens a board. Transient, like the pages: a factory holds the view models a
        // board page needs beside itself, and those belong to the page that opened the board.
        services.AddTransient<BoardPageFactory>();

        // Navigation goes through routes rather than page types: a page opens another one by route
        // and is told the parameters by the package's IQueryAttributable, which needs no platform.
        // See docs/projekte.md for why the shell's own GoToAsync is used with reservations.
        services.AddSingleton<INavigationService, NavigationService>();

        // Texterkennung: the store is the database, the recogniser is the model, and the queue is what
        // keeps the two off the user's path. The model itself is named by CreateFromEmbeddedResources
        // rather than by a file path, so it cannot go missing next to the app.
        services.AddSingleton<IRecognitionStore, LiteDbRecognitionStore>();
        services.AddSingleton<IInkTextRecognizer>(_ => OnnxInkTextRecognizer.CreateFromEmbeddedResources());
        services.AddSingleton<IInkRecognitionService, InkRecognitionService>();

        // Handed to the queue as a factory, not as a value: building the recogniser loads the model,
        // and a board nobody has written on should never pay for that.
        services.AddSingleton<Func<IInkRecognitionService>>(
            provider => provider.GetRequiredService<IInkRecognitionService>);
        services.AddSingleton<IRecognitionQueue, RecognitionQueue>();

        // The widget's copy of the overview belongs to the app, not to the page that happens to hold
        // the list: a board is left behind for good when its page is gone, and the copy outlives it.
        services.AddSingleton(sp => new WidgetSnapshotTrigger(sp.GetRequiredService<IPreferences>()));
        services.AddSingleton<WidgetBoardLink>();

        // The version travels with every piece of feedback, so the report can be matched to a build.
        services.AddSingleton<IFeedbackService>(_ => new FeedbackService(AppVersion.Display));
    }

    private static void RegisterPages(IServiceCollection services)
    {
        services.AddTransient<BoardsViewModel>();
        services.AddTransient<BoardsPage>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<DashboardPage>();
        services.AddTransient<ProjectsViewModel>();
        services.AddTransient<ProjectsPage>();
        services.AddTransient<ProjectPageViewModel>();
        services.AddTransient<ProjectPage>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<SearchViewModel>();
        services.AddTransient<FeedbackViewModel>();
        services.AddTransient<BoardDetailsViewModel>();
        services.AddTransient<ProjectDetailsViewModel>();

        // The pages a route can lead to. Everything else is built by hand where it is needed, but a
        // route has only the container to ask, so these have to be known here.
        services.AddTransient<SettingsPage>();
        services.AddTransient<HelpPage>();
        services.AddTransient<PrivacyPage>();
        services.AddTransient<AboutPage>();
        services.AddTransient<FeedbackPage>();
        services.AddTransient<BoardDetailsPage>();
        services.AddTransient<ProjectDetailsPage>();
    }

#if IOS
    /// <summary>
    /// Shows the board a tapped widget asked for. The tap itself only notes which board it was (see
    /// <see cref="WidgetBoardLink"/>): it arrives while the app is still in the background, and after
    /// a cold start there is no overview to open a board on either - so whichever is ready first
    /// takes the request, this activation or the overview when it appears.
    /// </summary>
    private static async void OpenRequestedBoard()
    {
        try
        {
            if (IPlatformApplication.Current?.Services is not { } services
                || Shell.Current is not AppShell { Boards: { } overview }
                || services.GetRequiredService<WidgetBoardLink>().Take() is not { } boardId)
            {
                return;
            }

            await overview.OpenBoardAsync(boardId);
        }
        catch (Exception exception)
        {
            // A tap on the home screen is not worth a crash; the app stays where it was.
            Debug.WriteLine($"Board of the widget not opened: {exception}");
        }
    }

    /// <summary>
    /// What happens on the way out of the app: the database file is given back, and the queue that
    /// reads ink stops taking work.
    /// <para>
    /// An open LiteDB holds a lock on <c>penban.db</c> for as long as it lives, and iOS kills an app
    /// that is suspended while it holds one (<c>0xdead10cc</c>, which the crash reports of 0.5.x are
    /// full of). Giving the file back is all this has to do about it - and it is why the call is
    /// synchronous: the lock has to be gone before iOS suspends the process.
    /// </para>
    /// <para>
    /// Nothing is started here, in particular no copy of the widget. The transition into the
    /// background is watched by a clock - ten seconds of wall time and no more - and drawing a
    /// board's previews on a device that has been in use for a while blew through it
    /// (<c>0x8BADF00D</c>, FRONTBOARD). The copy is written while the app is in front instead, where
    /// nobody is waiting on ten seconds: by the trigger when the overview changes, and by the
    /// overview itself on every visit to it, which is also what carries a note written on a board to
    /// the home screen (see BoardsPage).
    /// </para>
    /// </summary>
    private static void LeftTheApp()
    {
        // First of all, and before the file goes: the copy of the overview for the home screen is
        // drawn while the app is in front, and never on this side of the transition - drawing one can
        // take seconds, and iOS allows this transition ten of them (see WidgetSnapshotTrigger.Suspend).
        Widget()?.Suspend();

        // And before the file is given back as well, so that no card is picked up in between: reading
        // one is the longest thing this app does, and it is worth even less here than in front of it.
        Recognition()?.Pause();

        try
        {
            Storage()?.Suspend();
        }
        catch (Exception exception)
        {
            // The file stays locked, which is where the app was before this existed - nothing to be
            // gained by letting a failing release reach the lifecycle callback.
            Debug.WriteLine($"Database not given back on the way out: {exception}");
        }
    }

    /// <summary>
    /// The app is in front again: the database file may be opened as soon as it is wanted, the queue
    /// may read again once it is, and a copy of the overview that was owed is written now - from here
    /// on, where drawing one costs nobody anything but the app itself.
    /// </summary>
    private static void ComeBack()
    {
        OpenRequestedBoard();
        Storage()?.Resume();
        Recognition()?.Resume();
        _ = Widget()?.Resume();
    }

    private static LiteDbContext? Storage()
        => IPlatformApplication.Current?.Services.GetService<LiteDbContext>();

    private static IRecognitionQueue? Recognition()
        => IPlatformApplication.Current?.Services.GetService<IRecognitionQueue>();

    private static WidgetSnapshotTrigger? Widget()
        => IPlatformApplication.Current?.Services.GetService<WidgetSnapshotTrigger>();
#endif
}
