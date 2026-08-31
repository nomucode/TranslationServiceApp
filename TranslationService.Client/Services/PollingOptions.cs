namespace TranslationService.Client.Services;

/// Parámetros del sondeo. Extraídos a una clase para que la política sea explícita y
/// ajustable en un sitio, en vez de quedar como números mágicos dentro del componente.
public sealed class PollingOptions
{
    /// Primer sondeo muy temprano: la mayoría de traducciones terminan en ~300 ms, así que
    /// esperar más haría que la app pareciese lenta sin motivo.
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromMilliseconds(250);

    /// Backoff progresivo: si el trabajo tarda, se espacian las consultas para no castigar
    /// al servidor con decenas de peticiones inútiles.
    public double BackoffFactor { get; init; } = 1.6;

    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// Techo absoluto: sin él, un trabajo perdido dejaría al navegador sondeando para siempre.
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);

    public TimeSpan NextDelay(TimeSpan current)
    {
        var next = TimeSpan.FromMilliseconds(current.TotalMilliseconds * BackoffFactor);

        return next > MaxDelay ? MaxDelay : next;
    }
}
