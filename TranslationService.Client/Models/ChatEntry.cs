using TranslationService.Contracts.Translations;

namespace TranslationService.Client.Models;

/// Un intercambio del chat: lo que escribió el usuario y la traducción que le corresponde.
///
/// Es mutable a propósito. El sondeo actualiza la misma instancia que ya está pintada, de
/// modo que la burbuja optimista se convierte en la definitiva sin reconstruir la lista ni
/// perder la posición del scroll.
public sealed class ChatEntry(string sourceText)
{
    public string Id { get; } = Guid.NewGuid().ToString("N");

    public string SourceText { get; } = sourceText;

    public DateTimeOffset SentAt { get; } = DateTimeOffset.Now;

    public ChatEntryState State { get; private set; } = ChatEntryState.Sending;

    public string StatusUrl { get; private set; } = string.Empty;

    public string TranslatedText { get; private set; } = string.Empty;

    public string DetectedLanguage { get; private set; } = string.Empty;

    public bool WasTranslated { get; private set; }

    public long ProcessingTimeMs { get; private set; }

    public string ErrorMessage { get; private set; } = string.Empty;

    public void Accept(string statusUrl)
    {
        StatusUrl = statusUrl;
        State = ChatEntryState.Queued;
    }

    public void Apply(TranslationJobResponse job)
    {
        TranslatedText = job.TranslatedText;
        DetectedLanguage = job.DetectedLanguage;
        WasTranslated = job.WasTranslated;
        ProcessingTimeMs = job.ProcessingTimeMs;

        State = job.Status switch
        {
            TranslationJobStatus.Pending => ChatEntryState.Queued,
            TranslationJobStatus.Processing => ChatEntryState.Translating,
            TranslationJobStatus.Completed => ChatEntryState.Completed,
            TranslationJobStatus.Failed => ChatEntryState.Failed,
            _ => ChatEntryState.Failed
        };

        if (State is ChatEntryState.Failed)
        {
            ErrorMessage = job.FailureReason;
        }
    }

    public void Fail(string message)
    {
        State = ChatEntryState.Failed;
        ErrorMessage = message;
    }

    /// Distinto de Failed: el trabajo puede seguir vivo en el servidor, lo que se agotó es
    /// la paciencia del cliente. Se comunica como tal en lugar de mentir diciendo que falló.
    public void TimeOut()
    {
        State = ChatEntryState.TimedOut;
        ErrorMessage = "El servidor está tardando más de lo esperado. Puedes recargar para consultar el estado.";
    }

    public bool IsSettled => State is ChatEntryState.Completed or ChatEntryState.Failed or ChatEntryState.TimedOut;
}

public enum ChatEntryState
{
    Sending,
    Queued,
    Translating,
    Completed,
    Failed,
    TimedOut
}
