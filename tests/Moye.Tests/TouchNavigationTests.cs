using System.Windows;
using Moye.Controls;

namespace Moye.Tests;

public sealed class TouchNavigationTests
{
    [Theory]
    [InlineData(60)]
    [InlineData(120)]
    [InlineData(240)]
    public void PacketCoalescingRetainsEveryDipAtAnyInputFrequency(int packetRate)
    {
        var session = new TouchNavigationSession();
        session.BeginContact(7, new(300, 700), 0);
        Assert.False(session.HasPendingFrame);
        var total = default(Vector);
        var frameCount = 0;
        for (var packet = 1; packet <= packetRate; packet++)
        {
            var time = packet * 1000d / packetRate;
            session.MoveContact(7, new(300 - time * .1, 700 - time * .4), time);
            if (packet % (packetRate / 60) != 0) continue;
            Assert.True(session.TryTakeFrame(time, out var frame));
            Assert.False(frame.IsInertial);
            Assert.Equal(1, frame.Scale);
            total += frame.ScrollDelta;
            frameCount++;
            Assert.False(session.TryTakeFrame(time, out _));
        }
        Assert.Equal(60, frameCount);
        Assert.Equal(100, total.X, 8);
        Assert.Equal(400, total.Y, 8);
    }

    [Fact]
    public void LatestPositionsProduceOneExactCombinedPinchAndPan()
    {
        var session = new TouchNavigationSession();
        session.BeginContact(1, new(100, 100), 0);
        session.BeginContact(2, new(200, 100), 1);
        session.MoveContact(1, new(80, 130), 10);
        session.MoveContact(2, new(280, 130), 12);
        Assert.True(session.TryTakeFrame(16, out var frame));
        Assert.Equal(new Point(150, 100), frame.PreviousCenter);
        Assert.Equal(new Point(180, 130), frame.Center);
        Assert.Equal(new Vector(-30, -30), frame.ScrollDelta);
        Assert.Equal(2, frame.Scale, 10);
        Assert.False(session.HasPendingFrame);
    }

    [Fact]
    public void ContactChangesAndThirdFingerPauseHaveNoDiscontinuity()
    {
        var session = new TouchNavigationSession();
        session.BeginContact(1, new(100, 300), 0);
        session.MoveContact(1, new(100, 280), 20);
        Assert.True(session.TryTakeFrame(20, out _));
        session.BeginContact(2, new(200, 280), 21);
        Assert.False(session.TryTakeFrame(22, out _));
        session.BeginContact(3, new(300, 280), 23);
        session.MoveContact(1, new(0, 100), 30);
        session.MoveContact(2, new(200, 100), 30);
        Assert.Equal(3, session.Count);
        Assert.False(session.TryTakeFrame(31, out _));
        session.EndContact(3, 32);
        Assert.False(session.TryTakeFrame(33, out _));
        session.MoveContact(1, new(0, 90), 34);
        session.MoveContact(2, new(200, 90), 34);
        Assert.True(session.TryTakeFrame(35, out var frame));
        Assert.Equal(new Vector(0, 10), frame.ScrollDelta);
        Assert.Equal(1, frame.Scale);
        session.EndContact(2, 36);
        Assert.False(session.TryTakeFrame(37, out _));
        session.EndContact(1, 38);
        Assert.False(session.IsInertiaActive);
    }

    [Fact]
    public void SmallPinchDistancesPanWithoutUnstableScale()
    {
        var session = new TouchNavigationSession();
        session.BeginContact(1, new(0, 0), 0);
        session.BeginContact(2, new(10, 0), 0);
        session.MoveContact(2, new(50, 0), 10);
        Assert.True(session.TryTakeFrame(16, out var frame));
        Assert.Equal(1, frame.Scale);
        Assert.Equal(new Vector(-20, 0), frame.ScrollDelta);
        session.MoveContact(2, new(10, 0), 20);
        Assert.True(session.TryTakeFrame(32, out frame));
        Assert.Equal(1, frame.Scale);
    }

    [Fact]
    public void FinalReleasePositionCanBeFlushedBeforeStartingInertia()
    {
        var session = new TouchNavigationSession();
        session.BeginContact(1, new(50, 300), 0);
        session.MoveContact(1, new(50, 280), 20);
        Assert.True(session.TryTakeFrame(20, out var first));
        session.MoveContact(1, new(50, 265), 30);
        Assert.True(session.TryTakeFrame(30, out var release));
        session.EndContact(1, 30);
        Assert.Equal(new Vector(0, 35), first.ScrollDelta + release.ScrollDelta);
        Assert.True(session.IsInertiaActive);
        Assert.True(session.TryTakeFrame(40, out var fling));
        Assert.Equal(new Point(50, 265), fling.PreviousCenter);
        Assert.True(fling.IsInertial);
        Assert.True(fling.ScrollDelta.Y > 0);
    }

    [Theory]
    [InlineData(0, 20, 120)] // Stale movement followed by a hold.
    [InlineData(0, 10, 10)] // Too little time to estimate release speed.
    public void PauseAndVeryShortContactDoNotFling(double start, double movement, double release)
    {
        var session = new TouchNavigationSession();
        session.BeginContact(1, new(0, 200), start);
        session.MoveContact(1, new(0, 100), movement);
        session.TryTakeFrame(movement, out _);
        session.MoveContact(1, new(0, 100), release);
        session.EndContact(1, release);
        Assert.False(session.IsInertiaActive);
        Assert.False(session.HasPendingFrame);
    }

    [Fact]
    public void TinyMovementAndExplicitCancellationDoNotFling()
    {
        var session = new TouchNavigationSession();
        session.BeginContact(1, new(0, 100), 0);
        session.MoveContact(1, new(0, 98), 30);
        session.TryTakeFrame(30, out _);
        session.EndContact(1, 30);
        Assert.False(session.IsInertiaActive);
        StartFling(session, allowInertia: false);
        Assert.False(session.IsInertiaActive);
    }

    [Fact]
    public void InertiaDistanceIsIndependentOfFrameRateThroughItsFinalPartialFrame()
    {
        var at60 = InertiaTravel(60);
        var at120 = InertiaTravel(120);
        Assert.Equal(at60.X, at120.X, 8);
        Assert.Equal(at60.Y, at120.Y, 8);
        Assert.True(at60.X > 50);
        Assert.True(at60.Y > 100);
    }

    [Fact]
    public void BoundaryStopsOnlyTheBlockedInertiaAxis()
    {
        var session = new TouchNavigationSession();
        StartFling(session);
        session.StopInertiaAxes(horizontal: true, vertical: false);
        Assert.True(session.TryTakeFrame(36, out var frame));
        Assert.Equal(0, frame.ScrollDelta.X);
        Assert.True(frame.ScrollDelta.Y > 0);
        session.StopInertiaAxes(horizontal: false, vertical: true);
        Assert.False(session.IsInertiaActive);
        Assert.False(session.TryTakeFrame(52, out _));
    }

    [Fact]
    public void CancelDiscardsUnrenderedMotionAndInertia()
    {
        var session = new TouchNavigationSession();
        session.BeginContact(1, new(0, 300), 0);
        session.MoveContact(1, new(0, 100), 20);
        session.Cancel();
        Assert.Equal(0, session.Count);
        Assert.False(session.HasPendingFrame);
        Assert.False(session.TryTakeFrame(30, out _));
        StartFling(session);
        session.Cancel();
        Assert.False(session.IsInertiaActive);
        Assert.False(session.TryTakeFrame(36, out _));
    }

    [Fact]
    public void NewContactStopsInertiaWithoutCarryingMomentumIntoItsBaseline()
    {
        var session = new TouchNavigationSession();
        StartFling(session);
        session.BeginContact(2, new(300, 500), 30);
        Assert.False(session.IsInertiaActive);
        Assert.False(session.TryTakeFrame(32, out _));
        session.MoveContact(2, new(300, 490), 40);
        Assert.True(session.TryTakeFrame(48, out var frame));
        Assert.Equal(new Vector(0, 10), frame.ScrollDelta);
    }

    [Fact]
    public void LongRenderStallCancelsFlingInsteadOfJumpingTheViewport()
    {
        var session = new TouchNavigationSession();
        StartFling(session);
        Assert.False(session.TryTakeFrame(141, out _));
        Assert.False(session.IsInertiaActive);
        Assert.False(session.HasPendingFrame);
    }

    [Fact]
    public void RegressingTimeAndInvalidCoordinatesCannotCreateInvalidFrames()
    {
        var session = new TouchNavigationSession();
        session.BeginContact(1, new(0, 100), 100);
        session.MoveContact(1, new(double.NaN, 10), 110);
        Assert.False(session.HasPendingFrame);
        session.MoveContact(1, new(0, 80), 90);
        Assert.True(session.TryTakeFrame(90, out var frame));
        Assert.Equal(new Vector(0, 20), frame.ScrollDelta);
        session.EndContact(1, 90);
        Assert.False(session.IsInertiaActive);
    }

    [Fact]
    public void ReleaseSpeedIsCappedAfterVeryFastMotion()
    {
        var session = new TouchNavigationSession();
        session.BeginContact(1, new(0, 1000), 0);
        session.MoveContact(1, new(0, 0), 20);
        session.TryTakeFrame(20, out _);
        session.EndContact(1, 20);
        Assert.True(session.TryTakeFrame(21, out var frame));
        Assert.InRange(frame.ScrollDelta.Length, 2.9, 3);
    }

    [Fact]
    public void MarginTapAndRepeatedJitterDoNotMoveOrFlingThePage()
    {
        var session = new TouchNavigationSession();
        session.BeginContact(1, new(100, 200), 0, minimumPanDistance: 12);
        for (var index = 1; index <= 12; index++)
        {
            // The travelled path is much longer than 12 DIP, but the contact
            // remains near its origin and never becomes an intentional drag.
            session.MoveContact(1, new(100 + (index % 2 == 0 ? 9 : -9), 200), index * 10);
            Assert.False(session.HasPendingFrame);
            Assert.False(session.TryTakeFrame(index * 10, out _));
        }
        session.MoveContact(1, new(100, 200), 130);
        session.EndContact(1, 130);
        Assert.False(session.IsInertiaActive);
        Assert.False(session.HasPendingFrame);
    }

    [Fact]
    public void SlowMarginDragAccumulatesIntentAndDiscardsOnlyTheDeadZone()
    {
        var session = new TouchNavigationSession();
        session.BeginContact(1, new(100, 200), 0, minimumPanDistance: 12);
        for (var distance = 1; distance <= 12; distance++)
        {
            session.MoveContact(1, new(100, 200 - distance), distance * 20);
            Assert.False(session.TryTakeFrame(distance * 20, out _));
        }

        session.MoveContact(1, new(100, 185), 300);
        Assert.True(session.TryTakeFrame(300, out var first));
        Assert.Equal(new Point(100, 188), first.PreviousCenter);
        Assert.Equal(new Vector(0, 3), first.ScrollDelta);
        session.MoveContact(1, new(100, 184), 320);
        Assert.True(session.TryTakeFrame(320, out var next));
        Assert.Equal(new Vector(0, 1), next.ScrollDelta);
        session.MoveContact(1, new(100, 187), 340);
        Assert.True(session.TryTakeFrame(340, out var reversed));
        Assert.Equal(new Vector(0, -3), reversed.ScrollDelta);
    }

    [Fact]
    public void DiagonalMarginDragUsesRadialIntentDistanceAndRetainsExcess()
    {
        var session = new TouchNavigationSession();
        session.BeginContact(1, new(100, 200), 0, minimumPanDistance: 12);
        session.MoveContact(1, new(109, 188), 20); // 15 DIP, a 3-4-5 triangle.
        Assert.True(session.TryTakeFrame(20, out var frame));
        Assert.Equal(-1.8, frame.ScrollDelta.X, 8);
        Assert.Equal(2.4, frame.ScrollDelta.Y, 8);
        Assert.Equal(3, frame.ScrollDelta.Length, 8);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(11, 0)]
    [InlineData(12, 0)]
    [InlineData(20, 8)]
    public void FinalReleasePacketRespectsMarginIntentWithoutCreatingMomentum(double distance, double expectedScroll)
    {
        var session = new TouchNavigationSession();
        session.BeginContact(1, new(100, 200), 0, minimumPanDistance: 12);
        session.MoveContact(1, new(100, 200 - distance), 30);
        Assert.Equal(expectedScroll > 0, session.TryTakeFrame(30, out var frame));
        if (expectedScroll > 0) Assert.Equal(new Vector(0, expectedScroll), frame.ScrollDelta);
        session.EndContact(1, 30);
        Assert.False(session.IsInertiaActive);
        Assert.False(session.HasPendingFrame);
    }

    [Fact]
    public void IntentionalMarginDragBuildsMomentumOnlyAfterActivation()
    {
        var session = new TouchNavigationSession();
        session.BeginContact(1, new(100, 200), 0, minimumPanDistance: 12);
        session.MoveContact(1, new(100, 191), 10);
        session.MoveContact(1, new(100, 186), 20);
        Assert.True(session.TryTakeFrame(20, out var first));
        Assert.Equal(new Vector(0, 2), first.ScrollDelta);
        session.MoveContact(1, new(100, 176), 40);
        Assert.True(session.TryTakeFrame(40, out _));
        session.EndContact(1, 40);
        Assert.True(session.IsInertiaActive);
        Assert.True(session.TryTakeFrame(50, out var inertia));
        Assert.True(inertia.IsInertial);
        // Active motion is 10 DIP / 20 ms, not 24 DIP / 40 ms.
        Assert.Equal(.5 * (1 - Math.Exp(-.006 * 10)) / .006, inertia.ScrollDelta.Y, 8);
    }

    [Fact]
    public void SecondFingerImmediatelyRebaselinesPendingMarginIntentAndPinch()
    {
        var session = new TouchNavigationSession();
        session.BeginContact(1, new(100, 200), 0, minimumPanDistance: 12);
        session.MoveContact(1, new(100, 191), 10);
        Assert.False(session.TryTakeFrame(10, out _));
        session.BeginContact(2, new(200, 191), 11, minimumPanDistance: 12);
        Assert.False(session.TryTakeFrame(11, out _));
        session.MoveContact(1, new(99, 190), 20);
        session.MoveContact(2, new(201, 190), 20);
        Assert.True(session.TryTakeFrame(20, out var pinch));
        Assert.Equal(new Vector(0, 1), pinch.ScrollDelta);
        Assert.Equal(1.02, pinch.Scale, 8);
        session.EndContact(2, 21);
        Assert.False(session.TryTakeFrame(21, out _));
        session.MoveContact(1, new(99, 189), 22);
        Assert.True(session.TryTakeFrame(22, out var pan));
        Assert.Equal(new Vector(0, 1), pan.ScrollDelta);
    }

    [Fact]
    public void CancelAndNewContactResetMarginIntentWithoutAffectingPaperPanning()
    {
        var session = new TouchNavigationSession();
        session.BeginContact(1, new(100, 200), 0, minimumPanDistance: 12);
        session.MoveContact(1, new(100, 191), 10);
        session.Cancel();
        session.BeginContact(2, new(100, 200), 20);
        session.MoveContact(2, new(100, 199), 21);
        Assert.True(session.TryTakeFrame(21, out var paper));
        Assert.Equal(new Vector(0, 1), paper.ScrollDelta);
        session.EndContact(2, 22);
        session.BeginContact(3, new(100, 200), 30, minimumPanDistance: 12);
        session.MoveContact(3, new(100, 189), 40);
        Assert.False(session.TryTakeFrame(40, out _));
        session.EndContact(3, 40);
        Assert.False(session.IsInertiaActive);
    }

    private static Vector InertiaTravel(int frameRate)
    {
        var session = new TouchNavigationSession();
        StartFling(session);
        var total = default(Vector);
        for (var index = 1; index < 1000 && session.IsInertiaActive; index++)
        {
            if (session.TryTakeFrame(20 + index * 1000d / frameRate, out var frame)) total += frame.ScrollDelta;
        }
        Assert.False(session.IsInertiaActive);
        return total;
    }

    private static void StartFling(TouchNavigationSession session, bool allowInertia = true)
    {
        session.BeginContact(1, new(100, 200), 0);
        session.MoveContact(1, new(80, 160), 20);
        Assert.True(session.TryTakeFrame(20, out _));
        session.EndContact(1, 20, allowInertia);
        Assert.Equal(allowInertia, session.IsInertiaActive);
    }
}
