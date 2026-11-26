using FieldVisit.Infrastructure;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180OwnLocationRequestFilterTests
{
    private static V180B3RequestView Row(string operation, string? before, string after) =>
        new(Guid.NewGuid(),"1",1,"High","Pending",1,DateTime.UtcNow,before,after,"AAAAAAAAAAA=",operation);
    [Fact]
    public void Classifies_tax_only_and_mixed_changes_and_searches_old_address()
    {
        var tax=Row("UpdatePublishedLocation","{\"LocationName\":\"Example\",\"Address\":\"舊地址\"}","{\"LocationName\":\"Example\",\"Address\":\"舊地址\",\"TaxId\":\"12345678\"}");
        var mixed=Row("UpdatePublishedLocation",tax.BeforeJson,"{\"LocationName\":\"Example\",\"Address\":\"新地址\",\"TaxId\":\"12345678\"}");
        Assert.Equal("tax",V180OwnLocationRequestFilter.Category(tax));
        Assert.Equal("change",V180OwnLocationRequestFilter.Category(mixed));
        var f=new V180OwnLocationRequestFilter(null,null,"舊地址","tax","all");
        f.Validate();Assert.True(f.Matches(tax));Assert.False(f.Matches(mixed));
        Assert.True((f with {Kind="all"}).Matches(mixed));
    }
    [Fact]
    public void Invalid_filters_are_rejected_and_malformed_json_is_safe()
    {
        Assert.Throws<InvalidOperationException>(()=>new V180OwnLocationRequestFilter(new DateOnly(2026,11,2),new DateOnly(2026,11,1),null,"all","all").Validate());
        Assert.Throws<InvalidOperationException>(()=>new V180OwnLocationRequestFilter(null,null,null,"unknown","all").Validate());
        Assert.Equal("",V180OwnLocationRequestFilter.Read("not json")["Address"]);
        Assert.Equal("Applied",new V180OwnLocationRequestFilter(null,null,null,"all","completed").StatusCode);
    }
}
