using Cleaner.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cleaner.App.ViewModels;

/// <summary>Карточка категории («Система», «Браузеры», «Разработка») над её источниками.</summary>
public partial class CategoryViewModel : ObservableObject
{
    private readonly Action changed;
    private bool bulk;

    public CategoryViewModel(string title, IEnumerable<IJunkSource> sources, Action changed)
    {
        Title = title;
        this.changed = changed;
        Sources = sources.Select(s => new SourceViewModel(s, SourceChanged)).ToList();
        foreach (var s in Sources)
            s.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(SourceViewModel.IsScanning) or nameof(SourceViewModel.SizeText))
                {
                    OnPropertyChanged(nameof(IsScanning));
                    OnPropertyChanged(nameof(SizeText));
                }
            };
    }

    public string Title { get; }
    public IReadOnlyList<SourceViewModel> Sources { get; }

    public bool IsScanning => Sources.Any(s => s.IsScanning);
    public long TotalBytes => Sources.Sum(s => s.TotalBytes);
    public long SelectedBytes => Sources.Sum(s => s.SelectedBytes);
    public string SizeText => Format.Bytes(TotalBytes);

    public bool? IsChecked
    {
        get
        {
            var states = Sources.Where(s => s.Items.Count > 0).Select(s => s.IsChecked).Distinct().ToList();
            return states.Count == 0 ? false : states.Count == 1 ? states[0] : null;
        }
        set
        {
            bulk = true;
            foreach (var s in Sources) s.SetAll(value ?? false);
            bulk = false;
            SourceChanged();
        }
    }

    private void SourceChanged()
    {
        if (bulk) return;
        OnPropertyChanged(nameof(IsChecked));
        OnPropertyChanged(nameof(SelectedBytes));
        OnPropertyChanged(nameof(SizeText));
        changed();
    }
}
