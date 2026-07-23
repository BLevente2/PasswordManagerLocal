using PasswordManagerLocal.Windows.Agent.Background;

namespace PasswordManagerLocal.Windows.Agent.Hosting;

public sealed class WindowsAgentCommandLineParser
{
    public WindowsAgentCommandLineOptions Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count == 0)
            return new WindowsAgentCommandLineOptions(false);
        if (arguments.Count == 1 && string.Equals(
            arguments[0],
            WindowsStartupRegistrationConstants.BackgroundArgument,
            StringComparison.OrdinalIgnoreCase))
        {
            return new WindowsAgentCommandLineOptions(true);
        }

        throw new ArgumentException("The Windows agent command line is invalid.", nameof(arguments));
    }
}
