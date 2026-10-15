using iConsole.Data;
using iConsole.Model;
using iConsole.Model.Validations;
using iConsole.Services;
using System.Net;

namespace iTerminal.Core.Model.Validations;

public class HostValidationModel(ITextModel model, IDateTimeProviderService dateTimeProviderService) 
    : ValidationModel<ITextModel>(dateTimeProviderService, model)
{

    public override void Validate(DateTime now)
    {
        if (Model is not null)
        {
            var host = Model.Lines[0];
            if (string.IsNullOrEmpty(host))
            {
                SetValidationResult(now, false, "Supply host!");
            }
            else
            {
                if (!host.StartsWith(Container.SSH))
                {
                    host = Container.SSH + host;
                }
                try
                {
                    var result = new Uri(host);
                    if (string.IsNullOrEmpty(result.Host))
                    {
                        SetValidationResult(now, false, "Invalid host!");
                    }
                    else
                    {
                        SetValidationResult(now, true, string.Empty);
                    }
                }
                catch (Exception ex)
                {
                    SetValidationResult(now, false, ex.Message);
                }
            }
        }
        else
        {
            SetValidationResult(now, true, string.Empty);
        }
    }
}
