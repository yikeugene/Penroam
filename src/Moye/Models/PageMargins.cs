namespace Moye.Models;

/// <summary>Extra writing space in document DIPs; existing page content is never scaled.</summary>
public readonly record struct PageMargins(double Left, double Top, double Right, double Bottom);
