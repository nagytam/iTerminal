namespace iTerminal.Core.Services.Translation;

/// <summary>A guess at the language of a text: its code and how sure the guess is, from 0 to 1.</summary>
public sealed record LanguageDetection(string Code, double Confidence);

/// <summary>Tells which language a text is written in, without leaving the machine.</summary>
public interface ILanguageDetector
{
    /// <summary>The most likely language, or null when the text has too little to go on (no letters, or a short Latin text with no telling word).</summary>
    LanguageDetection? Detect(string text);
}

/// <summary>
/// An offline detector, good enough to pre-select the source language and to back an engine up. Scripts
/// that belong to one language (Hangul, Thai, Hebrew, Greek, kana) are decided by the letters alone;
/// Han, Cyrillic and Arabic are narrowed by the letters only some of their languages use; the Latin
/// languages are scored on their most common words plus the letters that give them away (ß, ñ, ő, ğ…).
/// It cannot tell close relatives apart reliably on a few words, which is why an engine's own detection
/// is asked first when one is configured.
/// </summary>
public sealed class LanguageDetector : ILanguageDetector
{
    private const double MinimumScriptShare = 0.4;

    /// <summary>Common function words per Latin-script language. Lower case; a word shared by several languages simply scores for each.</summary>
    private static readonly Dictionary<string, HashSet<string>> StopWords = new()
    {
        ["en"] = ["the", "and", "of", "to", "in", "is", "that", "it", "for", "with", "was", "are", "this", "you", "not", "have", "be", "on", "as", "at", "from", "my", "what"],
        ["de"] = ["der", "die", "das", "und", "ist", "nicht", "ich", "mit", "den", "von", "zu", "ein", "eine", "auf", "für", "sie", "es", "auch", "sich", "dem", "wir", "aber", "wie"],
        ["fr"] = ["le", "la", "les", "des", "et", "est", "un", "une", "du", "que", "pas", "pour", "dans", "qui", "je", "vous", "nous", "sur", "avec", "ce", "au", "il", "mais"],
        ["es"] = ["el", "la", "los", "las", "de", "que", "y", "en", "un", "una", "es", "por", "con", "no", "para", "se", "su", "al", "lo", "como", "más", "pero", "muy"],
        ["it"] = ["il", "lo", "la", "gli", "le", "di", "che", "e", "è", "un", "una", "per", "non", "con", "del", "della", "sono", "si", "ma", "come", "più", "anche", "questo"],
        ["pt"] = ["o", "os", "as", "de", "que", "e", "do", "da", "em", "um", "uma", "para", "com", "não", "se", "na", "no", "por", "mais", "mas", "como", "foi", "são"],
        ["nl"] = ["de", "het", "een", "en", "van", "ik", "te", "dat", "die", "in", "is", "niet", "op", "met", "zijn", "voor", "maar", "ook", "als", "aan", "er", "wij", "naar"],
        ["sv"] = ["och", "att", "det", "som", "en", "är", "på", "av", "för", "med", "inte", "jag", "den", "till", "har", "om", "ett", "men", "var", "vi", "så", "kan", "från"],
        ["da"] = ["og", "at", "det", "der", "en", "er", "på", "af", "for", "med", "ikke", "jeg", "den", "til", "har", "om", "et", "men", "var", "vi", "så", "kan", "fra"],
        ["nb"] = ["og", "at", "det", "som", "en", "er", "på", "av", "for", "med", "ikke", "jeg", "den", "til", "har", "om", "et", "men", "var", "vi", "så", "kan", "fra"],
        ["fi"] = ["ja", "on", "ei", "että", "se", "oli", "hän", "mutta", "ovat", "kun", "niin", "tai", "myös", "kuin", "minä", "mitä", "joka", "tämä", "olen", "ole", "vain", "sen", "jos"],
        ["pl"] = ["i", "w", "nie", "na", "z", "się", "do", "to", "że", "jest", "o", "jak", "ale", "po", "co", "tak", "za", "od", "go", "czy", "tylko", "już", "dla"],
        ["cs"] = ["a", "je", "se", "na", "v", "že", "to", "s", "z", "do", "není", "jsem", "ale", "jak", "pro", "by", "co", "už", "tak", "být", "ten", "která", "byl"],
        ["sk"] = ["a", "je", "sa", "na", "v", "že", "to", "s", "z", "do", "nie", "som", "ale", "ako", "pre", "by", "čo", "už", "tak", "byť", "ten", "ktorá", "bol"],
        ["hu"] = ["a", "az", "és", "hogy", "nem", "is", "egy", "van", "meg", "de", "ez", "csak", "már", "volt", "mint", "még", "el", "ki", "be", "én", "te", "ő", "vagy"],
        ["ro"] = ["și", "de", "în", "la", "cu", "nu", "este", "un", "o", "că", "pe", "se", "care", "sunt", "pentru", "mai", "dar", "din", "ce", "am", "au", "să", "acest"],
        ["tr"] = ["ve", "bir", "bu", "için", "de", "da", "ne", "ben", "çok", "ile", "var", "daha", "gibi", "ama", "mi", "değil", "olarak", "o", "her", "kadar", "sen", "biz", "ki"],
        ["id"] = ["yang", "dan", "di", "ke", "dari", "ini", "itu", "untuk", "dengan", "tidak", "pada", "adalah", "saya", "akan", "juga", "atau", "ada", "oleh", "mereka", "kami", "kita", "sudah", "karena"],
        ["ms"] = ["yang", "dan", "di", "ke", "dari", "ini", "itu", "untuk", "dengan", "tidak", "pada", "adalah", "saya", "akan", "juga", "atau", "ada", "oleh", "mereka", "kami", "kita", "telah", "kerana"],
        ["vi"] = ["và", "của", "là", "có", "không", "một", "những", "được", "trong", "cho", "này", "với", "để", "các", "người", "đã", "tôi", "khi", "như", "ở", "bạn", "rất", "nhưng"],
        ["ca"] = ["el", "la", "els", "les", "de", "que", "i", "en", "un", "una", "és", "per", "amb", "no", "es", "del", "al", "com", "més", "però", "molt", "són", "aquest"],
        ["hr"] = ["i", "je", "u", "se", "na", "da", "to", "ne", "za", "su", "s", "od", "sam", "ali", "kao", "što", "će", "bi", "ili", "ima", "ovo", "koji", "još"],
        ["sl"] = ["in", "je", "v", "se", "na", "da", "to", "ne", "za", "so", "z", "od", "sem", "ali", "kot", "ki", "bo", "bi", "ima", "ta", "tudi", "še", "ker"],
        ["et"] = ["ja", "on", "ei", "et", "see", "oli", "ta", "aga", "ning", "kui", "või", "ka", "mis", "mul", "oma", "ma", "kes", "siis", "veel", "olen", "ole", "nii", "need"],
        ["lt"] = ["ir", "yra", "kad", "su", "tai", "bet", "ne", "į", "iš", "kaip", "jis", "ji", "aš", "mes", "buvo", "tik", "ar", "dar", "jau", "gali", "tas", "taip", "po"],
        ["lv"] = ["un", "ir", "ka", "ar", "tas", "bet", "nav", "es", "no", "uz", "par", "kā", "viņš", "viņa", "mēs", "bija", "tikai", "vai", "vēl", "jau", "var", "tā", "pie"],
        ["ga"] = ["an", "agus", "a", "ar", "na", "is", "sa", "le", "go", "ag", "tá", "ní", "i", "do", "mé", "tú", "sé", "sí", "bhí", "ach", "den", "faoi", "leis"],
        ["eo"] = ["la", "kaj", "de", "en", "estas", "al", "li", "ŝi", "mi", "vi", "ni", "ne", "por", "kun", "el", "sur", "tio", "ĉi", "ĉar", "sed", "ĉu", "oni", "iu"],
    };

    /// <summary>Letters that point to a Latin-script language (or a few), each worth one common word.</summary>
    private static readonly Dictionary<char, string[]> LetterHints = new()
    {
        ['ß'] = ["de"],
        ['ñ'] = ["es"],
        ['¿'] = ["es"],
        ['¡'] = ["es"],
        ['ã'] = ["pt"],
        ['õ'] = ["pt"],
        ['œ'] = ["fr"],
        ['ê'] = ["fr", "pt", "vi"],
        ['è'] = ["fr", "it", "ca"],
        ['ù'] = ["fr", "it", "vi"],
        ['ç'] = ["fr", "pt", "ca", "tr"],
        ['å'] = ["sv", "da", "nb"],
        ['ø'] = ["da", "nb"],
        ['æ'] = ["da", "nb"],
        ['ł'] = ["pl"],
        ['ą'] = ["pl"],
        ['ę'] = ["pl"],
        ['ń'] = ["pl"],
        ['ź'] = ["pl"],
        ['ż'] = ["pl"],
        ['ő'] = ["hu"],
        ['ű'] = ["hu"],
        ['ğ'] = ["tr"],
        ['ş'] = ["tr"],
        ['ı'] = ["tr"],
        ['ř'] = ["cs"],
        ['ů'] = ["cs"],
        ['ě'] = ["cs"],
        ['ľ'] = ["sk"],
        ['ĺ'] = ["sk"],
        ['ŕ'] = ["sk"],
        ['ô'] = ["sk", "fr"],
        ['ă'] = ["ro", "vi"],
        ['ț'] = ["ro"],
        ['ș'] = ["ro"],
        ['ơ'] = ["vi"],
        ['ư'] = ["vi"],
        ['đ'] = ["vi", "hr"],
        ['ĉ'] = ["eo"],
        ['ĝ'] = ["eo"],
        ['ĥ'] = ["eo"],
        ['ĵ'] = ["eo"],
        ['ŝ'] = ["eo"],
        ['ŭ'] = ["eo"],
        ['ė'] = ["lt"],
        ['ų'] = ["lt"],
        ['ū'] = ["lt", "lv"],
        ['ā'] = ["lv"],
        ['ē'] = ["lv"],
        ['ģ'] = ["lv"],
        ['ķ'] = ["lv"],
        ['ļ'] = ["lv"],
        ['ņ'] = ["lv"],
    };

    public LanguageDetection? Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var scripts = new Dictionary<Script, int>();
        int letters = 0;
        foreach (char c in text)
        {
            if (!char.IsLetter(c))
            {
                continue;
            }

            letters++;
            Script letterScript = ScriptOf(c);
            scripts[letterScript] = scripts.GetValueOrDefault(letterScript) + 1;
        }

        if (letters == 0)
        {
            return null;
        }

        // Kana is only Japanese, and a Japanese text is mostly Han characters, so any real share of kana wins over Han.
        int kana = scripts.GetValueOrDefault(Script.Kana);
        if (kana > 0 && kana + scripts.GetValueOrDefault(Script.Han) >= letters * MinimumScriptShare)
        {
            return new LanguageDetection("ja", Math.Min(0.99, 0.7 + (double)kana / letters));
        }

        (Script script, int count) = scripts.OrderByDescending(pair => pair.Value).Select(pair => (pair.Key, pair.Value)).First();
        double share = (double)count / letters;
        if (share < MinimumScriptShare)
        {
            return null;
        }

        double confidence = Math.Min(0.99, 0.5 + share / 2);
        switch (script)
        {
            case Script.Hangul: return new LanguageDetection("ko", confidence);
            case Script.Han: return new LanguageDetection("zh", confidence);
            case Script.Thai: return new LanguageDetection("th", confidence);
            case Script.Hebrew: return new LanguageDetection("he", confidence);
            case Script.Greek: return new LanguageDetection("el", confidence);
            case Script.Devanagari: return new LanguageDetection("hi", confidence);
            case Script.Bengali: return new LanguageDetection("bn", confidence);
            case Script.Arabic:
                return new LanguageDetection(text.IndexOfAny(['پ', 'چ', 'ژ', 'گ']) >= 0 ? "fa" : "ar", confidence);
            case Script.Cyrillic:
                return DetectCyrillic(text, confidence);
            case Script.Latin:
                return DetectLatin(text);
            default:
                return null;
        }
    }

    private static LanguageDetection DetectCyrillic(string text, double confidence)
    {
        if (text.IndexOfAny(['і', 'ї', 'є', 'ґ', 'І', 'Ї', 'Є', 'Ґ']) >= 0)
        {
            return new LanguageDetection("uk", confidence);
        }

        if (text.IndexOfAny(['ђ', 'ћ', 'љ', 'њ', 'џ']) >= 0)
        {
            return new LanguageDetection("sr", confidence);
        }

        // ы, э and ё exist in Russian only; otherwise Bulgarian is told from Russian by its own common words and its "ъ".
        if (text.IndexOfAny(['ы', 'э', 'ё', 'Ы', 'Э', 'Ё']) >= 0)
        {
            return new LanguageDetection("ru", confidence);
        }

        string[] words = Words(text);
        int bulgarian = words.Count(word => word is "и" or "на" or "да" or "се" or "от" or "за" or "със" or "които" or "това" or "е");
        int russian = words.Count(word => word is "и" or "в" or "не" or "на" or "что" or "он" or "с" or "это" or "как" or "по" or "но" or "из" or "от");
        return bulgarian > russian && text.Contains('ъ', StringComparison.OrdinalIgnoreCase)
            ? new LanguageDetection("bg", confidence * 0.8)
            : new LanguageDetection("ru", russian == 0 && bulgarian == 0 ? confidence * 0.7 : confidence);
    }

    private static LanguageDetection? DetectLatin(string text)
    {
        string[] words = Words(text);
        var scores = new Dictionary<string, double>();

        foreach (string word in words)
        {
            foreach ((string code, HashSet<string> stops) in StopWords)
            {
                if (stops.Contains(word))
                {
                    scores[code] = scores.GetValueOrDefault(code) + 1;
                }
            }
        }

        foreach (char c in text.ToLowerInvariant())
        {
            if (LetterHints.TryGetValue(c, out string[]? codes))
            {
                foreach (string code in codes)
                {
                    scores[code] = scores.GetValueOrDefault(code) + 1.5 / codes.Length;
                }
            }
        }

        if (scores.Count == 0)
        {
            return null;
        }

        var ranked = scores.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal).ToList();
        double best = ranked[0].Value;
        double second = ranked.Count > 1 ? ranked[1].Value : 0;
        if (best < 1.5 && words.Length > 2 && best / words.Length < 0.15)
        {
            return null;
        }

        // How far ahead of the runner-up it is, and how much of the text it explains.
        double lead = best / (best + second + 0.5);
        double coverage = Math.Min(1, best / Math.Max(1, words.Length) * 2.5);
        return new LanguageDetection(ranked[0].Key, Math.Round(Math.Clamp(lead * (0.4 + 0.6 * coverage), 0.05, 0.99), 2));
    }

    private static string[] Words(string text) =>
        text.ToLowerInvariant()
            .Split([' ', '\t', '\r', '\n', '.', ',', ';', ':', '!', '?', '(', ')', '"', '\'', '«', '»', '“', '”', '-', '—', '/'], StringSplitOptions.RemoveEmptyEntries);

    private enum Script { Latin, Han, Kana, Hangul, Cyrillic, Arabic, Hebrew, Greek, Thai, Devanagari, Bengali, Other }

    private static Script ScriptOf(char c) => c switch
    {
        >= 'A' and <= 'ɏ' => Script.Latin,
        >= 'Ḁ' and <= 'ỿ' => Script.Latin,
        >= 'Ͱ' and <= 'Ͽ' => Script.Greek,
        >= 'Ѐ' and <= 'ԯ' => Script.Cyrillic,
        >= '֐' and <= '׿' => Script.Hebrew,
        >= '؀' and <= 'ۿ' => Script.Arabic,
        >= 'ݐ' and <= 'ݿ' => Script.Arabic,
        >= 'ऀ' and <= 'ॿ' => Script.Devanagari,
        >= 'ঀ' and <= '৿' => Script.Bengali,
        >= '฀' and <= '๿' => Script.Thai,
        >= '぀' and <= 'ヿ' => Script.Kana,
        >= '㐀' and <= '䶿' => Script.Han,
        >= '一' and <= '鿿' => Script.Han,
        >= 'ᄀ' and <= 'ᇿ' => Script.Hangul,
        >= '㄰' and <= '㆏' => Script.Hangul,
        >= '가' and <= '힯' => Script.Hangul,
        _ => Script.Other,
    };
}
