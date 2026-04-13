using Avalonia.Media;
using PasswordManagerLocalBackend.Responses;
using ReactiveUI;
using System.Reactive;

namespace PasswordManagerLocal.ViewModels.Pages;

public sealed class PasswordItemViewModel : ReactiveObject
{
    private PasswordItemViewModel(
        PasswordInfoResponse password,
        Func<PasswordItemViewModel, Task> editAsync,
        Func<PasswordItemViewModel, Task> deleteAsync)
    {
        Id = password.Id;
        Name = password.Name;
        Description = password.Description;
        Color = password.Color;
        CreatedAt = password.CreatedAt;
        LastUpdatedAt = password.LastUpdatedAt;
        ColorBrush = ParseBrush(password.Color);

        EditCommand = ReactiveCommand.CreateFromTask(() => editAsync(this));
        DeleteCommand = ReactiveCommand.CreateFromTask(() => deleteAsync(this));
    }

    public Guid Id { get; }

    public string Name { get; }

    public string Description { get; }

    public string Color { get; }

    public DateTime CreatedAt { get; }

    public DateTime LastUpdatedAt { get; }

    public IBrush ColorBrush { get; }

    public ReactiveCommand<Unit, Unit> EditCommand { get; }

    public ReactiveCommand<Unit, Unit> DeleteCommand { get; }

    public string DescriptionPreview => string.IsNullOrWhiteSpace(Description) ? "—" : Description;

    public string CreatedAtText => CreatedAt.ToLocalTime().ToString("g");

    public string LastUpdatedAtText => LastUpdatedAt.ToLocalTime().ToString("g");

    public static PasswordItemViewModel Create(
        PasswordInfoResponse password,
        Func<PasswordItemViewModel, Task> editAsync,
        Func<PasswordItemViewModel, Task> deleteAsync) =>
        new(password, editAsync, deleteAsync);

    private static IBrush ParseBrush(string color)
    {
        try
        {
            return Brush.Parse(string.IsNullOrWhiteSpace(color) ? "#FFFFD700" : color);
        }
        catch
        {
            return Brush.Parse("#FFFFD700");
        }
    }
}
