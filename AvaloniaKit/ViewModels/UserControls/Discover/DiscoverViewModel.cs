using AvaloniaKit.Messages;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace AvaloniaKit.ViewModels.UserControls.Discover;

public partial class DiscoverViewModel : PageViewModelBase
{
    public override string Title => "发现";
    [RelayCommand]
    private void OpenMoments()
    {
    }

    [RelayCommand]
    private void OpenChannels()
    {
    }

    [RelayCommand]
    private void OpenLive()
    {
    }

    [RelayCommand]
    private void OpenScan()
    {
    }

    [RelayCommand]
    private void OpenListen()
    {
    }

    [RelayCommand]
    private void OpenRead()
    {
    }

    [RelayCommand]
    private void OpenSearch()
    {
    }

    [RelayCommand]
    private void OpenNearby()
    {
    }

    [RelayCommand]
    private void OpenGames()
    {
        WeakReferenceMessenger.Default.Send(new NavigateToGameBoxesMessage());
    }

    [RelayCommand]
    private void OpenMiniApp()
    {
    }
}