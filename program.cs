using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Net;
using Newtonsoft.Json;
using Microsoft.Playwright;

class Program
{
    private static readonly string Url = "https://werkenbij.cogas.nl/vacatures";
    private static readonly string JsonFile = "vacancies.json";

    public static async Task Main(string[] args)
    {
        Console.WriteLine("Start vacature-controle via Playwright...");

        var currentVacancies = await GetCurrentVacanciesAsync();

        if (currentVacancies.Count == 0)
        {
            Console.WriteLine("Geen vacatures gevonden op de pagina. Afbreken.");
            return;
        }

        Console.WriteLine($"{currentVacancies.Count} vacature(s) gevonden op de pagina.");

        // Lees eerder opgeslagen vacatures uit JSON
        var oldVacancies = new List<JobVacancy>();
        if (File.Exists(JsonFile))
        {
            try
            {
                string jsonContent = File.ReadAllText(JsonFile);
                oldVacancies = JsonConvert.DeserializeObject<List<JobVacancy>>(jsonContent)
                               ?? new List<JobVacancy>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fout bij lezen JSON-bestand: {ex.Message}");
            }
        }
        else
        {
            File.WriteAllText(JsonFile, "[]");
        }

        // Vergelijk op basis van titel
        var oldTitles = new HashSet<string>(oldVacancies.Select(v => v.Title));
        var newVacancies = currentVacancies.Where(v => !oldTitles.Contains(v.Title)).ToList();

        if (newVacancies.Count > 0)
        {
            Console.WriteLine($"{newVacancies.Count} nieuwe vacature(s) gevonden!");
            SendEmailNotification(newVacancies);
        }
        else
        {
            Console.WriteLine("Geen nieuwe vacatures gevonden.");
        }

        // Sla huidige stand op
        string updatedJson = JsonConvert.SerializeObject(currentVacancies, Formatting.Indented);
        File.WriteAllText(JsonFile, updatedJson);
        Console.WriteLine("vacancies.json bijgewerkt.");
    }

    private static async Task<List<JobVacancy>> GetCurrentVacanciesAsync()
    {
        var vacancies = new List<JobVacancy>();

        try
        {
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true
            });

            // Stap 1: haal alle titels op van de overzichtspagina
            var overviewPage = await browser.NewPageAsync();
            await overviewPage.GotoAsync(Url, new PageGotoOptions
            {
                Timeout = 30_000,
                WaitUntil = WaitUntilState.NetworkIdle
            });

            var headings = await overviewPage.QuerySelectorAllAsync("h4");
            var titles = new List<string>();

            foreach (var heading in headings)
            {
                string title = (await heading.InnerTextAsync())?.Trim() ?? string.Empty;
                if (!string.IsNullOrEmpty(title) && title.Length > 3)
                    titles.Add(title);
            }

            Console.WriteLine($"{titles.Count} vacaturetitel(s) gevonden op de overzichtspagina.");
            await overviewPage.CloseAsync();

            // Stap 2: klik op elke kaart en vang de URL op
            foreach (var title in titles)
            {
                try
                {
                    var detailPage = await browser.NewPageAsync();

                    // Ga terug naar overzicht
                    await detailPage.GotoAsync(Url, new PageGotoOptions
                    {
                        Timeout = 30_000,
                        WaitUntil = WaitUntilState.NetworkIdle
                    });

                    // Klik op de h4 met deze exacte titel
                    var card = await detailPage.QuerySelectorAsync($"h4 >> text=\"{title}\"");
                    if (card == null)
                    {
                        Console.WriteLine($"  Kaart niet gevonden voor: {title}");
                        vacancies.Add(new JobVacancy { Title = title, Link = Url });
                        await detailPage.CloseAsync();
                        continue;
                    }

                    // Wacht op navigatie na de klik
                    await Task.WhenAll(
                        detailPage.WaitForNavigationAsync(new PageWaitForNavigationOptions
                        {
                            Timeout = 15_000,
                            WaitUntil = WaitUntilState.NetworkIdle
                        }),
                        card.ClickAsync()
                    );

                    string finalUrl = detailPage.Url;
                    Console.WriteLine($"  Gevonden: {title} -> {finalUrl}");
                    vacancies.Add(new JobVacancy { Title = title, Link = finalUrl });

                    await detailPage.CloseAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Fout bij ophalen link voor '{title}': {ex.Message}");
                    // Voeg toch toe met fallback-URL
                    vacancies.Add(new JobVacancy { Title = title, Link = Url });
                }
            }
        }
        catch (TimeoutException)
        {
            Console.WriteLine("Time-out bij laden van de vacaturepagina.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fout bij ophalen via Playwright: {ex.Message}");
        }

        return vacancies;
    }

    private static void SendEmailNotification(List<JobVacancy> newVacancies)
    {
        string? senderEmail    = Environment.GetEnvironmentVariable("MAIL_USERNAME");
        string? senderPassword = Environment.GetEnvironmentVariable("MAIL_PASSWORD");
        string? receiverEmail  = Environment.GetEnvironmentVariable("MAIL_TO");

        if (string.IsNullOrEmpty(senderEmail) ||
            string.IsNullOrEmpty(senderPassword) ||
            string.IsNullOrEmpty(receiverEmail))
        {
            Console.WriteLine("E-mailconfiguratie ontbreekt in omgevingsvariabelen.");
            return;
        }

        try
        {
            string vacancyListHtml = string.Join("\n", newVacancies.Select(v =>
                $"  <li><a href=\"{v.Link}\">{System.Net.WebUtility.HtmlEncode(v.Title)}</a></li>"
            ));

            string htmlBody = $"""
                <html>
                <body style="font-family: Arial, sans-serif; color: #222;">
                  <h2>Nieuwe Cogas vacature(s) gevonden!</h2>
                  <p>Er {(newVacancies.Count == 1 ? "is" : "zijn")} <strong>{newVacancies.Count}</strong>
                     nieuwe vacature(s) beschikbaar bij Cogas:</p>
                  <ul>
                {vacancyListHtml}
                  </ul>
                  <p style="color: #666; font-size: 0.9em;">
                    Bekijk alle vacatures op
                    <a href="https://werkenbij.cogas.nl/vacatures">werkenbij.cogas.nl</a>.
                  </p>
                </body>
                </html>
                """;

            var mailMessage = new MailMessage
            {
                From       = new MailAddress(senderEmail, "Cogas Vacature Melder"),
                Subject    = $"Nieuwe Cogas vacature(s) gevonden! ({newVacancies.Count})",
                Body       = htmlBody,
                IsBodyHtml = true
            };
            mailMessage.To.Add(receiverEmail);

            using var smtpClient = new SmtpClient("smtp.gmail.com")
            {
                Port        = 587,
                Credentials = new NetworkCredential(senderEmail, senderPassword),
                EnableSsl   = true
            };

            smtpClient.Send(mailMessage);
            Console.WriteLine("E-mail succesvol verzonden!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fout bij verzenden e-mail: {ex.Message}");
        }
    }
}

public class JobVacancy
{
    [JsonProperty("title")]
    public string Title { get; set; } = string.Empty;

    [JsonProperty("link")]
    public string Link { get; set; } = string.Empty;
}