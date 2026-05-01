using VentoyToolkitSetup.Wpf.Services;

namespace ForgerEMS.Wpf.Tests;

public sealed class MachineIntelligenceResaleEngineTests
{
    [Fact]
    public void HardwareReaderHandlesMissingGpuRamAndStorageWithoutCrash()
    {
        var profile = new SystemProfile
        {
            Manufacturer = "Dell",
            Model = "Latitude 5420",
            Cpu = "Intel Core i5-1145G7",
            RamTotal = "Unknown",
            RamTotalGb = null,
            Gpus = [],
            Disks = []
        };

        var result = new WindowsHardwareReader().Read(profile);

        Assert.Equal(HardwareProbeStatus.Partial, result.Status);
        Assert.NotEmpty(result.Warnings);
        Assert.Contains(result.Warnings, warning => warning.Field == "gpu");
        Assert.Contains(result.Warnings, warning => warning.Field == "ram");
        Assert.Contains(result.Warnings, warning => warning.Field == "storage");
    }

    [Fact]
    public void HardwareReaderClassifiesLaptopVsDesktop()
    {
        var laptop = new SystemProfile
        {
            Manufacturer = "Lenovo",
            Model = "ThinkPad T14",
            Cpu = "Intel Core i7-1185G7",
            Batteries = [new SystemBatteryProfile { Name = "Battery" }]
        };
        var desktop = new SystemProfile
        {
            Manufacturer = "Dell",
            Model = "OptiPlex 7090 Tower",
            Cpu = "Intel Core i5-10500"
        };

        var laptopResult = new WindowsHardwareReader().Read(laptop);
        var desktopResult = new WindowsHardwareReader().Read(desktop);

        Assert.Equal("laptop", laptopResult.Identity.DeviceType);
        Assert.Equal("desktop", desktopResult.Identity.DeviceType);
    }

    [Fact]
    public void PrivacyRedactorRemovesSerialPathsAndKeys()
    {
        var text = HardwarePrivacyRedactor.Redact(@"serial=ABC12345 path=C:\Users\Daddy_FDS\Secret token=sk-abcdef123456");
        Assert.DoesNotContain("ABC12345", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Daddy_FDS", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sk-abcdef123456", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OfflineEstimatorValuesDedicatedGpuHigherThanIntegratedOnly()
    {
        var service = new OfflineResaleEstimator();
        var integrated = service.Estimate(new DeviceResaleProfile
        {
            Identity = new DeviceIdentityProfile { Manufacturer = "HP", Model = "EliteBook" },
            RawSystemProfile = new SystemProfile
            {
                Manufacturer = "HP",
                Model = "EliteBook",
                Cpu = "Intel Core i5-1135G7",
                RamTotal = "16 GB",
                RamTotalGb = 16,
                Gpus = [new SystemGpuProfile { Name = "Intel Iris Xe" }],
                Disks = [new SystemDiskProfile { Name = "SSD", MediaType = "SSD", Size = "512 GB", Health = "Healthy", Status = "READY" }]
            }
        });
        var dedicated = service.Estimate(new DeviceResaleProfile
        {
            Identity = new DeviceIdentityProfile { Manufacturer = "HP", Model = "EliteBook" },
            RawSystemProfile = new SystemProfile
            {
                Manufacturer = "HP",
                Model = "EliteBook",
                Cpu = "Intel Core i7-11800H",
                RamTotal = "16 GB",
                RamTotalGb = 16,
                Gpus = [new SystemGpuProfile { Name = "NVIDIA RTX 3050" }],
                Disks = [new SystemDiskProfile { Name = "SSD", MediaType = "SSD", Size = "512 GB", Health = "Healthy", Status = "READY" }]
            }
        });

        Assert.True(dedicated.FairListingPrice >= integrated.FairListingPrice);
    }

    [Fact]
    public void ManualCompsCalculatesMedian()
    {
        var service = new ManualComparablePricingService(
        [
            new MarketComparable { Platform = "Manual", Title = "A", Price = 180m },
            new MarketComparable { Platform = "Manual", Title = "B", Price = 220m },
            new MarketComparable { Platform = "Manual", Title = "C", Price = 260m }
        ]);

        var result = service.GetComparables(new DeviceResaleProfile());

        Assert.True(result.HasData);
        Assert.Equal(220m, result.MedianPrice);
    }

    [Fact]
    public void EbayQueryBuilderCreatesSaneLaptopQuery()
    {
        var query = EbayMarketPricingService.BuildLaptopQuery(new DeviceResaleProfile
        {
            Identity = new DeviceIdentityProfile { Manufacturer = "Dell", Model = "Latitude 7420", CpuModel = "Intel Core i7-1185G7" },
            RawSystemProfile = new SystemProfile { RamTotalGb = 16 }
        });

        Assert.Contains("Dell", query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Latitude 7420", query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("laptop", query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OfflineEstimateListsHddAsValueReducer()
    {
        var service = new OfflineResaleEstimator();
        var profile = new DeviceResaleProfile
        {
            Identity = new DeviceIdentityProfile { Manufacturer = "Acer", Model = "Aspire 5", CpuModel = "Intel Core i5-1035G1" },
            RawSystemProfile = new SystemProfile
            {
                Manufacturer = "Acer",
                Model = "Aspire 5",
                Cpu = "Intel Core i5-1035G1",
                RamTotal = "8 GB",
                RamTotalGb = 8,
                Disks =
                [
                    new SystemDiskProfile
                    {
                        Name = "HDD",
                        MediaType = "HDD",
                        Size = "1 TB",
                        Health = "Healthy",
                        Status = "READY"
                    }
                ]
            }
        };

        var estimate = service.Estimate(profile);

        Assert.Contains(estimate.ValueReducers, r => r.Contains("HDD", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ListingDraftRedactsSerialStyleTokensInTitle()
    {
        var service = new OfflineResaleEstimator();
        var profile = new DeviceResaleProfile
        {
            Identity = new DeviceIdentityProfile
            {
                Manufacturer = "Dell",
                Model = "Latitude 5420 serial:ABC123456789",
                CpuModel = "Intel Core i5"
            },
            RawSystemProfile = new SystemProfile
            {
                Manufacturer = "Dell",
                Model = "Latitude 5420",
                Cpu = "Intel Core i5",
                RamTotal = "16 GB",
                RamTotalGb = 16,
                OperatingSystem = "Windows 11",
                Disks = [new SystemDiskProfile { Name = "SSD", MediaType = "SSD", Size = "256 GB", Health = "Healthy", Status = "READY" }]
            }
        };

        var estimate = service.Estimate(profile);
        var draft = service.GenerateListingDraft(profile, estimate);

        Assert.DoesNotContain("ABC123456789", draft.Title, StringComparison.Ordinal);
        Assert.Contains("redacted", draft.Title, StringComparison.OrdinalIgnoreCase);
    }
}

