namespace Industrial.Core;

/// <summary>SQL Server table names cannot be parameters. Validate the
/// identifier strictly before quoting it, and never accept dots/brackets from
/// resource configuration as part of the table name.</summary>
public static class SqlIdentifier
{
    public static string QuoteTable(string? value) => $"dbo.[{Validate(value)}]";

    public static string QuoteMySqlTable(string? value) => $"`{Validate(value)}`";

    private static string Validate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !value.All(ch => char.IsLetterOrDigit(ch) || ch == '_') ||
            char.IsDigit(value[0]))
        {
            throw new InvalidOperationException(
                $"invalid SQL table identifier '{value}'; use letters, digits and '_' only");
        }
        return value;
    }
}
