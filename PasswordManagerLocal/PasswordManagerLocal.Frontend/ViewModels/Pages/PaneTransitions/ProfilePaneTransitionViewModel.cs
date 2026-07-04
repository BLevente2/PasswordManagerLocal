namespace PasswordManagerLocal.Frontend.ViewModels.Pages;

public abstract class ProfilePaneTransitionViewModel
{
    protected ProfilePaneTransitionViewModel(ProfileViewModel owner)
    {
        Owner = owner;
    }

    public ProfileViewModel Owner { get; }
}
