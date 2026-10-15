namespace iConsole.Services;

public interface IFrequentExecutionGuardService
{
    void Execute(Action action);
}