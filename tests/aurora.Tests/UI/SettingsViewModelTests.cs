using Aurora.Domain.DTO;
using Aurora.Services;
using Aurora.UI;
using Xunit;

namespace Aurora.Tests.UI;

/// <summary>
/// SettingsViewModel.LoadFrom / Build 的字段往返测试。
/// 锁定 SnippingSettings 全部字段（含放大镜）都被正确拷贝，
/// 防止手写 Copy 漏字段导致“改了设置重开又恢复默认”。
/// </summary>
public class SettingsViewModelTests
{
    [Fact]
    public void LoadFrom_PreservesAllMagnifierFields()
    {
        var settings = new Settings
        {
            Preferences = new Preferences
            {
                Snipping = new SnippingSettings
                {
                    MagnifierPosition = MagnifierPosition.TopLeft,
                    ShowMagnifierCoordinates = false,
                    ShowMagnifierColor = false,
                    MagnifierZoomPreset = MagnifierZoomPreset.Large,
                },
            },
        };

        var vm = new SettingsViewModel();
        vm.LoadFrom(settings);

        Assert.Equal(MagnifierPosition.TopLeft, vm.Snipping.MagnifierPosition);
        Assert.False(vm.Snipping.ShowMagnifierCoordinates);
        Assert.False(vm.Snipping.ShowMagnifierColor);
        Assert.Equal(MagnifierZoomPreset.Large, vm.Snipping.MagnifierZoomPreset);
    }

    [Fact]
    public void Build_RoundTripsMagnifierFields()
    {
        var vm = new SettingsViewModel();
        vm.Snipping.MagnifierPosition = MagnifierPosition.TopLeft;
        vm.Snipping.ShowMagnifierCoordinates = false;
        vm.Snipping.ShowMagnifierColor = false;
        vm.Snipping.MagnifierZoomPreset = MagnifierZoomPreset.Large;

        var built = vm.Build(new SettingsBuilder());

        Assert.Equal(MagnifierPosition.TopLeft, built.Preferences.Snipping.MagnifierPosition);
        Assert.False(built.Preferences.Snipping.ShowMagnifierCoordinates);
        Assert.False(built.Preferences.Snipping.ShowMagnifierColor);
        Assert.Equal(MagnifierZoomPreset.Large, built.Preferences.Snipping.MagnifierZoomPreset);
    }
}
