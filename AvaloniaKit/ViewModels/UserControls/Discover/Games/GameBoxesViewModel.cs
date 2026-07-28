using AvaloniaKit.Messages;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace AvaloniaKit.ViewModels.UserControls.Discover.Games;

/// <summary>游戏盒子：六款游戏的入口列表页</summary>
public partial class GameBoxesViewModel : PageViewModelBase, ISubPageViewModel
{
    public override bool ShowTabBar => false;

    [RelayCommand]
    private void GoBack()
        => WeakReferenceMessenger.Default.Send(new NavigateBackFromTetrisMessage());

    [RelayCommand]
    private void GoTetris()
        => WeakReferenceMessenger.Default.Send(new NavigateToTetrisMessage());

    [RelayCommand]
    private void GoSnake()
        => WeakReferenceMessenger.Default.Send(new NavigateToSnakeMessage());

    [RelayCommand]
    private void Go2048()
        => WeakReferenceMessenger.Default.Send(new NavigateToGame2048Message());

    [RelayCommand]
    private void GoMinesweeper()
        => WeakReferenceMessenger.Default.Send(new NavigateToMinesweeperMessage());

    [RelayCommand]
    private void GoSudoku()
        => WeakReferenceMessenger.Default.Send(new NavigateToSudokuMessage());

    [RelayCommand]
    private void GoPlane()
        => WeakReferenceMessenger.Default.Send(new NavigateToPlaneMessage());
}
