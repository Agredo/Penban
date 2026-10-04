using CommunityToolkit.Maui;
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
        // the board a tap on it asks for (penban://board/<id>, read in Platforms/iOS/SceneDelegate.cs),
        // and the moment the app is left, which is when the copy the widget shows is brought up to
        // date.
        //
        // Asked for of the scene and of the process alike, because which of the two gets told is
        // iOS's business: since the scene manifest (see Platforms/iOS/Info.plist) the scene is what
        // the app is started as, while the process events keep coming as well. Both do the same
        // thing, and both are safe to see twice - the second look at the request finds it taken, and
        // the snapshot is written again to the same place.
        builder.ConfigureLifecycleEvents(events => events.AddiOS(ios => ios
            .OnActivated(_ => OpenRequestedBoard())
            .DidEnterBackground(_ => RefreshWidgetCopy())
            .SceneOnActivated(_ => OpenRequestedBoard())
            .SceneDidEnterBackground(_ => RefreshWidgetCopy())));
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
        services.AddSingleton<IBoardService, BoardService>();
        services.AddSingleton<ICardService, CardService>();
        services.AddSingleton<ISyncService, LocalOnlySyncService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();

        services.AddSingleton<IPreferences, MauiPreferences>();
        services.AddSingleton<ISecureStorage, MauiSecureStorage>();
        services.AddSingleton<IDialogService, MauiDialogService>();

        services.AddSingleton<IFileShareService, MauiFileShareService>();
        services.AddSingleton<IDataTransferService, DataTransferService>();
        services.AddSingleton<TransferCoordinator>();

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
        // the list: it is written again while the app is left, when no overview is on screen at all.
        services.AddSingleton(sp => new WidgetSnapshotTrigger(sp.GetRequiredService<IPreferences>()));
        services.AddSingleton<WidgetBoardLink>();

        // The version travels with every piece of feedback, so the report can be matched to a build.
        services.AddSingleton<IFeedbackService>(_ => new FeedbackService(AppVersion.Display));
    }

    private static void RegisterPages(IServiceCollection services)
    {
        services.AddTransient<BoardsViewModel>();
        services.AddTransient<BoardsPage>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<SearchViewModel>();
        services.AddTransient<FeedbackViewModel>();
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
    /// Writes the copy the widget shows again, with what the data holds now. Nobody passes the
    /// overview on the way out of a board, so a card added or a note written there would otherwise
    /// not reach the home screen until the overview is opened again.
    /// </summary>
    private static void RefreshWidgetCopy()
    {
        try
        {
            if (IPlatformApplication.Current?.Services.GetService<WidgetSnapshotTrigger>() is { } widget)
            {
                _ = widget.RefreshAsync();
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Copy of the widget not written: {exception}");
        }
    }
#endif
}
