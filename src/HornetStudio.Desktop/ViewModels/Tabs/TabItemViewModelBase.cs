namespace HornetStudio.Desktop.ViewModels.Tabs;

public abstract class TabItemViewModelBase : ViewModelBase
{
    private string _title = string.Empty;
    private string _iconKey = "IconTable";
    private bool _isClosable = true;
    private bool _isSelected;

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string IconKey
    {
        get => _iconKey;
        set => SetProperty(ref _iconKey, value);
    }

    public bool IsClosable
    {
        get => _isClosable;
        set => SetProperty(ref _isClosable, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public virtual bool CanClose() => true;
    public virtual void OnClosed() { }
}
