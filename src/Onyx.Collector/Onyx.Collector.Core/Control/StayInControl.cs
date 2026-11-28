using System.Globalization;
using System.Text;
using Onyx.Collector.Core.Crypto;

namespace Onyx.Collector.Core.Control;

/// <summary>
/// Stay in control mode: decides whether opening an app needs a question first, makes the questions and checks
/// the answers. The platform (Android) notices which app comes to the front and shows the question.
/// </summary>
public sealed class StayInControl(TimeProvider time)
{
    private readonly Dictionary<string, DateTimeOffset> m_Granted = new(StringComparer.Ordinal);
    private readonly Lock m_Lock = new();

    /// <summary>Whether opening <paramref name="identifier"/> needs a question now.</summary>
    /// <param name="category">The app's category as the user sees it (<see cref="VaultSettings.CategoryOf"/>).</param>
    public bool ShouldAsk(ControlSettings settings, int? deviceId, string identifier, string? category)
    {
        if (!settings.Enabled || !settings.AppliesTo(deviceId)
            || settings.Exempt.Matches(identifier, category, emptyMatchesAll: false))
            return false;

        lock (m_Lock)
            return !(m_Granted.TryGetValue(identifier, out var until) && until > time.GetUtcNow());
    }

    /// <summary>The app may be used without a question for the grace period.</summary>
    public void Grant(ControlSettings settings, string identifier)
    {
        lock (m_Lock)
            m_Granted[identifier] = time.GetUtcNow().AddMinutes(Math.Clamp(settings.GraceMinutes, 1, 24 * 60));
    }

    /// <summary>A question from the user's subject. Own questions fall back to arithmetic while there are none.</summary>
    public static ControlQuestion NextQuestion(ControlSettings settings, Random random) => settings.Subject switch
    {
        ControlSubject.Capitals => Capital(random),
        ControlSubject.Own when settings.Questions.Count(q => !string.IsNullOrWhiteSpace(q.Answer)) > 0 =>
            settings.Questions.Where(q => !string.IsNullOrWhiteSpace(q.Answer)).ElementAt(
                random.Next(settings.Questions.Count(q => !string.IsNullOrWhiteSpace(q.Answer)))),
        _ => Arithmetic(random)
    };

    /// <summary>Compares ignoring case, accents, punctuation and extra spaces; numbers by value. <c>|</c> separates
    /// accepted alternatives.</summary>
    public static bool IsCorrect(ControlQuestion question, string? given)
    {
        var answer = Normalize(given);
        if (answer.Length == 0)
            return false;

        return question.Answer.Split('|').Select(Normalize).Any(expected => expected.Length > 0
            && (expected == answer
                || (long.TryParse(expected, NumberStyles.None, CultureInfo.InvariantCulture, out var a)
                    && long.TryParse(answer, NumberStyles.None, CultureInfo.InvariantCulture, out var b) && a == b)));
    }

    /// <summary>Lower case without accents; letters and digits only, words separated by one space.</summary>
    private static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";
        var result = new StringBuilder(text.Length);
        foreach (var c in text.ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (char.IsLetterOrDigit(c))
                result.Append(c);
            else if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && result.Length > 0 && result[^1] != ' ')
                result.Append(' ');
        }

        return result.ToString().TrimEnd();
    }

    /// <summary>Takes a moment of thought, not a calculator.</summary>
    private static ControlQuestion Arithmetic(Random random)
    {
        switch (random.Next(3))
        {
            case 0:
            {
                int a = random.Next(12, 90), b = random.Next(12, 90);
                return new ControlQuestion($"{a} + {b} = ?", (a + b).ToString(CultureInfo.InvariantCulture));
            }
            case 1:
            {
                int a = random.Next(40, 100), b = random.Next(11, a - 5);
                return new ControlQuestion($"{a} − {b} = ?", (a - b).ToString(CultureInfo.InvariantCulture));
            }
            default:
            {
                int a = random.Next(3, 10), b = random.Next(6, 20);
                return new ControlQuestion($"{a} × {b} = ?", (a * b).ToString(CultureInfo.InvariantCulture));
            }
        }
    }

    private static ControlQuestion Capital(Random random)
    {
        var (country, capital) = s_Capitals[random.Next(s_Capitals.Length)];
        return new ControlQuestion($"What is the capital of {country}?", capital);
    }

    private static readonly (string Country, string Capital)[] s_Capitals =
    [
        ("Albania", "Tirana"), ("Argentina", "Buenos Aires"), ("Australia", "Canberra"), ("Austria", "Vienna|Wien"),
        ("Belgium", "Brussels|Bruxelles|Brussel"), ("Brazil", "Brasília|Brasilia"), ("Bulgaria", "Sofia"),
        ("Canada", "Ottawa"), ("Chile", "Santiago|Santiago de Chile"), ("China", "Beijing|Peking"),
        ("Colombia", "Bogotá|Bogota"), ("Croatia", "Zagreb"), ("Czechia", "Prague|Praha|Prag"),
        ("Denmark", "Copenhagen|København|Kopenhagen"), ("Egypt", "Cairo|Kairo"), ("Estonia", "Tallinn"),
        ("Finland", "Helsinki"), ("France", "Paris"), ("Germany", "Berlin"), ("Greece", "Athens|Athen"),
        ("Hungary", "Budapest"), ("Iceland", "Reykjavík|Reykjavik"), ("India", "New Delhi|Neu-Delhi|Delhi"),
        ("Indonesia", "Jakarta"), ("Ireland", "Dublin"), ("Italy", "Rome|Roma|Rom"), ("Japan", "Tokyo|Tokio"),
        ("Kenya", "Nairobi"), ("Latvia", "Riga"), ("Lithuania", "Vilnius"), ("Luxembourg", "Luxembourg|Luxemburg"),
        ("Mexico", "Mexico City|Ciudad de México|Mexiko-Stadt"), ("Morocco", "Rabat"), ("Netherlands", "Amsterdam"),
        ("New Zealand", "Wellington"), ("Nigeria", "Abuja"), ("Norway", "Oslo"), ("Peru", "Lima"),
        ("Poland", "Warsaw|Warszawa|Warschau"), ("Portugal", "Lisbon|Lisboa|Lissabon"), ("Romania", "Bucharest|București|Bukarest"),
        ("Serbia", "Belgrade|Beograd|Belgrad"), ("Slovakia", "Bratislava"), ("Slovenia", "Ljubljana"),
        ("South Africa", "Pretoria"), ("South Korea", "Seoul"), ("Spain", "Madrid"), ("Sweden", "Stockholm"),
        ("Switzerland", "Bern|Berne"), ("Thailand", "Bangkok"), ("Turkey", "Ankara"), ("Ukraine", "Kyiv|Kiev|Kyjiw"),
        ("United Kingdom", "London"), ("United States", "Washington|Washington, D.C.|Washington DC"),
        ("Vietnam", "Hanoi|Hà Nội")
    ];
}
