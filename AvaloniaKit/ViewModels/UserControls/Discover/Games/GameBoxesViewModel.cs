using AvaloniaKit.ViewModels.Messages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace AvaloniaKit.ViewModels.UserControls.Discover.Games
{
    public partial class GameBoxesViewModel : ObservableObject
    {
        public GameBoxesViewModel()
        {

        }


        [RelayCommand]
        private void GoTetris()
        {
            WeakReferenceMessenger.Default.Send(new NavigateToTetrisMessages());
        }

        [RelayCommand]
        private void GoBack()
        {
            WeakReferenceMessenger.Default.Send(new NavigateBackFromTetrisMessage());
        }


        [RelayCommand]
        private void GoSnake()
        {
            WeakReferenceMessenger.Default.Send(new NavigateToSnakeMessages());
        }

        [RelayCommand]
        private void Go2048()
        {
            WeakReferenceMessenger.Default.Send(new NavigateToGame2048Messages());
        }

        [RelayCommand]
        private void GoMinesweeper()
        {
            WeakReferenceMessenger.Default.Send(new NavigateToMinesweeperMessages());
        }

        [RelayCommand]
        private void GoSudoku()
        {
            WeakReferenceMessenger.Default.Send(new NavigateToSudokuMessages());
        }

        [RelayCommand]
        private void GoPlane()
        {
            WeakReferenceMessenger.Default.Send(new NavigateToPlaneMessages());
        }

    }
}
