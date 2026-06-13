using Avalonia.Media;
using PasswordManagerLocalBackend.Responses;
using ReactiveUI;
using System.Reactive;

namespace PasswordManagerLocal.ViewModels.Pages;

public sealed class PasswordItemViewModel : ReactiveObject
{
    private string _editLabel;
    private string _deleteLabel;

    private PasswordItemViewModel(
        PasswordInfoResponse password,
        string editLabel,
        string deleteLabel,
        Func<PasswordItemViewModel, Task> viewAsync,
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
        _editLabel = editLabel;
        _deleteLabel = deleteLabel;

        ViewCommand = ReactiveCommand.CreateFromTask(() => viewAsync(this));
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

    public string EditLabel
    {
        get => _editLabel;
        private set => this.RaiseAndSetIfChanged(ref _editLabel, value);
    }

    public string DeleteLabel
    {
        get => _deleteLabel;
        private set => this.RaiseAndSetIfChanged(ref _deleteLabel, value);
    }

    public ReactiveCommand<Unit, Unit> ViewCommand { get; }

    public ReactiveCommand<Unit, Unit> EditCommand { get; }

    public ReactiveCommand<Unit, Unit> DeleteCommand { get; }

    public string DescriptionPreview => string.IsNullOrWhiteSpace(Description) ? "—" : Description;

    public string CreatedAtText => CreatedAt.ToLocalTime().ToString("g");

    public string LastUpdatedAtText => LastUpdatedAt.ToLocalTime().ToString("g");

    public void ApplyActionLabels(string editLabel, string deleteLabel)
    {
        EditLabel = editLabel;
        DeleteLabel = deleteLabel;
    }

    public static PasswordItemViewModel Create(
        PasswordInfoResponse password,
        string editLabel,
        string deleteLabel,
        Func<PasswordItemViewModel, Task> viewAsync,
        Func<PasswordItemViewModel, Task> editAsync,
        Func<PasswordItemViewModel, Task> deleteAsync) =>
        new(password, editLabel, deleteLabel, viewAsync, editAsync, deleteAsync);

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
