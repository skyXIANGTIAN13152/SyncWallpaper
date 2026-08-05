using SyncWallpaper.AudioEngine;
using SyncWallpaper.Core;

namespace SyncWallpaper.Tests;

[TestClass]
public class AudioEngineStage1Tests
{
    private static (FakeAudioProvider Provider, AudioConfigurationEngine Engine, AudioEndpointReference Speaker, AudioEndpointReference Mic) Create()
    {
        var provider = new FakeAudioProvider();
        var speaker = new AudioEndpointReference { DeviceId = "speaker", FriendlyName = "扬声器", Kind = AudioEndpointKind.Playback, State = AudioEndpointState.Active };
        var headset = new AudioEndpointReference { DeviceId = "headset", FriendlyName = "耳机", Kind = AudioEndpointKind.Playback, State = AudioEndpointState.Active };
        var mic = new AudioEndpointReference { DeviceId = "mic", FriendlyName = "麦克风", Kind = AudioEndpointKind.Capture, State = AudioEndpointState.Active };
        provider.Endpoints.AddRange(new[] { speaker, headset, mic });
        provider.Defaults[AudioEndpointRole.Console] = speaker;
        provider.Defaults[AudioEndpointRole.Multimedia] = speaker;
        provider.Defaults[AudioEndpointRole.Communications] = headset;
        provider.Defaults[AudioEndpointRole.Recording] = mic;
        return (provider, new AudioConfigurationEngine(provider, new TestLogger()), speaker, mic);
    }

    [TestMethod]
    public async Task NormalRoleAssignmentsAreAppliedAndReadBack()
    {
        var (provider, engine, speaker, mic) = Create();
        var target = new AudioProfile { Name = "三屏" };
        target.Assignments.Add(new AudioRoleAssignment { Role = AudioEndpointRole.Console, Endpoint = provider.Endpoints.Single(x => x.DeviceId == "headset") });
        var result = await engine.ApplyAsync(target, AudioStepMode.Required);
        Assert.IsTrue(result.Success); Assert.AreEqual("headset", provider.GetDefault(AudioEndpointRole.Console)!.DeviceId);
    }

    [TestMethod]
    public async Task MissingOptionalDeviceDoesNotFailWholeAudioStep()
    {
        var (_, engine, _, _) = Create();
        var profile = new AudioProfile { Name = "optional" };
        profile.Assignments.Add(new AudioRoleAssignment { Role = AudioEndpointRole.Console, Mode = AudioStepMode.Optional, Endpoint = new() { DeviceId = "missing", FriendlyName = "断开" } });
        var result = await engine.ApplyAsync(profile, AudioStepMode.Optional);
        Assert.IsTrue(result.Success); Assert.IsFalse(result.RequiredFailure);
    }

    [TestMethod]
    public async Task MissingRequiredDeviceIsReported()
    {
        var (_, engine, _, _) = Create();
        var profile = new AudioProfile();
        profile.Assignments.Add(new AudioRoleAssignment { Role = AudioEndpointRole.Console, Mode = AudioStepMode.Required, Endpoint = new() { DeviceId = "missing" } });
        var result = await engine.ApplyAsync(profile, AudioStepMode.Required);
        Assert.IsFalse(result.Success); Assert.IsTrue(result.RequiredFailure); Assert.IsTrue(result.RollbackAttempted);
    }

    [TestMethod]
    public async Task DisabledAudioStepDoesNothing()
    {
        var (provider, engine, speaker, _) = Create();
        var profile = new AudioProfile();
        profile.Assignments.Add(new AudioRoleAssignment { Role = AudioEndpointRole.Console, Endpoint = new() { DeviceId = "headset" } });
        var result = await engine.ApplyAsync(profile, AudioStepMode.Disabled);
        Assert.IsTrue(result.Success); Assert.AreEqual("speaker", provider.GetDefault(AudioEndpointRole.Console)!.DeviceId);
    }

    [TestMethod]
    public async Task CommunicationsAndMultimediaRolesAreIndependent()
    {
        var (provider, engine, _, _) = Create();
        var profile = new AudioProfile();
        profile.Assignments.Add(new AudioRoleAssignment { Role = AudioEndpointRole.Communications, Endpoint = provider.Endpoints.Single(x => x.DeviceId == "speaker"), Mode = AudioStepMode.Required });
        profile.Assignments.Add(new AudioRoleAssignment { Role = AudioEndpointRole.Multimedia, Endpoint = provider.Endpoints.Single(x => x.DeviceId == "headset"), Mode = AudioStepMode.Required });
        var result = await engine.ApplyAsync(profile, AudioStepMode.Required);
        Assert.IsTrue(result.Success); Assert.AreEqual("speaker", provider.GetDefault(AudioEndpointRole.Communications)!.DeviceId); Assert.AreEqual("headset", provider.GetDefault(AudioEndpointRole.Multimedia)!.DeviceId);
    }

    [TestMethod]
    public async Task OptionalSetFailureRestoresChangedRoles()
    {
        var (provider, engine, _, _) = Create();
        provider.FailRoles.Add(AudioEndpointRole.Multimedia);
        var profile = new AudioProfile();
        profile.Assignments.Add(new AudioRoleAssignment { Role = AudioEndpointRole.Console, Endpoint = provider.Endpoints.Single(x => x.DeviceId == "headset"), Mode = AudioStepMode.Optional });
        profile.Assignments.Add(new AudioRoleAssignment { Role = AudioEndpointRole.Multimedia, Endpoint = provider.Endpoints.Single(x => x.DeviceId == "speaker"), Mode = AudioStepMode.Optional });
        var result = await engine.ApplyAsync(profile);
        Assert.IsTrue(result.Success); Assert.IsTrue(result.RollbackSucceeded); Assert.AreEqual("speaker", provider.GetDefault(AudioEndpointRole.Console)!.DeviceId);
    }

    [TestMethod]
    public async Task RequiredSetFailureReportsRequiredFailure()
    {
        var (provider, engine, _, _) = Create();
        provider.FailRoles.Add(AudioEndpointRole.Console);
        var profile = new AudioProfile();
        profile.Assignments.Add(new AudioRoleAssignment { Role = AudioEndpointRole.Console, Endpoint = provider.Endpoints.Single(x => x.DeviceId == "headset"), Mode = AudioStepMode.Required });
        var result = await engine.ApplyAsync(profile, AudioStepMode.Required);
        Assert.IsFalse(result.Success); Assert.IsTrue(result.RequiredFailure);
    }

    [TestMethod]
    public async Task InjectedAudioFailureRollsBackChangedRoles()
    {
        var (provider, _, _, _) = Create();
        var engine = new AudioConfigurationEngine(provider, new TestLogger(), new ConfigurableFaultInjector(new[] { FaultPoint.AudioApply }));
        var profile = new AudioProfile();
        profile.Assignments.Add(new AudioRoleAssignment { Role = AudioEndpointRole.Console, Endpoint = provider.Endpoints.Single(x => x.DeviceId == "headset"), Mode = AudioStepMode.Required });
        var result = await engine.ApplyAsync(profile, AudioStepMode.Required);
        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.RollbackAttempted);
        Assert.AreEqual("speaker", provider.GetDefault(AudioEndpointRole.Console)!.DeviceId);
    }

    [TestMethod]
    public async Task AudioCancellationReturnsBoundedRollbackResult()
    {
        var (provider, _, _, _) = Create();
        provider.SetDelay = TimeSpan.FromMilliseconds(100);
        var profile = new AudioProfile();
        profile.Assignments.Add(new AudioRoleAssignment { Role = AudioEndpointRole.Console, Endpoint = provider.Endpoints.Single(x => x.DeviceId == "headset"), Mode = AudioStepMode.Required });
        using var source = new CancellationTokenSource();
        var task = new AudioConfigurationEngine(provider, new TestLogger()).ApplyAsync(profile, AudioStepMode.Required, source.Token);
        await Task.Delay(10);
        source.Cancel();
        var result = await task;
        Assert.IsTrue(result.Cancelled);
        Assert.IsTrue(result.RollbackAttempted);
        Assert.IsTrue(result.RollbackSucceeded);
        Assert.AreEqual("speaker", provider.GetDefault(AudioEndpointRole.Console)!.DeviceId);
    }

    [TestMethod]
    public void ProviderRaisesDeviceAndDefaultEvents()
    {
        var (provider, _, _, _) = Create(); var devices = 0; var defaults = 0;
        provider.DevicesChanged += (_, _) => devices++; provider.DefaultsChanged += (_, _) => defaults++;
        provider.RaiseDevices(); provider.RaiseDefaults();
        Assert.AreEqual(1, devices); Assert.AreEqual(1, defaults);
    }
}
