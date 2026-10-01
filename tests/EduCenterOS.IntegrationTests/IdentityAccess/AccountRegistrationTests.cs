using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EduCenterOS.IntegrationTests.Api;
using EduCenterOS.IntegrationTests.Infrastructure;
using EduCenterOS.Modules.IdentityAccess.Domain;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace EduCenterOS.IntegrationTests.IdentityAccess;

public sealed class AccountRegistrationTests(OwnedPostgresFixture database) : IClassFixture<OwnedPostgresFixture>
{
    private static readonly DateTimeOffset Now = new(2026,10,1,12,0,0,TimeSpan.Zero);
    private const string Password = " Example-secret-password ";
    private IdentityAccessDbContext Context()=>new(IdentityAccessDbContext.Options(database.ModuleConnectionString));
    private async Task Prepare(){await database.PrepareIdentityAsync();await database.ResetIdentityAsync();}
    private sealed record Proof(Guid Id,string Token);
    private static object Request(Proof proof,string? email=null)=>new{challengeId=proof.Id,verificationProof=proof.Token,fullName="  اسم مستخدم  ",password=Password,emailAddress=email};
    private static async Task<Proof> Verify(HttpClient client,TestingApiFactory factory,string phone="01012345678")
    {
        using var issued=await client.PostAsJsonAsync("/api/v1/phone-verifications",new{phoneNumber=phone},TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK,issued.StatusCode);
        using var issue=JsonDocument.Parse(await issued.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));var id=issue.RootElement.GetProperty("challengeId").GetGuid();
        using var verified=await client.PostAsJsonAsync($"/api/v1/phone-verifications/{id}/verify",new{code=factory.Sender.Read(id).Code},TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK,verified.StatusCode);
        using var body=JsonDocument.Parse(await verified.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));return new(id,body.RootElement.GetProperty("verificationProof").GetString()!);
    }
    private static Task<HttpResponseMessage> Register(HttpClient client,Proof proof,string? email=null)=>client.PostAsJsonAsync("/api/v1/accounts",Request(proof,email),TestContext.Current.CancellationToken);
    [Theory] [InlineData(null)] [InlineData(" User@Example.COM ")]
    public async Task Registration_CreatesOwnedIdentityAndConsumesProofAtomically(string? email)
    {
        await Prepare();await using var factory=new TestingApiFactory(database,new ControlledClock(Now));using var client=factory.CreateClient();var proof=await Verify(client,factory);
        using var response=await Register(client,proof,email);Assert.Equal(HttpStatusCode.Created,response.StatusCode);Assert.Null(response.Headers.Location);Assert.Empty(response.Headers.WwwAuthenticate);
        var text=await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);using var body=JsonDocument.Parse(text);Assert.Equal(3,body.RootElement.EnumerateObject().Count());
        var accountId=body.RootElement.GetProperty("userAccountId").GetGuid();var personId=body.RootElement.GetProperty("personIdentityId").GetGuid();Assert.NotEqual(accountId,personId);Assert.Equal(7,accountId.Version);Assert.Equal(7,personId.Version);Assert.Equal(Now,body.RootElement.GetProperty("createdAtUtc").GetDateTimeOffset());
        await using var context=Context();var account=await context.Accounts.SingleAsync(TestContext.Current.CancellationToken);var person=await context.People.SingleAsync(TestContext.Current.CancellationToken);var challenge=await context.Challenges.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(accountId,account.Id);Assert.Equal(personId,account.PersonIdentityId);Assert.Equal(person.Id,account.PersonIdentityId);Assert.Equal("اسم مستخدم",person.FullName);
        Assert.Equal("+201012345678",account.PhoneNumber);Assert.Equal(account.PhoneNumber,account.NormalizedPhoneNumber);Assert.Equal(Now,account.PhoneVerifiedAtUtc);Assert.Equal(Now,account.PasswordChangedAtUtc);
        Assert.Equal(email is null ? null:"user@example.com",account.NormalizedEmailAddress);Assert.Equal(account.NormalizedEmailAddress,account.EmailAddress);Assert.Null(account.EmailVerifiedAtUtc);Assert.Equal(AccountStatus.Active,account.Status);Assert.Equal(1,account.SecurityVersion);Assert.Equal(1,account.Version);Assert.Equal(0,account.AccessFailedCount);Assert.Null(account.InitialOnboardingIntent);Assert.Null(account.LockoutEndUtc);
        Assert.Equal(ChallengeStatus.Consumed,challenge.Status);Assert.Null(challenge.ProofHash);
        using var scope=factory.Services.CreateScope();var hasher=scope.ServiceProvider.GetRequiredService<IPasswordHasher<UserAccount>>();
        Assert.Equal(PasswordVerificationResult.Success,hasher.VerifyHashedPassword(account,account.PasswordHash,Password));Assert.Equal(PasswordVerificationResult.Failed,hasher.VerifyHashedPassword(account,account.PasswordHash,Password.Trim()));
        var bytes=Convert.FromBase64String(account.PasswordHash);Assert.Equal(1,bytes[0]);Assert.Equal(210000,BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(5,4)));
        foreach(var sensitive in new[]{Password,proof.Token,account.PasswordHash,account.PhoneNumber,email ?? "absent@example.com"})
        {Assert.DoesNotContain(sensitive,text,StringComparison.Ordinal);Assert.DoesNotContain(factory.Logs.Events,entry=>entry.Message.Contains(sensitive,StringComparison.Ordinal));}
        using var replay=await Register(client,proof,email);Assert.Equal((HttpStatusCode)422,replay.StatusCode);
    }
    [Theory] [InlineData(299,true)] [InlineData(300,false)] [InlineData(301,false)]
    public async Task ProofExpiry_UsesExactAuthoritativeBoundary(int seconds,bool allowed)
    {
        await Prepare();var clock=new ControlledClock(Now);await using var factory=new TestingApiFactory(database,clock);using var client=factory.CreateClient();var proof=await Verify(client,factory);clock.UtcNow=Now.AddSeconds(seconds);
        using var response=await Register(client,proof);Assert.Equal(allowed ? HttpStatusCode.Created:(HttpStatusCode)422,response.StatusCode);
        await using var context=Context();Assert.Equal(allowed?1:0,await context.Accounts.CountAsync(TestContext.Current.CancellationToken));Assert.Equal(allowed?ChallengeStatus.Consumed:ChallengeStatus.Verified,(await context.Challenges.SingleAsync(TestContext.Current.CancellationToken)).Status);
    }
    [Fact]
    public async Task InvalidFields_AggregateSafeErrorsWithoutConsumingProof()
    {
        await Prepare();await using var factory=new TestingApiFactory(database,new ControlledClock(Now));using var client=factory.CreateClient();var proof=await Verify(client,factory);
        using var response=await client.PostAsJsonAsync("/api/v1/accounts",new{challengeId=Guid.Empty,verificationProof="sensitive-invalid-proof",fullName="x",password="short",emailAddress="bad"},TestContext.Current.CancellationToken);Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        var text=await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);using var body=JsonDocument.Parse(text);Assert.Equal(5,body.RootElement.GetProperty("errors").EnumerateObject().Count());Assert.DoesNotContain("sensitive-invalid-proof",text,StringComparison.Ordinal);
        await using var context=Context();Assert.Equal(ChallengeStatus.Verified,(await context.Challenges.SingleAsync(TestContext.Current.CancellationToken)).Status);Assert.Empty(await context.People.ToListAsync(TestContext.Current.CancellationToken));
        using var corrected=await Register(client,proof);Assert.Equal(HttpStatusCode.Created,corrected.StatusCode);
    }
    [Fact]
    public async Task WrongBindingAndUnknownChallenge_DoNotConsumeEitherProof()
    {
        await Prepare();await using var factory=new TestingApiFactory(database,new ControlledClock(Now));using var client=factory.CreateClient();var a=await Verify(client,factory);var b=await Verify(client,factory,"01112345678");
        using var swapped=await Register(client,new(a.Id,b.Token));using var unknown=await Register(client,new(Guid.CreateVersion7(),a.Token));Assert.Equal((HttpStatusCode)422,swapped.StatusCode);Assert.Equal(swapped.StatusCode,unknown.StatusCode);
        using var swappedBody=JsonDocument.Parse(await swapped.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));using var unknownBody=JsonDocument.Parse(await unknown.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));Assert.Equal(swappedBody.RootElement.GetProperty("detail").GetString(),unknownBody.RootElement.GetProperty("detail").GetString());
        await using var context=Context();Assert.All(await context.Challenges.ToListAsync(TestContext.Current.CancellationToken),row=>Assert.Equal(ChallengeStatus.Verified,row.Status));Assert.Empty(await context.People.ToListAsync(TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task Resend_InvalidatesProofForRegistration()
    {
        await Prepare();var clock=new ControlledClock(Now);await using var factory=new TestingApiFactory(database,clock);using var client=factory.CreateClient();var proof=await Verify(client,factory);clock.UtcNow=Now.AddSeconds(60);
        using var resent=await client.PostAsJsonAsync($"/api/v1/phone-verifications/{proof.Id}/resend",new{},TestContext.Current.CancellationToken);Assert.Equal(HttpStatusCode.OK,resent.StatusCode);
        using var rejected=await Register(client,proof);Assert.Equal((HttpStatusCode)422,rejected.StatusCode);await using var context=Context();Assert.Empty(await context.Accounts.ToListAsync(TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task DuplicatePhoneAndEmail_ReturnSameSafeConflictAndPreserveProof()
    {
        await Prepare();var clock=new ControlledClock(Now);await using var factory=new TestingApiFactory(database,clock);using var client=factory.CreateClient();var first=await Verify(client,factory);using var created=await Register(client,first,"user@example.com");Assert.Equal(HttpStatusCode.Created,created.StatusCode);
        clock.UtcNow=Now.AddSeconds(60);var phone=await Verify(client,factory,"+201012345678");var email=await Verify(client,factory,"01112345678");
        using var phoneConflict=await Register(client,phone);using var emailConflict=await Register(client,email," USER@EXAMPLE.COM ");Assert.Equal(HttpStatusCode.Conflict,phoneConflict.StatusCode);Assert.Equal(phoneConflict.StatusCode,emailConflict.StatusCode);
        using var a=JsonDocument.Parse(await phoneConflict.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));using var b=JsonDocument.Parse(await emailConflict.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));Assert.Equal(a.RootElement.GetProperty("detail").GetString(),b.RootElement.GetProperty("detail").GetString());Assert.Equal("IdentityAccess.RegistrationRejected",a.RootElement.GetProperty("code").GetString());
        await using var context=Context();Assert.Equal(1,await context.Accounts.CountAsync(TestContext.Current.CancellationToken));Assert.Equal(1,await context.People.CountAsync(TestContext.Current.CancellationToken));Assert.All(await context.Challenges.Where(value=>value.Id!=first.Id).ToListAsync(TestContext.Current.CancellationToken),row=>Assert.Equal(ChallengeStatus.Verified,row.Status));
    }
    [Fact]
    public async Task SameProofOverlap_HasOneWinnerWithNoOrphans()
    {
        await Prepare();var overlap=new TargetOverlapInterceptor();await using var factory=new TestingApiFactory(database,new ControlledClock(Now),overlap);using var client=factory.CreateClient();var proof=await Verify(client,factory);overlap.Enabled=true;
        var responses=await Task.WhenAll(Register(client,proof),Register(client,proof));try{Assert.Single(responses,r=>r.StatusCode==HttpStatusCode.Created);Assert.Single(responses,r=>r.StatusCode==(HttpStatusCode)422);}finally{foreach(var r in responses)r.Dispose();}
        Assert.Equal(2,overlap.Contexts.Distinct().Count());await using var context=Context();Assert.Equal(1,await context.Accounts.CountAsync(TestContext.Current.CancellationToken));Assert.Equal(1,await context.People.CountAsync(TestContext.Current.CancellationToken));Assert.Equal(ChallengeStatus.Consumed,(await context.Challenges.SingleAsync(TestContext.Current.CancellationToken)).Status);
    }
    [Fact]
    public async Task DistinctProofSamePhoneOverlap_RejectsSupersededProofWithOneAccount()
    {
        await Prepare();var clock=new ControlledClock(Now);var overlap=new TargetOverlapInterceptor();await using var factory=new TestingApiFactory(database,clock,overlap);using var client=factory.CreateClient();
        var old=await Verify(client,factory);clock.UtcNow=Now.AddSeconds(60);var current=await Verify(client,factory,"+201012345678");overlap.Enabled=true;
        var responses=await Task.WhenAll(Register(client,old),Register(client,current));try{Assert.Single(responses,r=>r.StatusCode==HttpStatusCode.Created);Assert.Single(responses,r=>r.StatusCode==(HttpStatusCode)422);}finally{foreach(var r in responses)r.Dispose();}
        Assert.Equal(2,overlap.Contexts.Distinct().Count());await using var context=Context();Assert.Equal(1,await context.Accounts.CountAsync(TestContext.Current.CancellationToken));Assert.Equal(1,await context.People.CountAsync(TestContext.Current.CancellationToken));Assert.Equal(ChallengeStatus.Invalidated,(await context.Challenges.SingleAsync(value=>value.Id==old.Id,TestContext.Current.CancellationToken)).Status);Assert.Equal(ChallengeStatus.Consumed,(await context.Challenges.SingleAsync(value=>value.Id==current.Id,TestContext.Current.CancellationToken)).Status);
    }
    [Fact]
    public async Task DistinctProofEmailOverlap_DatabaseArbitratesAndRollsBackLosingPerson()
    {
        await Prepare();var overlap=new AccountSaveOverlapInterceptor();await using var factory=new TestingApiFactory(database,new ControlledClock(Now),overlap);using var client=factory.CreateClient();var a=await Verify(client,factory);var b=await Verify(client,factory,"01112345678");
        var responses=await Task.WhenAll(Register(client,a,"race@example.com"),Register(client,b," RACE@EXAMPLE.COM "));try{Assert.Single(responses,r=>r.StatusCode==HttpStatusCode.Created);var loser=Assert.Single(responses,r=>r.StatusCode==HttpStatusCode.Conflict);var text=await loser.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);Assert.DoesNotContain("23505",text,StringComparison.Ordinal);Assert.DoesNotContain("race@example.com",text,StringComparison.Ordinal);}finally{foreach(var r in responses)r.Dispose();}
        Assert.Equal(2,overlap.Contexts.Distinct().Count());await using var context=Context();Assert.Equal(1,await context.Accounts.CountAsync(TestContext.Current.CancellationToken));Assert.Equal(1,await context.People.CountAsync(TestContext.Current.CancellationToken));var states=await context.Challenges.ToListAsync(TestContext.Current.CancellationToken);Assert.Single(states,s=>s.Status==ChallengeStatus.Consumed);Assert.Single(states,s=>s.Status==ChallengeStatus.Verified);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task CommitFault_FailsSafelyAndNeverRetriesOrClaimsAnOutcome(bool afterCommit)
    {
        await Prepare();var fault=new RegistrationCommitFaultInterceptor(afterCommit);await using var factory=new TestingApiFactory(database,new ControlledClock(Now),fault);using var client=factory.CreateClient();var proof=await Verify(client,factory);fault.Enabled=true;
        using var failed=await Register(client,proof);Assert.Equal(HttpStatusCode.InternalServerError,failed.StatusCode);Assert.Null(failed.Headers.RetryAfter);var text=await failed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);Assert.Contains("General.UnexpectedError",text,StringComparison.Ordinal);Assert.DoesNotContain("synthetic-secret-commit-fault",text,StringComparison.Ordinal);Assert.Equal(1,fault.CommitCalls);
        await using(var context=Context()){Assert.Equal(afterCommit?1:0,await context.Accounts.CountAsync(TestContext.Current.CancellationToken));Assert.Equal(afterCommit?1:0,await context.People.CountAsync(TestContext.Current.CancellationToken));var challenge=await context.Challenges.SingleAsync(TestContext.Current.CancellationToken);Assert.Equal(afterCommit?ChallengeStatus.Consumed:ChallengeStatus.Verified,challenge.Status);Assert.Equal(afterCommit,challenge.ProofHash is null);}
        Assert.DoesNotContain(factory.Logs.Events,entry=>entry.Message.Contains("synthetic-secret-commit-fault",StringComparison.Ordinal));fault.Enabled=false;
        using var retry=await Register(client,proof);Assert.Equal(afterCommit?(HttpStatusCode)422:HttpStatusCode.Created,retry.StatusCode);
    }
    [Theory]
    [InlineData("phoneNumber", "\"01012345678\"")] [InlineData("status", "\"Active\"")] [InlineData("securityVersion", "1")] [InlineData("role", "\"Owner\"")] [InlineData("institutionId", "null")] [InlineData("initialOnboardingIntent", "\"Owner\"")]
    public async Task RegistrationRejectsExtraBusinessOrSecurityFields(string member,string value)
    {
        await Prepare();await using var factory=new TestingApiFactory(database,new ControlledClock(Now));using var client=factory.CreateClient();var proof=await Verify(client,factory);var payload=JsonSerializer.Serialize(Request(proof));payload=payload[..^1]+",\""+member+"\":"+value+"}";
        using var response=await client.PostAsync("/api/v1/accounts",new StringContent(payload,Encoding.UTF8,"application/json"),TestContext.Current.CancellationToken);Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);await using var context=Context();Assert.Empty(await context.People.ToListAsync(TestContext.Current.CancellationToken));Assert.Equal(ChallengeStatus.Verified,(await context.Challenges.SingleAsync(TestContext.Current.CancellationToken)).Status);
    }
}
