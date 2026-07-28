using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace AvaloniaKit.ViewModels.UserControls.Contacts;

public partial class ContactsViewModel : PageViewModelBase
{
    public override string Title => "通讯录";
    public ObservableCollection<ContactItemViewModel> Contacts { get; } = new()
    {
        new() { Name = "张三" },
        new() { Name = "李四" },
        new() { Name = "王五" },
        new() { Name = "赵六" },
        new() { Name = "钱七" },
    };

    [RelayCommand]
    private void OpenContact(ContactItemViewModel item)
    {
    }

    [RelayCommand]
    private void AddFriend()
    {
    }

    [RelayCommand]
    private void NewFriendRequest()
    {
    }

    [RelayCommand]
    private void OpenGroupChat()
    {
    }

    [RelayCommand]
    private void OpenTagList()
    {
    }
}

public partial class ContactItemViewModel : ObservableObject
{
    [ObservableProperty] private string _name = "";
    public string AvatarLetter => Name.Length > 0 ? Name[..1] : "?";
}