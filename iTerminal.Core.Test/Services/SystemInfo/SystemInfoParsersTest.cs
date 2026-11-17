using iTerminal.Core.Services.SystemInfo;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Services.SystemInfo.Test;

/// <summary>The formats the system information report reads, tested against fixture text so they run on any platform.</summary>
[TestClass]
public class SystemInfoParsersTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void CpuModel_IsTheFirstModelName_AndNullOnArm()
    {
        const string x86 = "processor\t: 0\nvendor_id\t: GenuineIntel\nmodel name\t: Intel(R) Core(TM) i7-8700 CPU @ 3.20GHz\n";
        const string arm = "processor\t: 0\nCPU implementer\t: 0x41\n";

        Assert.AreEqual("Intel(R) Core(TM) i7-8700 CPU @ 3.20GHz", SystemInfoParsers.ParseCpuModel(x86));
        Assert.IsNull(SystemInfoParsers.ParseCpuModel(arm));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CpuTicks_CountIdleAndIoWaitAsIdle()
    {
        CpuTicks? ticks = SystemInfoParsers.ParseCpuTicks("cpu  100 0 50 800 50 0 0 0 0 0\ncpu0 1 0 1 1 1\n");

        Assert.IsNotNull(ticks);
        Assert.AreEqual(850UL, ticks.Idle);
        Assert.AreEqual(1000UL, ticks.Total);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CpuLoad_IsTheBusyShareOfTheTimeBetweenTwoReads()
    {
        var previous = new CpuTicks(850, 1000);

        Assert.AreEqual(100.0, SystemInfoParsers.CpuLoadPercent(previous, new CpuTicks(850, 1100)));
        Assert.AreEqual(50.0, SystemInfoParsers.CpuLoadPercent(previous, new CpuTicks(900, 1100)));
        Assert.IsNull(SystemInfoParsers.CpuLoadPercent(previous, new CpuTicks(850, 1000)), "nothing changed");
        Assert.IsNull(SystemInfoParsers.CpuLoadPercent(previous, new CpuTicks(800, 900)), "the counters went back");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MemInfo_ReadsTotalAndAvailableInBytes()
    {
        MemoryReading? memory = SystemInfoParsers.ParseMemInfo("MemTotal:       16000000 kB\nMemFree:        1000000 kB\nMemAvailable:    8000000 kB\n");

        Assert.IsNotNull(memory);
        Assert.AreEqual(16000000UL * 1024, memory.TotalBytes);
        Assert.AreEqual(8000000UL * 1024, memory.AvailableBytes);
        Assert.IsNull(SystemInfoParsers.ParseMemInfo("MemTotal:  100 kB\n"), "both figures are needed");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DiskStats_ReadsSectorsAsBytesPerDevice()
    {
        const string diskstats =
            "   8       0 sda 1 0 100 0 2 0 200 0 0 0 0\n" +
            "   8       1 sda1 1 0 50 0 1 0 100 0 0 0 0\n" +
            "   7       0 loop0 1 0 999 0 1 0 999 0 0 0 0\n";

        IReadOnlyDictionary<string, DiskCounters> disks = SystemInfoParsers.ParseDiskStats(diskstats);

        Assert.AreEqual(100UL * 512, disks["sda"].ReadBytes);
        Assert.AreEqual(200UL * 512, disks["sda"].WriteBytes);
        Assert.IsTrue(disks.ContainsKey("sda1"), "partitions are listed too; the probe keeps only whole disks");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ExcludedDisks_AreLoopRamCompressedAndOptical()
    {
        Assert.IsTrue(SystemInfoParsers.IsExcludedDiskName("loop0"));
        Assert.IsTrue(SystemInfoParsers.IsExcludedDiskName("ram3"));
        Assert.IsTrue(SystemInfoParsers.IsExcludedDiskName("zram0"));
        Assert.IsTrue(SystemInfoParsers.IsExcludedDiskName("sr0"));
        Assert.IsFalse(SystemInfoParsers.IsExcludedDiskName("sda"));
        Assert.IsFalse(SystemInfoParsers.IsExcludedDiskName("nvme0n1"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PerSecond_IsNullWithoutElapsedTimeOrOnACounterReset()
    {
        Assert.AreEqual(100.0, SystemInfoParsers.PerSecond(100, 300, 0, 2000));
        Assert.IsNull(SystemInfoParsers.PerSecond(100, 300, 500, 500), "no time passed");
        Assert.IsNull(SystemInfoParsers.PerSecond(300, 100, 0, 1000), "the counter went back");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PciId_AndPciName_FindTheDeviceInPciIds()
    {
        const string pciIds =
            "# comment\n" +
            "10de  NVIDIA Corporation\n" +
            "\t1b80  GP104 [GeForce GTX 1080]\n" +
            "\t\t1043 8666  Some subsystem\n" +
            "8086  Intel Corporation\n" +
            "\t3e92  UHD Graphics 630\n";

        var ids = SystemInfoParsers.ParsePciId("DRIVER=nvidia\nPCI_ID=10DE:1B80\n");

        Assert.AreEqual(("10de", "1b80"), ids);
        Assert.AreEqual("GP104 [GeForce GTX 1080]", SystemInfoParsers.ParsePciName(pciIds, "10de", "1b80"));
        Assert.AreEqual("UHD Graphics 630", SystemInfoParsers.ParsePciName(pciIds, "8086", "3e92"));
        Assert.IsNull(SystemInfoParsers.ParsePciName(pciIds, "10de", "ffff"), "an unlisted device has no name");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PowerSupply_Discharging_ReadsChargeTimeAndHealth()
    {
        var attributes = new Dictionary<string, string>
        {
            ["status"] = "Discharging\n",
            ["capacity"] = "80\n",
            ["energy_now"] = "40000000\n",
            ["energy_full"] = "50000000\n",
            ["energy_full_design"] = "60000000\n",
            ["power_now"] = "10000000\n",
            ["cycle_count"] = "123\n",
            ["manufacturer"] = "SMP\n",
            ["model_name"] = "DELL\n",
        };

        BatteryReading battery = SystemInfoParsers.ParsePowerSupply(attributes);

        Assert.AreEqual(BatteryStatus.Discharging, battery.Status);
        Assert.AreEqual(80.0, battery.Percent);
        Assert.AreEqual(TimeSpan.FromHours(4), battery.TimeRemaining, "40 Wh at 10 W");
        Assert.AreEqual(60.0, battery.DesignWattHours);
        Assert.AreEqual(50.0, battery.FullWattHours);
        Assert.AreEqual(10.0, battery.PowerWatts);
        Assert.AreEqual(123, battery.Cycles);
        Assert.AreEqual("SMP", battery.Manufacturer);
        Assert.IsNull(battery.TimeToFull, "only a charging battery has a time to full");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PowerSupply_Charging_ReadsTimeToFull()
    {
        var attributes = new Dictionary<string, string>
        {
            ["status"] = "Charging\n",
            ["energy_now"] = "40000000\n",
            ["energy_full"] = "50000000\n",
            ["power_now"] = "10000000\n",
        };

        BatteryReading battery = SystemInfoParsers.ParsePowerSupply(attributes);

        Assert.AreEqual(BatteryStatus.Charging, battery.Status);
        Assert.AreEqual(TimeSpan.FromHours(1), battery.TimeToFull, "10 Wh still to go at 10 W");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PowerSupply_ChargeCounters_GiveTimeFromTheCurrent()
    {
        var attributes = new Dictionary<string, string>
        {
            ["status"] = "Discharging\n",
            ["charge_now"] = "3000000\n",
            ["current_now"] = "1500000\n",
        };

        BatteryReading battery = SystemInfoParsers.ParsePowerSupply(attributes);

        Assert.AreEqual(TimeSpan.FromHours(2), battery.TimeRemaining, "3 Ah at 1.5 A");
        Assert.IsNull(battery.DesignWattHours);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PowerSupply_ZeroPower_GivesNoTime()
    {
        var attributes = new Dictionary<string, string>
        {
            ["status"] = "Discharging\n",
            ["energy_now"] = "40000000\n",
            ["power_now"] = "0\n",
        };

        BatteryReading battery = SystemInfoParsers.ParsePowerSupply(attributes);

        Assert.IsNull(battery.TimeRemaining, "a battery drawing nothing has no time remaining to report");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PowerStatus_MapsAcDischargingAndUnknownValues()
    {
        BatteryReading? discharging = SystemInfoParsers.MapPowerStatus(0, 50, 1, 3600);
        Assert.IsNotNull(discharging);
        Assert.AreEqual(BatteryStatus.Discharging, discharging.Status);
        Assert.AreEqual(50.0, discharging.Percent);
        Assert.AreEqual(TimeSpan.FromHours(1), discharging.TimeRemaining);
        Assert.IsNull(discharging.DesignWattHours, "health is not reported on Windows");

        BatteryReading? full = SystemInfoParsers.MapPowerStatus(1, 100, 0, -1);
        Assert.IsNotNull(full);
        Assert.AreEqual(BatteryStatus.Full, full.Status);
        Assert.IsNull(full.TimeRemaining, "a negative lifetime is unknown");

        BatteryReading? unknown = SystemInfoParsers.MapPowerStatus(255, 255, 255, -1);
        Assert.IsNotNull(unknown);
        Assert.AreEqual(BatteryStatus.Unknown, unknown.Status);
        Assert.IsNull(unknown.Percent, "255 is unknown, not 255 percent");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PowerStatus_NoBatteryFlag_GivesNoBattery()
    {
        Assert.IsNull(SystemInfoParsers.MapPowerStatus(1, 255, 128, -1));
    }
}
