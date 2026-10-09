using Nsc.Combat;
using Nsc.Robots;
using NUnit.Framework;

/// <summary>
/// กติกาการล้มของหุ่น (FallRules) เป็นคลาสธรรมดาไม่ผูก Unity/เครือข่าย — ทดสอบได้ตรงๆ ใน EditMode
/// </summary>
public class FallRulesTests
{
    private const float Frame = 0.02f;
    private FallRules rules;

    [SetUp]
    public void SetUp() => rules = new FallRules();

    private static BalanceInput Standing() => new BalanceInput
    {
        footCount = 2, groundedFeet = 2, balancedFeet = 2, supportFoot = true,
    };

    private FallReason Run(BalanceInput input, float seconds)
    {
        FallReason reason = FallReason.None;
        for (float t = 0f; t < seconds && reason == FallReason.None; t += Frame)
            reason = rules.Evaluate(input, Frame);
        return reason;
    }

    [Test]
    public void StandingStillNeverFalls()
    {
        Assert.That(Run(Standing(), 10f), Is.EqualTo(FallReason.None));
    }

    [Test]
    public void StressAboveLimitFallsUnlessHandIsGripping()
    {
        BalanceInput input = Standing();
        input.stress = rules.maxStress;
        Assert.That(rules.Evaluate(input, Frame), Is.EqualTo(FallReason.StressOverload));

        input.handSupport = true;
        Assert.That(rules.Evaluate(input, Frame), Is.EqualTo(FallReason.None));
    }

    [Test]
    public void OneFrameOfBothFeetSteppingIsForgiven()
    {
        BalanceInput input = Standing();
        input.walkSteppingFeet = 2;
        Assert.That(rules.Evaluate(input, Frame), Is.EqualTo(FallReason.None));
        Assert.That(rules.Evaluate(Standing(), Frame), Is.EqualTo(FallReason.None));
    }

    [Test]
    public void BothFeetSteppingTooLongFalls()
    {
        BalanceInput input = Standing();
        input.walkSteppingFeet = 2;
        Assert.That(Run(input, rules.bothSteppingGrace + 0.1f), Is.EqualTo(FallReason.BothFeetStepping));
    }

    [Test]
    public void JumpProtectsAgainstSteppingRule()
    {
        BalanceInput input = Standing();
        input.walkSteppingFeet = 2;
        input.jumpProtected = true;
        Assert.That(Run(input, 1f), Is.EqualTo(FallReason.None));
    }

    [Test]
    public void FreeFallEventuallyFalls()
    {
        BalanceInput input = new BalanceInput { footCount = 2, groundedFeet = 0, balancedFeet = 1 };
        Assert.That(Run(input, rules.airborneGrace + 0.1f), Is.EqualTo(FallReason.AirborneTooLong));
    }

    [Test]
    public void ResetClearsAccumulatedAirTime()
    {
        BalanceInput air = new BalanceInput { footCount = 2, groundedFeet = 0, balancedFeet = 1 };
        Run(air, rules.airborneGrace * 0.9f);
        rules.Reset();
        Assert.That(rules.Evaluate(air, Frame), Is.EqualTo(FallReason.None));
    }

    [TestCase(Team.Red, Team.Blue, true)]
    [TestCase(Team.Red, Team.Red, false)]
    [TestCase(Team.Red, Team.Enemy, true)]
    [TestCase(Team.None, Team.Red, true)]
    public void TeamHostility(Team attacker, Team victim, bool hostile)
    {
        Assert.That(attacker.IsHostileTo(victim), Is.EqualTo(hostile));
    }
}
