using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using RockCore.Core.Interfaces;
using RockCore.Core.Models;
using RockCore.Core.Services;
using RockCore.Infrastructure.Data;
using RockCore.Infrastructure.ImageAnalysis;
using RockCore.Infrastructure.Repositories;
using RockCore.Infrastructure.Services;
using RockCore.Wpf.ThreeDim;
using RockCore.Wpf.ViewModels;

namespace RockCore.Wpf;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

        try
        {
            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();
            Services = _serviceProvider;

            // 初始化岩体结构映射服务提供者
            RockStructureMapping.Initialize(() => _serviceProvider.GetService<ISpecificationService>());

            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            mainWindow.Activated += (sender, args) => { mainWindow.Activate(); mainWindow.Topmost = true; mainWindow.Topmost = false; };
            mainWindow.Show();
            mainWindow.Activate();
            mainWindow.Focus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"应用启动失败：{ex.Message}{Environment.NewLine}{Environment.NewLine}{ex.StackTrace}",
                "启动错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        MessageBox.Show(
            $"发生未处理错误：{e.Exception.Message}{Environment.NewLine}{Environment.NewLine}{e.Exception.StackTrace}",
            "运行错误",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        MessageBox.Show(
            $"发生致命错误：{ex?.Message}{Environment.NewLine}{Environment.NewLine}{ex?.StackTrace}",
            "致命错误",
            MessageBoxButton.OK,
            MessageBoxImage.Stop);
    }

    private void ConfigureServices(IServiceCollection services)
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RockCore");

        if (!Directory.Exists(appDataPath))
            Directory.CreateDirectory(appDataPath);

        var dbPath = Path.Combine(appDataPath, "rockcore.db");
        var dbContext = new RockCoreDbContext(dbPath);

        services.AddSingleton(dbContext);
        services.AddSingleton<IProjectRepository, ProjectRepository>();
        services.AddSingleton<IBoreholeRepository, BoreholeRepository>();
        services.AddSingleton<ICorePhotoRepository, CorePhotoRepository>();
        services.AddSingleton<IBoreholeIntegritySegmentRepository, BoreholeIntegritySegmentRepository>();
        services.AddSingleton<IClassificationSegmentRepository, ClassificationSegmentRepository>();
        services.AddSingleton<IProjectSummaryStatisticsRepository, ProjectSummaryStatisticsRepository>();

        // 应用服务
        services.AddSingleton<PhotoImportService>();

        // 图像分析服务（纯规则引擎，无 AI）
        services.AddSingleton<RuleEngineImageAnalyzer>();

        // 图像标注服务
        services.AddSingleton<ImageAnnotationService>();

        // 数据访问服务
        services.AddSingleton<IStructuralPlaneRepository, StructuralPlaneRepository>();
        services.AddSingleton<IAnalysisMetricsRepository, AnalysisMetricsRepository>();
        services.AddSingleton<ISpecificationService, SpecificationService>();

        // 阶段五：三段融合 + 围岩分类 + 项目统计
        services.AddSingleton<IClassificationService, ClassificationService>();
        services.AddSingleton<ProjectStatisticsService>();

        services.AddSingleton<MainViewModel>();
        services.AddTransient<MainWindow>();

        // 阶段六：三维可视化服务
        services.AddSingleton<IThreeDimSceneService, ThreeDimSceneService>();
        services.AddSingleton<ThreeDimViewModel>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
