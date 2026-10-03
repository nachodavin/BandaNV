namespace BandaNV.Core.Models;

public sealed record CategoryDefinition(
    string Name,
    IReadOnlyList<string> Extensions,
    int Order);
