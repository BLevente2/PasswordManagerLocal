using ReactiveUI;
using System.Reactive;

namespace PasswordManagerLocal.Common.Frontend.ViewModels.Pages;

public sealed class ExportTargetProfileItemViewModel
{
    public ExportTargetProfileItemViewModel(
        Guid token,
        string displayName,
        string subtitle,
        Action<Guid> select)
    {
        Token = token;
        DisplayName = displayName;
        Subtitle = subtitle;
        SelectCommand = ReactiveCommand.Create(() => select(Token));
    }

    public Guid Token { get; }

    public string DisplayName { get; }

    public string Subtitle { get; }

    public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);

    public ReactiveCommand<Unit, Unit> SelectCommand { get; }
}
