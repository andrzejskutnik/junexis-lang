using JunexisLang.Compiler.IR;

namespace JunexisLang.Compiler.Tests.IR;

public sealed class TaskLifecycleRuntimeContractTests
{
    [Fact]
    public void CanonicalLifecycle_BoundsTicksWithSingleStartAndStop()
    {
        Assert.Equal(
            ["start", "tick", "tick", "tick", "stop"],
            TaskLifecycleRuntimeContract.CanonicalLifecycle(3));
    }

    [Fact]
    public void CanonicalLifecycle_RejectsNegativeCompletedTickCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TaskLifecycleRuntimeContract.CanonicalLifecycle(-1));
    }
}
