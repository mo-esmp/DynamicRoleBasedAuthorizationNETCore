using Microsoft.AspNetCore.Html;

namespace DynamicAuthorization.AspNetCore.Dashboard;

/// <summary>
///   Inline SVG icons drawn on a 20×20 grid with a 1.5px stroke, in the style of Fluent UI regular icons.
/// </summary>
internal static class DashboardIcons
{
    public static IHtmlContent Overview { get; } = Icon("""<rect x="3" y="3" width="6" height="6" rx="1.5"/><rect x="11" y="3" width="6" height="6" rx="1.5"/><rect x="3" y="11" width="6" height="6" rx="1.5"/><rect x="11" y="11" width="6" height="6" rx="1.5"/>""");

    public static IHtmlContent Endpoints { get; } = Icon("""<path d="M8 5h9M8 10h9M8 15h9"/><circle cx="4" cy="5" r="1.25"/><circle cx="4" cy="10" r="1.25"/><circle cx="4" cy="15" r="1.25"/>""");

    public static IHtmlContent Shield { get; } = Icon("""<path d="M10 2.5 3.5 5v4.6c0 3.9 2.8 7.1 6.5 8.4 3.7-1.3 6.5-4.5 6.5-8.4V5L10 2.5Z"/>""");

    public static IHtmlContent ShieldCheck { get; } = Icon("""<path d="M10 2.5 3.5 5v4.6c0 3.9 2.8 7.1 6.5 8.4 3.7-1.3 6.5-4.5 6.5-8.4V5L10 2.5Z"/><path d="m7.3 10.2 1.9 1.9 3.6-3.8"/>""");

    public static IHtmlContent People { get; } = Icon("""<circle cx="8" cy="7" r="3"/><path d="M2.5 16.5c.6-2.8 2.8-4.5 5.5-4.5s4.9 1.7 5.5 4.5"/><path d="M13 4.2a3 3 0 0 1 0 5.6M15 12.3c1.3.6 2.2 2 2.5 4.2"/>""");

    public static IHtmlContent Person { get; } = Icon("""<circle cx="10" cy="7" r="3.2"/><path d="M4 17c.7-3 3-4.8 6-4.8s5.3 1.8 6 4.8"/>""");

    public static IHtmlContent Key { get; } = Icon("""<circle cx="7" cy="13" r="3.5"/><path d="m9.5 10.5 7-7M14 6l2 2M12 8l1.5 1.5"/>""");

    public static IHtmlContent ArrowLeft { get; } = Icon("""<path d="M16 10H4m5-5-5 5 5 5"/>""");

    public static IHtmlContent Sun { get; } = Icon("""<circle cx="10" cy="10" r="3.5"/><path d="M10 2v1.5M10 16.5V18M2 10h1.5M16.5 10H18M4.3 4.3l1.1 1.1M14.6 14.6l1.1 1.1M4.3 15.7l1.1-1.1M14.6 5.4l1.1-1.1"/>""");

    public static IHtmlContent Moon { get; } = Icon("""<path d="M16.5 12.5A7 7 0 0 1 7.5 3.5a7 7 0 1 0 9 9Z"/>""");

    public static IHtmlContent Contrast { get; } = Icon("""<circle cx="10" cy="10" r="7"/><path d="M10 3a7 7 0 0 1 0 14Z" fill="currentColor"/>""");

    public static IHtmlContent Search { get; } = Icon("""<circle cx="9" cy="9" r="5.5"/><path d="m13 13 4 4"/>""");

    public static IHtmlContent Add { get; } = Icon("""<path d="M10 4v12M4 10h12"/>""");

    public static IHtmlContent Edit { get; } = Icon("""<path d="M13.5 3.5 16.5 6.5 7 16H4v-3l9.5-9.5Z"/>""");

    public static IHtmlContent Delete { get; } = Icon("""<path d="M4 6h12M8 6V4h4v2M5.5 6l.8 10.5h7.4L14.5 6"/>""");

    public static IHtmlContent CheckCircle { get; } = Icon("""<circle cx="10" cy="10" r="7.5"/><path d="m6.8 10.2 2.2 2.2 4.2-4.6"/>""");

    public static IHtmlContent DismissCircle { get; } = Icon("""<circle cx="10" cy="10" r="7.5"/><path d="m7.5 7.5 5 5m0-5-5 5"/>""");

    public static IHtmlContent Warning { get; } = Icon("""<path d="M10 3 2.5 16.5h15L10 3Z"/><path d="M10 8.5V12m0 2.3v.2"/>""");

    public static IHtmlContent Info { get; } = Icon("""<circle cx="10" cy="10" r="7.5"/><path d="M10 9v5m0-7.7v.2"/>""");

    public static IHtmlContent Lock { get; } = Icon("""<rect x="4.5" y="9" width="11" height="8" rx="1.5"/><path d="M7 9V6.5a3 3 0 0 1 6 0V9"/>""");

    public static IHtmlContent Globe { get; } = Icon("""<circle cx="10" cy="10" r="7.5"/><path d="M2.5 10h15M10 2.5c2 2.2 3 4.7 3 7.5s-1 5.3-3 7.5c-2-2.2-3-4.7-3-7.5s1-5.3 3-7.5Z"/>""");

    public static IHtmlContent ChevronRight { get; } = Icon("""<path d="m8 5 5 5-5 5"/>""");

    public static IHtmlContent ChevronLeft { get; } = Icon("""<path d="m12 5-5 5 5 5"/>""");

    private static HtmlString Icon(string paths) =>
        new($"""<svg viewBox="0 0 20 20" width="20" height="20" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">{paths}</svg>""");
}
