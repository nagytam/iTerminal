using iConsole.Controls;
using System;

namespace iConsole.Model.Validations;

public class ValidationResult(DateTime now, bool isValid, string? errorMessage)
{
    public DateTime TimeStamp { get; private set; } = now;
    public bool IsValid { get; private set; } = isValid;
    public string? ErrorMessage { get; private set; } = errorMessage;

}
