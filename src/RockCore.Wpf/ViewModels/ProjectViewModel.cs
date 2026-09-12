using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RockCore.Core.Interfaces;
using RockCore.Core.Models;

namespace RockCore.Wpf.ViewModels;

public partial class ProjectViewModel : ObservableObject
{
    private readonly IBoreholeRepository _boreholeRepository;

    [ObservableProperty]
    private int _id;

    [ObservableProperty]
    private string _projectNumber = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _phase = string.Empty;

    [ObservableProperty]
    private double _caveAxisAzimuth;

    [ObservableProperty]
    private ObservableCollection<BoreholeViewModel> _boreholes = new();

    [ObservableProperty]
    private bool _isExpanded = true;

    public ProjectViewModel(Project project, IBoreholeRepository boreholeRepository, ICorePhotoRepository _)
    {
        _boreholeRepository = boreholeRepository;
        Id = project.Id;
        ProjectNumber = project.ProjectNumber ?? string.Empty;
        Name = project.Name;
        Phase = project.Phase;
        CaveAxisAzimuth = project.CaveAxisAzimuth;
    }

    public async Task LoadBoreholesAsync()
    {
        var boreholes = await _boreholeRepository.GetByProjectIdAsync(Id);
        Boreholes.Clear();
        foreach (var borehole in boreholes)
        {
            Boreholes.Add(new BoreholeViewModel(borehole));
        }
    }

    public Project ToModel()
    {
        return new Project
        {
            Id = Id,
            ProjectNumber = ProjectNumber,
            Name = Name,
            Phase = Phase,
            CaveAxisAzimuth = CaveAxisAzimuth,
            UpdatedAt = DateTime.Now
        };
    }
}
