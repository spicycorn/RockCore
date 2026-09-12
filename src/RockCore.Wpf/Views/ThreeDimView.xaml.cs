using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using RockCore.Wpf.ThreeDim;
using RockCore.Wpf.ViewModels;

namespace RockCore.Wpf.Views;

public partial class ThreeDimView : UserControl
{
    private ModelVisual3D? _currentScene;

    public ThreeDimView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AdjustCameraLimits();
        UpdateScene();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ThreeDimViewModel oldVm)
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;

        if (e.NewValue is ThreeDimViewModel newVm)
            newVm.PropertyChanged += OnViewModelPropertyChanged;

        UpdateScene();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ThreeDimViewModel.SceneContent))
            UpdateScene();
    }

    private void UpdateScene()
    {
        if (DataContext is not ThreeDimViewModel vm) return;

        if (_currentScene != null)
        {
            MainViewport.Children.Remove(_currentScene);
            _currentScene = null;
        }

        if (vm.SceneContent != null)
        {
            _currentScene = vm.SceneContent;
            MainViewport.Children.Add(_currentScene);
            MainViewport.ZoomExtents();
        }
    }

    private void AdjustCameraLimits()
    {
        if (MainViewport.Camera is PerspectiveCamera cam)
        {
            cam.NearPlaneDistance = 0.001;
            cam.FarPlaneDistance = 100000;
        }
    }

    private void OnColorSchemeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is ThreeDimViewModel vm && sender is ComboBox cb)
        {
            if (cb.SelectedItem is ColorSchemeInfo info)
                vm.ColorScheme = info.Value;
        }
    }
}