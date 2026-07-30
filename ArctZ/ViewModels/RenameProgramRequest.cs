using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ArctZ.ViewModels;

public partial class RenameProgramRequest : ObservableObject
{
    internal RenameProgramRequest(string initialName, TaskCompletionSource<string?> completion)
    {
        _name = initialName;
        Completion = completion;
    }

    [ObservableProperty]
    private string _name;

    internal TaskCompletionSource<string?> Completion { get; }
}
