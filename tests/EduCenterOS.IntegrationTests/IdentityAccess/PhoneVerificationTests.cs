using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EduCenterOS.IntegrationTests.Api;
using EduCenterOS.IntegrationTests.Infrastructure;
using EduCenterOS.Modules.IdentityAccess.Domain;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace EduCenterOS.IntegrationTests.IdentityAccess;

public sealed class PhoneVerificationTests(OwnedPostgresFixture database) : IClassFixture<OwnedPostgresFixture>
{
    private static readonly DateTimeOffset Now = new(2026,10,1,12,0,0,TimeSpan.Zero);
    private const string Root="/api/v1/phone-verifications";
    private IdentityAccessDbContext Context()=>new(IdentityAccessDbContext.Options(database.ModuleConnectionString));
    private async Task Prepare(){await database.PrepareIdentityAsync();await database.ResetIdentityAsync();}
    private static async Task<Guid> Issue(HttpClient client,string phone="01012345678")
    {
        using var response=await client.PostAsJsonAsync(Root,new{phoneNumber=phone},TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        using var body=JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(3,body.RootElement.EnumerateObject().Count());return body.RootElement.GetProperty("challengeId").GetGuid();
    }
    private static async Task<string> Verify(HttpClient client,TestingApiFactory factory,Guid id)
    {
        using var response=await client.PostAsJsonAsync($"{Root}/{id}/verify",new{code=factory.Sender.Read(id).Code},TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        using var body=JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2,body.RootElement.EnumerateObject().Count());return body.RootElement.GetProperty("verificationProof").GetString()!;
    }
    [Fact]
    public async Task IssueAndVerify_UseProductionFlowAndStoreOnlyHashesWithoutAccountCreation()
    {
        await Prepare();await using var factory=new TestingApiFactory(database,new ControlledClock(Now));using var client=factory.CreateClient();
        var id=await Issue(client);var code=factory.Sender.Read(id).Code;var proof=await Verify(client,factory,id);
        Assert.True(RegistrationCryptography.IsProof(proof));Assert.False(factory.Sender.Contains(id));
        await using var context=Context();var challenge=await context.Challenges.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("+201012345678",challenge.NormalizedTarget);Assert.Equal(ChallengeStatus.Verified,challenge.Status);Assert.Equal(32,challenge.CodeHash.Length);Assert.Equal(32,challenge.ProofHash!.Length);
        Assert.Empty(await context.Accounts.ToListAsync(TestContext.Current.CancellationToken));Assert.Empty(await context.People.ToListAsync(TestContext.Current.CancellationToken));
        Assert.DoesNotContain(factory.Logs.Events,entry=>entry.Message.Contains(code,StringComparison.Ordinal)||entry.Message.Contains(proof,StringComparison.Ordinal)||entry.Message.Contains(challenge.NormalizedTarget,StringComparison.Ordinal)||entry.Exception is not null);
        using var duplicate=await client.PostAsJsonAsync($"{Root}/{id}/verify",new{code},TestContext.Current.CancellationToken);Assert.Equal((HttpStatusCode)422,duplicate.StatusCode);
    }
    [Fact]
    public async Task InvalidInput_UsesBoundedLocationDictionaryWithoutEcho()
    {
        await Prepare();await using var factory=new TestingApiFactory(database,new ControlledClock(Now));using var client=factory.CreateClient();
        using var response=await client.PostAsJsonAsync(Root,new{phoneNumber="diagnostic-sensitive-marker"},TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);var text=await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document=JsonDocument.Parse(text);var errors=document.RootElement.GetProperty("errors");Assert.Equal(JsonValueKind.Object,errors.ValueKind);
        Assert.Equal("IdentityAccess.Input.Invalid",errors.GetProperty("body.phoneNumber")[0].GetProperty("code").GetString());Assert.DoesNotContain("diagnostic-sensitive-marker",text,StringComparison.Ordinal);
    }
    [Theory]
    [InlineData(299,true)] [InlineData(300,false)] [InlineData(301,false)]
    public async Task CodeExpiry_HttpHonorsExactBoundary(int seconds,bool allowed)
    {
        await Prepare();var clock=new ControlledClock(Now);await using var factory=new TestingApiFactory(database,clock);using var client=factory.CreateClient();var id=await Issue(client);
        clock.UtcNow=Now.AddSeconds(seconds);using var response=await client.PostAsJsonAsync($"{Root}/{id}/verify",new{code=factory.Sender.Read(id).Code},TestContext.Current.CancellationToken);
        Assert.Equal(allowed ? HttpStatusCode.OK:(HttpStatusCode)422,response.StatusCode);
    }
    [Fact]
    public async Task FailedAttempts_LockCodeAndDoNotResetAcrossResend()
    {
        await Prepare();var clock=new ControlledClock(Now);await using var factory=new TestingApiFactory(database,clock);using var client=factory.CreateClient();var id=await Issue(client);
        var actual=factory.Sender.Read(id).Code;var wrong=actual=="000000" ? "000001":"000000";
        for(var i=0;i<5;i++){using var failed=await client.PostAsJsonAsync($"{Root}/{id}/verify",new{code=wrong},TestContext.Current.CancellationToken);Assert.Equal((HttpStatusCode)422,failed.StatusCode);}
        clock.UtcNow=Now.AddSeconds(60);using var resend=await client.PostAsJsonAsync($"{Root}/{id}/resend",new{},TestContext.Current.CancellationToken);Assert.Equal(HttpStatusCode.OK,resend.StatusCode);
        using var body=JsonDocument.Parse(await resend.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));var next=body.RootElement.GetProperty("challengeId").GetGuid();var newCode=factory.Sender.Read(next).Code;wrong=newCode=="000000"?"000001":"000000";
        for(var i=0;i<5;i++){using var failed=await client.PostAsJsonAsync($"{Root}/{next}/verify",new{code=wrong},TestContext.Current.CancellationToken);Assert.Equal((HttpStatusCode)422,failed.StatusCode);}
        using var exhausted=await client.PostAsJsonAsync($"{Root}/{next}/verify",new{code=newCode},TestContext.Current.CancellationToken);Assert.Equal(HttpStatusCode.TooManyRequests,exhausted.StatusCode);
        await using var context=Context();Assert.All(await context.Challenges.ToListAsync(TestContext.Current.CancellationToken),challenge=>{Assert.Equal(5,challenge.FailedAttempts);Assert.Null(challenge.ProofHash);});
        Assert.Equal(10,(await context.Targets.SingleAsync(TestContext.Current.CancellationToken)).VerificationsUtc.Length);
    }
    [Fact]
    public async Task Resend_InvalidatesOldCodeAndVerifiedProofAndEnforcesCooldown()
    {
        await Prepare();var clock=new ControlledClock(Now);await using var factory=new TestingApiFactory(database,clock);using var client=factory.CreateClient();var id=await Issue(client);var code=factory.Sender.Read(id).Code;await Verify(client,factory,id);
        using var early=await client.PostAsJsonAsync($"{Root}/{id}/resend",new{},TestContext.Current.CancellationToken);Assert.Equal(HttpStatusCode.TooManyRequests,early.StatusCode);Assert.Equal(TimeSpan.FromSeconds(60),early.Headers.RetryAfter?.Delta);
        clock.UtcNow=Now.AddSeconds(60);using var resent=await client.PostAsJsonAsync($"{Root}/{id}/resend",new{},TestContext.Current.CancellationToken);Assert.Equal(HttpStatusCode.OK,resent.StatusCode);
        using var body=JsonDocument.Parse(await resent.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));var next=body.RootElement.GetProperty("challengeId").GetGuid();Assert.NotEqual(id,next);
        using var old=await client.PostAsJsonAsync($"{Root}/{id}/verify",new{code},TestContext.Current.CancellationToken);Assert.Equal((HttpStatusCode)422,old.StatusCode);
        await using var context=Context();var row=await context.Challenges.SingleAsync(value=>value.Id==id,TestContext.Current.CancellationToken);Assert.Equal(ChallengeStatus.Invalidated,row.Status);Assert.Null(row.ProofHash);
    }
    [Fact]
    public async Task TargetIssueWindow_SurvivesNewChallengesAndHostRestart()
    {
        await Prepare();var clock=new ControlledClock(Now);
        await using(var factory=new TestingApiFactory(database,clock))
        {
            using var client=factory.CreateClient();await Issue(client);clock.UtcNow=Now.AddSeconds(60);await Issue(client,"+201012345678");clock.UtcNow=Now.AddSeconds(120);await Issue(client,"00201012345678");
        }
        clock.UtcNow=Now.AddSeconds(180);await using var restarted=new TestingApiFactory(database,clock);using var other=restarted.CreateClient();
        using var rejected=await other.PostAsJsonAsync(Root,new{phoneNumber="01012345678"},TestContext.Current.CancellationToken);Assert.Equal(HttpStatusCode.TooManyRequests,rejected.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(720),rejected.Headers.RetryAfter?.Delta);
        await using var context=Context();Assert.Equal(3,(await context.Targets.SingleAsync(TestContext.Current.CancellationToken)).IssuesUtc.Length);
    }
    [Fact]
    public async Task DeliveryFailure_ConsumesReservationAndInvalidatesUndeliveredCode()
    {
        await Prepare();await using var factory=new TestingApiFactory(database,new ControlledClock(Now));factory.Sender.RejectDeliveries=true;using var client=factory.CreateClient();
        using var failed=await client.PostAsJsonAsync(Root,new{phoneNumber="01012345678"},TestContext.Current.CancellationToken);Assert.Equal(HttpStatusCode.ServiceUnavailable,failed.StatusCode);
        using var repeat=await client.PostAsJsonAsync(Root,new{phoneNumber="01012345678"},TestContext.Current.CancellationToken);Assert.Equal(HttpStatusCode.TooManyRequests,repeat.StatusCode);
        await using var context=Context();Assert.Equal(ChallengeStatus.Invalidated,(await context.Challenges.SingleAsync(TestContext.Current.CancellationToken)).Status);Assert.Single((await context.Targets.SingleAsync(TestContext.Current.CancellationToken)).IssuesUtc);
    }
    [Theory]
    [InlineData("{\"phoneNumber\":\"01012345678\",\"status\":\"Active\"}")]
    [InlineData("{\"PhoneNumber\":\"01012345678\"}")]
    [InlineData("{\"phoneNumber\":\"01012345678\",\"phoneNumber\":\"01112345678\"}")]
    [InlineData("{}")] [InlineData("[]")] [InlineData("{")]
    public async Task StrictBody_RejectsUnknownCaseDuplicateMissingAndMalformedFields(string payload)
    {
        await Prepare();await using var factory=new TestingApiFactory(database,new ControlledClock(Now));using var client=factory.CreateClient();
        using var response=await client.PostAsync(Root,new StringContent(payload,Encoding.UTF8,"application/json"),TestContext.Current.CancellationToken);Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        await using var context=Context();Assert.Empty(await context.Challenges.ToListAsync(TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task TransportFailures_HaveSafeCodesAndNoDevelopmentOtpEndpoint()
    {
        await Prepare();await using var factory=new TestingApiFactory(database,new ControlledClock(Now));using var client=factory.CreateClient();
        using var media=await client.PostAsync(Root,new StringContent("{}",Encoding.UTF8,"text/plain"),TestContext.Current.CancellationToken);Assert.Equal(HttpStatusCode.UnsupportedMediaType,media.StatusCode);
        using var oversized=await client.PostAsync(Root,new StringContent(new string('x',16385),Encoding.UTF8,"application/json"),TestContext.Current.CancellationToken);Assert.Equal(HttpStatusCode.RequestEntityTooLarge,oversized.StatusCode);
        using var queried=await client.PostAsJsonAsync(Root+"?purpose=RegisterAccount",new{phoneNumber="01012345678"},TestContext.Current.CancellationToken);Assert.Equal(HttpStatusCode.BadRequest,queried.StatusCode);
        using var hidden=await client.GetAsync("/api/v1/development/otp",TestContext.Current.CancellationToken);Assert.Equal(HttpStatusCode.NotFound,hidden.StatusCode);
    }
    [Fact]
    public async Task OverlappingIssue_HasOneWinnerAndOneActiveChallenge()
    {
        await Prepare();var overlap=new TargetOverlapInterceptor{Enabled=true};await using var factory=new TestingApiFactory(database,new ControlledClock(Now),overlap);using var client=factory.CreateClient();
        var calls=new[]{client.PostAsJsonAsync(Root,new{phoneNumber="01012345678"},TestContext.Current.CancellationToken),client.PostAsJsonAsync(Root,new{phoneNumber="+201012345678"},TestContext.Current.CancellationToken)};
        var responses=await Task.WhenAll(calls);try{Assert.Single(responses,response=>response.StatusCode==HttpStatusCode.OK);Assert.Single(responses,response=>response.StatusCode==HttpStatusCode.TooManyRequests);}finally{foreach(var response in responses)response.Dispose();}
        Assert.Equal(2,overlap.Contexts.Distinct().Count());await using var context=Context();Assert.Single(await context.Challenges.Where(value=>value.Status==ChallengeStatus.Active).ToListAsync(TestContext.Current.CancellationToken));Assert.Single((await context.Targets.SingleAsync(TestContext.Current.CancellationToken)).IssuesUtc);
    }
    [Fact]
    public async Task OverlappingResend_InvalidatesOldChallengeWithOneReplacementWinner()
    {
        await Prepare();var clock=new ControlledClock(Now);var overlap=new TargetOverlapInterceptor();
        await using var factory=new TestingApiFactory(database,clock,overlap);using var client=factory.CreateClient();var id=await Issue(client);
        clock.UtcNow=Now.AddSeconds(60);overlap.Enabled=true;
        var responses=await Task.WhenAll(client.PostAsJsonAsync($"{Root}/{id}/resend",new{},TestContext.Current.CancellationToken),client.PostAsJsonAsync($"{Root}/{id}/resend",new{},TestContext.Current.CancellationToken));
        try{Assert.Single(responses,response=>response.StatusCode==HttpStatusCode.OK);Assert.Single(responses,response=>response.StatusCode==(HttpStatusCode)422);}finally{foreach(var response in responses)response.Dispose();}
        Assert.Equal(2,overlap.Contexts.Distinct().Count());Assert.False(factory.Sender.Contains(id));await using var context=Context();
        Assert.Single(await context.Challenges.Where(value=>value.Status==ChallengeStatus.Active).ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(ChallengeStatus.Invalidated,(await context.Challenges.SingleAsync(value=>value.Id==id,TestContext.Current.CancellationToken)).Status);
    }
    [Fact]
    public async Task OpenApi_DescribesOnlyTheThreeActivePhoneOperations()
    {
        await Prepare();await using var factory=new TestingApiFactory(database,new ControlledClock(Now));using var client=factory.CreateClient();
        using var response=await client.GetAsync("/openapi/v1.json",TestContext.Current.CancellationToken);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        using var document=JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var paths=document.RootElement.GetProperty("paths");Assert.Equal(3,paths.EnumerateObject().Count());
        foreach(var path in paths.EnumerateObject())
        {Assert.StartsWith(Root,path.Name);Assert.Equal("post",Assert.Single(path.Value.EnumerateObject()).Name);Assert.Contains("AnonymousSecurity",path.Value.GetProperty("post").GetProperty("description").GetString(),StringComparison.Ordinal);}
    }
    [Fact]
    public async Task OverlappingVerify_ReturnsOnlyOneProof()
    {
        await Prepare();var overlap=new TargetOverlapInterceptor();await using var factory=new TestingApiFactory(database,new ControlledClock(Now),overlap);using var client=factory.CreateClient();var id=await Issue(client);var code=factory.Sender.Read(id).Code;overlap.Enabled=true;
        var responses=await Task.WhenAll(client.PostAsJsonAsync($"{Root}/{id}/verify",new{code},TestContext.Current.CancellationToken),client.PostAsJsonAsync($"{Root}/{id}/verify",new{code},TestContext.Current.CancellationToken));
        try{Assert.Single(responses,response=>response.StatusCode==HttpStatusCode.OK);Assert.Single(responses,response=>response.StatusCode==(HttpStatusCode)422);}finally{foreach(var response in responses)response.Dispose();}
        Assert.Equal(2,overlap.Contexts.Distinct().Count());await using var context=Context();Assert.Equal(ChallengeStatus.Verified,(await context.Challenges.SingleAsync(TestContext.Current.CancellationToken)).Status);Assert.Equal(2,(await context.Targets.SingleAsync(TestContext.Current.CancellationToken)).VerificationsUtc.Length);
    }
}
