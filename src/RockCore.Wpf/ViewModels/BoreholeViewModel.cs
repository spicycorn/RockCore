using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RockCore.Core.Models;

namespace RockCore.Wpf.ViewModels;

public partial class BoreholeViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<CorePhotoViewModel> _corePhotos = new();

    [ObservableProperty]
    private int _corePhotosCount;
    [ObservableProperty]
    private int _id;

    [ObservableProperty]
    private int _projectId;

    [ObservableProperty]
    private string _number = string.Empty;

    [ObservableProperty]
    private double _orificeElevation;

    [ObservableProperty]
    private double _totalDepth;

    [ObservableProperty]
    private double _groundwaterDepth;

    [ObservableProperty]
    private double _azimuth;

    [ObservableProperty]
    private double _inclinationAngle;

    [ObservableProperty]
    private double _orificeX;

    [ObservableProperty]
    private double _orificeY;

    [ObservableProperty]
    private double _orificeZ;

    [ObservableProperty]
    private bool _isExpanded;

    public BoreholeViewModel(Borehole borehole)
    {
        Id = borehole.Id;
        ProjectId = borehole.ProjectId;
        Number = borehole.Number;
        OrificeElevation = borehole.OrificeElevation;
        TotalDepth = borehole.TotalDepth;
        GroundwaterDepth = borehole.GroundwaterDepth;
        Azimuth = borehole.Azimuth;
        InclinationAngle = borehole.InclinationAngle;
        OrificeX = borehole.OrificeX;
        OrificeY = borehole.OrificeY;
        OrificeZ = borehole.OrificeZ;
    }

    public Borehole ToModel()
    {
        return new Borehole
        {
            Id = Id,
            ProjectId = ProjectId,
            Number = Number,
            OrificeElevation = OrificeElevation,
            TotalDepth = TotalDepth,
            GroundwaterDepth = GroundwaterDepth,
            Azimuth = Azimuth,
            InclinationAngle = InclinationAngle,
            OrificeX = OrificeX,
            OrificeY = OrificeY,
            OrificeZ = OrificeZ,
            UpdatedAt = DateTime.Now
        };
    }
}
