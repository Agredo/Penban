using CommunityToolkit.Maui;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Penban.Data;
using Penban.Maui.Services;
using Penban.Maui.Views.Pages;
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
        services.AddSingleton<Penban.Services.Abstractions.INavigation, MauiNavigation>();
        services.AddSingleton<IDialogService, MauiDialogService>();
    }

    private static void RegisterPages(IServiceCollection services)
    {
        services.AddTransient<BoardsViewModel>();
        services.AddTransient<BoardsPage>();
        services.AddTransient<BoardHostPage>();
    }
}
