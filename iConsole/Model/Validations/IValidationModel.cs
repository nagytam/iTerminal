namespace iConsole.Model.Validations;

public interface IValidationModel
{
    bool IsValid { get; }
    string ErrorMessage { get; }

    void Validate(DateTime now);
}
