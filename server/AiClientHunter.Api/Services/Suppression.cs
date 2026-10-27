using AiClientHunter.Api.Data;
using AiClientHunter.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AiClientHunter.Api.Services;

public static class Suppression
{
    // Checks whether a lead's status is do_not_contact or its phone, email, or domain exists in the DoNotContact suppression list
    public static async Task<bool> IsBlockedAsync(AppDbContext db, Lead lead)
    {
        if (lead.Status == "do_not_contact")
            return true;

        var domain = Normalizer.Domain(lead.WebsiteUrl);

        return await db.DoNotContactList.AnyAsync(d =>
            (lead.Phone != null && d.Value == lead.Phone) ||
            (lead.Email != null && d.Value == lead.Email) ||
            (domain != null && d.Value == domain) ||
            (!string.IsNullOrWhiteSpace(lead.WebsiteUrl) && d.Value == lead.WebsiteUrl));
    }
}
