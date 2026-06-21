using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;

namespace PasswordManagerLocal.Views.Controls;

public sealed class LinkifiedTextBlock : TextBlock
{
    private static readonly Regex UrlRegex = new(
        @"(?<![\p{L}\p{N}_@])(?:https?://|www\.)[^\s<>""']+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    public static readonly StyledProperty<string?> SourceTextProperty =
        AvaloniaProperty.Register<LinkifiedTextBlock, string?>(nameof(SourceText));

    public string? SourceText
    {
        get => GetValue(SourceTextProperty);
        set => SetValue(SourceTextProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SourceTextProperty)
        {
            RebuildInlines();
        }
    }

    private void RebuildInlines()
    {
        Inlines.Clear();

        var text = SourceText ?? string.Empty;

        try
        {
            AddParsedInlines(text);
        }
        catch (RegexMatchTimeoutException)
        {
            Inlines.Clear();
            Inlines.Add(new Run(text));
        }
    }

    private void AddParsedInlines(string text)
    {
        var currentIndex = 0;

        foreach (Match match in UrlRegex.Matches(text))
        {
            var urlLength = GetUrlLengthWithoutTrailingPunctuation(match.Value);
            if (urlLength == 0)
            {
                continue;
            }

            if (match.Index > currentIndex)
            {
                Inlines.Add(new Run(text[currentIndex..match.Index]));
            }

            var displayedUrl = match.Value[..urlLength];
            if (TryCreateWebUri(displayedUrl, out var uri))
            {
                var link = new HyperlinkButton
                {
                    Content = displayedUrl,
                    NavigateUri = uri,
                    Padding = new Thickness(0),
                    Margin = new Thickness(0),
                    MinWidth = 0,
                    MinHeight = 0
                };

                Inlines.Add(new InlineUIContainer(link));
            }
            else
            {
                Inlines.Add(new Run(displayedUrl));
            }

            currentIndex = match.Index + urlLength;
        }

        if (currentIndex < text.Length)
        {
            Inlines.Add(new Run(text[currentIndex..]));
        }
    }

    private static bool TryCreateWebUri(string displayedUrl, out Uri uri)
    {
        var uriText = displayedUrl.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            ? $"https://{displayedUrl}"
            : displayedUrl;

        if (Uri.TryCreate(uriText, UriKind.Absolute, out var parsedUri)
            && (parsedUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || parsedUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            uri = parsedUri;
            return true;
        }

        uri = null!;
        return false;
    }

    private static int GetUrlLengthWithoutTrailingPunctuation(string value)
    {
        var length = value.Length;

        while (length > 0)
        {
            var lastCharacter = value[length - 1];

            if (lastCharacter is '.' or ',' or ';' or ':' or '!' or '?')
            {
                length--;
                continue;
            }

            if ((lastCharacter == ')' && HasMoreClosingCharacters(value, length, '(', ')'))
                || (lastCharacter == ']' && HasMoreClosingCharacters(value, length, '[', ']'))
                || (lastCharacter == '}' && HasMoreClosingCharacters(value, length, '{', '}')))
            {
                length--;
                continue;
            }

            break;
        }

        return length;
    }

    private static bool HasMoreClosingCharacters(string value, int length, char opening, char closing)
    {
        var balance = 0;

        for (var index = 0; index < length; index++)
        {
            if (value[index] == opening)
            {
                balance++;
            }
            else if (value[index] == closing)
            {
                balance--;
            }
        }

        return balance < 0;
    }
}
