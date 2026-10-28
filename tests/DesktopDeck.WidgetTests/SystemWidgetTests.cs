using System;
using System.Collections.Generic;
using System.Globalization;
using DesktopDeck.Widgets;
public static class SystemStatsTests
{
    public static void CpuIncludesIdleInKernel(){Expect.Near(75,SystemStatsLogic.CpuPercent(100,200,100,150,350,150),1e-8,"kernel includes idle");Expect.Near(0,SystemStatsLogic.CpuPercent(100,200,100,100,200,100),1e-8,"no interval");}
    public static void HistoryAndThresholds(){var h=new List<double>();for(int i=0;i<8;i++)SystemStatsLogic.Push(h,i,3);Expect.Equal(3,h.Count,"bounded");Expect.Near(6,SystemStatsLogic.Average(h,3),1e-8,"last samples");Expect.Equal(1,SystemStatsLogic.Level(90,95,80),"reversed thresholds normalized");}
    public static void BytesFormatting(){Expect.Equal("1.0 GB",SystemStatsLogic.FormatBytes(1024d*1024*1024,CultureInfo.InvariantCulture),"binary scaling");}
}
public static class DiskSpaceTests
{
    public static void FilteringAndSorting(){var d=DiskSpaceLogic.Arrange(DiskSpaceLogic.FakeDrives(),"fullest",["c"],true,false);Expect.Equal("E:",d[0].Name,"highest fullness first");Expect.True(!d.Exists(x=>x.Name=="C:"||x.Name=="Z:"),"hidden and network excluded");}
    public static void Bounds(){Expect.Near(0,DiskSpaceLogic.UsedFraction(0,10),1e-8,"no capacity");Expect.Near(1,DiskSpaceLogic.UsedFraction(100,-1),1e-8,"invalid free bounded");}
}
public static class NetworkSpeedTests
{
    public static void DeltaResets(){Expect.Near(100,NetworkSpeedLogic.Rate(100,300,2),1e-8,"elapsed seconds");Expect.Near(0,NetworkSpeedLogic.Rate(300,100,2),1e-8,"reset no spike");}
    public static void UnitsAndVirtual(){Expect.Equal("8.0 Mb/s",NetworkSpeedLogic.FormatRate(1_000_000,"Mbps",1,CultureInfo.InvariantCulture),"bits per byte");Expect.True(NetworkSpeedLogic.IsVirtual("vEthernet","Hyper-V"),"virtual filter");Expect.True(NetworkSpeedLogic.Matches("Wi-Fi","Intel adapter","intel"),"description filter");}
}
public static class BatteryTests
{
    public static void DesktopAndCharge(){Expect.Equal("AC power",BatteryLogic.State(new(false,true,false,-1,-1,false,"Balanced")),"desktop");Expect.Equal(0,BatteryLogic.Level(5,25,10,true),"charging not low");Expect.Equal(2,BatteryLogic.Level(5,25,10,false),"critical");}
    public static void Estimates(){Expect.Near(3600,BatteryLogic.TimeToFull(30000,50000,20000)!.Value,1e-8,"charge estimate");Expect.Near(.8,BatteryLogic.Health(40000,50000)!.Value,1e-8,"capacity health");Expect.Equal("",BatteryLogic.Duration(-1),"unknown");}
}
public static class PcInfoTests
{
    public static void ModernWindowsName(){Expect.Equal("Windows 11 Pro",PcInfoLogic.WindowsName("Windows 10 Pro",22631),"registry compatibility name");}
    public static void ProcessorAndUptime(){Expect.Equal("Intel Core i7",PcInfoLogic.CpuName("Intel(R) Core(TM) i7 CPU @ 2.60GHz",true),"tidy");Expect.Equal("3d 4h 12m",PcInfoLogic.Uptime(TimeSpan.FromDays(3)+TimeSpan.FromHours(4)+TimeSpan.FromMinutes(12),"short"),"uptime");}
}
public static class TopAppsTests
{
    public static void GroupAndExclude(){var a=TopAppsLogic.Top(TopAppsLogic.FakeProcesses(),true,"memory",5,TopAppsLogic.DefaultExclude.Split('\n'));Expect.Equal("msedge",a[0].Name,"memory rank");Expect.Equal(3,a[0].Count,"process grouping");Expect.True(!a.Exists(x=>x.Name=="System"),"protected bookkeeping excluded");}
    public static void CpuNormalization(){Expect.Near(25,TopAppsLogic.CpuPercent(0,10_000_000,1,4),1e-8,"all CPU scale");Expect.Near(100,TopAppsLogic.CpuPercent(0,10_000_000,1,4,"core"),1e-8,"single core scale");}
}

