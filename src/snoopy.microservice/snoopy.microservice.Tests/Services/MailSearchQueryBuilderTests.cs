using MailKit.Search;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public class MailSearchQueryBuilderTests
{
    private static readonly DateTime Today = new(2026, 7, 23);

    private static MailSearchCriteria Empty => new(null, null, null, null, null, null, false, false, false);

    [Fact]
    public void HasAnyCriterion_is_false_when_everything_is_blank()
        => Assert.False(MailSearchQueryBuilder.HasAnyCriterion(Empty with { Quick = "  " }));

    [Theory]
    [InlineData("quick")]
    [InlineData("from")]
    [InlineData("to")]
    [InlineData("subject")]
    [InlineData("text")]
    public void HasAnyCriterion_sees_each_text_field(string field)
    {
        var criteria = field switch
        {
            "quick" => Empty with { Quick = "x" },
            "from" => Empty with { From = "x" },
            "to" => Empty with { To = "x" },
            "subject" => Empty with { Subject = "x" },
            _ => Empty with { Text = "x" },
        };
        Assert.True(MailSearchQueryBuilder.HasAnyCriterion(criteria));
    }

    [Fact]
    public void HasAnyCriterion_sees_flags_and_date()
    {
        Assert.True(MailSearchQueryBuilder.HasAnyCriterion(Empty with { Unread = true }));
        Assert.True(MailSearchQueryBuilder.HasAnyCriterion(Empty with { Flagged = true }));
        Assert.True(MailSearchQueryBuilder.HasAnyCriterion(Empty with { HasAttachment = true }));
        Assert.True(MailSearchQueryBuilder.HasAnyCriterion(Empty with { SinceDays = 7 }));
    }

    [Fact]
    public void Quick_compiles_to_subject_or_from()
    {
        var query = MailSearchQueryBuilder.Build(Empty with { Quick = "facture" }, Today);

        var or = Assert.IsType<BinarySearchQuery>(query);
        Assert.Equal(SearchTerm.Or, or.Term);
        var subject = Assert.IsType<TextSearchQuery>(or.Left);
        Assert.Equal(SearchTerm.SubjectContains, subject.Term);
        Assert.Equal("facture", subject.Text);
        var from = Assert.IsType<TextSearchQuery>(or.Right);
        Assert.Equal(SearchTerm.FromContains, from.Term);
        Assert.Equal("facture", from.Text);
    }

    [Fact]
    public void Each_advanced_field_compiles_to_its_term()
    {
        Assert.Equal(SearchTerm.FromContains,
            Assert.IsType<TextSearchQuery>(MailSearchQueryBuilder.Build(Empty with { From = "a" }, Today)).Term);
        Assert.Equal(SearchTerm.ToContains,
            Assert.IsType<TextSearchQuery>(MailSearchQueryBuilder.Build(Empty with { To = "a" }, Today)).Term);
        Assert.Equal(SearchTerm.SubjectContains,
            Assert.IsType<TextSearchQuery>(MailSearchQueryBuilder.Build(Empty with { Subject = "a" }, Today)).Term);
        Assert.Equal(SearchTerm.BodyContains,
            Assert.IsType<TextSearchQuery>(MailSearchQueryBuilder.Build(Empty with { Text = "a" }, Today)).Term);
        Assert.Equal(SearchTerm.NotSeen,
            MailSearchQueryBuilder.Build(Empty with { Unread = true }, Today).Term);
        Assert.Equal(SearchTerm.Flagged,
            MailSearchQueryBuilder.Build(Empty with { Flagged = true }, Today).Term);
    }

    [Fact]
    public void SinceDays_compiles_to_delivered_after_today_minus_days()
    {
        var query = MailSearchQueryBuilder.Build(Empty with { SinceDays = 7 }, Today);

        var date = Assert.IsType<DateSearchQuery>(query);
        Assert.Equal(SearchTerm.DeliveredAfter, date.Term);
        Assert.Equal(Today.AddDays(-7), date.Date);
    }

    [Fact]
    public void Filled_fields_combine_with_and()
    {
        var query = MailSearchQueryBuilder.Build(
            Empty with { From = "alice", Unread = true }, Today);

        var and = Assert.IsType<BinarySearchQuery>(query);
        Assert.Equal(SearchTerm.And, and.Term);
        Assert.Equal(SearchTerm.FromContains, Assert.IsType<TextSearchQuery>(and.Left).Term);
        Assert.Equal(SearchTerm.NotSeen, and.Right.Term);
    }

    [Fact]
    public void Attachment_alone_compiles_to_all_it_is_a_post_filter()
        => Assert.Same(SearchQuery.All, MailSearchQueryBuilder.Build(Empty with { HasAttachment = true }, Today));

    [Fact]
    public void Blank_criteria_compile_to_all()
        => Assert.Same(SearchQuery.All, MailSearchQueryBuilder.Build(Empty, Today));
}
