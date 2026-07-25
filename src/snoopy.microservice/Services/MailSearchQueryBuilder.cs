using MailKit.Search;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// Compiles search criteria into a MailKit <see cref="SearchQuery"/>. Pure: the date is
/// injected so tests never race midnight. HasAttachment is deliberately not compiled —
/// no standard IMAP criterion exists, it is post-filtered on BODYSTRUCTURE by the session.
/// </summary>
internal static class MailSearchQueryBuilder
{
    public static bool HasAnyCriterion(MailSearchCriteria criteria) =>
        !string.IsNullOrWhiteSpace(criteria.Quick)
        || !string.IsNullOrWhiteSpace(criteria.From)
        || !string.IsNullOrWhiteSpace(criteria.To)
        || !string.IsNullOrWhiteSpace(criteria.Subject)
        || !string.IsNullOrWhiteSpace(criteria.Text)
        || criteria.SinceDays is > 0
        || criteria.Unread || criteria.Flagged || criteria.HasAttachment;

    public static SearchQuery Build(MailSearchCriteria criteria, DateTime todayUtc)
    {
        var terms = new List<SearchQuery>();

        if (!string.IsNullOrWhiteSpace(criteria.Quick))
            terms.Add(SearchQuery.SubjectContains(criteria.Quick).Or(SearchQuery.FromContains(criteria.Quick)));
        if (!string.IsNullOrWhiteSpace(criteria.From)) terms.Add(SearchQuery.FromContains(criteria.From));
        if (!string.IsNullOrWhiteSpace(criteria.To)) terms.Add(SearchQuery.ToContains(criteria.To));
        if (!string.IsNullOrWhiteSpace(criteria.Subject)) terms.Add(SearchQuery.SubjectContains(criteria.Subject));
        if (!string.IsNullOrWhiteSpace(criteria.Text)) terms.Add(SearchQuery.BodyContains(criteria.Text));
        if (criteria.SinceDays is int days and > 0) terms.Add(SearchQuery.DeliveredAfter(todayUtc.AddDays(-days)));
        if (criteria.Unread) terms.Add(SearchQuery.NotSeen);
        if (criteria.Flagged) terms.Add(SearchQuery.Flagged);

        return terms.Count == 0 ? SearchQuery.All : terms.Aggregate((left, right) => left.And(right));
    }
}
