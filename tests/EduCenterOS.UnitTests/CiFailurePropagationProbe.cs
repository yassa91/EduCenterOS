using Xunit;

namespace EduCenterOS.UnitTests;

// Temporary S01-T06 pipeline acceptance experiment; removed before merge.
public sealed class CiFailurePropagationProbe
{
    [Fact]
    public void IntentionalFailure_ProvesCiCannotReportSuccess() => Assert.Fail("Intentional CI propagation probe.");
}
