using iConsole.Localization;
﻿using iConsole.Buffers;
using iConsole.Display;
using iConsole.Model.Validations;
using iConsole.Services;

namespace iConsole.Controls;

public class ErrorMessage(
        IValidationModel validationModel, 
        IThemeService themeService) 
    : Label("Error: ", themeService)
{
    /// <summary>
    /// Nothing at all when there is no message. Visibility and text are read at different moments
    /// of a frame, and validation finishes on a background task in between — so "visible" could be
    /// answered for one result and the text for the next, and a dialog showed a bare "Error:" with
    /// nothing after it. An empty text simply blanks the row — which is also why the row stays
    /// visible without a message: a hidden control draws nothing, so hiding it would leave the last
    /// message on screen instead of clearing it.
    /// </summary>
    public override string Text
    {
        get => validationModel.ErrorMessage is { Length: > 0 } message
            ? string.Format(UiText.Get("Msg.ErrorPrefix", "Error: {0}"), message)
            : _failure.Length > 0 ? string.Format(UiText.Get("Msg.ErrorPrefix", "Error: {0}"), _failure) : string.Empty;

        // What a command that failed says: the validation model knows nothing of it. It was once
        // dropped, so a copy that threw left the dialog open with no word why. Validation's own
        // message wins while there is one; setting the text empty withdraws this one.
        set => _failure = value ?? string.Empty;
    }

    private string _failure = string.Empty;

    public override void Draw(IBuffer buffer)
    {
        GetColors(out var foregroundColor, out var backgroundColor);
        ForegroundColor = foregroundColor;
        BackgroundColor = backgroundColor;
        base.Draw(buffer);
    }

    protected virtual void GetColors(out Color foregroundColor, out Color backgroundColor)
    {
        var colorScheme = ThemeService.CurrentColorScheme;
        foregroundColor = colorScheme.Error;
        backgroundColor = colorScheme.Background;
    }
}
