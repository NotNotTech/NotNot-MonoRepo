
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;

namespace NotNot.Validation._internal;

/// <summary>
/// Normalizer Settings
/// </summary>
public class NormalizerSettings
{
	/// <summary>
	/// Remove "." for applicable domains
	/// </summary>
	public bool RemoveDots { get; set; } = true;

	/// <summary>
	/// Remove tags (ex: '+' and '-') for applicable domains)
	/// </summary>
	public bool RemoveTags { get; set; } = true;

	/// <summary>
	/// Lowercase
	/// </summary>
	public bool LowerCase { get; set; } = true;

	/// <summary>
	/// Replace the host with the canonical version (ex: googlemail.com => gmail.com)
	/// </summary>
	public bool NormalizeHost { get; set; } = true;
}

/// <summary>
/// MIT, from https://github.com/naile/canonical-emails
/// </summary>
public static class EmailNormalizer
{

	private static NormalizerSettings DefaultSettings;

	private static readonly Dictionary<string, string> domainTags;
	private static readonly Dictionary<string, string> normalizedDomains;
	private static readonly string[] domainsWithDots;

	static EmailNormalizer()
	{
		domainTags = GetDomainsWithTags();
		domainsWithDots = GetDomainsWithDots();
		normalizedDomains = GetNormalizedDomains();
		DefaultSettings = new NormalizerSettings();
	}

	/// <summary>
	/// Configure the default settings to use when normalizing
	/// </summary>
	/// <param name="settings"></param>
	public static void ConfigureDefaults(NormalizerSettings settings)
	{
		DefaultSettings = settings;
	}

	/// <summary>
	/// Normalize email
	/// </summary>
	/// <param name="email"></param>
	/// <param name="settings"></param>
	/// <returns></returns>
	public static MailAddress Normalize(MailAddress email, NormalizerSettings settings = null)
	{
		settings = settings ?? DefaultSettings;

		var host = email.Host.ToLower();
		var user = email.User;

		if (settings.LowerCase)
			user = user.ToLowerInvariant();

		if (settings.NormalizeHost && normalizedDomains.TryGetValue(host, out var normalizedHost))
			host = normalizedHost;

		if (settings.RemoveDots && domainsWithDots.Contains(host))
			user = user.Replace(".", string.Empty);

		if (settings.RemoveTags && domainTags.TryGetValue(host, out var tag) && user.IndexOf(tag) != -1)
			user = user.Substring(0, user.IndexOf(tag));

		return new MailAddress(user + "@" + host, email.DisplayName);
	}

	private static Dictionary<string, string> GetDomainsWithTags()
	{
		return new Dictionary<string, string>
				{
                // Google
                { "gmail.com", "+" },
					 { "googlemail.com", "+" },
					 { "google.com", "+" },
                // Microsoft
                { "outlook.com", "+" },
					 { "hotmail.com", "+" },
					 { "live.com", "+" },
                // ProtonMail - https://protonmail.com/support/knowledge-base/addresses-and-aliases/
                { "protonmail.com", "+" },
					 { "protonmail.ch", "+" },
                // Fastmail - https://www.fastmail.com/help/receive/addressing.html
                { "fastmail.com", "+" },
					 { "fastmail.fm", "+" },
                // Yahoo Mail Plus accounts, per https,//en.wikipedia.org/wiki/Yahoo!_Mail#Email_domains, use hyphens - http,//www.cnet.com/forums/discussions/did-yahoo-break-disposable-email-addresses-mail-plus-395088/
                { "yahoo.com.ar" , "-" },
					 { "yahoo.com.au" , "-" },
					 { "yahoo.at" , "-" },
					 { "yahoo.be/fr" , "-" },
					 { "yahoo.be/nl" , "-" },
					 { "yahoo.com.br" , "-" },
					 { "ca.yahoo.com" , "-" },
					 { "qc.yahoo.com" , "-" },
					 { "yahoo.com.co" , "-" },
					 { "yahoo.com.hr" , "-" },
					 { "yahoo.cz" , "-" },
					 { "yahoo.dk" , "-" },
					 { "yahoo.fi" , "-" },
					 { "yahoo.fr" , "-" },
					 { "yahoo.de" , "-" },
					 { "yahoo.gr" , "-" },
					 { "yahoo.com.hk" , "-" },
					 { "yahoo.hu" , "-" },
					 { "yahoo.co.in/yahoo.in" , "-" },
					 { "yahoo.co.id" , "-" },
					 { "yahoo.ie" , "-" },
					 { "yahoo.co.il" , "-" },
					 { "yahoo.it" , "-" },
					 { "yahoo.co.jp" , "-" },
					 { "yahoo.com.my" , "-" },
					 { "yahoo.com.mx" , "-" },
					 { "yahoo.ae" , "-" },
					 { "yahoo.nl" , "-" },
					 { "yahoo.co.nz" , "-" },
					 { "yahoo.no" , "-" },
					 { "yahoo.com.ph" , "-" },
					 { "yahoo.pl" , "-" },
					 { "yahoo.pt" , "-" },
					 { "yahoo.ro" , "-" },
					 { "yahoo.ru" , "-" },
					 { "yahoo.com.sg" , "-" },
					 { "yahoo.co.za" , "-" },
					 { "yahoo.es" , "-" },
					 { "yahoo.se" , "-" },
					 { "yahoo.ch/fr" , "-" },
					 { "yahoo.ch/de" , "-" },
					 { "yahoo.com.tw" , "-" },
					 { "yahoo.co.th" , "-" },
					 { "yahoo.com.tr" , "-" },
					 { "yahoo.co.uk" , "-" },
					 { "yahoo.com" , "-" },
					 { "yahoo.com.vn" , "-" }
				};
	}

	private static string[] GetDomainsWithDots()
	{
		return new[]
		{
					 "gmail.com",
					 "googlemail.com",
					 "google.com"
				};
	}

	private static Dictionary<string, string> GetNormalizedDomains()
	{
		return new Dictionary<string, string>
				{
					 { "googlemail.com", "gmail.com" },
                // protonmail https://protonmail.com/support/knowledge-base/pm-me-addresses/
                { "pm.me" , "protonmail.com" }
				};
	}
}
