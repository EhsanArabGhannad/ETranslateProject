namespace ETranslate.Web.Localization;

// DataAnnotations requires public static resource properties.
public static class UiValidation
{
    public static string EPostaZorunludur => UiText.Get("EPostaZorunludur");
    public static string GecerliBirEPostaAdresiGirin => UiText.Get("GecerliBirEPostaAdresiGirin");
    public static string SifreZorunludur => UiText.Get("SifreZorunludur");
    public static string Sifre6256KarakterOlmalidir => UiText.Get("Sifre6256KarakterOlmalidir");
    public static string SablonAdiZorunludur => UiText.Get("SablonAdiZorunludur");
    public static string SablonAdiEnFazla200KarakterOlabilir => UiText.Get("SablonAdiEnFazla200KarakterOlabilir");
    public static string UstbilgiEnFazla10000KarakterOlabilir => UiText.Get("UstbilgiEnFazla10000KarakterOlabilir");
    public static string AltbilgiEnFazla2000KarakterOlabilir => UiText.Get("AltbilgiEnFazla2000KarakterOlabilir");
    public static string BaslangicMetniEnFazla50000KarakterOlabilir => UiText.Get("BaslangicMetniEnFazla50000KarakterOlabilir");
    public static string FiligranEnFazla100KarakterOlabilir => UiText.Get("FiligranEnFazla100KarakterOlabilir");
    public static string UstbilgiHizalamaVerisiCokUzun => UiText.Get("UstbilgiHizalamaVerisiCokUzun");
    public static string AltbilgiHizalamaVerisiCokUzun => UiText.Get("AltbilgiHizalamaVerisiCokUzun");
}
