using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Zettelkasten.App.ViewModels;
using Zettelkasten.App.Views;
using Zettelkasten.Core.Abstractions;
using Zettelkasten.Infrastructure;
using Zettelkasten.Infrastructure.Data;
using Zettelkasten.Infrastructure.Embeddings;

namespace Zettelkasten.App;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var paths = AppPaths.Default();
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(Path.Combine(paths.LogDirectory, "zettelkasten-.log"),
                rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
            .CreateLogger();
        RegisterGlobalExceptionHandlers();

        try
        {
            var builder = Host.CreateApplicationBuilder();
            builder.Services.AddSerilog();
            builder.Services.AddZettelkastenApp(paths);
            _host = builder.Build();
            var services = _host.Services;

            await services.GetRequiredService<DbInitializer>().InitializeAsync();
            var backfill = services.GetRequiredService<EmbeddingBackfillService>();
            await backfill.LoadIndexAsync();

            var vm = services.GetRequiredService<MainViewModel>();
            var window = services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();
            await vm.InitializeAsync();

            // Load the model in the background; the app works keyword-only until it's ready.
            _ = Task.Run(async () =>
            {
                try
                {
                    await services.GetRequiredService<IEmbeddingService>().InitializeAsync();
                    await backfill.BackfillAsync();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Background model initialization failed");
                }
                finally
                {
                    Dispatcher.Invoke(vm.OnModelLoadCompleted);
                }
            });
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Startup failed");
            MessageBox.Show($"Zettelkasten could not start:\n\n{ex.Message}\n\nDetails are in {paths.LogDirectory}",
                "Zettelkasten", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "Unhandled UI exception");
            MessageBox.Show($"Something went wrong:\n\n{args.Exception.Message}\n\nThe app keeps running. Details are in the log.",
                "Zettelkasten", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Fatal(args.ExceptionObject as Exception, "Fatal unhandled exception");
    }
}
