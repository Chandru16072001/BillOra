namespace BillOra.Application.Common.Interfaces;

public interface IActivityLogger
{
    Task LogAsync(string action, string? details = null);
}
