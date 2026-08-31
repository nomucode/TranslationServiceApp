using System.ComponentModel.DataAnnotations;

namespace TranslationService.Application.Configuration;

/// Política de negocio, no detalle de Azure: por eso vive en Application y no en
/// Infrastructure. El idioma destino es configurable aunque hoy sea siempre 'es'.
public sealed class TranslationOptions
{
    public const string SectionName = "Translation";

    [Required]
    public string TargetLanguage { get; init; } = "es";
}
